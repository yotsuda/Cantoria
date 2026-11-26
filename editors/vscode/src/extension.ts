// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

// Cantoria as a VS Code extension: carries the `cantoria` program, holds the settings (where
// NEUTRINO is, where the DiffSinger voices are, which voice), and sings for other extensions
// through the API `activate` returns (docs/design.md §4.2 in the repository). Lily# is the first
// caller: its preview's Play hands the sung part's notes here and plays the WAV beside its synth.
// Nothing of NEUTRINO or of a voice is bundled — the user installs them and points this
// extension at the folders.

import * as cp from 'child_process';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import * as vscode from 'vscode';
import {
    PlanAsk, SingParameter, SingProgress, SingerPlan, planAsked, planOfNoOne,
    CantoriaScore, DefaultVoices, ENGINES, EngineName, VOICEVOX_URL, VoiceEntry, VoiceGender, defaultVoiceFor, diffSingerEntriesIn, diffSingerVoicesIn, findEngines, languageProblem, missingProgramNote, needsVoicevox, neutrinoFolderProblems, voicevoxRunIn, programCandidates,
    resolveSinger, scoreProblems, splitVoiceId, voiceEntriesIn, voiceId, voicesFolderProblems, voicesIn,
    VoiceOffer, defaultVoicesFolder, fetchLine, offerDetail,
} from './core';
import { Engine, EngineSetup, RenderedVoice } from './engine';
import { VoicevoxServer } from './voicevox';

/** What `status()` answers: whether a `sing` can succeed now, and why not. */
export interface CantoriaStatus {
    ready: boolean;
    program: string | undefined;
    /** The engine of the voice the setting names. */
    backend: EngineName;
    neutrinoFolder: string;
    /** The DiffSinger voices folder. */
    voicesFolder: string;
    /** The voice the setting names, as `engine/name`: `neutrino/MERROW`, `diffsinger/TIGER`. */
    voice: string;
    /** Every voice installed, as `engine/name`; `voices()` has each as an entry. */
    voices: string[];
    problems: string[];
}

/** The API another extension gets from `vscode.extensions.getExtension('yotsuda.cantoria').activate()`. */
export interface CantoriaApi {
    /** This extension's version. */
    readonly version: string;
    /** The API's shape; raised when a call changes incompatibly. */
    readonly apiVersion: 1;
    status(): Promise<CantoriaStatus>;
    /** The voices installed, each as `{ id: 'neutrino/MERROW', engine, name, label, language? }`. */
    voices(): Promise<VoiceEntry[]>;
    /** Sings `score` (the README's JSON: notes in seconds with syllables) in `options.voice` — an
     *  `engine/name` from `voices()`, or a bare NEUTRINO voice name — else the setting's voice. The
     *  WAV is kept under the extension's storage until "Forget Rendered Voices"; the same score in
     *  the same voice is sung once. Throws with a sentence when it cannot: no program, no engine set
     *  up, a voice or an engine that is not there, a score the engine refused. `options.signal`
     *  lets go of a render no longer wanted: it rejects at once, and the engine stops at its next
     *  phrase, keeping the phrases it sang. `options.onProgress` hears how far the render is
     *  (after 0.4.0): `{ toSing, sung }` in seconds of music — what has to be sung ANEW (the phrases
     *  kept from an earlier sing are not counted) and how much of it is done; told once before
     *  the first phrase and after each. A voice whose engine sings a score whole tells nothing. */
    sing(score: CantoriaScore, options?: { voice?: string; signal?: AbortSignal; onProgress?: (progress: SingProgress) => void }): Promise<RenderedVoice & { cached: boolean }>;
    /** The settings a voice takes in a score's `sing` (after 0.4.0): each with its name, where it
     *  can change, what it does and its range. `voice` as for `sing`. None for a voice whose engine has no such setting. What a score
     *  sets and the voice does not take is not sung, and said in `sing`'s `notes`. */
    parameters(voice?: string): Promise<SingParameter[]>;
    /** Lets the engine go (NEUTRINO's server and the DiffSinger sessions with it); the next `sing` starts it again. */
    release(): void;
    /** The voice a score's `singer` string comes to here: the voice it names when installed, else the
     *  default voice of the gender it says or of `options.gender`; and a sentence when it is not the one asked for. */
    resolveSinger(singer: string, options?: { language?: string; gender?: string }): Promise<{ voice: string; note?: string }>;
    /** Who would sing what a score asks for, without singing it (after 0.4.0): for each voice asked
     *  for — `{ asked: [{ part?, key?, voice?, gender?, language? }, …] }`, or a score with
     *  `options.voice` or `options.gender` — the installed voice (`engine/name`) that would, with a
     *  `note` when it is not the one asked for, or `voice: null` and the `reason`: no installed voice
     *  sings the language, a default voice that does not, an engine that is not set up. `ready` when
     *  each has a singer. The `cantoria` program answers (its `plan`), by the rule `resolveSinger`
     *  goes by and what `sing` would refuse; no model is loaded for it. */
    plan(score: CantoriaScore | { asked: readonly PlanAsk[] }, options?: { voice?: string; gender?: string }): Promise<SingerPlan>;
}

let output: vscode.OutputChannel;
let engine: Engine | undefined;
let storage: string;

function settings(): vscode.WorkspaceConfiguration { return vscode.workspace.getConfiguration('cantoria'); }
function neutrinoFolder(): string { return settings().get<string>('neutrinoFolder', '').trim(); }
function voicesFolder(): string { return settings().get<string>('voicesFolder', '').trim(); }
function vocoderFolder(): string { return settings().get<string>('vocoderFolder', '').trim(); }
function voicevoxUrl(): string { return settings().get<string>('voicevoxUrl', VOICEVOX_URL).trim(); }
function voicevoxFolder(): string { return settings().get<string>('voicevoxFolder', '').trim(); }

/** VOICEVOX: an engine that is a server has no folder to read, so its voices are asked for. */
const voicevox = new VoicevoxServer(() => ({ url: voicevoxUrl(), folder: voicevoxFolder(), gpu: settings().get<boolean>('gpu', true) }), {
    fetchJson: async (url, timeoutMs) => {
        const answer = await fetch(url, { signal: AbortSignal.timeout(timeoutMs) });
        if (!answer.ok) { throw new Error(`HTTP ${answer.status}`); }
        return answer.json();
    },
    spawn: (program, args, cwd) => {
        const child = cp.spawn(program, args, { cwd, windowsHide: true, stdio: 'ignore' });
        return child;
    },
    exists: fs.existsSync,
    log: line => output.appendLine(line),
    now: Date.now,
    sleep: ms => new Promise(resolve => setTimeout(resolve, ms)),
});

/** A voice as its engine, name and `engine/name`. */
interface Voice { engine: string; name: string; id: string }
function voiceOf(text: string | undefined): Voice {
    const v = splitVoiceId(text);
    return { ...v, id: voiceId(v.engine, v.name) };
}
/** The two default-voice settings; '' is not set. */
function defaultVoices(): DefaultVoices {
    return { female: settings().get<string>('defaultFemaleVoice', ''), male: settings().get<string>('defaultMaleVoice', '') };
}
/** The voice a caller who names none gets, and what `status().voice` says. */
function voiceSetting(): Voice { return voiceOf(defaultVoiceFor(voices(), defaultVoices(), 'female').voice); }

function readTextOr(p: string): string | undefined { try { return fs.readFileSync(p, 'utf8'); } catch { return undefined; } }
function listDirs(dir: string): string[] { try { return fs.readdirSync(dir, { withFileTypes: true }).filter(d => d.isDirectory()).map(d => d.name); } catch { return []; } }
/** Every voice installed: NEUTRINO's, the DiffSinger folder's, the running VOICEVOX's. */
function voices(): VoiceEntry[] {
    const n = neutrinoFolder(), d = voicesFolder();
    return [
        ...(n ? voiceEntriesIn(n, listDirs, fs.existsSync, readTextOr, path.join) : []),
        ...(d ? diffSingerEntriesIn(d, listDirs, fs.existsSync, readTextOr, path.join) : []),
        ...voicevox.voices,
    ];
}

/** The `cantoria` program: the first candidate that exists (the bare name is trusted to the PATH). */
function program(context: vscode.ExtensionContext): string | undefined {
    const candidates = programCandidates(process.platform, process.arch, context.extensionPath, settings().get<string>('program', ''), path.join);
    for (const c of candidates) {
        if (!path.isAbsolute(c)) { return commandExists(c) ? c : undefined; }
        if (fs.existsSync(c)) { return c; }
    }
    return undefined;
}

/** The runtime identifiers this install bundles a program for: the folders under `bin/`. */
function bundledBuilds(context: vscode.ExtensionContext): string[] {
    try { return listDirs(path.join(context.extensionPath, 'bin')); } catch { return []; }
}

function commandExists(cmd: string): boolean {
    const probe = process.platform === 'win32' ? 'where' : 'which';
    try { return cp.spawnSync(probe, [cmd], { stdio: 'ignore', windowsHide: true }).status === 0; } catch { return false; }
}

/** The engine's setup as the settings stand, or the problems that stop it: no program, no engine folder at all, a folder that is not one. */
function setupNow(context: vscode.ExtensionContext): { setup?: EngineSetup; problems: string[]; program?: string } {
    const prog = program(context);
    const n = neutrinoFolder(), d = voicesFolder();
    const problems: string[] = [];
    if (!prog) { problems.push(missingProgramNote(process.platform, process.arch, bundledBuilds(context))); }
    if (!n && !d && voicevox.voices.length === 0 && !voicevoxFolder()) { problems.push('no engine is set up: choose a DiffSinger voices folder (Cantoria: Choose Voices Folder…) or a NEUTRINO folder (Cantoria: Choose NEUTRINO Folder…), or start VOICEVOX'); }
    problems.push(...neutrinoFolderProblems(n, fs.existsSync, path.join, process.platform));
    problems.push(...voicesFolderProblems(d, fs.existsSync));
    if (problems.length > 0) { return { problems, program: prog }; }
    return {
        setup: { program: prog!, neutrinoFolder: n, voicesFolder: d, vocoderFolder: vocoderFolder(), gpu: settings().get<boolean>('gpu', true), threads: settings().get<number>('threads', 4), cacheSizeMB: settings().get<number>('cacheSizeMB', 1000) },
        problems, program: prog,
    };
}

/** The engine for the settings as they stand; a changed setting lets the old one go. */
function engineFor(context: vscode.ExtensionContext): Engine {
    const { setup, problems } = setupNow(context);
    if (!setup) { throw new Error(problems.join('; ')); }
    if (engine && (engine.setup.program !== setup.program || engine.setup.neutrinoFolder !== setup.neutrinoFolder
        || engine.setup.voicesFolder !== setup.voicesFolder || engine.setup.vocoderFolder !== setup.vocoderFolder
        || engine.setup.gpu !== setup.gpu || engine.setup.threads !== setup.threads || engine.setup.cacheSizeMB !== setup.cacheSizeMB)) {
        engine.dispose();
        engine = undefined;
    }
    return engine ??= new Engine(setup, path.join(storage, 'voices'), line => output.appendLine(line));
}

function release(): void {
    engine?.dispose();
    engine = undefined;
}

/** Why `voice` cannot be sung with: an engine this extension has not, an engine not set up, a voice not installed. */
function voiceProblems(voice: Voice): string[] {
    if (!(ENGINES as readonly string[]).includes(voice.engine)) { return [`no engine named ${voice.engine} in ${voice.id} (there is: ${ENGINES.join(', ')})`]; }
    if (voice.engine === 'neutrino') {
        const n = neutrinoFolder();
        if (!n) { return [`${voice.id} needs a NEUTRINO folder (Cantoria: Choose NEUTRINO Folder…)`]; }
        const names = voicesIn(n, listDirs, fs.existsSync, path.join);
        return names.includes(voice.name) ? [] : [`the voice ${voice.name} is not under ${path.join(n, 'model')} (installed: ${names.join(', ') || 'none'})`];
    }
    if (voice.engine === 'voicevox') {
        if (voicevox.voices.some(v => v.id === voice.id)) { return []; }
        return [voicevox.voices.length === 0 ? `VOICEVOX is not answering at ${voicevoxUrl() || VOICEVOX_URL}: start VOICEVOX, or set its folder so that Cantoria can (Cantoria: Choose VOICEVOX Folder…), to sing with ${voice.id}` : `VOICEVOX has no voice ${voice.name} (there is: ${voicevox.voices.map(v => v.name).join(', ')})`];
    }
    if (fs.existsSync(path.join(voice.name, 'dsconfig.yaml'))) { return []; }   // a folder named outright
    const d = voicesFolder();
    if (!d) { return [`${voice.id} needs a DiffSinger voices folder (Cantoria: Choose Voices Folder…)`]; }
    const names = diffSingerEntriesIn(d, listDirs, fs.existsSync, readTextOr, path.join).map(v => v.name);
    return names.includes(voice.name) ? [] : [`the voice ${voice.name} is not under ${d} (installed: ${names.join(', ') || 'none'})`];
}

async function status(context: vscode.ExtensionContext): Promise<CantoriaStatus> {
    await voicevox.ask();
    const { setup, problems, program: prog } = setupNow(context);
    const voice = voiceSetting();
    if (setup) { problems.push(...voiceProblems(voice)); }
    const backend = voice.engine as EngineName;
    return { ready: problems.length === 0, program: prog, backend, neutrinoFolder: neutrinoFolder(), voicesFolder: voicesFolder(), voice: voice.id, voices: voices().map(v => v.id), problems };
}

async function sing(context: vscode.ExtensionContext, score: CantoriaScore, options?: { voice?: string; signal?: AbortSignal; onProgress?: (progress: SingProgress) => void }): Promise<RenderedVoice & { cached: boolean }> {
    const problems = scoreProblems(score);
    if (problems.length > 0) { throw new Error(problems.join('; ')); }
    const asked = (options?.voice ?? '').trim();
    if (needsVoicevox(asked, defaultVoices(), voices().length - voicevox.voices.length, score.language ?? 'ja')) { await voicevox.ensure(); } else { await voicevox.ask(); }
    const voice = asked ? voiceOf(asked) : voiceSetting();
    const wrong = voiceProblems(voice);
    if (wrong.length > 0) { throw new Error(wrong.join('; ')); }
    const language = languageProblem(voices().find(v => v.id === voice.id), score.language);
    if (language) { throw new Error(language); }
    const singing = engineFor(context);
    const { key, rendered, cached } = singing.sing(score, voice.id, voice.id, voicevox.answering, options?.onProgress);
    if (options?.signal && !cached) {
        if (options.signal.aborted) { singing.cancel(key); } else { options.signal.addEventListener('abort', () => singing.cancel(key), { once: true }); }
    }
    const t0 = Date.now();
    const v = await rendered;
    if (!cached) { output.appendLine(`sung ${key.slice(0, 8)} in ${voice.id}: ${v.duration.toFixed(1)} s, ${Date.now() - t0} ms`); }
    return { ...v, cached };
}

/** Who would sing what is asked: the program's answer over the folders and the default voices set
 *  here. VOICEVOX is started as a `sing` of the same would start it, so that the two agree. */
async function plan(context: vscode.ExtensionContext, score: CantoriaScore | { asked: readonly PlanAsk[] }, options?: { voice?: string; gender?: string }): Promise<SingerPlan> {
    const asked = planAsked(score, options);
    const defaults = defaultVoices();
    const others = voices().length - voicevox.voices.length;
    if (asked.some(a => needsVoicevox(a.voice ?? '', defaults, others, a.language))) { await voicevox.ensure(); } else { await voicevox.ask(); }
    const { setup, problems } = setupNow(context);
    if (!setup) { return planOfNoOne(asked, problems.join('; ')); }
    return engineFor(context).plan(asked, defaults, voicevox.answering ?? '');
}

async function chooseFolder(title: string, setting: string, check: (folder: string) => string[], said: (folder: string) => string): Promise<void> {
    // A folder that fails the check is never set: nothing sings from it (setupNow refuses the same
    // folder), so the way on is to choose again, starting where the last choice was, or to leave
    // the setting as it stood. (Until 2026-11-18 the one button was "Use it anyway".)
    let folder: string;
    for (let from: vscode.Uri | undefined; ;) {
        const picked = await vscode.window.showOpenDialog({ canSelectFiles: false, canSelectFolders: true, canSelectMany: false, title, openLabel: 'Use this folder', defaultUri: from });
        if (!picked || picked.length === 0) { return; }
        folder = picked[0].fsPath;
        const problems = check(folder);
        if (problems.length === 0) { break; }
        const again = 'Choose Another Folder…';
        const choice = await vscode.window.showWarningMessage('Cantoria: this folder cannot be used', { modal: true, detail: `${problems[0]}\n\nThe setting is left as it was.` }, again);
        if (choice !== again) { return; }   // Cancel (the modal's own button) or Esc
        from = picked[0];
    }
    await settings().update(setting, folder, vscode.ConfigurationTarget.Global);
    vscode.window.setStatusBarMessage(said(folder), 5000);
}

function chooseNeutrinoFolder(): Promise<void> {
    return chooseFolder('Cantoria: the NEUTRINO folder (holding bin, model and settings)', 'neutrinoFolder',
        folder => neutrinoFolderProblems(folder, fs.existsSync, path.join), folder => `Cantoria: NEUTRINO is ${folder}`);
}

function chooseVoicesFolder(): Promise<void> {
    return chooseFolder('Cantoria: the folder whose subfolders are DiffSinger voices (each with a dsconfig.yaml)', 'voicesFolder',
        folder => diffSingerVoicesIn(folder, listDirs, fs.existsSync, path.join).length > 0 ? [] : [`no DiffSinger voice under ${folder} (a voice is a subfolder with dsconfig.yaml)`],
        folder => `Cantoria: the DiffSinger voices are under ${folder}`);
}

function chooseVoicevoxFolder(): Promise<void> {
    return chooseFolder('Cantoria: the VOICEVOX folder (holding vv-engine)', 'voicevoxFolder',
        folder => voicevoxRunIn(folder, fs.existsSync, path.join) ? [] : [`no VOICEVOX engine under ${folder} (it holds vv-engine\\run.exe)`],
        folder => `Cantoria: VOICEVOX is ${folder}`);
}

function chooseVocoderFolder(): Promise<void> {
    return chooseFolder('Cantoria: the vocoder folder (holding vocoder.yaml and its model)', 'vocoderFolder',
        folder => fs.existsSync(path.join(folder, 'vocoder.yaml')) ? [] : [`no vocoder.yaml in ${folder}`],
        folder => `Cantoria: the vocoder is ${folder}`);
}

/** The steps to install each engine (the owner asked for a way to be led to them, 2026-11-12):
 *  this extension's README, its Setup section. It took the place of "Get NEUTRINO", one engine's site. */
let readme: vscode.Uri | undefined;
const HOW_TO_INSTALL = 'How to Install a Voice';
/** …shown inside VS Code since 2026-11-18, from the copy this extension carries: the steps do not
 *  hang on an address (the owner asked). Its links are the engines' own sites. */
function openInstallGuide(): void { if (readme) { void vscode.commands.executeCommand('markdown.showPreview', readme); } }

/** A default voice from a list: every voice but those that say they are the other gender. */
async function chooseDefaultVoice(context: vscode.ExtensionContext, gender: VoiceGender): Promise<void> {
    await voicevox.ensure();
    const s = await status(context);
    const all = voices();
    if (all.length === 0) {
        const neutrino = 'Choose NEUTRINO Folder…', ds = 'Choose Voices Folder…';
        const choice = await vscode.window.showWarningMessage(`Cantoria: no voice to list — ${s.problems[0] ?? 'the folders hold no voice'}`, DOWNLOAD_VOICE, HOW_TO_INSTALL, neutrino, ds);
        if (choice === DOWNLOAD_VOICE) { await downloadVoice(context); }
        else if (choice === HOW_TO_INSTALL) { openInstallGuide(); }
        else if (choice === neutrino) { await chooseNeutrinoFolder(); }
        else if (choice === ds) { await chooseVoicesFolder(); }
        return;
    }
    const current = defaultVoiceFor(all, defaultVoices(), gender).voice;
    const pick = await vscode.window.showQuickPick(
        all.filter(v => !v.gender || v.gender === gender).map(v => ({
            label: v.id,
            description: [v.label !== v.name ? v.label : '', v.gender ?? '', v.language ?? '', v.id === current ? '(current)' : ''].filter(Boolean).join(' · '),
        })),
        { title: `Cantoria: Default ${gender === 'male' ? 'Male' : 'Female'} Voice`, placeHolder: `The voice that sings a ${gender} part when the score names none` });
    if (!pick) { return; }
    await settings().update(gender === 'male' ? 'defaultMaleVoice' : 'defaultFemaleVoice', pick.label, vscode.ConfigurationTarget.Global);
    vscode.window.setStatusBarMessage(`Cantoria: the default ${gender} voice is ${pick.label}`, 4000);
}

const FIND_ENGINES = 'Find Engines on This Computer';
const DOWNLOAD_VOICE = 'Download a Voice…';

/**
 * Offers a voice whose terms let a program fetch it for the user (roadmap F; the program's
 * `offers`), shows those terms, and has the program fetch it into the voices folder — the one set,
 * else ~/Cantoria/voices, which is then set. The program checks each file against the SHA-256 it
 * was published with and leaves nothing half there; stopped, what it was fetching is removed.
 */
async function downloadVoice(context: vscode.ExtensionContext): Promise<void> {
    const prog = program(context);
    if (!prog) { vscode.window.showErrorMessage(`Cantoria: ${missingProgramNote(process.platform, process.arch, bundledBuilds(context))}`); return; }
    let offers: VoiceOffer[];
    try { offers = JSON.parse(cp.execFileSync(prog, ['offers', '--json'], { encoding: 'utf8', windowsHide: true })); }
    catch (e) { vscode.window.showErrorMessage(`Cantoria: could not list the voices to download: ${(e as Error).message}`); return; }
    const installed = new Set(voices().map(v => v.id));
    // Always a list, even of one offer: its last line says that the voices offered are not the only ones (the owner asked, 2026-11-26).
    const items: (vscode.QuickPickItem & { offer?: VoiceOffer })[] = [
        ...offers.map(o => ({ label: o.title, description: `${o.languages.join(', ')} · ${Math.round(o.size / 1048576)} MB${installed.has(o.voice) ? ' · installed' : ''}`, detail: 'Fetched for you from its author, once you agree to its terms', offer: o })),
        { label: '', kind: vscode.QuickPickItemKind.Separator },
        { label: `$(folder-opened) ${OWN_VOICE}`, description: 'any DiffSinger voice in the OpenUtau format', detail: 'One you download from its author yourself: unpacked into the voices folder, it sings with nothing to set' },
    ];
    const pick = await vscode.window.showQuickPick(items, { title: 'Cantoria: Download a Voice', placeHolder: 'A voice Cantoria fetches for you, or one of your own' });
    if (!pick) { return; }
    if (!pick.offer) { await addOwnVoice(); return; }
    const offer = pick.offer;
    const folder = voicesFolder() || defaultVoicesFolder(os.homedir(), path.join);
    const agree = 'Agree and Download', read = 'Read the Terms';
    for (;;) {
        const choice = await vscode.window.showInformationMessage(`Download ${offer.title}?`, { modal: true, detail: offerDetail(offer, folder) }, agree, read);
        if (choice === read) { await vscode.env.openExternal(vscode.Uri.parse(offer.termsUrl)); continue; }
        if (choice !== agree) { return; }
        break;
    }
    const chosen = offer;
    const done = await vscode.window.withProgress({ location: vscode.ProgressLocation.Notification, title: `Cantoria: downloading ${chosen.title}`, cancellable: true },
        (progress, token) => new Promise<{ installed: string } | undefined>((resolve, reject) => {
            const child = cp.spawn(prog, ['fetch', chosen.id, '--voices-dir', folder, '--json'], { windowsHide: true });
            const received = new Map<string, number>();
            let shown = 0, current = '', errors = '', answer: { installed: string } | undefined, buffered = '';
            child.stdout.setEncoding('utf8');
            child.stdout.on('data', (chunk: string) => {
                buffered += chunk;
                let nl: number;
                while ((nl = buffered.indexOf('\n')) >= 0) {
                    const line = buffered.slice(0, nl).trim();
                    buffered = buffered.slice(nl + 1);
                    const said = fetchLine(line, received);
                    if (said && 'received' in said) {
                        current = [...received.keys()].at(-1) ?? current;
                        const share = Math.min(100, 100 * said.received / chosen.size);
                        progress.report({ increment: share - shown, message: `${Math.round(said.received / 1048576)} of ${Math.round(chosen.size / 1048576)} MB` });
                        shown = share;
                    } else if (said) { answer = said; }
                }
            });
            child.stderr.setEncoding('utf8');
            child.stderr.on('data', (chunk: string) => { errors += chunk; });
            token.onCancellationRequested(() => child.kill());
            child.on('error', reject);
            child.on('close', code => {
                if (token.isCancellationRequested) {
                    // Killed: what it was fetching is not left behind (the program removes it itself only when it ends on its own).
                    for (const leftover of [`.${current}.download`, `.${current}.unpacking`]) {
                        if (current) { try { fs.rmSync(path.join(folder, leftover), { recursive: true, force: true }); } catch { /* the next fetch removes it */ } }
                    }
                    resolve(undefined);
                } else if (code === 0 && answer) { resolve(answer); }
                else { reject(new Error(errors.trim().split('\n').at(-1)?.replace(/^cantoria: /, '') || `the program ended with ${code}`)); }
            });
        })).then(r => r, (e: Error) => { vscode.window.showErrorMessage(`Cantoria: ${chosen.title} was not downloaded: ${e.message}`); return null; });
    if (!done) {
        if (done === undefined) { vscode.window.setStatusBarMessage(`Cantoria: the download of ${chosen.title} was stopped`, 4000); }
        return;
    }
    output.appendLine(`download: ${done.installed} into ${folder}`);
    if (!voicesFolder()) { await settings().update('voicesFolder', folder, vscode.ConfigurationTarget.Global); }
    release();
    vscode.window.showInformationMessage(`Cantoria: ${done.installed} is installed in ${folder} and sings. Where you publish a song made with it, follow its terms: say it is synthesised with AI, and credit the voice and DiffSinger. Other voices you choose go beside it.`, read, OWN_VOICE)
        .then(c => {
            if (c === read) { void vscode.env.openExternal(vscode.Uri.parse(chosen.termsUrl)); }
            else if (c === OWN_VOICE) { void addOwnVoice(); }
        });
}

const OWN_VOICE = 'Add a Voice of Your Own…';

/**
 * How to add a voice the user chooses: what Download a Voice offers is only what may be fetched for
 * them, and any DiffSinger voice in the OpenUtau format sings once it is unpacked into the voices
 * folder (the owner asked that this be plain, 2026-11-26). Opens that folder — the one set, else
 * ~/Cantoria/voices, made and set as Download a Voice would — or the steps in this README.
 */
async function addOwnVoice(): Promise<void> {
    const folder = voicesFolder() || defaultVoicesFolder(os.homedir(), path.join);
    const open = 'Open the Voices Folder';
    const detail = 'Download a DiffSinger voice in the OpenUtau format from its author, under the author\'s terms, and unpack it '
        + `so that it is a folder of its own holding a dsconfig.yaml, in the voices folder:\n\n${folder}\n\n`
        + 'It is listed as diffsinger/<its folder> at once, with nothing to set. A voice with no vocoder of its own sings with the one it names, '
        + 'unpacked beside it (How to Install a Voice says where to get one). '
        + 'NEUTRINO and VOICEVOX voices are installed in those programs: Cantoria finds them there.';
    const choice = await vscode.window.showInformationMessage('Add a voice of your own', { modal: true, detail }, open, HOW_TO_INSTALL);
    if (choice === open) {
        try { fs.mkdirSync(folder, { recursive: true }); }
        catch (e) { vscode.window.showErrorMessage(`Cantoria: could not make ${folder}: ${(e as Error).message}`); return; }
        if (!voicesFolder()) { await settings().update('voicesFolder', folder, vscode.ConfigurationTarget.Global); }
        await vscode.env.openExternal(vscode.Uri.file(folder));
    } else if (choice === HOW_TO_INSTALL) { openInstallGuide(); }
}

/**
 * Looks where the engines' own programs put them (core's findEngines) and offers what stands
 * there for the settings that are empty or name something else: VOICEVOX as its installer left
 * it, OpenUtau's DiffSinger voices and a vocoder among its dependencies. Nothing is set unasked.
 */
async function findEnginesHere(): Promise<void> {
    const found = findEngines(process.platform, os.homedir(), process.env, listDirs, fs.existsSync, path.join);
    const names: Record<keyof typeof found, string> = { voicevoxFolder: 'VOICEVOX', voicesFolder: 'DiffSinger voices (OpenUtau\'s Singers)', vocoderFolder: 'DiffSinger vocoder (among OpenUtau\'s Dependencies)' };
    const fresh = (Object.keys(found) as (keyof typeof found)[]).filter(key => settings().get<string>(key, '').trim() !== found[key]);
    output.appendLine(`find engines: ${(Object.keys(found) as (keyof typeof found)[]).map(key => `${key} ${found[key]}`).join('; ') || 'none where their programs put them'}`);
    if (fresh.length === 0) {
        const all = Object.keys(found).length;
        const choice = await vscode.window.showInformationMessage(all > 0
            ? 'Cantoria: the engines found on this computer are the ones already set.'
            : 'Cantoria: no engine stands where its own program puts it. VOICEVOX is looked for as its installer leaves it, DiffSinger voices in OpenUtau\'s Singers folder; NEUTRINO, and anything unpacked elsewhere, is chosen by its folder.', HOW_TO_INSTALL);
        if (choice === HOW_TO_INSTALL) { openInstallGuide(); }
        return;
    }
    const picks = await vscode.window.showQuickPick(
        fresh.map(key => ({ label: names[key], description: found[key], picked: true, key })),
        { canPickMany: true, title: 'Cantoria: Engines Found on This Computer', placeHolder: 'The ones to sing with' });
    for (const pick of picks ?? []) {
        await settings().update(pick.key, found[pick.key], vscode.ConfigurationTarget.Global);
    }
    if (picks?.length) { vscode.window.setStatusBarMessage(`Cantoria: ${picks.map(p => p.label).join(', ')} set`, 5000); }
}

async function checkSetup(context: vscode.ExtensionContext): Promise<void> {
    const s = await status(context);
    output.appendLine(`setup: program ${s.program ?? 'none'}; NEUTRINO ${s.neutrinoFolder || 'not set'}; DiffSinger voices ${s.voicesFolder || 'not set'}; voice ${s.voice}; installed ${s.voices.join(', ') || 'none'}; ${s.ready ? 'ready' : s.problems.join('; ')}`);
    if (s.ready) {
        const where = s.backend === 'neutrino' ? `through NEUTRINO at ${s.neutrinoFolder}` : s.backend === 'voicevox' ? 'through the running VOICEVOX' : `a DiffSinger voice under ${s.voicesFolder}`;
        vscode.window.showInformationMessage(`Cantoria is ready: ${s.voice}, ${where}.`);
        return;
    }
    const neutrino = 'Choose NEUTRINO Folder…', ds = 'Choose Voices Folder…', log = 'Show Output';
    const choice = await vscode.window.showWarningMessage(`Cantoria cannot sing yet: ${s.problems[0]}`, DOWNLOAD_VOICE, HOW_TO_INSTALL, FIND_ENGINES, neutrino, ds, log);
    if (choice === DOWNLOAD_VOICE) { await downloadVoice(context); }
    else if (choice === FIND_ENGINES) { await findEnginesHere(); }
    else if (choice === neutrino) { await chooseNeutrinoFolder(); }
    else if (choice === ds) { await chooseVoicesFolder(); }
    else if (choice === HOW_TO_INSTALL) { openInstallGuide(); }
    else if (choice === log) { output.show(); }
}

export function activate(context: vscode.ExtensionContext): CantoriaApi {
    output = vscode.window.createOutputChannel('Cantoria');
    storage = context.globalStorageUri.fsPath;
    readme = vscode.Uri.joinPath(context.extensionUri, 'README.md');
    context.subscriptions.push(
        output,
        vscode.commands.registerCommand('cantoria.chooseNeutrinoFolder', () => chooseNeutrinoFolder()),
        vscode.commands.registerCommand('cantoria.chooseVoicesFolder', () => chooseVoicesFolder()),
        vscode.commands.registerCommand('cantoria.chooseVocoderFolder', () => chooseVocoderFolder()),
        vscode.commands.registerCommand('cantoria.chooseVoicevoxFolder', () => chooseVoicevoxFolder()),
        vscode.commands.registerCommand('cantoria.chooseFemaleVoice', () => chooseDefaultVoice(context, 'female')),
        vscode.commands.registerCommand('cantoria.chooseMaleVoice', () => chooseDefaultVoice(context, 'male')),
        vscode.commands.registerCommand('cantoria.checkSetup', () => checkSetup(context)),
        vscode.commands.registerCommand('cantoria.findEngines', () => findEnginesHere()),
        vscode.commands.registerCommand('cantoria.downloadVoice', () => downloadVoice(context)),
        vscode.commands.registerCommand('cantoria.addOwnVoice', () => addOwnVoice()),
        vscode.commands.registerCommand('cantoria.forgetVoices', () => {
            release();      // the engine holds phrases in memory too
            const n = new Engine({ program: '', neutrinoFolder: '', voicesFolder: '', vocoderFolder: '', gpu: true, threads: 1, cacheSizeMB: 0 }, path.join(storage, 'voices'), () => undefined).forget();
            vscode.window.setStatusBarMessage(`Cantoria: ${n} rendered voice${n === 1 ? '' : 's'} forgotten`, 4000);
        }),
        // A changed engine setting lets the engine go; the next sing starts one for the new settings.
        vscode.workspace.onDidChangeConfiguration(e => {
            if (['neutrinoFolder', 'voicesFolder', 'vocoderFolder', 'program', 'gpu', 'threads', 'cacheSizeMB'].some(s => e.affectsConfiguration('cantoria.' + s))) {
                release();
            }
            if (['voicevoxUrl', 'voicevoxFolder', 'gpu'].some(s => e.affectsConfiguration('cantoria.' + s))) { voicevox.changed(); }
        }),
        { dispose: release },
        { dispose: () => voicevox.dispose() },
    );
    const version = String((context.extension.packageJSON as { version?: string }).version ?? '0');
    return Object.freeze<CantoriaApi>({
        version,
        apiVersion: 1,
        status: () => status(context),
        voices: async () => { await voicevox.ensure(); return voices(); },
        sing: (score, options) => sing(context, score, options),
        parameters: async (voice) => {
            const asked = (voice ?? '').trim();
            const singer = asked ? voiceOf(asked) : voiceSetting();
            const wrong = voiceProblems(singer);
            if (wrong.length > 0) { throw new Error(wrong.join('; ')); }
            return engineFor(context).parameters(singer.id);
        },
        release,
        resolveSinger: async (singer, options) => {
            if (needsVoicevox(singer, defaultVoices(), voices().length - voicevox.voices.length, options?.language)) { await voicevox.ensure(); } else { await voicevox.ask(); }
            return resolveSinger(singer, voices(), defaultVoices(), options);
        },
        plan: (score, options) => plan(context, score, options),
    });
}

export function deactivate(): void {
    release();
    voicevox.dispose();
}

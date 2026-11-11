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
import * as path from 'path';
import * as vscode from 'vscode';
import {
    SingParameter, SingProgress,
    CantoriaScore, DefaultVoices, ENGINES, EngineName, VOICEVOX_URL, VoiceEntry, VoiceGender, defaultVoiceFor, diffSingerEntriesIn, diffSingerVoicesIn, languageProblem, missingProgramNote, needsVoicevox, neutrinoFolderProblems, voicevoxRunIn, programCandidates,
    resolveSinger, scoreProblems, splitVoiceId, voiceEntriesIn, voiceId, voicePieces, voicesFolderProblems, voicesIn, 
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
     *  can change, what it does and its range. `voice` as for `sing`; several joined by `+` take
     *  what any of them does. None for a voice whose engine has no such setting. What a score
     *  sets and the voice does not take is not sung, and said in `sing`'s `notes`. */
    parameters(voice?: string): Promise<SingParameter[]>;
    /** Lets the engine go (NEUTRINO's server and the DiffSinger sessions with it); the next `sing` starts it again. */
    release(): void;
    /** The voice a score's `singer` string comes to here: the voice it names when installed, else the
     *  default voice of the gender it says or of `options.gender`; and a sentence when it is not the one asked for. */
    resolveSinger(singer: string, options?: { language?: string; gender?: string }): Promise<{ voice: string; note?: string }>;
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
    if (!n && !d && voicevox.voices.length === 0 && !voicevoxFolder()) { problems.push('no engine is set up: choose a NEUTRINO folder (Cantoria: Choose NEUTRINO Folder…) or a DiffSinger voices folder (Cantoria: Choose Voices Folder…), or start VOICEVOX'); }
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
    const singers = asked && voicePieces(asked).length > 0 ? voicePieces(asked).map(voiceOf) : [voiceSetting()];
    const wrong = singers.flatMap(voiceProblems);
    if (wrong.length > 0) { throw new Error(wrong.join('; ')); }
    const language = singers.map(s => languageProblem(voices().find(v => v.id === s.id), score.language)).find(Boolean);
    if (language) { throw new Error(language); }
    const voice = { id: singers.map(s => s.id).join('+') };
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

async function chooseFolder(title: string, setting: string, check: (folder: string) => string[], said: (folder: string) => string): Promise<void> {
    const picked = await vscode.window.showOpenDialog({ canSelectFiles: false, canSelectFolders: true, canSelectMany: false, title, openLabel: 'Use this folder' });
    if (!picked || picked.length === 0) { return; }
    const folder = picked[0].fsPath;
    const problems = check(folder);
    if (problems.length > 0) {
        const anyway = 'Use it anyway';
        const choice = await vscode.window.showWarningMessage(`Cantoria: ${problems[0]}`, anyway);
        if (choice !== anyway) { return; }
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

/** A default voice from a list: every voice but those that say they are the other gender. */
async function chooseDefaultVoice(context: vscode.ExtensionContext, gender: VoiceGender): Promise<void> {
    await voicevox.ensure();
    const s = await status(context);
    const all = voices();
    if (all.length === 0) {
        const neutrino = 'Choose NEUTRINO Folder…', ds = 'Choose Voices Folder…';
        const choice = await vscode.window.showWarningMessage(`Cantoria: no voice to list — ${s.problems[0] ?? 'the folders hold no voice'}`, neutrino, ds);
        if (choice === neutrino) { await chooseNeutrinoFolder(); }
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

async function checkSetup(context: vscode.ExtensionContext): Promise<void> {
    const s = await status(context);
    output.appendLine(`setup: program ${s.program ?? 'none'}; NEUTRINO ${s.neutrinoFolder || 'not set'}; DiffSinger voices ${s.voicesFolder || 'not set'}; voice ${s.voice}; installed ${s.voices.join(', ') || 'none'}; ${s.ready ? 'ready' : s.problems.join('; ')}`);
    if (s.ready) {
        const where = s.backend === 'neutrino' ? `through NEUTRINO at ${s.neutrinoFolder}` : s.backend === 'voicevox' ? 'through the running VOICEVOX' : `a DiffSinger voice under ${s.voicesFolder}`;
        vscode.window.showInformationMessage(`Cantoria is ready: ${s.voice}, ${where}.`);
        return;
    }
    const neutrino = 'Choose NEUTRINO Folder…', ds = 'Choose Voices Folder…', site = 'Get NEUTRINO', log = 'Show Output';
    const choice = await vscode.window.showWarningMessage(`Cantoria cannot sing yet: ${s.problems[0]}`, neutrino, ds, site, log);
    if (choice === neutrino) { await chooseNeutrinoFolder(); }
    else if (choice === ds) { await chooseVoicesFolder(); }
    else if (choice === site) { void vscode.env.openExternal(vscode.Uri.parse('https://studio-neutrino.com/')); }
    else if (choice === log) { output.show(); }
}

export function activate(context: vscode.ExtensionContext): CantoriaApi {
    output = vscode.window.createOutputChannel('Cantoria');
    storage = context.globalStorageUri.fsPath;
    context.subscriptions.push(
        output,
        vscode.commands.registerCommand('cantoria.chooseNeutrinoFolder', () => chooseNeutrinoFolder()),
        vscode.commands.registerCommand('cantoria.chooseVoicesFolder', () => chooseVoicesFolder()),
        vscode.commands.registerCommand('cantoria.chooseVocoderFolder', () => chooseVocoderFolder()),
        vscode.commands.registerCommand('cantoria.chooseVoicevoxFolder', () => chooseVoicevoxFolder()),
        vscode.commands.registerCommand('cantoria.chooseFemaleVoice', () => chooseDefaultVoice(context, 'female')),
        vscode.commands.registerCommand('cantoria.chooseMaleVoice', () => chooseDefaultVoice(context, 'male')),
        vscode.commands.registerCommand('cantoria.checkSetup', () => checkSetup(context)),
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
            const singers = asked && voicePieces(asked).length > 0 ? voicePieces(asked).map(voiceOf) : [voiceSetting()];
            const wrong = singers.flatMap(voiceProblems);
            if (wrong.length > 0) { throw new Error(wrong.join('; ')); }
            return engineFor(context).parameters(singers.map(s => s.id).join('+'));
        },
        release,
        resolveSinger: async (singer, options) => {
            if (needsVoicevox(singer, defaultVoices(), voices().length - voicevox.voices.length, options?.language)) { await voicevox.ensure(); } else { await voicevox.ask(); }
            return resolveSinger(singer, voices(), defaultVoices(), options);
        },
    });
}

export function deactivate(): void {
    release();
    voicevox.dispose();
}

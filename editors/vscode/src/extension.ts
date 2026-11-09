// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

// Cantilena as a VS Code extension: carries the `cantilena` program, holds the settings (where
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
    CantilenaScore, ENGINES, VoiceEntry, castNames, withCast, diffSingerEntriesIn, diffSingerVoicesIn, firstOfGender, languageProblem, missingProgramNote, neutrinoFolderProblems, programCandidates,
    resolveSinger, scoreProblems, splitVoiceId, voiceEntriesIn, voiceId, voicesFolderProblems, voicesIn,
} from './core';
import { Engine, EngineSetup, RenderedVoice } from './engine';

/** What `status()` answers: whether a `sing` can succeed now, and why not. */
export interface CantilenaStatus {
    ready: boolean;
    program: string | undefined;
    /** The engine of the voice the setting names. */
    backend: 'neutrino' | 'diffsinger';
    neutrinoFolder: string;
    /** The DiffSinger voices folder. */
    voicesFolder: string;
    /** The voice the setting names, as `engine/name`: `neutrino/MERROW`, `diffsinger/TIGER`. */
    voice: string;
    /** Every voice installed, as `engine/name`; `voices()` has each as an entry. */
    voices: string[];
    problems: string[];
}

/** The API another extension gets from `vscode.extensions.getExtension('yotsuda.cantilena').activate()`. */
export interface CantilenaApi {
    /** This extension's version. */
    readonly version: string;
    /** The API's shape; raised when a call changes incompatibly. */
    readonly apiVersion: 1;
    status(): Promise<CantilenaStatus>;
    /** The voices installed, each as `{ id: 'neutrino/MERROW', engine, name, label, language? }`. */
    voices(): Promise<VoiceEntry[]>;
    /** Sings `score` (the README's JSON: notes in seconds with syllables) in `options.voice` — an
     *  `engine/name` from `voices()`, or a bare NEUTRINO voice name — else the setting's voice. The
     *  WAV is kept under the extension's storage until "Forget Rendered Voices"; the same score in
     *  the same voice is sung once. Throws with a sentence when it cannot: no program, no engine set
     *  up, a voice or an engine that is not there, a score the engine refused. */
    sing(score: CantilenaScore, options?: { voice?: string }): Promise<RenderedVoice & { cached: boolean }>;
    /** Lets the engine go (NEUTRINO's server and the DiffSinger sessions with it); the next `sing` starts it again. */
    release(): void;
    /** The voice a score's `singer` string comes to here (`cantilena.voices`, then the installed
     *  voices, for `male` or `female` the first such voice that sings `language`, then the setting's
     *  voice), and a sentence when it is not the one asked for. */
    resolveSinger(singer: string, options?: { language?: string }): Promise<{ voice: string; note?: string }>;
    /** `cantilena.voices`: role or voice → voice. */
    cast(): Promise<Record<string, string>>;
}

let output: vscode.OutputChannel;
let engine: Engine | undefined;
let storage: string;

function settings(): vscode.WorkspaceConfiguration { return vscode.workspace.getConfiguration('cantilena'); }
function neutrinoFolder(): string { return settings().get<string>('neutrinoFolder', '').trim(); }
function voicesFolder(): string { return settings().get<string>('voicesFolder', '').trim(); }
function vocoderFolder(): string { return settings().get<string>('vocoderFolder', '').trim(); }

/** A voice as its engine, name and `engine/name`. */
interface Voice { engine: string; name: string; id: string }
function voiceOf(text: string | undefined): Voice {
    const v = splitVoiceId(text);
    return { ...v, id: voiceId(v.engine, v.name) };
}
/** The setting's voice (`neutrino/MERROW`; a bare name is NEUTRINO's). */
function voiceSetting(): Voice { return voiceOf(settings().get<string>('voice', '')); }
/** `cantilena.voices`: the cast, role or voice → voice. */
function castSetting(): Record<string, string> { return settings().get<Record<string, string>>('voices', {}); }

function readTextOr(p: string): string | undefined { try { return fs.readFileSync(p, 'utf8'); } catch { return undefined; } }
function listDirs(dir: string): string[] { try { return fs.readdirSync(dir, { withFileTypes: true }).filter(d => d.isDirectory()).map(d => d.name); } catch { return []; } }
/** Every voice installed, NEUTRINO's then the DiffSinger folder's. */
function voices(): VoiceEntry[] {
    const n = neutrinoFolder(), d = voicesFolder();
    return [
        ...(n ? voiceEntriesIn(n, listDirs, fs.existsSync, readTextOr, path.join) : []),
        ...(d ? diffSingerEntriesIn(d, listDirs, fs.existsSync, readTextOr, path.join) : []),
    ];
}

/** The `cantilena` program: the first candidate that exists (the bare name is trusted to the PATH). */
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
    if (!n && !d) { problems.push('no engine is set up: choose a NEUTRINO folder (Cantilena: Choose NEUTRINO Folder…) or a DiffSinger voices folder (Cantilena: Choose Voices Folder…)'); }
    problems.push(...neutrinoFolderProblems(n, fs.existsSync, path.join, process.platform));
    problems.push(...voicesFolderProblems(d, fs.existsSync));
    if (problems.length > 0) { return { problems, program: prog }; }
    return {
        setup: { program: prog!, neutrinoFolder: n, voicesFolder: d, vocoderFolder: vocoderFolder(), gpu: settings().get<boolean>('gpu', true), threads: settings().get<number>('threads', 4) },
        problems, program: prog,
    };
}

/** The engine for the settings as they stand; a changed setting lets the old one go. */
function engineFor(context: vscode.ExtensionContext): Engine {
    const { setup, problems } = setupNow(context);
    if (!setup) { throw new Error(problems.join('; ')); }
    if (engine && (engine.setup.program !== setup.program || engine.setup.neutrinoFolder !== setup.neutrinoFolder
        || engine.setup.voicesFolder !== setup.voicesFolder || engine.setup.vocoderFolder !== setup.vocoderFolder
        || engine.setup.gpu !== setup.gpu || engine.setup.threads !== setup.threads)) {
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
        if (!n) { return [`${voice.id} needs a NEUTRINO folder (Cantilena: Choose NEUTRINO Folder…)`]; }
        const names = voicesIn(n, listDirs, fs.existsSync, path.join);
        return names.includes(voice.name) ? [] : [`the voice ${voice.name} is not under ${path.join(n, 'model')} (installed: ${names.join(', ') || 'none'})`];
    }
    if (fs.existsSync(path.join(voice.name, 'dsconfig.yaml'))) { return []; }   // a folder named outright
    const d = voicesFolder();
    if (!d) { return [`${voice.id} needs a DiffSinger voices folder (Cantilena: Choose Voices Folder…)`]; }
    const names = diffSingerVoicesIn(d, listDirs, fs.existsSync, path.join);
    return names.includes(voice.name) ? [] : [`the voice ${voice.name} is not under ${d} (installed: ${names.join(', ') || 'none'})`];
}

async function status(context: vscode.ExtensionContext): Promise<CantilenaStatus> {
    const { setup, problems, program: prog } = setupNow(context);
    const voice = voiceSetting();
    if (setup) { problems.push(...voiceProblems(voice)); }
    const backend = voice.engine === 'diffsinger' ? 'diffsinger' : 'neutrino';
    return { ready: problems.length === 0, program: prog, backend, neutrinoFolder: neutrinoFolder(), voicesFolder: voicesFolder(), voice: voice.id, voices: voices().map(v => v.id), problems };
}

async function sing(context: vscode.ExtensionContext, score: CantilenaScore, options?: { voice?: string }): Promise<RenderedVoice & { cached: boolean }> {
    const problems = scoreProblems(score);
    if (problems.length > 0) { throw new Error(problems.join('; ')); }
    const asked = (options?.voice ?? '').trim();
    const voice = asked ? voiceOf(asked) : voiceSetting();
    const wrong = voiceProblems(voice);
    if (wrong.length > 0) { throw new Error(wrong.join('; ')); }
    const language = languageProblem(voices().find(v => v.id === voice.id), score.language);
    if (language) { throw new Error(language); }
    const { key, rendered, cached } = engineFor(context).sing(score, voice.id);
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
        const choice = await vscode.window.showWarningMessage(`Cantilena: ${problems[0]}`, anyway);
        if (choice !== anyway) { return; }
    }
    await settings().update(setting, folder, vscode.ConfigurationTarget.Global);
    vscode.window.setStatusBarMessage(said(folder), 5000);
}

function chooseNeutrinoFolder(): Promise<void> {
    return chooseFolder('Cantilena: the NEUTRINO folder (holding bin, model and settings)', 'neutrinoFolder',
        folder => neutrinoFolderProblems(folder, fs.existsSync, path.join), folder => `Cantilena: NEUTRINO is ${folder}`);
}

function chooseVoicesFolder(): Promise<void> {
    return chooseFolder('Cantilena: the folder whose subfolders are DiffSinger voices (each with a dsconfig.yaml)', 'voicesFolder',
        folder => diffSingerVoicesIn(folder, listDirs, fs.existsSync, path.join).length > 0 ? [] : [`no DiffSinger voice under ${folder} (a voice is a subfolder with dsconfig.yaml)`],
        folder => `Cantilena: the DiffSinger voices are under ${folder}`);
}

function chooseVocoderFolder(): Promise<void> {
    return chooseFolder('Cantilena: the vocoder folder (holding vocoder.yaml and its model)', 'vocoderFolder',
        folder => fs.existsSync(path.join(folder, 'vocoder.yaml')) ? [] : [`no vocoder.yaml in ${folder}`],
        folder => `Cantilena: the vocoder is ${folder}`);
}

async function chooseVoice(context: vscode.ExtensionContext): Promise<void> {
    const s = await status(context);
    const all = voices();
    if (all.length === 0) {
        const neutrino = 'Choose NEUTRINO Folder…', ds = 'Choose Voices Folder…';
        const choice = await vscode.window.showWarningMessage(`Cantilena: no voice to list — ${s.problems[0] ?? 'the folders hold no voice'}`, neutrino, ds);
        if (choice === neutrino) { await chooseNeutrinoFolder(); }
        else if (choice === ds) { await chooseVoicesFolder(); }
        return;
    }
    const pick = await vscode.window.showQuickPick(
        all.map(v => ({
            label: v.id,
            description: [v.label !== v.name ? v.label : '', v.language ?? '', v.id === s.voice ? '(current)' : ''].filter(Boolean).join(' · '),
        })),
        { title: 'Cantilena: Choose Voice', placeHolder: 'The voice the lyrics are sung in (engine/name)' });
    if (!pick) { return; }
    await settings().update('voice', pick.label, vscode.ConfigurationTarget.Global);
    vscode.window.setStatusBarMessage(`Cantilena: the voice is ${pick.label}`, 4000);
}

/** `cantilena.voices` from two lists: which name a score writes, and who sings it here. */
async function castVoice(): Promise<void> {
    const all = voices();
    if (all.length === 0) {
        const neutrino = 'Choose NEUTRINO Folder…', ds = 'Choose Voices Folder…';
        const choice = await vscode.window.showWarningMessage('Cantilena: no voice is installed to cast yet', neutrino, ds);
        if (choice === neutrino) { await chooseNeutrinoFolder(); }
        else if (choice === ds) { await chooseVoicesFolder(); }
        return;
    }
    const cast = castSetting();
    const typed = 'Another name…';
    const named = await vscode.window.showQuickPick(
        [...castNames(cast).map(name => ({ label: name, description: cast[name] ?? '' })), { label: typed, description: 'A role of your own, or a voice to replace' }],
        { title: 'Cantilena: Cast a Singer', placeHolder: 'The name a score writes after singer' });
    if (!named) { return; }
    const name = named.label === typed
        ? (await vscode.window.showInputBox({ title: 'Cantilena: Cast a Singer', prompt: 'The name a score writes after singer' }))?.trim()
        : named.label;
    if (!name) { return; }
    const remove = 'Sing it as the score says';
    const items = [
        ...all.map(v => ({ label: v.id, description: [v.label !== v.name ? v.label : '', v.gender ?? '', v.language ?? '', v.id === cast[name] ? '(current)' : ''].filter(Boolean).join(' · ') })),
        ...(cast[name] ? [{ label: remove, description: 'Take this name out of the cast' }] : []),
    ];
    const pick = await vscode.window.showQuickPick(items, { title: `Cantilena: who sings "${name}" on this machine` });
    if (!pick) { return; }
    const voice = pick.label === remove ? undefined : pick.label;
    await settings().update('voices', withCast(cast, name, voice), vscode.ConfigurationTarget.Global);
    vscode.window.setStatusBarMessage(voice ? `Cantilena: "${name}" is sung by ${voice}` : `Cantilena: "${name}" is out of the cast`, 4000);
}

async function checkSetup(context: vscode.ExtensionContext): Promise<void> {
    const s = await status(context);
    output.appendLine(`setup: program ${s.program ?? 'none'}; NEUTRINO ${s.neutrinoFolder || 'not set'}; DiffSinger voices ${s.voicesFolder || 'not set'}; voice ${s.voice}; installed ${s.voices.join(', ') || 'none'}; ${s.ready ? 'ready' : s.problems.join('; ')}`);
    if (s.ready) {
        const where = s.backend === 'neutrino' ? `through NEUTRINO at ${s.neutrinoFolder}` : `a DiffSinger voice under ${s.voicesFolder}`;
        vscode.window.showInformationMessage(`Cantilena is ready: ${s.voice}, ${where}.`);
        return;
    }
    const neutrino = 'Choose NEUTRINO Folder…', ds = 'Choose Voices Folder…', site = 'Get NEUTRINO', log = 'Show Output';
    const choice = await vscode.window.showWarningMessage(`Cantilena cannot sing yet: ${s.problems[0]}`, neutrino, ds, site, log);
    if (choice === neutrino) { await chooseNeutrinoFolder(); }
    else if (choice === ds) { await chooseVoicesFolder(); }
    else if (choice === site) { void vscode.env.openExternal(vscode.Uri.parse('https://studio-neutrino.com/')); }
    else if (choice === log) { output.show(); }
}

export function activate(context: vscode.ExtensionContext): CantilenaApi {
    output = vscode.window.createOutputChannel('Cantilena');
    storage = context.globalStorageUri.fsPath;
    context.subscriptions.push(
        output,
        vscode.commands.registerCommand('cantilena.chooseNeutrinoFolder', () => chooseNeutrinoFolder()),
        vscode.commands.registerCommand('cantilena.chooseVoicesFolder', () => chooseVoicesFolder()),
        vscode.commands.registerCommand('cantilena.chooseVocoderFolder', () => chooseVocoderFolder()),
        vscode.commands.registerCommand('cantilena.castVoice', () => castVoice()),
        vscode.commands.registerCommand('cantilena.chooseVoice', () => chooseVoice(context)),
        vscode.commands.registerCommand('cantilena.checkSetup', () => checkSetup(context)),
        vscode.commands.registerCommand('cantilena.forgetVoices', () => {
            const e = engine ?? new Engine({ program: '', neutrinoFolder: '', voicesFolder: '', vocoderFolder: '', gpu: true, threads: 1 }, path.join(storage, 'voices'), () => undefined);
            const n = e.forget();
            vscode.window.setStatusBarMessage(`Cantilena: ${n} rendered voice${n === 1 ? '' : 's'} forgotten`, 4000);
        }),
        // A changed engine setting lets the engine go; the next sing starts one for the new settings.
        vscode.workspace.onDidChangeConfiguration(e => {
            if (['neutrinoFolder', 'voicesFolder', 'vocoderFolder', 'program', 'gpu', 'threads'].some(s => e.affectsConfiguration('cantilena.' + s))) {
                release();
            }
        }),
        { dispose: release },
    );
    const version = String((context.extension.packageJSON as { version?: string }).version ?? '0');
    return Object.freeze<CantilenaApi>({
        version,
        apiVersion: 1,
        status: () => status(context),
        voices: async () => voices(),
        sing: (score, options) => sing(context, score, options),
        release,
        resolveSinger: async (singer, options) => resolveSinger(singer, castSetting(), voices().map(v => v.id), voiceSetting().id,
            gender => firstOfGender(voices(), gender, options?.language)),
        cast: async () => castSetting(),
    });
}

export function deactivate(): void {
    release();
}

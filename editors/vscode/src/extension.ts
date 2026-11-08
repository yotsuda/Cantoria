// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

// Cantilena as a VS Code extension: carries the `cantilena` program, holds the settings (where
// NEUTRINO is, which voice), and sings for other extensions through the API `activate` returns
// (docs/design.md §4.2 in the repository). Lily# is the first caller: its preview's Play hands
// the sung part's notes here and plays the WAV beside its synth. Nothing of NEUTRINO or of a
// voice is bundled — the user installs them and points this extension at the folder.

import * as cp from 'child_process';
import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import { CantilenaScore, ENGINES, VoiceEntry, neutrinoFolderProblems, programCandidates, scoreProblems, splitVoiceId, voiceEntriesIn, voiceId, voicesIn } from './core';
import { Engine, EngineSetup, RenderedVoice } from './engine';

/** What `status()` answers: whether a `sing` can succeed now, and why not. */
export interface CantilenaStatus {
    ready: boolean;
    program: string | undefined;
    backend: 'neutrino';
    neutrinoFolder: string;
    /** The voice the setting names, as `engine/name`: `neutrino/MERROW`. */
    voice: string;
    /** The NEUTRINO voices installed, by their folder names; `voices()` has each as an entry. */
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
     *  the same voice is sung once. Throws with a sentence when it cannot: no program, no NEUTRINO
     *  folder, a voice or an engine that is not there, a score the engine refused. */
    sing(score: CantilenaScore, options?: { voice?: string }): Promise<RenderedVoice & { cached: boolean }>;
    /** Lets the engine go (NEUTRINO's server with it); the next `sing` starts it again. */
    release(): void;
}

let output: vscode.OutputChannel;
let engine: Engine | undefined;
let storage: string;

function settings(): vscode.WorkspaceConfiguration { return vscode.workspace.getConfiguration('cantilena'); }
function neutrinoFolder(): string { return settings().get<string>('neutrinoFolder', '').trim(); }
/** The setting's voice as its engine and name (`neutrino/MERROW`; a bare name is NEUTRINO's). */
function voiceSetting(): { engine: string; name: string; id: string } {
    const v = splitVoiceId(settings().get<string>('voice', ''));
    return { ...v, id: voiceId(v.engine, v.name) };
}

function readTextOr(p: string): string | undefined { try { return fs.readFileSync(p, 'utf8'); } catch { return undefined; } }
function listDirs(dir: string): string[] { return fs.readdirSync(dir, { withFileTypes: true }).filter(d => d.isDirectory()).map(d => d.name); }
function voices(): VoiceEntry[] {
    const folder = neutrinoFolder();
    return folder ? voiceEntriesIn(folder, listDirs, fs.existsSync, readTextOr, path.join) : [];
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

function commandExists(cmd: string): boolean {
    const probe = process.platform === 'win32' ? 'where' : 'which';
    try { return cp.spawnSync(probe, [cmd], { stdio: 'ignore', windowsHide: true }).status === 0; } catch { return false; }
}

function setupNow(context: vscode.ExtensionContext): { setup?: EngineSetup; problems: string[]; program?: string } {
    const prog = program(context);
    const folder = neutrinoFolder();
    const problems: string[] = [];
    if (!prog) { problems.push('the cantilena program was not found (this extension is built for Windows x64; developers set cantilena.program)'); }
    problems.push(...neutrinoFolderProblems(folder, fs.existsSync, path.join));
    if (problems.length > 0) { return { problems, program: prog }; }
    return { setup: { program: prog!, neutrinoFolder: folder, gpu: settings().get<boolean>('gpu', true), threads: settings().get<number>('threads', 4) }, problems, program: prog };
}

/** The engine for the settings as they stand; a changed setting lets the old one go. */
function engineFor(context: vscode.ExtensionContext): Engine {
    const { setup, problems } = setupNow(context);
    if (!setup) { throw new Error(problems.join('; ')); }
    if (engine && (engine.setup.program !== setup.program || engine.setup.neutrinoFolder !== setup.neutrinoFolder
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

async function status(context: vscode.ExtensionContext): Promise<CantilenaStatus> {
    const { setup, problems, program: prog } = setupNow(context);
    const folder = neutrinoFolder();
    const names = folder ? voicesIn(folder, listDirs, fs.existsSync, path.join) : [];
    const voice = voiceSetting();
    if (setup) { problems.push(...voiceProblems(voice, names, folder)); }
    return { ready: problems.length === 0, program: prog, backend: 'neutrino', neutrinoFolder: folder, voice: voice.id, voices: names, problems };
}

/** Why `voice` cannot be sung with: an engine this extension has not, or a NEUTRINO voice not installed. */
function voiceProblems(voice: { engine: string; name: string; id: string }, names: string[], folder: string): string[] {
    if (!(ENGINES as readonly string[]).includes(voice.engine)) { return [`no engine named ${voice.engine} in ${voice.id} (there is: ${ENGINES.join(', ')})`]; }
    if (!names.includes(voice.name)) { return [`the voice ${voice.name} is not under ${path.join(folder, 'model')} (installed: ${names.join(', ') || 'none'})`]; }
    return [];
}

async function sing(context: vscode.ExtensionContext, score: CantilenaScore, options?: { voice?: string }): Promise<RenderedVoice & { cached: boolean }> {
    const problems = scoreProblems(score);
    if (problems.length > 0) { throw new Error(problems.join('; ')); }
    const asked = (options?.voice ?? '').trim();
    const voice = asked ? (() => { const v = splitVoiceId(asked); return { ...v, id: voiceId(v.engine, v.name) }; })() : voiceSetting();
    if (!(ENGINES as readonly string[]).includes(voice.engine)) { throw new Error(`no engine named ${voice.engine} in ${voice.id} (there is: ${ENGINES.join(', ')})`); }
    const { key, rendered, cached } = engineFor(context).sing(score, voice.name, voice.id);
    const t0 = Date.now();
    const v = await rendered;
    if (!cached) { output.appendLine(`sung ${key.slice(0, 8)} in ${voice.id}: ${v.duration.toFixed(1)} s, ${Date.now() - t0} ms`); }
    return { ...v, cached };
}

async function chooseNeutrinoFolder(): Promise<void> {
    const picked = await vscode.window.showOpenDialog({
        canSelectFiles: false, canSelectFolders: true, canSelectMany: false,
        title: 'Cantilena: the NEUTRINO folder (holding bin, model and settings)', openLabel: 'Use this folder',
    });
    if (!picked || picked.length === 0) { return; }
    const folder = picked[0].fsPath;
    const problems = neutrinoFolderProblems(folder, fs.existsSync, path.join);
    if (problems.length > 0) {
        const anyway = 'Use it anyway';
        const choice = await vscode.window.showWarningMessage(`Cantilena: ${problems[0]}`, anyway);
        if (choice !== anyway) { return; }
    }
    await settings().update('neutrinoFolder', folder, vscode.ConfigurationTarget.Global);
    vscode.window.setStatusBarMessage(`Cantilena: NEUTRINO is ${folder}`, 5000);
}

async function chooseVoice(context: vscode.ExtensionContext): Promise<void> {
    const s = await status(context);
    if (s.voices.length === 0) {
        const choose = 'Choose NEUTRINO Folder…';
        const choice = await vscode.window.showWarningMessage(`Cantilena: no voice to list — ${s.problems[0] ?? 'the NEUTRINO folder holds no model'}`, choose);
        if (choice === choose) { await chooseNeutrinoFolder(); }
        return;
    }
    const pick = await vscode.window.showQuickPick(
        voices().map(v => ({
            label: v.id,
            description: [v.label !== v.name ? v.label : '', v.language ?? '', v.id === s.voice ? '(current)' : ''].filter(Boolean).join(' · '),
        })),
        { title: 'Cantilena: Choose Voice', placeHolder: 'The voice the lyrics are sung in (engine/name)' });
    if (!pick) { return; }
    await settings().update('voice', pick.label, vscode.ConfigurationTarget.Global);
    vscode.window.setStatusBarMessage(`Cantilena: the voice is ${pick.label}`, 4000);
}

async function checkSetup(context: vscode.ExtensionContext): Promise<void> {
    const s = await status(context);
    output.appendLine(`setup: program ${s.program ?? 'none'}; NEUTRINO ${s.neutrinoFolder || 'not set'}; voice ${s.voice}; installed ${s.voices.join(', ') || 'none'}; ${s.ready ? 'ready' : s.problems.join('; ')}`);
    if (s.ready) {
        vscode.window.showInformationMessage(`Cantilena is ready: ${s.voice} through NEUTRINO at ${s.neutrinoFolder}.`);
        return;
    }
    const folder = 'Choose NEUTRINO Folder…', site = 'Get NEUTRINO', log = 'Show Output';
    const choice = await vscode.window.showWarningMessage(`Cantilena cannot sing yet: ${s.problems[0]}`, folder, site, log);
    if (choice === folder) { await chooseNeutrinoFolder(); }
    else if (choice === site) { void vscode.env.openExternal(vscode.Uri.parse('https://studio-neutrino.com/')); }
    else if (choice === log) { output.show(); }
}

export function activate(context: vscode.ExtensionContext): CantilenaApi {
    output = vscode.window.createOutputChannel('Cantilena');
    storage = context.globalStorageUri.fsPath;
    context.subscriptions.push(
        output,
        vscode.commands.registerCommand('cantilena.chooseNeutrinoFolder', () => chooseNeutrinoFolder()),
        vscode.commands.registerCommand('cantilena.chooseVoice', () => chooseVoice(context)),
        vscode.commands.registerCommand('cantilena.checkSetup', () => checkSetup(context)),
        vscode.commands.registerCommand('cantilena.forgetVoices', () => {
            const e = engine ?? new Engine({ program: '', neutrinoFolder: '', gpu: true, threads: 1 }, path.join(storage, 'voices'), () => undefined);
            const n = e.forget();
            vscode.window.setStatusBarMessage(`Cantilena: ${n} rendered voice${n === 1 ? '' : 's'} forgotten`, 4000);
        }),
        // A changed engine setting lets the engine go; the next sing starts one for the new settings.
        vscode.workspace.onDidChangeConfiguration(e => {
            if (e.affectsConfiguration('cantilena.neutrinoFolder') || e.affectsConfiguration('cantilena.program')
                || e.affectsConfiguration('cantilena.gpu') || e.affectsConfiguration('cantilena.threads')) {
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
    });
}

export function deactivate(): void {
    release();
}

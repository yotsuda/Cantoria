// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

// The extension's pure half: the score's shape, the key a rendered voice is named by, the lines
// of `cantilena serve`'s wire, where the program and the voices are looked for. No vscode, no
// process here — test/core.test.ts drives it under node.

import { createHash } from 'crypto';

/** One sung note (the README's score): seconds, MIDI pitch, the syllable as pronounced. */
export interface CantilenaNote {
    start: number;
    duration: number;
    pitch: number;
    lyric?: string;
    melisma?: boolean;
    breath?: boolean;
}

/** The score a caller hands `sing`: notes in seconds; the sample rate and language optional. */
export interface CantilenaScore {
    sampleRate?: number;
    language?: string;
    notes: CantilenaNote[];
}

/** What is wrong with a score's SHAPE, in words; empty when it can be sent. The engine checks
 *  the music (order, overlap, a lyric on every sung note) and answers in its own words. */
export function scoreProblems(score: unknown): string[] {
    const problems: string[] = [];
    if (!score || typeof score !== 'object') { return ['the score is not an object']; }
    const s = score as Partial<CantilenaScore>;
    if (!Array.isArray(s.notes)) { return ['the score has no "notes" array']; }
    if (s.notes.length === 0) { problems.push('the score has no notes'); }
    s.notes.forEach((n, i) => {
        if (!n || typeof n !== 'object') { problems.push(`note ${i} is not an object`); return; }
        for (const field of ['start', 'duration', 'pitch'] as const) {
            if (typeof n[field] !== 'number' || !Number.isFinite(n[field])) { problems.push(`note ${i}: "${field}" is not a number`); }
        }
        if (!n.melisma && (typeof n.lyric !== 'string' || n.lyric.length === 0)) { problems.push(`note ${i}: no lyric (write one, or mark the note melisma)`); }
    });
    return problems.slice(0, 8);
}

/** What a rendered voice is cached and named by: the voice and the score's bytes. */
export function voiceKey(score: CantilenaScore, voice: string): string {
    return createHash('sha1').update(voice).update('\n').update(JSON.stringify(score)).digest('hex');
}

/** One line of `cantilena serve`'s stdout. */
export interface ServeAnswer {
    ready?: boolean; backend?: string; voices?: string[];
    id?: string; wav?: string; duration?: number; sampleRate?: number; seconds?: number; error?: string;
}

/** The answer a line holds, or null for a line that is not one. */
export function parseLine(line: string): ServeAnswer | null {
    const text = line.trim();
    if (!text.startsWith('{')) { return null; }
    try {
        const parsed: unknown = JSON.parse(text);
        return parsed && typeof parsed === 'object' ? parsed as ServeAnswer : null;
    } catch {
        return null;
    }
}

/** One request of `cantilena serve`'s stdin. */
export function requestLine(id: string, score: CantilenaScore, voice: string, output: string): string {
    return JSON.stringify({ id, score, voice, output }) + '\n';
}

/** The shutdown request. */
export const SHUTDOWN_LINE = '{"command":"shutdown"}\n';

/** The arguments of the resident engine: where the WAVs go, the GPU or not, the threads. */
export function serveArgs(outDir: string, gpu: boolean, threads: number): string[] {
    const args = ['serve', '--out-dir', outDir, '--threads', String(Math.max(1, Math.floor(threads) || 1))];
    if (!gpu) { args.push('--cpu'); }
    return args;
}

/** The bundled program's runtime identifier for this machine: `win-x64`, `osx-arm64`, …; undefined elsewhere. */
export function rid(platform: string, arch: string): string | undefined {
    const os = platform === 'win32' ? 'win' : platform === 'darwin' ? 'osx' : platform === 'linux' ? 'linux' : undefined;
    return os && (arch === 'x64' || arch === 'arm64') ? `${os}-${arch}` : undefined;
}

/** Where the `cantilena` program is looked for, in order: the setting (a developer's build), the
 *  one bundled under `bin/<rid>/`, then the bare name on the PATH. */
export function programCandidates(platform: string, arch: string, extensionPath: string, setting: string | undefined,
    join: (...parts: string[]) => string): string[] {
    const list: string[] = [];
    const own = (setting ?? '').trim();
    if (own) { list.push(own); }
    const r = rid(platform, arch);
    const exe = platform === 'win32' ? 'cantilena.exe' : 'cantilena';
    if (r) { list.push(join(extensionPath, 'bin', r, exe)); }
    list.push(exe);
    return list;
}

/** The voices installed under NEUTRINO's folder: the folders of `model/` that hold an `info.toml`
 *  (as the engine lists them). */
export function voicesIn(folder: string, listDirs: (dir: string) => string[], exists: (p: string) => boolean,
    join: (...parts: string[]) => string): string[] {
    const model = join(folder, 'model');
    if (!exists(model)) { return []; }
    return listDirs(model).filter(name => exists(join(model, name, 'info.toml'))).sort();
}

/** What a NEUTRINO folder must hold to be one. */
export function neutrinoFolderProblems(folder: string, exists: (p: string) => boolean, join: (...parts: string[]) => string): string[] {
    const f = folder.trim();
    if (!f) { return ['no NEUTRINO folder is set (Cantilena: Choose NEUTRINO Folder…)']; }
    const problems: string[] = [];
    if (!exists(f)) { return [`the NEUTRINO folder does not exist: ${f}`]; }
    for (const file of [['bin', 'neutrino_server.exe'], ['bin', 'neutrino_client.exe'], ['bin', 'musicXMLtoLabel.exe'], ['settings', 'dic']]) {
        if (!exists(join(f, ...file))) { problems.push(`not a NEUTRINO (Tau v3.2+) folder: ${file.join('\\')} is missing under ${f}`); break; }
    }
    return problems;
}

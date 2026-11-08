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

/** The engines this extension sings through; the first is what a bare voice name means. */
export const ENGINES = ['neutrino', 'diffsinger'] as const;
export type EngineName = typeof ENGINES[number];

/** A voice as the API names it: `engine/name`, e.g. `neutrino/MERROW` — the form `sing`'s
 *  `voice`, the setting `cantilena.voice` and `status().voice` take, so a second engine's voice
 *  needs no new setting. */
export interface VoiceEntry {
    /** `engine/name`. */
    id: string;
    engine: EngineName;
    /** The engine's own name for the voice: for NEUTRINO, its folder under `model\`; for DiffSinger, its folder under the voices folder. */
    name: string;
    /** What the voice calls itself (NEUTRINO's `info.toml` speaker name, a DiffSinger voice's `character.yaml` name), else `name`. */
    label: string;
    /** The language it sings, as a BCP-47 tag where known (`ja`). */
    language?: string;
    /** The terms it is used under, where the voice or its engine states them; a NEUTRINO voice
     *  leaves it to NEUTRINO's agreement and says nothing here. */
    licence?: string;
}

/** `engine/name`. */
export function voiceId(engine: string, name: string): string { return `${engine}/${name}`; }

/** The engine and the name a voice setting or option means: `neutrino/MERROW`; a bare `MERROW`
 *  is the first engine's; empty is its default voice. The engine is not checked here. */
export function splitVoiceId(text: string | undefined): { engine: string; name: string } {
    const t = (text ?? '').trim();
    const slash = t.indexOf('/');
    if (slash < 0) { return { engine: ENGINES[0], name: t || 'MERROW' }; }
    return { engine: t.slice(0, slash).trim().toLowerCase() || ENGINES[0], name: t.slice(slash + 1).trim() || 'MERROW' };
}

/** What a NEUTRINO voice's `info.toml` says of its speaker: `[speaker] name = "Merrow"` and
 *  `language = "Japanese"`; either missing when the file does not say. */
export function speakerOf(toml: string): { name?: string; language?: string } {
    const section = /^\s*\[speaker\]\s*$([\s\S]*?)(?=^\s*\[|(?![\s\S]))/m.exec(toml)?.[1] ?? '';
    const value = (key: string) => new RegExp(`^\\s*${key}\\s*=\\s*"([^"]*)"`, 'm').exec(section)?.[1]?.trim() || undefined;
    return { name: value('name'), language: value('language') };
}

const LANGUAGE_TAGS: Record<string, string> = { japanese: 'ja', english: 'en', chinese: 'zh', mandarin: 'zh', korean: 'ko' };

/** A language as a BCP-47 tag: a name the voices write (`Japanese`), or a tag already (`ja`, `en-US`); undefined otherwise. */
export function languageTag(name: string | undefined): string | undefined {
    const n = (name ?? '').trim();
    if (!n) { return undefined; }
    return LANGUAGE_TAGS[n.toLowerCase()] ?? (/^[a-z]{2,3}(-[A-Za-z0-9]{2,8})*$/.test(n) ? n : undefined);
}

/** The voices under a NEUTRINO folder as the API lists them: voicesIn, each read for its speaker. */
export function voiceEntriesIn(folder: string, listDirs: (dir: string) => string[], exists: (p: string) => boolean,
    readText: (p: string) => string | undefined, join: (...parts: string[]) => string): VoiceEntry[] {
    return voicesIn(folder, listDirs, exists, join).map(name => {
        const info = readText(join(folder, 'model', name, 'info.toml'));
        const speaker = info ? speakerOf(info) : {};
        const entry: VoiceEntry = { id: voiceId('neutrino', name), engine: 'neutrino', name, label: speaker.name || name };
        const language = languageTag(speaker.language);
        if (language) { entry.language = language; }
        return entry;
    });
}

/** The DiffSinger voices under a folder: its subfolders holding a `dsconfig.yaml` (as the engine lists them). */
export function diffSingerVoicesIn(folder: string, listDirs: (dir: string) => string[], exists: (p: string) => boolean,
    join: (...parts: string[]) => string): string[] {
    if (!folder || !exists(folder)) { return []; }
    return listDirs(folder).filter(name => exists(join(folder, name, 'dsconfig.yaml'))).sort();
}

/** What a DiffSinger voice's `character.yaml` calls it: its `name:` line, unquoted; undefined when there is none. */
export function characterNameOf(yaml: string): string | undefined {
    const m = /^\s*name\s*:\s*(.+?)\s*$/m.exec(yaml);
    if (!m) { return undefined; }
    const v = m[1].replace(/^(["'])(.*)\1$/, '$2').trim();
    return v || undefined;
}

/** The DiffSinger voices under a folder as the API lists them: the voices' folders, each read for its name. */
export function diffSingerEntriesIn(folder: string, listDirs: (dir: string) => string[], exists: (p: string) => boolean,
    readText: (p: string) => string | undefined, join: (...parts: string[]) => string): VoiceEntry[] {
    return diffSingerVoicesIn(folder, listDirs, exists, join).map(name => {
        const character = readText(join(folder, name, 'character.yaml'));
        const label = (character && characterNameOf(character)) || name;
        const entry: VoiceEntry = { id: voiceId('diffsinger', name), engine: 'diffsinger', name, label };
        // The voices sing what their dictionaries spell; this extension reads kana through them (ja) so far.
        if (exists(join(folder, name, 'dsdur', 'dsdict-ja.yaml'))) { entry.language = 'ja'; }
        return entry;
    });
}

/** What a DiffSinger voices folder must be: a folder (its voices are listed; none is a warning, not an error). */
export function voicesFolderProblems(folder: string, exists: (p: string) => boolean): string[] {
    const f = folder.trim();
    if (!f) { return []; }
    return exists(f) ? [] : [`the DiffSinger voices folder does not exist: ${f}`];
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

/** What a rendered voice is cached and named by: the voice (its `engine/name`) and the score's bytes. */
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

/** The arguments of the resident engine: where the WAVs go, the GPU or not, the threads, the DiffSinger folders when set. */
export function serveArgs(outDir: string, gpu: boolean, threads: number, voicesFolder = '', vocoderFolder = ''): string[] {
    const args = ['serve', '--out-dir', outDir, '--threads', String(Math.max(1, Math.floor(threads) || 1))];
    if (!gpu) { args.push('--cpu'); }
    if (voicesFolder.trim()) { args.push('--voices-dir', voicesFolder.trim()); }
    if (vocoderFolder.trim()) { args.push('--vocoder-dir', vocoderFolder.trim()); }
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

/** What a NEUTRINO folder must hold to be one; an empty setting is no problem of its own (another engine may be set up). */
export function neutrinoFolderProblems(folder: string, exists: (p: string) => boolean, join: (...parts: string[]) => string): string[] {
    const f = folder.trim();
    if (!f) { return []; }
    const problems: string[] = [];
    if (!exists(f)) { return [`the NEUTRINO folder does not exist: ${f}`]; }
    for (const file of [['bin', 'neutrino_server.exe'], ['bin', 'neutrino_client.exe'], ['bin', 'musicXMLtoLabel.exe'], ['settings', 'dic']]) {
        if (!exists(join(f, ...file))) { problems.push(`not a NEUTRINO (Tau v3.2+) folder: ${file.join('\\')} is missing under ${f}`); break; }
    }
    return problems;
}

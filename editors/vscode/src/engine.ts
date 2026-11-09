// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

// The engine as a process: one resident `cantilena serve`, spoken to in JSON lines, and the
// voices it rendered, kept by their key under the extension's storage so a phrase is sung once
// per machine. No vscode here: the settings and the commands are extension.ts's.

import * as cp from 'child_process';
import * as fs from 'fs';
import * as path from 'path';
import * as readline from 'readline';
import { CantilenaScore, SHUTDOWN_LINE, parseLine, requestLine, serveArgs, voiceKey } from './core';

/** A voice the engine rendered: the WAV's path, its length, its rate. */
export interface RenderedVoice { wav: string; duration: number; sampleRate: number }

/** How the engine is to run: the program, NEUTRINO's folder (its environment), the DiffSinger voices and vocoder folders, the GPU, the threads. */
export interface EngineSetup { program: string; neutrinoFolder: string; voicesFolder: string; vocoderFolder: string; gpu: boolean; threads: number }

/**
 * The resident engine: started on the first render, spoken to on stdin, answered on stdout one
 * line a request, in order. A request is named by its key and the WAV by it too, so a repeat
 * writes the same file. An engine that dies fails what it owed and starts again on the next
 * render.
 */
export class Engine {
    private child: cp.ChildProcess | undefined;
    private readonly pending = new Map<string, { resolve: (v: RenderedVoice) => void; reject: (e: Error) => void }>();
    private readonly done = new Map<string, RenderedVoice>();
    private readonly inFlight = new Map<string, Promise<RenderedVoice>>();
    /** What the engine said on its first line: its voices. */
    voices: string[] = [];
    /** Where the last DiffSinger render ran (`DirectML` or `CPU`), once one has; logged when it changes. */
    provider: string | undefined;

    constructor(readonly setup: EngineSetup, readonly outDir: string, private readonly log: (line: string) => void) {}

    get running(): boolean { return this.child !== undefined; }

    private start(): cp.ChildProcess {
        fs.mkdirSync(this.outDir, { recursive: true });
        const env = { ...process.env };
        if (this.setup.neutrinoFolder) { env.CANTILENA_NEUTRINO_DIR = this.setup.neutrinoFolder; } else { delete env.CANTILENA_NEUTRINO_DIR; }
        const args = serveArgs(this.outDir, this.setup.gpu, this.setup.threads, this.setup.voicesFolder, this.setup.vocoderFolder);
        // The bundled program is packed on Windows, where a VSIX carries no file modes: on Linux and
        // macOS it arrives without its execute bit, so it is given one before the first start.
        if (process.platform !== 'win32' && path.isAbsolute(this.setup.program)) {
            try { fs.chmodSync(this.setup.program, 0o755); } catch { /* a read-only install: spawn says so */ }
        }
        const child = cp.spawn(this.setup.program, args, { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'], env });
        this.child = child;
        readline.createInterface({ input: child.stdout! }).on('line', line => this.answer(line));
        readline.createInterface({ input: child.stderr! }).on('line', line => this.log(line));
        child.on('error', e => this.gone(child, `the engine could not start (${this.setup.program}): ${e.message}`));
        child.on('exit', (code, signal) => this.gone(child, `the engine exited (${signal ?? code})`));
        this.log(`engine: ${this.setup.program} ${args.join(' ')}`);
        return child;
    }

    private answer(line: string): void {
        const answer = parseLine(line);
        if (!answer) { this.log(line); return; }
        if (answer.ready) {
            this.voices = answer.voices ?? [];
            this.log(`engine ready (${answer.backend ?? '?'}): ${this.voices.join(', ') || 'no voice installed'}`);
            return;
        }
        const waiting = answer.id ? this.pending.get(answer.id) : undefined;
        if (!waiting || !answer.id) { return; }
        this.pending.delete(answer.id);
        if (answer.provider && answer.provider !== this.provider) {
            this.provider = answer.provider;
            this.log(`diffsinger: the models run on ${answer.provider}`);
        }
        if (answer.error || !answer.wav) { waiting.reject(new Error(answer.error ?? 'the engine answered no WAV')); }
        else { waiting.resolve({ wav: answer.wav, duration: answer.duration ?? 0, sampleRate: answer.sampleRate ?? 0 }); }
    }

    private gone(child: cp.ChildProcess, why: string): void {
        if (this.child !== child) { return; }
        this.child = undefined;
        this.log(why);
        for (const waiting of this.pending.values()) { waiting.reject(new Error(why)); }
        this.pending.clear();
    }

    /** The voice for `score` in `voice` (the engine's own name for it; `id` its `engine/name`, what
     *  the render is keyed by): at once when it was sung before (the file still there), else when
     *  the engine has sung it; a second ask while it is in flight joins the first. */
    sing(score: CantilenaScore, voice: string, id = voice, voicevoxUrl?: string): { key: string; rendered: Promise<RenderedVoice>; cached: boolean } {
        const key = voiceKey(score, id);
        const output = path.join(this.outDir, `${key}.wav`);
        const hit = this.done.get(key);
        if (hit && fs.existsSync(hit.wav)) { return { key, rendered: Promise.resolve(hit), cached: true }; }
        let rendered = this.inFlight.get(key);
        if (!rendered) {
            rendered = this.request(key, score, voice, output, voicevoxUrl).then(v => { this.done.set(key, v); return v; });
            this.inFlight.set(key, rendered);
            rendered.finally(() => this.inFlight.delete(key)).catch(() => undefined);
        }
        return { key, rendered, cached: false };
    }

    private request(key: string, score: CantilenaScore, voice: string, output: string, voicevoxUrl?: string): Promise<RenderedVoice> {
        const child = this.child ?? this.start();
        return new Promise<RenderedVoice>((resolve, reject) => {
            this.pending.set(key, { resolve, reject });
            child.stdin!.write(requestLine(key, score, voice, output, voicevoxUrl), e => {
                if (e) { this.pending.delete(key); reject(e); }
            });
        });
    }

    /** Forgets the rendered voices and deletes their files. */
    forget(): number {
        let removed = 0;
        this.done.clear();
        if (fs.existsSync(this.outDir)) {
            for (const name of fs.readdirSync(this.outDir)) {
                if (name.endsWith('.wav')) { try { fs.unlinkSync(path.join(this.outDir, name)); removed++; } catch { /* in use */ } }
            }
        }
        return removed;
    }

    /** Asks the engine to shut down (NEUTRINO's server with it) and lets go of it. */
    dispose(): void {
        const child = this.child;
        if (!child) { return; }
        this.gone(child, 'the engine was let go');
        try {
            child.stdin!.write(SHUTDOWN_LINE);
            child.stdin!.end();
        } catch { /* already gone */ }
        const timer = setTimeout(() => { try { child.kill(); } catch { /* gone */ } }, 3000);
        child.on('exit', () => clearTimeout(timer));
    }
}

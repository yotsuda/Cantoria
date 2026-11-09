// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

// VOICEVOX as a server: asked what it sings, and, where the user has named its folder, started
// when one of its voices is needed and stopped with VS Code. No vscode here.

import * as path from 'path';
import { VOICEVOX_OWN_URL, VOICEVOX_OWN_PORT, VoiceEntry, voicevoxEntriesFrom, voicevoxRunIn } from './core';

export interface VoicevoxSetup { url: string; folder: string; gpu: boolean }

/** What is started: enough of a child process to know it ended and to end it. */
export interface StartedEngine { kill(): unknown; once(event: 'exit' | 'error', listener: () => void): unknown }

export interface VoicevoxDeps {
    fetchJson(url: string, timeoutMs: number): Promise<unknown>;
    spawn(program: string, args: string[], cwd: string): StartedEngine;
    exists(p: string): boolean;
    log(line: string): void;
    now(): number;
    sleep(ms: number): Promise<void>;
}

const ASK_EVERY_MS = 2000;
const READY_WITHIN_MS = 60000;
const RETRY_AFTER_MS = 60000;

/**
 * The running VOICEVOX, whoever started it. One the user has open is used and left alone. Failing
 * that, with a folder set, an engine is started on a port of its own, so that the user's VOICEVOX
 * can still open on its usual one; only that engine is stopped here.
 */
export class VoicevoxServer {
    /** Its singing voices, as last asked. */
    voices: VoiceEntry[] = [];
    /** Where they were heard from. */
    answering: string | undefined;

    private child: StartedEngine | undefined;
    private asking: Promise<void> | undefined;
    private starting: Promise<void> | undefined;
    private askedAt = -Infinity;
    private failedAt = -Infinity;

    constructor(private readonly setup: () => VoicevoxSetup, private readonly deps: VoicevoxDeps) {}

    /** Learns what is running, at most every two seconds. Starts nothing. */
    ask(): Promise<void> {
        if (this.asking) { return this.asking; }
        if (this.deps.now() - this.askedAt < ASK_EVERY_MS) { return Promise.resolve(); }
        this.asking = this.look().finally(() => { this.askedAt = this.deps.now(); this.asking = undefined; });
        return this.asking;
    }

    /** Learns what is running and, when a voice is needed and none answers, starts the engine. */
    async ensure(): Promise<void> {
        await this.ask();
        if (this.voices.length > 0 || !this.setup().folder) { return; }
        if (!this.starting) {
            if (this.deps.now() - this.failedAt < RETRY_AFTER_MS) { return; }
            this.starting = this.start().finally(() => { this.starting = undefined; });
        }
        return this.starting;
    }

    /** The settings changed: what was started under the old ones goes, and the next ask looks again. */
    changed(): void {
        this.dispose();
        this.askedAt = -Infinity;
        this.failedAt = -Infinity;
    }

    /** Stops the engine started here, and only that. */
    dispose(): void {
        const child = this.child;
        this.child = undefined;
        if (child) {
            this.voices = [];
            this.answering = undefined;
            try { child.kill(); } catch { /* already gone */ }
        }
    }

    private async singers(url: string): Promise<VoiceEntry[]> {
        try { return voicevoxEntriesFrom(await this.deps.fetchJson(`${url.replace(/\/+$/, '')}/singers`, 1500)); } catch { return []; }
    }

    private async look(): Promise<void> {
        const { url, folder } = this.setup();
        for (const candidate of [url, folder ? VOICEVOX_OWN_URL : ''].filter(Boolean)) {
            const voices = await this.singers(candidate);
            if (voices.length > 0) {
                this.voices = voices;
                this.answering = candidate;
                return;
            }
        }
        this.voices = [];
        this.answering = undefined;
    }

    private async start(): Promise<void> {
        const { folder, gpu } = this.setup();
        const run = voicevoxRunIn(folder, this.deps.exists, path.join);
        if (!run) {
            this.failedAt = this.deps.now();
            this.deps.log(`voicevox: no engine under ${folder} (it holds vv-engine\\run.exe)`);
            return;
        }
        // A build for the CPU refuses the GPU, so the GPU is only a first try.
        for (const useGpu of gpu ? [true, false] : [false]) {
            if (await this.launch(run, useGpu)) { return; }
        }
        this.failedAt = this.deps.now();
        this.deps.log(`voicevox: the engine at ${run} did not answer`);
    }

    /** True when the engine started and answers with its voices. */
    private async launch(run: string, useGpu: boolean): Promise<boolean> {
        const args = ['--host', '127.0.0.1', '--port', String(VOICEVOX_OWN_PORT), ...(useGpu ? ['--use_gpu'] : [])];
        this.deps.log(`voicevox: starting ${run} ${args.join(' ')}`);
        let child: StartedEngine;
        try { child = this.deps.spawn(run, args, path.dirname(run)); } catch (e) { this.deps.log(`voicevox: ${String(e)}`); return false; }
        let ended = false;
        const onEnd = () => {
            ended = true;
            if (this.child === child) {
                this.child = undefined;
                this.voices = [];
                this.answering = undefined;
                this.askedAt = -Infinity;
                this.deps.log('voicevox: the engine ended');
            }
        };
        child.once('exit', onEnd);
        child.once('error', onEnd);
        const startedAt = this.deps.now();
        while (!ended && this.deps.now() - startedAt < READY_WITHIN_MS) {
            const voices = await this.singers(VOICEVOX_OWN_URL);
            if (ended) { break; }
            if (voices.length > 0) {
                this.child = child;
                this.voices = voices;
                this.answering = VOICEVOX_OWN_URL;
                this.askedAt = this.deps.now();
                this.deps.log(`voicevox: ready, ${voices.length} voices, in ${this.deps.now() - startedAt} ms`);
                return true;
            }
            await this.deps.sleep(300);
        }
        if (!ended) { try { child.kill(); } catch { /* already gone */ } }
        return false;
    }
}

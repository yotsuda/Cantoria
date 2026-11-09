import { strict as assert } from 'node:assert';
import * as path from 'node:path';
import { describe, it } from 'node:test';
import { VOICEVOX_OWN_URL, needsVoicevox, voicevoxRunIn } from '../src/core';
import { StartedEngine, VoicevoxDeps, VoicevoxServer } from '../src/voicevox';

const SINGERS = [{ name: 'ず', styles: [{ name: 'ノーマル', type: 'frame_decode' }] }];
const USER_URL = 'http://127.0.0.1:50021';
// (joined as the server joins it, so that the fake machine finds the engine on every platform CI runs on)
const RUN = path.join('C:\\V', 'vv-engine', 'run.exe');

/** A machine: which URLs answer, what is started, and a clock that runs on sleep. */
function machine(options: { answering?: string[]; startsAnswering?: (args: string[]) => boolean; run?: boolean } = {}) {
    const up = new Set(options.answering ?? []);
    const started: { args: string[]; killed: boolean; end: () => void }[] = [];
    let time = 0;
    const deps: VoicevoxDeps = {
        fetchJson: async url => {
            if (![...up].some(u => url.startsWith(u))) { throw new Error('refused'); }
            return SINGERS;
        },
        spawn: (_program, args) => {
            const handlers: (() => void)[] = [];
            const record = { args, killed: false, end: () => { up.delete(VOICEVOX_OWN_URL); handlers.forEach(h => h()); } };
            started.push(record);
            if (options.startsAnswering?.(args) ?? true) { up.add(VOICEVOX_OWN_URL); } else { setImmediate(record.end); }
            const child: StartedEngine = { kill: () => { record.killed = true; up.delete(VOICEVOX_OWN_URL); return true; }, once: (_e, h) => handlers.push(h) };
            return child;
        },
        exists: p => (options.run ?? true) && p === RUN,
        log: () => undefined,
        now: () => time,
        sleep: async ms => { time += ms; await new Promise(r => setImmediate(r)); },
    };
    return { deps, started, up, pass: (ms: number) => { time += ms; } };
}

const server = (m: ReturnType<typeof machine>, folder = 'C:\\V', gpu = true) => new VoicevoxServer(() => ({ url: USER_URL, folder, gpu }), m.deps);

describe('the running VOICEVOX', () => {
    it('uses the one the user has open, starts nothing, and leaves it alone', async () => {
        const m = machine({ answering: [USER_URL] });
        const s = server(m);
        await s.ensure();
        assert.equal(s.answering, USER_URL);
        assert.equal(s.voices.length, 1);
        assert.equal(m.started.length, 0);
        s.dispose();
        assert.equal(s.answering, USER_URL, 'not ours to stop');
    });

    it('only asks, starting nothing, when no voice is needed', async () => {
        const m = machine();
        const s = server(m);
        await s.ask();
        assert.equal(m.started.length, 0);
        assert.deepEqual(s.voices, []);
    });

    it('starts nothing without a folder', async () => {
        const m = machine();
        const s = server(m, '');
        await s.ensure();
        assert.equal(m.started.length, 0);
    });

    it('starts the engine on its own port when a voice is needed, and stops it', async () => {
        const m = machine();
        const s = server(m);
        await s.ensure();
        assert.deepEqual(m.started[0].args, ['--host', '127.0.0.1', '--port', '50121', '--use_gpu']);
        assert.equal(s.answering, VOICEVOX_OWN_URL);
        assert.equal(s.voices.length, 1);
        s.dispose();
        assert.equal(m.started[0].killed, true);
        assert.deepEqual(s.voices, []);
    });

    it('starts it once when two ask together', async () => {
        const m = machine();
        const s = server(m);
        await Promise.all([s.ensure(), s.ensure()]);
        assert.equal(m.started.length, 1);
    });

    it('uses an engine another window started, without owning it', async () => {
        const m = machine({ answering: [VOICEVOX_OWN_URL] });
        const s = server(m);
        await s.ensure();
        assert.equal(s.answering, VOICEVOX_OWN_URL);
        assert.equal(m.started.length, 0);
    });

    it('falls back from the GPU when a build refuses it', async () => {
        const m = machine({ startsAnswering: args => !args.includes('--use_gpu') });
        const s = server(m);
        await s.ensure();
        assert.deepEqual(m.started.map(c => c.args.includes('--use_gpu')), [true, false]);
        assert.equal(s.voices.length, 1);
    });

    it('does not start on the GPU when the setting says not', async () => {
        const m = machine();
        await server(m, 'C:\\V', false).ensure();
        assert.equal(m.started[0].args.includes('--use_gpu'), false);
    });

    it('waits a minute before trying again after a failure', async () => {
        const m = machine({ startsAnswering: () => false });
        const s = server(m);
        await s.ensure();
        assert.equal(m.started.length, 2);
        m.pass(5000);
        await s.ensure();
        assert.equal(m.started.length, 2, 'not yet');
        m.pass(60000);
        await s.ensure();
        assert.equal(m.started.length, 4);
    });

    it('notices that the engine ended, and starts it again when needed', async () => {
        const m = machine();
        const s = server(m);
        await s.ensure();
        m.started[0].end();
        assert.deepEqual(s.voices, []);
        await s.ensure();
        assert.equal(m.started.length, 2);
    });

    it('finds no engine in a folder without one', async () => {
        const m = machine({ run: false });
        await server(m).ensure();
        assert.equal(m.started.length, 0);
    });
});

describe('when a VOICEVOX voice is needed', () => {
    const none = { female: '', male: '' };

    it('is when the score names one, or the default is one, for words to sing', () => {
        assert.equal(needsVoicevox('voicevox/ず-ノーマル', none, 5, 'ja'), true);
        assert.equal(needsVoicevox('neutrino/MERROW', none, 5, 'ja'), false);
        assert.equal(needsVoicevox('', { female: 'voicevox/ず-ノーマル', male: '' }, 5, 'ja'), true);
        assert.equal(needsVoicevox('neutrino/MERROW', { female: 'voicevox/ず-ノーマル', male: '' }, 5, 'ja'), false);
        assert.equal(needsVoicevox('', none, 5, 'ja'), false);
    });

    it('is when any of the voices that sing together is one', () => assert.equal(needsVoicevox('neutrino/M + voicevox/ず-ノーマル', none, 5, 'ja'), true));

    it('is when nothing else can sing', () => assert.equal(needsVoicevox('', none, 0, 'ja'), true));

    it('is not when no words are being sung', () => assert.equal(needsVoicevox('', { female: 'voicevox/ず-ノーマル', male: '' }, 5, undefined), false));

    it('finds the engine in an install or in the engine folder itself', () => {
        const join = (...p: string[]) => p.join('\\');
        assert.equal(voicevoxRunIn('C:\\V', p => p === 'C:\\V\\vv-engine\\run.exe', join), 'C:\\V\\vv-engine\\run.exe');
        assert.equal(voicevoxRunIn('C:\\V\\vv-engine', p => p === 'C:\\V\\vv-engine\\run.exe', join), 'C:\\V\\vv-engine\\run.exe');
        assert.equal(voicevoxRunIn('C:\\V', () => false, join), undefined);
    });
});

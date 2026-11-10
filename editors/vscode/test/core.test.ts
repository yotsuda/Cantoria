// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

import { describe, it } from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import {
    ENGINES, characterNameOf, diffSingerEntriesIn, diffSingerSpeakersOf, diffSingerVoicesIn, languageProblem, languageTag, missingProgramNote, neutrinoFolderProblems, parseLine, programCandidates,
    defaultVoiceFor, firstOfGender, genderOfWord, requestLine, resolveSinger, rid, scoreProblems, serveArgs, speakerOf, splitVoiceId, voiceEntriesIn, voiceId, voiceKey, voicePieces, voicesFolderProblems, voicesIn, voicevoxEntriesFrom, vsixTarget,
} from '../src/core';
import { VOICEVOX_GENDERS, voicevoxGender } from '../src/voicevoxGenders';

const join = (...parts: string[]) => parts.join('\\');

describe('a score\'s singer on this machine', () => {
    const list = [
        { id: 'n/A', gender: 'female', language: 'ja' },
        { id: 'n/B', language: 'ja' },
        { id: 'n/C', gender: 'male', language: 'en' },
        { id: 'n/D', gender: 'male', language: 'ja' },
        { id: 'n/E', gender: 'male', language: 'ja' },
        { id: 'n/F', gender: 'female', language: 'ja' },
    ];
    const unset = { female: '', male: '' };

    it('sings a voice that is named and installed', () => {
        assert.deepEqual(resolveSinger('n/B', list, unset), { voice: 'n/B' });
    });

    it('takes the setting for a gender, else the first such voice for the words\' language', () => {
        assert.deepEqual(resolveSinger('male', list, unset, { language: 'ja' }), { voice: 'n/D' });
        assert.deepEqual(resolveSinger('male', list, unset, { language: 'en' }), { voice: 'n/C' });
        assert.deepEqual(resolveSinger('female', list, unset, { language: 'ja' }), { voice: 'n/A' });
        assert.deepEqual(resolveSinger('male', list, { female: '', male: 'n/E' }, { language: 'ja' }), { voice: 'n/E' });
        assert.deepEqual(resolveSinger('female', list, { female: 'n/F', male: '' }, { language: 'ja' }), { voice: 'n/F' });
    });

    it('reads a part\'s words, and with no singer the gender the score\'s reader says', () => {
        assert.equal(genderOfWord('soprano'), 'female');
        assert.equal(genderOfWord('alto'), 'female');
        assert.equal(genderOfWord('tenor'), 'male');
        assert.equal(genderOfWord('bass'), 'male');
        assert.equal(genderOfWord('lead'), undefined);
        assert.deepEqual(resolveSinger('tenor', list, unset, { language: 'ja', gender: 'female' }), { voice: 'n/D' });
        assert.deepEqual(resolveSinger('', list, unset, { language: 'ja', gender: 'male' }), { voice: 'n/D' });
        assert.deepEqual(resolveSinger('  ', list, unset, { language: 'ja' }), { voice: 'n/A' });
    });

    it('falls to the other gender, and to a voice that names none, and says so', () => {
        const femaleOnly = list.filter(v => v.gender !== 'male');
        const r = resolveSinger('male', femaleOnly, unset, { language: 'ja' });
        assert.equal(r.voice, 'n/A');
        assert.match(r.note!, /no installed voice for these words says it is male/);
        const silent = defaultVoiceFor([{ id: 'x/Y' }], unset, 'male');
        assert.equal(silent.voice, 'x/Y');
        assert.ok(silent.note);
    });

    it('says when a name or a setting is not there, or a word names no voice', () => {
        const missing = resolveSinger('diffsinger/NOPE', list, unset, { language: 'ja', gender: 'male' });
        assert.equal(missing.voice, 'n/D');
        assert.match(missing.note!, /diffsinger\/NOPE is not installed; singing with n\/D/);
        const setting = resolveSinger('male', list, { female: '', male: 'n/GONE' }, { language: 'ja' });
        assert.equal(setting.voice, 'n/D');
        assert.match(setting.note!, /n\/GONE is not installed/);
        assert.match(resolveSinger('lead', list, unset, { language: 'ja' }).note!, /"lead" names no voice; singing with n\/A/);
    });

    it('lists no one for a gender no voice says', () => {
        assert.equal(firstOfGender(list, 'male', 'ko'), undefined);
    });

    it('refuses words in a language the voice says it does not sing, and lets a silent voice try', () => {
        assert.match(languageProblem({ id: 'neutrino/MERROW', language: 'ja' }, 'en')!, /sings ja, and these words are en/);
        assert.deepEqual(voicePieces(' a/b + c/d+'), ['a/b', 'c/d']);
        const chorus = [{ id: 'n/A' }, { id: 'd/B' }];
        assert.deepEqual(resolveSinger('n/A + d/B', chorus, { female: '', male: '' }), { voice: 'n/A+d/B' }, 'each sings');
        assert.equal(resolveSinger('n/A + n/A', chorus, { female: '', male: '' }).voice, 'n/A', 'one voice once');
        assert.match(resolveSinger('n/A + x/Y', chorus, { female: '', male: '' }).note!, /x\/Y is not installed/);
        assert.equal(languageProblem({ id: 'neutrino/MERROW', language: 'ja' }, 'ja'), undefined);
        assert.equal(languageProblem({ id: 'd/T', language: 'ja', languages: ['ja', 'en'] }, 'en'), undefined, 'a voice of several languages');
        assert.match(languageProblem({ id: 'd/T', language: 'ja', languages: ['ja', 'en'] }, 'ko')!, /sings ja, en, and these words are ko/);
        assert.deepEqual(resolveSinger('male', [{ id: 'd/M', gender: 'male', language: 'ja', languages: ['ja', 'en'] }], { female: '', male: '' }, { language: 'en' }), { voice: 'd/M' });
        assert.equal(languageProblem({ id: 'x/y' }, 'en'), undefined);
        assert.equal(languageProblem(undefined, 'en'), undefined);
    });
});

describe('the score a caller hands sing', () => {
    const score = { sampleRate: 48000, language: 'ja', notes: [
        { start: 0, duration: 0.5, pitch: 60, lyric: 'ゆ' }, { start: 0.5, duration: 0.25, pitch: 62, melisma: true },
    ] };

    it('is checked for its shape only; the music is the engine\'s to judge', () => {
        assert.deepEqual(scoreProblems(score), []);
        assert.deepEqual(scoreProblems(null), ['the score is not an object']);
        assert.deepEqual(scoreProblems({}), ['the score has no "notes" array']);
        assert.deepEqual(scoreProblems({ notes: [] }), ['the score has no notes']);
        assert.deepEqual(scoreProblems({ notes: [{ start: 0, duration: 1, pitch: 60 }] }), ['note 0: no lyric (write one, or mark the note melisma)']);
        assert.deepEqual(scoreProblems({ notes: [{ start: 'a', duration: 1, pitch: 60, lyric: 'x' }] }), ['note 0: "start" is not a number']);
    });

    it('is keyed by the voice and its bytes', () => {
        const a = voiceKey(score, 'MERROW');
        assert.match(a, /^[0-9a-f]{40}$/);
        assert.equal(voiceKey(score, 'MERROW'), a);
        assert.notEqual(voiceKey(score, 'OTHER'), a);
        assert.notEqual(voiceKey({ ...score, notes: score.notes.slice(0, 1) }, 'MERROW'), a);
    });
});

describe('the engine\'s wire', () => {
    it('reads the answers and leaves the chatter alone', () => {
        assert.deepEqual(parseLine('{"ready":true,"backend":"neutrino","voices":["MERROW"],"outDir":"x"}'), { ready: true, backend: 'neutrino', voices: ['MERROW'], outDir: 'x' });
        assert.deepEqual(parseLine('{"id":"k","wav":"C:\\\\v\\\\k.wav","duration":8,"sampleRate":48000,"seconds":0.4}'), { id: 'k', wav: 'C:\\v\\k.wav', duration: 8, sampleRate: 48000, seconds: 0.4 });
        assert.deepEqual(parseLine('{"id":"k","error":"no lyric"}'), { id: 'k', error: 'no lyric' });
        assert.equal(parseLine('cantoria: serving neutrino from …'), null);
        assert.equal(parseLine('{broken'), null);
    });

    it('writes one request a line and names the WAV by the key', () => {
        const line = requestLine('k1', { notes: [] }, 'MERROW', 'C:\\v\\k1.wav');
        assert.ok(line.endsWith('\n'));
        assert.deepEqual(JSON.parse(line), { id: 'k1', score: { notes: [] }, voice: 'MERROW', output: 'C:\\v\\k1.wav' });
    });

    it('starts the engine resident, with the GPU unless told not to, and the DiffSinger folders when set', () => {
        assert.deepEqual(serveArgs('C:\\v', true, 4), ['serve', '--out-dir', 'C:\\v', '--threads', '4']);
        assert.deepEqual(serveArgs('C:\\v', false, 0), ['serve', '--out-dir', 'C:\\v', '--threads', '1', '--cpu']);
        assert.deepEqual(serveArgs('C:\\v', true, 4, 'C:\\voices', ' '), ['serve', '--out-dir', 'C:\\v', '--threads', '4', '--voices-dir', 'C:\\voices']);
        assert.deepEqual(serveArgs('C:\\v', true, 4, '', 'C:\\voc'), ['serve', '--out-dir', 'C:\\v', '--threads', '4', '--vocoder-dir', 'C:\\voc']);
        assert.equal(JSON.parse(requestLine('k', { notes: [] }, 'voicevox/x', 'o', 'http://x:1')).voicevoxUrl, 'http://x:1');
        assert.deepEqual(voicevoxEntriesFrom({ detail: 'no' }), [], 'not a list of singers');
        assert.deepEqual(voicevoxEntriesFrom([{ name: 'ず', styles: [{ name: 'ふつう', type: 'talk' }, { name: 'ノーマル', type: 'frame_decode' }] }]),
            [{ id: 'voicevox/ず-ノーマル', engine: 'voicevox', name: 'ず-ノーマル', label: 'ず (ノーマル)', language: 'ja', licence: 'Credit "VOICEVOX:ず" and follow the character\'s terms' }]);
    });

    it('takes a VOICEVOX voice\'s gender from the table, and leaves one it does not list unsaid', () => {
        const styles = [{ name: 'ノーマル', type: 'frame_decode' }];
        assert.equal(voicevoxEntriesFrom([{ name: '春日部つむぎ', styles }])[0].gender, 'female');
        assert.equal(voicevoxEntriesFrom([{ name: '玄野武宏', styles }])[0].gender, 'male');
        assert.ok(!('gender' in voicevoxEntriesFrom([{ name: 'だれか', styles }])[0]));
        assert.equal(voicevoxGender('constructor'), undefined, 'a name that is an Object member is not listed');
        assert.equal(firstOfGender(voicevoxEntriesFrom([{ name: '玄野武宏', styles }]), 'male', 'ja'), 'voicevox/玄野武宏-ノーマル');
    });

    it('writes only female or male in the table', () => {
        assert.deepEqual([...new Set(Object.values(VOICEVOX_GENDERS))].sort(), ['female', 'male']);
    });
});

describe('where the program and the voices are', () => {
    it('names the bundled build by the machine', () => {
        assert.equal(rid('win32', 'x64'), 'win-x64');
        assert.equal(rid('darwin', 'arm64'), 'osx-arm64');
        assert.equal(rid('linux', 'x64'), 'linux-x64');
        assert.equal(rid('freebsd', 'x64'), undefined);
    });

    it('says which package a machine needs when the bundled program is for another platform', () => {
        assert.equal(vsixTarget('win-x64'), 'win32-x64');
        assert.equal(vsixTarget('osx-arm64'), 'darwin-arm64');
        assert.equal(vsixTarget('linux-x64'), 'linux-x64');
        assert.equal(missingProgramNote('win32', 'x64', ['osx-arm64']),
            'the cantoria program was not found: this machine is win32-x64 (win-x64), this VSIX bundles osx-arm64 — it is the package for another platform; install cantoria-win32-x64-<version>.vsix (developers set cantoria.program)');
        assert.equal(missingProgramNote('linux', 'x64', []),
            'the cantoria program was not found: this machine is linux-x64 (linux-x64), this VSIX bundles no program — install the linux-x64 package (developers set cantoria.program)');
        assert.match(missingProgramNote('freebsd', 'x64', ['win-x64']), /which no bundled build is made for/);
    });

    it('looks for the program in the setting, the bundle, then the PATH', () => {
        assert.deepEqual(programCandidates('win32', 'x64', 'C:\\ext', 'C:\\dev\\cantoria.exe', join),
            ['C:\\dev\\cantoria.exe', 'C:\\ext\\bin\\win-x64\\cantoria.exe', 'cantoria.exe']);
        assert.deepEqual(programCandidates('darwin', 'arm64', '/ext', '  ', (...p) => p.join('/')),
            ['/ext/bin/osx-arm64/cantoria', 'cantoria']);
    });

    it('lists the voices as the engine does: the model folders holding an info.toml', () => {
        const tree = new Set(['N\\model', 'N\\model\\MERROW\\info.toml', 'N\\model\\KIRITAN\\info.toml', 'N\\model\\_broken']);
        const dirs = (d: string) => d === 'N\\model' ? ['_broken', 'MERROW', 'KIRITAN'] : [];
        assert.deepEqual(voicesIn('N', dirs, p => tree.has(p), join), ['KIRITAN', 'MERROW']);
        assert.deepEqual(voicesIn('X', dirs, p => tree.has(p), join), []);
    });

    // A voice is named engine/name everywhere (docs/roadmap.md A), so a second engine's voice
    // needs no new setting; a bare name is the first engine's.
    it('names a voice engine/name, and reads either form', () => {
        assert.deepEqual(ENGINES, ['neutrino', 'diffsinger', 'voicevox']);
        assert.equal(voiceId('neutrino', 'MERROW'), 'neutrino/MERROW');
        assert.deepEqual(splitVoiceId('neutrino/MERROW'), { engine: 'neutrino', name: 'MERROW' });
        assert.deepEqual(splitVoiceId(' Neutrino/KIRITAN '), { engine: 'neutrino', name: 'KIRITAN' });
        assert.deepEqual(splitVoiceId('MERROW'), { engine: 'neutrino', name: 'MERROW' });
        assert.deepEqual(splitVoiceId(''), { engine: 'neutrino', name: 'MERROW' });
        assert.deepEqual(splitVoiceId(undefined), { engine: 'neutrino', name: 'MERROW' });
        assert.deepEqual(splitVoiceId('diffsinger/C:/voices/x'), { engine: 'diffsinger', name: 'C:/voices/x' });
    });

    it('reads a NEUTRINO voice\'s speaker from its info.toml, and lists the voices as entries', () => {
        const merrow = 'version = "v3.2.2"\ntype = "stable"\n\n[speaker]\nname = "Merrow"\ntype = "standard"\ngender = "female"\nlanguage = "Japanese"\n\n[timing]\nname = "not the speaker"\n';
        assert.deepEqual(speakerOf(merrow), { name: 'Merrow', language: 'Japanese', gender: 'female' });
        assert.deepEqual(speakerOf('[timing]\nname = "x"\n'), { name: undefined, language: undefined, gender: undefined });
        assert.deepEqual(speakerOf(''), { name: undefined, language: undefined, gender: undefined });
        assert.equal(languageTag('Japanese'), 'ja');
        assert.equal(languageTag('english'), 'en');
        assert.equal(languageTag('en-US'), 'en-US');
        assert.equal(languageTag('Klingon'), undefined);
        assert.equal(languageTag(undefined), undefined);
        const tree = new Set(['N\\model', 'N\\model\\MERROW\\info.toml', 'N\\model\\KIRITAN\\info.toml']);
        const dirs = (d: string) => d === 'N\\model' ? ['MERROW', 'KIRITAN'] : [];
        const text = (p: string) => p === 'N\\model\\MERROW\\info.toml' ? merrow : undefined;
        assert.deepEqual(voiceEntriesIn('N', dirs, p => tree.has(p), text, join), [
            { id: 'neutrino/KIRITAN', engine: 'neutrino', name: 'KIRITAN', label: 'KIRITAN' },
            { id: 'neutrino/MERROW', engine: 'neutrino', name: 'MERROW', label: 'Merrow', language: 'ja', gender: 'female' },
        ]);
    });

    it("lists the other speakers of a voice after it, named as its character.yaml names them", () => {
        const yaml = 'hidden_size: 256\nspeakers:\n  - dsacoustic/tiger_fresh\n  - dsacoustic/tiger_disco\n  - dsacoustic/plain\n\nnext: 1\n';
        assert.deepEqual(diffSingerSpeakersOf(yaml), ['tiger_fresh', 'tiger_disco', 'plain']);
        assert.deepEqual(diffSingerSpeakersOf('hidden_size: 256\n'), []);
        const text = (p: string) => p.endsWith('dsconfig.yaml') ? yaml : `name: "T"\nsubbanks:\n- {color: '02 Disco', suffix: dsacoustic/tiger_disco} # soft\n`;
        const entries = diffSingerEntriesIn('V', d => d === 'V' ? ['T'] : [], p => p === 'V' || p.endsWith('dsconfig.yaml') || p.endsWith('dsdict-en.yaml'), text, join);
        assert.deepEqual(entries.map(e => [e.id, e.label, e.languages]), [
            ['diffsinger/T', 'T', ['en']],
            ['diffsinger/T/tiger_disco', 'T (Disco)', ['en']],
            ['diffsinger/T/plain', 'T (plain)', ['en']],
        ]);
    });

    it('lists the DiffSinger voices: the subfolders with a dsconfig.yaml, each named by its character.yaml', () => {
        const tree = new Set(['V', 'V\\TIGER\\dsconfig.yaml', 'V\\TIGER\\character.yaml', 'V\\TIGER\\dsdur\\dsdict-ja.yaml', 'V\\Plain\\dsconfig.yaml', 'V\\notes']);
        const dirs = (d: string) => d === 'V' ? ['notes', 'TIGER', 'Plain'] : [];
        const text = (p: string) => p === 'V\\TIGER\\character.yaml' ? 'name: "TIGER DS v106"\nauthor: x\n' : undefined;
        assert.deepEqual(diffSingerVoicesIn('V', dirs, p => tree.has(p), join), ['Plain', 'TIGER']);
        assert.deepEqual(diffSingerVoicesIn('', dirs, p => tree.has(p), join), []);
        assert.deepEqual(diffSingerEntriesIn('V', dirs, p => tree.has(p), text, join), [
            { id: 'diffsinger/Plain', engine: 'diffsinger', name: 'Plain', label: 'Plain' },
            { id: 'diffsinger/TIGER', engine: 'diffsinger', name: 'TIGER', label: 'TIGER DS v106', language: 'ja', languages: ['ja'] },
        ]);
        assert.equal(characterNameOf("name: 'Momo'\n"), 'Momo');
        assert.equal(characterNameOf('singer_type: diffsinger\n'), undefined);
        assert.deepEqual(voicesFolderProblems('', () => false), []);
        assert.deepEqual(voicesFolderProblems('V', () => false), ['the DiffSinger voices folder does not exist: V']);
    });

    it('says what a NEUTRINO folder lacks (an empty setting is no problem of its own)', () => {
        assert.deepEqual(neutrinoFolderProblems('', () => true, join), []);
        assert.deepEqual(neutrinoFolderProblems('N', () => false, join), ['the NEUTRINO folder does not exist: N']);
        const has = new Set(['N', 'N\\bin\\neutrino_server.exe']);
        assert.deepEqual(neutrinoFolderProblems('N', p => has.has(p), join), ['not a NEUTRINO (Tau v3.2+) folder: bin\\neutrino_client.exe is missing under N']);
        const all = new Set(['N', 'N\\bin\\neutrino_server.exe', 'N\\bin\\neutrino_client.exe', 'N\\bin\\musicXMLtoLabel.exe', 'N\\settings\\dic']);
        assert.deepEqual(neutrinoFolderProblems('N', p => all.has(p), join), []);
        // Off Windows a NEUTRINO folder is not searched: it cannot run there.
        assert.deepEqual(neutrinoFolderProblems('N', p => all.has(p), join, 'linux'),
            ['NEUTRINO runs on Windows only: leave the NEUTRINO folder empty here and set up a DiffSinger voices folder']);
        assert.deepEqual(neutrinoFolderProblems('', () => true, join, 'darwin'), []);
    });
});

describe('the manifest', () => {
    const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8'));
    const props = Object.assign({}, ...manifest.contributes.configuration.map((c: { properties: object }) => c.properties));

    it('puts the general settings first, the engines in alphabetical order, the developer\'s last', () => {
        const sections = [...manifest.contributes.configuration].sort((a: { order: number }, b: { order: number }) => a.order - b.order);
        assert.deepEqual(sections.map((c: { title: string }) => c.title), ['General', 'DiffSinger', 'NEUTRINO', 'VOICEVOX', 'Developer']);
    });

    it('groups the settings by what they are for', () => {
        const groups = Object.fromEntries(manifest.contributes.configuration.map((c: { title: string; properties: object }) => [c.title, Object.keys(c.properties).map(k => k.replace('cantoria.', '')).sort()]));
        assert.deepEqual(groups, {
            General: ['defaultFemaleVoice', 'defaultMaleVoice', 'gpu'],
            NEUTRINO: ['neutrinoFolder', 'threads'],
            DiffSinger: ['vocoderFolder', 'voicesFolder'],
            VOICEVOX: ['voicevoxFolder', 'voicevoxUrl'],
            Developer: ['program'],
        });
    });

    const commands = (manifest.contributes.commands as { command: string }[]).map(c => c.command);

    it('holds the settings, each engine setting machine-overridable', () => {
        for (const name of ['cantoria.neutrinoFolder', 'cantoria.voicesFolder', 'cantoria.vocoderFolder', 'cantoria.defaultFemaleVoice', 'cantoria.defaultMaleVoice', 'cantoria.gpu', 'cantoria.threads', 'cantoria.program']) {
            assert.ok(props[name], `${name} is contributed`);
            assert.equal(props[name].scope, 'machine-overridable', name);
        }
        assert.equal(props['cantoria.defaultFemaleVoice'].default, '');
        assert.equal(props['cantoria.defaultMaleVoice'].default, '');
        assert.match(props['cantoria.neutrinoFolder'].markdownDescription, /command:cantoria\.chooseNeutrinoFolder/);
        assert.match(props['cantoria.defaultFemaleVoice'].markdownDescription, /command:cantoria\.chooseFemaleVoice/);
        assert.match(props['cantoria.defaultMaleVoice'].markdownDescription, /command:cantoria\.chooseMaleVoice/);
        assert.match(props['cantoria.voicesFolder'].markdownDescription, /command:cantoria\.chooseVoicesFolder/);
        assert.match(props['cantoria.vocoderFolder'].markdownDescription, /command:cantoria\.chooseVocoderFolder/);
    });

    it('contributes the eight commands and starts on nothing but them and the API', () => {
        assert.deepEqual(commands.sort(), ['cantoria.checkSetup', 'cantoria.chooseFemaleVoice', 'cantoria.chooseMaleVoice', 'cantoria.chooseNeutrinoFolder', 'cantoria.chooseVocoderFolder', 'cantoria.chooseVoicesFolder', 'cantoria.chooseVoicevoxFolder', 'cantoria.forgetVoices']);
        assert.deepEqual(manifest.activationEvents, []);
        assert.equal(manifest.main, './out/extension.js');
        assert.equal(manifest.license, 'Apache-2.0');
    });
});

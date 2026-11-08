# Cantilena Singing Voice

Sings the lyrics of a score for notation extensions such as
[Lily#](https://marketplace.visualstudio.com/items?itemName=yotsuda.lilysharp): the extension
carries the `cantilena` program, holds the settings — where your NEUTRINO is, where your
DiffSinger voices are, which voice — and sings for other extensions through its API. It has no
editor of its own.

Open source, Apache-2.0: [github.com/yotsuda/cantilena](https://github.com/yotsuda/cantilena).
Published for Windows x64, Linux x64 and macOS arm64 with the program bundled (NEUTRINO itself
runs on Windows only; DiffSinger voices sing everywhere). Other platforms can point
`cantilena.program` at a build of their own.

## Setup

Two engines; set up either or both. A voice is named `engine/name`.

**NEUTRINO** (Japanese voices; Windows):
1. Install **NEUTRINO** (Tau, v3.2 or later) from [studio-neutrino.com](https://studio-neutrino.com/).
   It is free software of STUDIO NEUTRINO under its own terms, not part of this extension.
   Its bundled voice is MERROW; other voices are their authors' work, installed under its `model\` folder.
2. **Cantilena: Choose NEUTRINO Folder…** — the folder holding `bin\`, `model\` and `settings\`.

**DiffSinger** (voices in the OpenUtau format, each a folder with a `dsconfig.yaml`):
1. Put the voices you have installed, under their authors' terms, side by side in one folder
   (OpenUtau's `Singers` folder is one).
2. **Cantilena: Choose Voices Folder… (DiffSinger)** — that folder. Japanese lyrics (kana) are read
   through the voice's dictionary so far; a voice without a bundled vocoder needs
   `cantilena.vocoderFolder`.

Then **Cantilena: Choose Voice…** (`neutrino/MERROW` by default) and **Cantilena: Check Setup** to
see that all is well.

In Lily#, the preview's Play then sings every part a lyrics row sings (hiragana or katakana on
each syllable), in time with the other parts. The first phrase takes a while (NEUTRINO loads its
models — up to a minute; a DiffSinger voice a couple of seconds on a DirectX 12 GPU through DirectML,
or ten or more on the CPU where there is none); the same phrase is sung once and kept
(**Cantilena: Forget Rendered Voices** clears them).

## For extension authors

```ts
const ext = vscode.extensions.getExtension('yotsuda.cantilena');
const api = ext && (ext.isActive ? ext.exports : await ext.activate());
const { ready, problems, voice } = await api.status();     // voice: 'neutrino/MERROW'
const installed = await api.voices();                        // [{ id: 'neutrino/MERROW', engine: 'neutrino', name: 'MERROW', label: 'Merrow', language: 'ja' },
                                                             //  { id: 'diffsinger/TIGER', engine: 'diffsinger', name: 'TIGER', label: 'TIGER DS v106', language: 'ja' }]
const { wav, duration } = await api.sing({
    sampleRate: 48000, language: 'ja',
    notes: [
        { start: 0.0, duration: 0.5, pitch: 60, lyric: 'ゆ' },
        { start: 0.5, duration: 0.25, pitch: 62, melisma: true },
        { start: 0.75, duration: 0.5, pitch: 60, lyric: 'き', breath: true },
    ],
});
```

- Times are **seconds**, resolved by the caller (tempo, repeats, a practice speed); gaps are
  rests. `pitch` is MIDI (fractional allowed). `lyric` is the syllable as pronounced (kana for
  the Japanese voices); a `melisma` note carries the previous vowel on and has no lyric;
  `breath` marks a phrase end after the note.
- `sing` answers the WAV's path (mono; 48 kHz from NEUTRINO, the vocoder's rate — 44.1 kHz as a
  rule — from a DiffSinger voice) and its duration; the file stays until the voices are forgotten.
  Its `options.voice` is an `id` from `voices()` (`engine/name`; a bare name is a NEUTRINO voice),
  else the setting's voice. It throws with one sentence when it cannot sing (no program, no engine
  set up, an engine or a voice that is not there, a score the engine refused). `release()` lets
  the engine go; `apiVersion` is `1` (`voices()` was added in 0.2.0 without changing it — an older
  extension has no `voices`).
- Everything is local: the engine is a resident child process of this extension, spoken to in
  JSON lines; nothing leaves the machine.

## Notices

NEUTRINO is invoked as installed by the user and is not redistributed; its agreement forbids use
for voice conversion — Cantilena synthesizes from scores only. The bundled program is Cantilena's
own code (Apache-2.0) published with the .NET runtime (MIT); see THIRD-PARTY-NOTICES.md.

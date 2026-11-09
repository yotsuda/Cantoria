# Cantoria Singing Voice

Sings the lyrics of a score for notation extensions such as
[Lily#](https://marketplace.visualstudio.com/items?itemName=yotsuda.lilysharp), through the singing
engines you have installed: **DiffSinger** voices, **NEUTRINO** and **VOICEVOX**. Voices from
different engines can sing together in one score, even in one part. The extension carries the
`cantoria` program, holds the settings and sings for other extensions through its API. It has no
editor of its own.

Open source, Apache-2.0: [github.com/yotsuda/Cantoria](https://github.com/yotsuda/Cantoria).
Published for Windows x64, Linux x64 and macOS arm64 with the program bundled. NEUTRINO runs on
Windows only; DiffSinger voices sing everywhere, on the CPU outside Windows. Other platforms can
point `cantoria.program` at a build of their own.

## Setup

Three engines; set up any of them. A voice is named `engine/name`.

**DiffSinger** (voices in the OpenUtau format, each a folder with a `dsconfig.yaml`):
1. Put the voices you have installed, under their authors' terms, side by side in one folder
   (OpenUtau's `Singers` folder is one).
2. **Cantoria: Choose Voices Folder… (DiffSinger)**: that folder. A voice without a bundled
   vocoder needs `cantoria.vocoderFolder`.

A voice sings Japanese through its dictionary, and English where it has a `dsdict-en.yaml`. An
English word the dictionary lacks is sung from its spelling; the guess can be wrong, and the
Cantoria output channel names each word sung this way. A voice with several speakers offers each
as a voice of its own, such as `diffsinger/TIGER/tiger_disco`.

**NEUTRINO** (Japanese voices; Windows):
1. Install **NEUTRINO** (Tau, v3.2 or later) from [studio-neutrino.com](https://studio-neutrino.com/).
   It is free software of STUDIO NEUTRINO under its own terms, not part of this extension.
   Its bundled voice is MERROW; other voices are their authors' work, installed under its `model\` folder.
2. **Cantoria: Choose NEUTRINO Folder…**: the folder holding `bin\`, `model\` and `settings\`.

**VOICEVOX** (Japanese voices, named `voicevox/name-style`):
1. Install **VOICEVOX** from [voicevox.hiroshiba.jp](https://voicevox.hiroshiba.jp/). It is not part
   of this extension. You use it under [its terms](https://voicevox.hiroshiba.jp/term/), each
   character has its own, and a published work credits `VOICEVOX:name`.
2. **Cantoria: Choose VOICEVOX Folder…**: the folder holding `vv-engine`. Cantoria then starts the
   engine when one of its voices is needed, on a port of its own, and stops it when VS Code closes.
   A VOICEVOX you opened yourself is used and left alone. Without the folder, only a VOICEVOX you
   have opened sings (`cantoria.voicevoxUrl` says where it answers).

Then **Cantoria: Choose Default Female Voice…** and **Choose Default Male Voice…** if you want
particular voices (empty: the first installed voice of that gender that sings the words' language),
and **Cantoria: Check Setup** to see that all is well.

## Singing in Lily#

The preview's Play sings every part a lyrics row sings, in time with the other parts. Write kana for
a Japanese voice and syllables of English words for an English one. A lyrics row names who sings it:

```
lyrics verse sings melody singer "neutrino/MERROW"
lyrics verse sings melody singer "neutrino/MERROW","diffsinger/TIGER"
lyrics verse sings melody singer female
```

Several voices joined by commas sing the part together, and they may come from different engines.
`male` or `female` takes the default voice of that gender.

The first phrase takes a while: NEUTRINO loads its models for up to a minute, and a DiffSinger voice
takes a couple of seconds on a DirectX 12 GPU through DirectML, or ten or more on the CPU where
there is none. The same phrase is sung once and kept (**Cantoria: Forget Rendered Voices** clears
them).

## For extension authors

```ts
const ext = vscode.extensions.getExtension('yotsuda.cantoria');
const api = ext && (ext.isActive ? ext.exports : await ext.activate());
const { ready, problems, voice } = await api.status();     // voice: 'neutrino/MERROW'
const installed = await api.voices();
// [{ id: 'neutrino/MERROW', engine: 'neutrino', name: 'MERROW', label: 'Merrow', language: 'ja', gender: 'female' },
//  { id: 'diffsinger/TIGER', engine: 'diffsinger', name: 'TIGER', label: 'TIGER DS v106', language: 'ja', languages: ['ja', 'en'] }]
const { wav, duration } = await api.sing({
    sampleRate: 48000, language: 'ja',
    notes: [
        { start: 0.0, duration: 0.5, pitch: 60, lyric: 'ゆ' },
        { start: 0.5, duration: 0.25, pitch: 62, melisma: true },
        { start: 0.75, duration: 0.5, pitch: 60, lyric: 'き', breath: true },
    ],
}, { voice: 'neutrino/MERROW+diffsinger/TIGER' });
```

- Times are **seconds**, resolved by the caller (tempo, repeats, a practice speed); gaps are
  rests. `pitch` is MIDI (fractional allowed).
- `language` is the words' language (`ja`, `en`). `lyric` is the syllable: kana for Japanese, a
  syllable of the word for English, with `hyphen` on a note whose next syllable is of the same word
  (`Twin-`, `kle`), so that the whole word is looked up.
- A `melisma` note carries the previous vowel on and has no lyric. `breath` marks a phrase end after
  the note, and `inhale` an audible breath before the next one. `level` and `levelEnd` scale the
  sung audio across the note (absent: 1).
- `sing` answers the WAV's path (mono; 48 kHz from NEUTRINO, the vocoder's rate, 44.1 kHz as a
  rule, from a DiffSinger voice) and its duration; the file stays until the voices are forgotten.
  Its `options.voice` is an `id` from `voices()` (a bare name is a NEUTRINO voice), or several joined
  by `+`, each singing the score and their sounds laid together. Without it, the default voice
  sings. It throws with one sentence when it cannot sing: no program, no engine set up, an engine or
  a voice that is not there, words in a language the voice does not sing, a score the engine refused.
- `resolveSinger(singer, { language, gender })` answers the voice a score's `singer` comes to on this
  machine: the voice it names when installed, else the default voice of the gender it says or of
  `gender`, with a `note` when that is not the one asked for.
- `release()` lets the engine go. `apiVersion` is `1`.
- Everything is local: nothing leaves the machine.

## Notices

NEUTRINO and VOICEVOX are invoked as installed by the user and are not redistributed. NEUTRINO's
agreement forbids use for voice conversion; Cantoria synthesizes from scores only. No voice is
bundled. The bundled program is Cantoria's own code (Apache-2.0) published with the .NET runtime
(MIT), ONNX Runtime and YamlDotNet (MIT) and, on Windows, DirectML; see THIRD-PARTY-NOTICES.md.

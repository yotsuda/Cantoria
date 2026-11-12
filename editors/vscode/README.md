# Cantoria Singing Voice

Sings the lyrics of a score for notation extensions such as
[Lily#](https://marketplace.visualstudio.com/items?itemName=yotsuda.lilysharp), through the singing
engines you have installed: **DiffSinger** voices, **NEUTRINO** and **VOICEVOX**. Voices from
different engines can sing together in one score, even in one part. The extension carries the
`cantoria` program, holds the settings and sings for other extensions through its API. It has no
editor of its own.

Open source, Apache-2.0: [github.com/yotsuda/Cantoria](https://github.com/yotsuda/Cantoria).
Published for Windows x64, Linux x64 and macOS arm64 with the program bundled. NEUTRINO runs on
Windows only. DiffSinger voices are built to sing on all three, on the CPU outside Windows, but
have been heard only on Windows so far. Other platforms can point `cantoria.program` at a build of
their own.

## Setup

Three engines; set up any of them. A voice is named `engine/name`. **Cantoria: How to Install a
Voice** opens this section, and so does the button of that name where Cantoria says it has no voice.

**DiffSinger** (voices in the OpenUtau format, each a folder with a `dsconfig.yaml`):
1. Download a voice from its author, under the author's terms, and unpack it so that it is a
   folder of its own holding a `dsconfig.yaml`. Put your voices side by side in one folder,
   `C:\voices\TIGER\dsconfig.yaml` for one (OpenUtau's `Singers` folder is such a folder).
2. **Cantoria: Choose Voices Folder… (DiffSinger)**: that folder (`C:\voices`).
3. A voice with no `dsvocoder` folder of its own needs a vocoder, the part that turns what the
   voice computes into sound. Download the `.oudep` file of the one the voice's readme names (for
   `nsf_hifigan`, the usual one, `nsf_hifigan_44.1k_hop512_128bin_2024.02.oudep`) from the
   [DiffSinger community vocoders](https://github.com/openvpi/vocoders/releases), rename it to
   `.zip`, unpack it into a folder, and choose that folder with **Cantoria: Choose Vocoder
   Folder…**. The vocoders have terms of their own, in the `NOTICE.txt` they come with.
4. Name the voice by its folder: `diffsinger/TIGER`.

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

Needs the Lily# extension 0.16.0 or later. The preview's Play sings every part a `singer` line sings, in time with the other parts. Write kana for
a Japanese voice and syllables of English words for an English one. A top-level line says who sings what
([the page on singing](https://yotsuda.github.io/LilySharp/sing.html) has the rest):

```
singer "neutrino/MERROW" sings verse on melody
singer "diffsinger/TIGER" sings verse on melody
singer female sings verse on melody
```

Several lines with the same words on one part sing it together, and they may come from different engines.
`male` or `female` takes the default voice of that gender.

The first phrase takes a while: NEUTRINO loads its models for up to a minute, and a DiffSinger voice
takes a couple of seconds on a DirectX 12 GPU through DirectML, or ten or more on the CPU where
there is none. The same phrase is sung once and kept, even after VS Code closes, up to the disk
`cantoria.cacheSizeMB` allows (**Cantoria: Forget Rendered Voices** clears them).

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
- A score's `sing` says how the voice sings it — `{ "sing": { "breathiness": 3, "tension": -2 }, "notes": [ … ] }` —
  by the names the voice takes. `parameters(voice)` lists them, each as `{ name, scope, what, min, max,
  default, unit }`: `scope` is where the setting can change (`curve` at any note, `phrase` where a
  phrase ends, `voice` once a score). They are the engine's own inputs and nothing is made up for a
  voice that has none: a DiffSinger voice takes what its model was trained with (breathiness,
  tension, energy, voicing — each an offset on what the voice does by itself — gender, velocity),
  a NEUTRINO voice one (`support`, another of its
  voices by name — `word: true`, with the names in `values`), a VOICEVOX voice none. A name the voice does not take is not sung and is said in
  the answer's `notes`, sentences for whoever wrote the score. A NOTE's `sing` changes a setting
  from that note on, until the next one written for the same name (`"default"` gives it back to
  the score's value), and its `singTo` arrives at a value with the note, gliding from where the
  value before was written — for a setting of scope `curve`. A setting of scope `phrase` may be
  written on a note too: it steps, and each phrase is sung with the value at its first note.
- `sing`'s `options.onProgress` hears how far a render is, for a bar: `{ toSing, sung }` in seconds
  of music — what has to be sung **anew** (a phrase kept from an earlier sing costs nothing and is
  not counted) and how much of it is done. It is told once before the first phrase and once after
  each, so the count begins at 0 and is full when the score is; a score with nothing new to sing
  says `{ toSing: 0, sung: 0 }`. Voices joined by `+` are one count, its total growing as each
  begins.
- `resolveSinger(singer, { language, gender })` answers the voice a score's `singer` comes to on this
  machine: the voice it names when installed, else the default voice of the gender it says or of
  `gender`, with a `note` when that is not the one asked for.
- `plan({ asked: [{ part: 'alto', gender: 'female', language: 'la' }, …] })` answers who would sing
  what a score asks for, **without singing** — `{ ready, singers: [{ part?, key?, asked, voice, note?,
  reason, languages? }] }`: for each voice asked for (by `voice`, an id or what `resolveSinger`
  takes; by `gender` alone; in a `language`; `part` and `key` are yours and come back), the
  installed `voice` that would sing it, or `voice: null` and the `reason` in words for whoever wrote
  the score — `no installed voice sings Latin (la); installed voices sing ja, en`. `ready` is true
  when each has a singer. `plan(score, { voice?, gender? })` asks for a score's one voice. The
  `cantoria` program answers (`cantoria plan`), by the rule `resolveSinger` goes by and what `sing`
  would then refuse; it loads no model, so it can be asked as a score is written.
- `release()` lets the engine go. `apiVersion` is `1`.
- Everything is local: nothing leaves the machine.

## Notices

NEUTRINO and VOICEVOX are invoked as installed by the user and are not redistributed. NEUTRINO's
agreement forbids use for voice conversion; Cantoria synthesizes from scores only. No voice is
bundled. The bundled program is Cantoria's own code (Apache-2.0) published with the .NET runtime
(MIT), ONNX Runtime and YamlDotNet (MIT) and, on Windows, DirectML; see THIRD-PARTY-NOTICES.md.

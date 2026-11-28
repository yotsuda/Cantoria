# Cantoria Singing Voice

Sings the lyrics of a score for notation extensions such as
[Lily#](https://marketplace.visualstudio.com/items?itemName=yotsuda.lilysharp), through the singing
engines you have installed: **DiffSinger** voices, **NEUTRINO** and **VOICEVOX**, and **Sinsy**
and **UTAU** voicebanks, which are sung inside the extension, Japanese on any computer. Voices from
different engines can sing in one score, each asked for by itself. The extension carries the
`cantoria` program, holds the settings and sings for other extensions through its API. It has no
editor of its own.

Open source, Apache-2.0: [github.com/yotsuda/Cantoria](https://github.com/yotsuda/Cantoria).
Published for Windows x64, Linux x64 and macOS arm64 with the program bundled. NEUTRINO runs on
Windows only. DiffSinger voices sing on all three: on a Mac with Apple's CoreML for the vocoder
and the processor for the rest (sung on GitHub's macOS machines, not yet heard on a Mac at hand),
on Linux on the processor. Other platforms can point `cantoria.developer.program` at a build of
their own.

## Setup

**A GPU makes the difference.** NEUTRINO sings on an NVIDIA GPU (CUDA), and a DiffSinger voice on
any DirectX 12 GPU on Windows: there 43 s of song are sung in about 7 (an RTX 5070 Ti). With no
such GPU a voice sings on the processor, and NEUTRINO then takes several times as long as the song
itself: on a laptop with Intel graphics, a 36-second part in about 2¼ minutes with the package
that has the tuning tool, and 4 with the plain one. What was sung is kept, so after the first
time only a phrase you change is sung again.

**The quickest way: Cantoria: Download a Voice…** (also a button where Cantoria says it cannot
sing yet). It offers Qixuan 绮萱, a free DiffSinger voice by the DiffSinger community that sings
Chinese, Japanese and English, shows its terms, and on your agreement fetches it and its vocoder
(about 450 MB) from their authors' own pages into your voices folder (`~/Cantoria/DiffSinger` when
none is set), each file checked against the one published. Then it sings, with nothing else to
set. Where you publish a song made with it, its terms ask you to say that it is synthesised with
AI and to credit "绮萱 (Qixuan)" and "DiffSinger"; read them before you use it. Cantoria itself
ships no voice.

**A voice that sings on any computer**: Download a Voice also offers NIT SONG070 F001, a Japanese
voice for Sinsy (5 MB, CC BY 3.0, by Nagoya Institute of Technology). Sinsy is inside this extension:
no GPU, nothing else to install, a phrase sung in well under a second. It is plainer than the
others, an older kind of voice (HMM). Where you publish a song made with it, credit the voice as
its licence asks.

**UTAU voicebanks sing too**, inside this extension and with no UTAU: put a voicebank's folder (the
recordings with `oto.ini`, unpacked as its author ships it) in the UTAU voices folder and it is listed
as `utau/` and the folder's name. Japanese kana, from 連続音 (VCV), CVVC and 単独音 (CV) banks. Each voicebank
is its author's: download it from the author and use it under the author's terms.

**Any other voice you like sings too.** Qixuan is only the voice Cantoria may fetch for you. Any
DiffSinger voice in the OpenUtau format, downloaded from its author under the author's terms, sings
once it is unpacked beside Qixuan in the DiffSinger voices folder (step 1 of DiffSinger, below).
**Cantoria: Add a Voice of Your Own…** (also the last line of Download a Voice) opens that folder.

Five engines; set up any of them. **Each engine keeps its voices in its own place**; there is no
one folder for all:

| Engine | Where its voices are | The setting |
|---|---|---|
| DiffSinger | the DiffSinger voices folder, one folder a voice | `cantoria.diffsinger.voicesFolder` |
| NEUTRINO | NEUTRINO's own `model\` folder, as NEUTRINO installs them | `cantoria.neutrino.installFolder` (NEUTRINO itself) |
| VOICEVOX | inside VOICEVOX, which lists them | `cantoria.voicevox.installFolder` (VOICEVOX itself) |
| Sinsy | the Sinsy voices folder, a `.htsvoice` in it or one folder down | `cantoria.sinsy.voicesFolder` (empty: `~/Cantoria/Sinsy`) |
| UTAU | the UTAU voices folder, one voicebank folder (with `oto.ini`) a voice | `cantoria.utau.voicesFolder` (empty: `~/Cantoria/UTAU`) |

A score names a voice `engine/name` — a name, not a path: `diffsinger/Qixuan`,
`neutrino/MERROW`, `voicevox/ずんだもん-ノーマル`. A third part picks one of a voice's speakers
(`diffsinger/TIGER/tiger_disco`). The button **How to Install a Voice**, where Cantoria says it has
no voice, opens this page inside VS Code.

**Cantoria: Find Engines on This Computer** looks where the engines' own programs put them and
offers what it finds, so that no folder has to be looked for: VOICEVOX as its installer leaves it
(the engine in its folder, or the one it downloads into its user data), and OpenUtau's `Singers`
folder with a vocoder among its dependencies. NEUTRINO, and anything you unpacked somewhere of your
own, is chosen by its folder as below.

**DiffSinger** (voices in the OpenUtau format, each a folder with a `dsconfig.yaml`):
1. Download a voice from its author, under the author's terms, and unpack it into the DiffSinger
   voices folder so that its `dsconfig.yaml` is **one folder down**, each voice a folder of its own
   (OpenUtau's `Singers` folder is such a folder):

   ```
   C:\Users\you\Cantoria\DiffSinger\     ← the DiffSinger voices folder
   ├─ Qixuan\
   │  ├─ dsconfig.yaml                   → diffsinger/Qixuan
   │  └─ …
   ├─ pc_nsf_hifigan\                     (a vocoder, beside the voices: step 3)
   └─ MyVoice\
      └─ dsconfig.yaml                   → diffsinger/MyVoice
   ```

   Not a folder deeper. A voice's zip mostly holds a folder of its own, and Windows' **Extract
   All** puts it in another named after the zip: `voices\MyVoice_v1.0\MyVoice\dsconfig.yaml` is
   not listed. Cantoria notices this (in **Add a Voice of Your Own…**, **Check Setup** and the
   default-voice lists) and offers **Move It Up**.
2. **Cantoria: Choose Voices Folder… (DiffSinger)**: that folder, if it is not the one Download a
   Voice set (`~/Cantoria/DiffSinger`).
3. A voice with no `dsvocoder` folder of its own needs a vocoder, the part that turns what the
   voice computes into sound. Download the `.oudep` file of the one the voice's readme names (for
   `nsf_hifigan`, the usual one, `nsf_hifigan_44.1k_hop512_128bin_2024.02.oudep`) from the
   [DiffSinger community vocoders](https://github.com/openvpi/vocoders) (their releases), rename it to
   `.zip`, and unpack it into a folder of its own beside your voices (`voices\pc_nsf_hifigan`):
   each voice is sung with the vocoder its `dsconfig.yaml` names, found there, in or beside the
   folder chosen with **Cantoria: Choose Vocoder Folder…** (OpenUtau's `Dependencies` will do), or
   else the chosen one. The vocoders have terms of their own, in the `NOTICE.txt` they come with.
4. Name the voice by its folder's name: `diffsinger/MyVoice`.

A voice sings Japanese through its dictionary, and English where it has a `dsdict-en.yaml`. An
English word the dictionary lacks is sung from its spelling; the guess can be wrong, and the
Cantoria output channel names each word sung this way. A voice with several speakers offers each
as a voice of its own, such as `diffsinger/TIGER/tiger_disco`.

**NEUTRINO** (Japanese voices; Windows):
1. Install **NEUTRINO** (Tau, v3.2 or later) from [studio-neutrino.com](https://studio-neutrino.com/).
   It is free software of STUDIO NEUTRINO under its own terms, not part of this extension.
   Either Windows package sings. The one with the tuning tool (調声支援ツール同梱版) is the faster: it
   has a server that keeps the voice loaded, so a phrase changed is sung again in well under a
   second, where the plain package loads the voice for every phrase (about 3.5 s each on a fast GPU).
   The package with the tuning tool comes with the voice MERROW, the plain one with five; other
   voices are their authors' work, installed under its `model\` folder.
2. **Cantoria: Choose NEUTRINO Folder…**: the folder holding `bin\`, `model\` and `settings\`
   (in the package with the tuning tool, the inner folder `NEUTRINO 調声支援ツール`).

**VOICEVOX** (Japanese voices, named `voicevox/name-style`):
1. Install **VOICEVOX** from [voicevox.hiroshiba.jp](https://voicevox.hiroshiba.jp/). It is not part
   of this extension. You use it under [its terms](https://voicevox.hiroshiba.jp/term/), each
   character has its own, and a published work credits `VOICEVOX:name`.
2. **Cantoria: Find Engines on This Computer** finds an installed VOICEVOX; or **Cantoria: Choose
   VOICEVOX Folder…**: the folder holding `vv-engine` (a zip, or an installer that brings the
   engine), or the engine's own folder, the one holding `run.exe` (the installer that downloads
   the engine keeps it in its user data, under `vvpp-engines`). Cantoria then starts the
   engine when one of its voices is needed, on a port of its own, and stops it when VS Code closes.
   A VOICEVOX you opened yourself is used and left alone. Without the folder, only a VOICEVOX you
   have opened sings (`cantoria.voicevox.address` says where it answers).

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
`cantoria.general.cacheSizeMB` allows (**Cantoria: Forget Rendered Voices** clears them).

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
}, { voice: 'neutrino/MERROW' });
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
  says `{ toSing: 0, sung: 0 }`.
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
bundled. The bundled program is Cantoria's own code (Apache-2.0) with Sinsy and hts_engine API (Modified BSD; their licenses are in its `sinsy-dic` folder) and WORLD (Modified BSD; `COPYING.world`), published with the .NET runtime
(MIT), ONNX Runtime and YamlDotNet (MIT) and, on Windows, DirectML; see THIRD-PARTY-NOTICES.md.

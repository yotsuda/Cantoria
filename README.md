# Cantoria

A singing engine for notation software: give it notes with lyrics, get back a
voice singing them. Voices from different engines can sing one score together.

*Cantoria* (Italian) is the gallery of a church where the singers stand: the
place where voices gather. A score player cannot sing with a sound font, and
that is the one thing this library adds.

## What it is

- A **.NET library** (`Cantoria.Core`) with one small contract: a *score* of sung
  notes in, mono audio out (`ISinger`).
- A **command-line tool** (`cantoria`) that reads that score as JSON and
  writes a WAV, so any editor or notation program can call it without linking
  to it.
- A **VS Code extension** (`yotsuda.cantoria`, in `editors/vscode`) that carries the
  CLI, holds the settings (where each engine is, which voices) and sings for other
  extensions through an API — how Lily# sings its lyrics. A voice is named
  `engine/name` there (`neutrino/MERROW`), so a second engine's voice needs no new setting.
- **Backends** behind the same contract:
  - `diffsinger` — in-process inference of
    [DiffSinger](https://github.com/openvpi/DiffSinger) voices in the OpenUtau
    format (a folder with `dsconfig.yaml`, as OpenUtau installs them) through
    ONNX Runtime, after the C# implementation in
    [OpenUtau](https://github.com/stakira/OpenUtau). Japanese lyrics (kana),
    and English, Chinese and Korean where the voice has that language's dictionary
    (`dsdict-en.yaml`, `dsdict-zh.yaml`, `dsdict-ko.yaml`). On Windows the models run
    on the GPU through DirectML (any DirectX 12 device; the CPU where there is
    none, or with `--cpu`); on Linux and macOS on the CPU (CoreML on request,
    `--provider coreml`, untested).
  - `neutrino` — drives a user's own installation of
    [NEUTRINO](https://studio-neutrino.com/) (closed-source freeware, Japanese
    voices, Windows only) as a separate process.
  - `voicevox` — sings through a running
    [VOICEVOX](https://voicevox.hiroshiba.jp/) over its local API (Japanese
    voices); the user's own installation, never redistributed.

  The program is published for win-x64, linux-x64 and osx-arm64, and has been
  heard only on Windows so far.

Cantoria ships **no voice**. A voice (a trained model, a singing library) is
its author's work under its author's terms; the user installs one and points
Cantoria at it, exactly as a score player is pointed at a sound font.

## Status

Early. All three backends sing. A voice is named `engine/name`, and voices joined by `+` sing
together:

```
cantoria sing examples/yukiyakonko.json -o yuki.wav --voice neutrino/MERROW --neutrino-dir "C:\path\to\NEUTRINO"
cantoria sing examples/yukiyakonko.json -o yuki.wav --voice diffsinger/TIGER --voices-dir "C:\path\to\voices"
cantoria sing examples/yukiyakonko.json -o yuki.wav --voice voicevox/ずんだもん-ノーマル
cantoria sing examples/yukiyakonko.json -o yuki.wav --voice "neutrino/MERROW+diffsinger/TIGER" --neutrino-dir … --voices-dir …
cantoria voices --neutrino-dir … --voices-dir …
```

NEUTRINO writes the 8-second WAV in a few seconds (the first run loads its
models); a DiffSinger voice takes a few seconds on the CPU, under a second on a
GPU once its models are resident. `cantoria serve`
keeps the engines warm for an editor (JSON lines on stdin and stdout; the next
NEUTRINO render of a score takes under half a second). See
[docs/design.md](docs/design.md).

## The score

```json
{
  "sampleRate": 44100,
  "language": "ja",
  "notes": [
    { "start": 0.000, "duration": 0.682, "pitch": 60, "lyric": "ゆ" },
    { "start": 0.682, "duration": 0.227, "pitch": 62, "melisma": true },
    { "start": 0.909, "duration": 0.682, "pitch": 60, "lyric": "き" },
    { "start": 1.591, "duration": 0.227, "pitch": 62, "lyric": "や", "breath": true }
  ]
}
```

- Times are **seconds**: tempo, meter and repeats are the caller's business,
  already resolved. Gaps between notes are rests.
- `pitch` is a MIDI note number (60 = C4); fractional values are allowed.
- `language` says the words' language (`ja`, `en`, `zh`, `ko`). `lyric` is the
  syllable, in the language's own script: hiragana or katakana for `ja`, a
  syllable of the word for `en`, with `hyphen: true` on a note whose next
  syllable is of the same word, a Han character or pinyin for `zh`, a Hangul
  syllable or its romanisation for `ko`. A note with `melisma: true` continues the previous syllable's vowel
  and has no lyric of its own.
- `breath: true` marks a phrase end after the note (the singer breathes), and
  `inhale: true` an audible breath before the next one. `level` and `levelEnd`
  scale the sung audio across the note (absent: 1).

## Relation to Lily#

Cantoria was started for [Lily#](https://github.com/yotsuda/LilySharp), a
music notation compiler, so that its preview could sing the lyrics. It is a
separate project on purpose: Lily# is GPL-3.0 and Cantoria is Apache-2.0, so
Lily# may use Cantoria but Cantoria must never depend on Lily#, and anyone
else's notation program may use Cantoria too.

## License

Apache License 2.0 — see [LICENSE](LICENSE). Third-party works and how each
is used are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

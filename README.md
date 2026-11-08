# Cantilena

A singing engine for notation software: give it notes with lyrics, get back a
voice singing them.

*Cantilena* (Italian, from Latin *cantilena*, "little song") is the musical
term for a flowing, vocal melodic line — the kind a singer carries over the
instruments. That is the one thing a score player cannot do with a sound font,
and the one thing this library adds.

## What it is

- A **.NET library** (`Cantilena.Core`) with one small contract: a *score* of sung
  notes in, mono audio out (`ISinger`).
- A **command-line tool** (`cantilena`) that reads that score as JSON and
  writes a WAV, so any editor or notation program can call it without linking
  to it.
- A **VS Code extension** (`yotsuda.cantilena`, in `editors/vscode`) that carries the
  CLI, holds the settings (where NEUTRINO is, which voice) and sings for other
  extensions through an API — how Lily# sings its lyrics. A voice is named
  `engine/name` there (`neutrino/MERROW`), so a second engine's voice needs no new setting.
- **Backends** behind the same contract:
  - `neutrino` — drives a user's own installation of
    [NEUTRINO](https://studio-neutrino.com/) (closed-source freeware, Japanese
    voices) as a separate process.
  - `diffsinger` — in-process inference of
    [DiffSinger](https://github.com/openvpi/DiffSinger) voices in the OpenUtau
    format (a folder with `dsconfig.yaml`, as OpenUtau installs them) through
    ONNX Runtime, after the C# implementation in
    [OpenUtau](https://github.com/stakira/OpenUtau). Japanese lyrics (kana) so
    far; the CPU so far.

Cantilena ships **no voice**. A voice (a trained model, a singing library) is
its author's work under its author's terms; the user installs one and points
Cantilena at it, exactly as a score player is pointed at a sound font.

## Status

Early. Both backends sing. A voice is named `engine/name`:

```
cantilena sing examples/yukiyakonko.json -o yuki.wav --voice neutrino/MERROW --neutrino-dir "C:\path\to\NEUTRINO"
cantilena sing examples/yukiyakonko.json -o yuki.wav --voice diffsinger/TIGER --voices-dir "C:\path\to\voices"
cantilena voices --neutrino-dir … --voices-dir …
```

NEUTRINO writes the 8-second WAV in a few seconds (the first run loads its
models); a DiffSinger voice takes a few seconds on the CPU. `cantilena serve`
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
- `lyric` is the syllable as pronounced, in the language's own script
  (hiragana or katakana for `ja`). A note with `melisma: true` continues the
  previous syllable's vowel and has no lyric of its own.
- `breath: true` marks a phrase end after the note (the singer breathes).

## Relation to Lily#

Cantilena was started for [Lily#](https://github.com/yotsuda/LilySharp), a
music notation compiler, so that its preview could sing the lyrics. It is a
separate project on purpose: Lily# is GPL-3.0 and Cantilena is Apache-2.0, so
Lily# may use Cantilena but Cantilena must never depend on Lily#, and anyone
else's notation program may use Cantilena too.

## License

Apache License 2.0 — see [LICENSE](LICENSE). Third-party works and how each
is used are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

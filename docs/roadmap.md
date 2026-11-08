# Cantilena — roadmap

What comes after the `neutrino` backend, in order, and why. `design.md` says what Cantilena is;
this page says where it goes. Dates are not promised; each step has a *done when*.

## Aim

One contract (a sung score in seconds, a voice out), several engines behind it, on every
platform a notation program runs on, with no voice bundled and every voice's terms visible.
Cantilena is a library, a CLI and an editor extension — never an editor.

## A. Finish the first release

- **Publish**: the repository on GitHub, the extension on the Marketplace (win-x64).
- **Voice names are `engine/voice`** everywhere: `neutrino/MERROW`. The CLI prints them so
  already; the extension's setting and API accept the prefixed form and the API gains
  `voices()` → `[{ id, engine, language, licence? }]`. Additive, so `apiVersion` stays 1.
- **One NEUTRINO server for all its voices** (the voice is an argument of the render; today it
  is one server per voice, `design.md` §4.1).
- **`serve` protocol notes**: a `{"command":"voices"}` request; a `{"command":"status"}` that
  answers the engine's state, so an editor need not restart the process to ask.

*Done when* a second backend can be added without touching a setting's shape.

## B. The `diffsinger` backend (in-process ONNX)

The main step: open voices in many languages, CPU or GPU, every platform. After OpenUtau's
C# implementation (MIT; notice in `THIRD-PARTY-NOTICES.md`), reading voices exported in the
OpenUtau DiffSinger format — a folder the user installs, as a sound font is.

1. **B1 — spike**: load one voice folder (`dsconfig.yaml`, `phonemes.txt`, the dictionary,
   acoustic model, vocoder), hand-write the phoneme sequence for one phrase, run it through
   `Microsoft.ML.OnnxRuntime` on the CPU, hear it. Record times, memory, and the licence of
   the voice used. *Done when* there is a WAV and a page of numbers.
2. **B2 — the pipeline**: phonemiser per the voice's language (Japanese first, after
   OpenUtau's), the linguistic and duration models for phoneme timing, the pitch model (or a
   plain curve from the notes when the voice has none), variance where present, acoustic,
   vocoder. The contract's `melisma` becomes the `+` continuation; `breath` becomes the
   voice's breath phoneme where it has one, else a rest.
3. **B3 — providers**: CUDA and DirectML on Windows, CoreML on macOS, CPU everywhere; chosen
   by setting, auto by default.
4. **B4 — `serve` and the extension**: both engines resident; the voices list shows both; the
   setting `cantilena.voicesFolder` (where DiffSinger voices live) beside `neutrinoFolder`.
5. **B5 — platform builds**: `linux-x64`, `osx-arm64` (self-contained publish, as `win-x64`
   is), one VSIX per target. CI sings one phrase per platform with a small voice.

*Done when* the same score sings through `neutrino/…` and `diffsinger/…` by changing the voice
setting, and through `diffsinger/…` on Linux or macOS with no GPU.

## C. Languages

Phonemisation is the backend's; the caller sends the text as sung and the language.

- Japanese: kana (now); romaji → kana in the Japanese phonemiser so every caller gets it;
  kanji is the caller's to read (Lily# attaches a reading to the syllable).
- Through DiffSinger voices: English, Chinese, Korean and others as their dictionaries allow;
  the contract's `language` picks the dictionary; a mismatch is one sentence back.
- An optional per-note `phonemes` override in the contract for what a dictionary gets wrong
  (stress, an unusual split). Older engines ignore unknown fields already.

## D. Expression hints

Optional fields on a note or a span, mapped by each backend to what it understands and
ignored otherwise: loudness (0..1, from dynamics), legato (from slurs), vibrato depth/rate,
a pitch-bend curve. The contract stays small; nothing here is required.

## E. More backends

In the order their value against their cost looks today:

| Backend | How | Languages | Notes |
|---|---|---|---|
| `voicevox` | invoked (local HTTP server, song mode) | ja | free, popular; verify the song endpoints and the terms |
| `sinsy` | in-process (thin native shim) or invoked | ja | BSD, HMM/DNN, no GPU; a candidate for a fallback that always sings |
| `cevio` | COM, Windows | ja, en | commercial; on request |
| `guide` | in-process, no model (vowel/formant synthesis) | any (vowels) | zero setup; a pitch guide for rehearsal; worth shipping only if it sounds acceptable |

Not planned: engines with no external API (Synthesizer V, VOCALOID, ACE Studio); engines that
need a Python runtime as a product dependency; any voice conversion.

## F. A voice on every machine

Cantilena ships no voice. The extension offers to **download** one whose terms allow it, shows
those terms, and installs it into the voices folder; the Setup page lists each installed
voice's engine and licence. Which voice: research, licence first. Until one is found, Setup
points at where voices are published and what their terms say.

*Done when* a fresh machine can sing within ten minutes of installing the extension.

## G. For other callers

The CLI and the extension API are the public surface. A notation program that is not a VS
Code extension uses `cantilena serve` (JSON lines) or `cantilena sing`; a NuGet package of
`Cantilena.Core` when someone asks for in-process use. Nothing of Lily# is needed.

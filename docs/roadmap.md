# Cantilena — roadmap

What comes after the `neutrino` backend, in order, and why. `design.md` says what Cantilena is;
this page says where it goes. Dates are not promised; each step has a *done when*.

## Aim

One contract (a sung score in seconds, a voice out), several engines behind it, on every
platform a notation program runs on, with no voice bundled and every voice's terms visible.
Cantilena is a library, a CLI and an editor extension — never an editor.

## A. Finish the first release

- **Publish**: the repository on GitHub, the extension on the Marketplace (win-x64).
- **Voice names are `engine/voice`** everywhere: `neutrino/MERROW`. *Done 2026-11-08 (0.2.0)*:
  the CLI printed them so already; the extension's setting, picker, `status().voice` and
  `sing` take the prefixed form, and the API gained `voices()` → `[{ id, engine, name, label,
  language?, licence? }]`. Additive, so `apiVersion` stays 1.
- **One NEUTRINO server for all its voices** (the voice is an argument of the render; today it
  is one server per voice, `design.md` §4.1).
- **`serve` protocol notes**: a `{"command":"voices"}` request; a `{"command":"status"}` that
  answers the engine's state, so an editor need not restart the process to ask.

*Done when* a second backend can be added without touching a setting's shape.

## B. The `diffsinger` backend (in-process ONNX)

The main step: open voices in many languages, CPU or GPU, every platform. After OpenUtau's
C# implementation (MIT; notice in `THIRD-PARTY-NOTICES.md`), reading voices exported in the
OpenUtau DiffSinger format — a folder the user installs, as a sound font is.

1. **B1 — spike**: *done 2026-11-08* (Lily# session 1134; the probe and the numbers are in the
   owner's private lab, `sessions/p1134/`). A C# console over `Microsoft.ML.OnnxRuntime`
   (1.23.0) sang Cantilena's `examples/yukiyakonko.json` through a DiffSinger voice in the
   OpenUtau format, every stage included (dsdur linguistic + duration → timing aligned to the
   notes; dspitch → f0; acoustic → mel; the voice's bundled vocoder → samples). Checked by an
   autocorrelation pitch tracker per note: all 12 notes voiced, mean |sung − written| 0.19
   semitones. Times for 7 s of audio: CPU 5–18 s whole process (acoustic 1.3–14 s at 10–20
   steps; it swings), **DirectML warm 3.0 s whole process with the acoustic at 0.29 s** and the
   models' load being most of it → keep the sessions resident, as for NEUTRINO. What the format
   is, as met: each module (`dsdur/`, `dspitch/`, `dsvariance/`) has its own `dsconfig.yaml`,
   `phonemes.txt`, `.emb` speaker files and dictionaries; a Japanese dictionary's graphemes may
   be the kana themselves (TIGER) or romaji — try the kana first; `languages.json` only with
   `use_lang_id`; a voice may bundle `dsvocoder/`. The voice used (TIGER DS v106, CC BY-NC-ND +
   Commons Clause) and the OpenVPI vocoder (CC BY-NC-SA) were used for the local test only and
   nothing of them is in this repository.
2. **B2 — the pipeline**: *the library and the CLI done 2026-11-08* (Lily# session 1135:
   `Cantilena.Core/DiffSinger/`, `cantilena sing|voices|serve` with `engine/name` voices and
   `--voices-dir`; nets `DiffSingerCoreTests` and, where a voice is installed,
   `DiffSingerSingerTests` with the pitch check; the extension's side is B4). As planned:
   the spike ported into `Cantilena.Core/DiffSinger` as classes behind
   one `ISinger` (voice folder, speaker, provider): the voice and its modules, the phonemiser
   per the voice's language (Japanese first — kana morae to the dictionary's graphemes, kana or
   romaji; English and others through `dsdict-<lang>.yaml`), the linguistic and duration models
   for phoneme timing (a melisma note is an extra note of its word, done in the spike), the
   pitch model (or a plain curve from the notes when the voice has none), variance where present
   (untested in the spike: TIGER has none — test with a voice that has `dsvariance` first),
   acoustic, vocoder. `breath` becomes the voice's `AP` phoneme in the gap where its dictionary
   has one, else a rest. A net: a rendered phrase's pitch per note within half a semitone of the
   score (the spike's `analyze`).
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

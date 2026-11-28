# Cantoria — design

## 1. Purpose

A notation program plays its score through a sound font; the lyrics stay
silent. Cantoria takes the notes a part sings, with their syllables, and
returns the voice. It is a library and a CLI, not an editor: no piano roll,
no tuning UI. Tuning (pitch curves, timing) is out of scope until a caller
needs it.

## 2. The contract

```csharp
public interface ISinger
{
    string Name { get; }
    Task<SungAudio> SingAsync(SungScore score, CancellationToken ct = default);
}
public sealed record SungAudio(float[] Samples, int SampleRate);   // mono, −1..1
```

`SungScore` is the JSON in the README: seconds, MIDI pitch, syllable,
melisma flag, breath flag. Decisions:

- **Seconds, not ticks.** Every caller already knows its tempo map; making
  Cantoria understand tempo, meter, repeats and fermatas would copy the
  caller's hardest code. A backend that needs a tempo (NEUTRINO's MusicXML)
  gets a constant one and absolute durations, which is lossless.
- **Syllable, not phonemes.** Phonemization is language- and voice-specific
  (a Japanese CV voice, a DiffSinger voice with its own dictionary), so it
  belongs to the backend. The caller writes what the singer should sing.
- **Melisma is a flag on the note, not a lyric.** Each backend spells it its
  own way: `ー` for NEUTRINO, `+` for OpenUtau/DiffSinger, a vowel repeat for
  a plain CV voice.
- **Rests are gaps.** No rest objects. Breaths are flags, because a breath is
  a phrase boundary the singer acts on, not silence.
- **A caller's rounding is not an overlap.** `ScoreJson.Read` runs `SungScore.Tidy`:
  a note starting less than a millisecond before the one before it ends has that
  note cut to its start (the two touched in the caller's music; its start and
  duration were rounded apart — sixteenths at 132 are repeating decimals). A longer
  overlap is still a `Validate` problem.

## 3. Pipeline

Every modern singing synthesizer is these four stages; Cantoria's backends
are the two ways of running them.

1. **Phonemize**: syllables → phonemes (`こん` → `k o N`).
2. **Timing and pitch**: note lengths → phoneme times; note pitches → f0 curve
   (portamento, vibrato).
3. **Acoustic model**: phonemes, times, f0 → mel-spectrogram. The voice.
4. **Vocoder**: mel-spectrogram → waveform.

### 3.1 `neutrino` backend (first)

Runs the user's NEUTRINO Tau (v3.2.x) installation. What was learnt driving
it by hand (Lily# session 1124, 2026-11-08):

- Input is **uncompressed MusicXML**: one part, every note with a hiragana
  lyric, melisma as `ー`, rests as rests, a constant tempo. NEUTRINO ignores
  slurs, dynamics, articulations and key signatures, and warns if the score
  does not start with a rest (it adds one).
- `bin\musicXMLtoLabel.exe score.musicxml full.lab mono.lab -x settings\dic`
  writes Sinsy-format labels.
- The ".ex" package has no `Run.bat`: synthesis is a daemon,
  `bin\neutrino_server.exe`, started by the caller and bound to the caller's
  **process id** (the client finds it through its parent process, so the
  client must be a direct child of the process that started the server), and
  `bin\neutrino_client.exe full.lab out.lab out.f0 out.melspec out.wav
  model\<VOICE>\ -n <threads> [-m] -t` for one render. Control commands:
  `neutrino_client.exe --owner-pid <pid> status|help|shutdown`.
- Cold start ~45 s (model load, GPU); then **~0.5 s for a 4.5 s phrase** on an
  RTX 5070 Ti. Cantoria therefore keeps the server alive for the process's
  lifetime and shuts it down on dispose.
- The command lines are **configuration**, with these as defaults: NEUTRINO
  has changed its layout between versions (`Run.bat` → server/client) and
  will again.
- The **plain package** (no tuning tool; `Run.bat`, five voices) has no server: its
  `bin\neutrino.exe` takes the client's arguments and loads the models on every
  run. Cantoria uses the server where the folder has it and `neutrino.exe`
  otherwise (`NeutrinoOptions.HasServer`, 2026-11-18). Measured that day (RTX
  5070 Ti, MERROW, 43.2 s of song): plain v3.2.2 6.8 s every time, one 4.8 s
  phrase 3.6 s; the v3.2.6 server 6.6 s the first time, 2.2 s after. A plain
  package's voices (v3.2.3 models) sing under the v3.2.6 server as well.
  The package with the tuning tool has a `neutrino.exe` too, a 5 KB stand-in
  that prints its version and refuses to sing: renaming its server away does
  not make it the plain package (to try the plain one, unpack the plain one).
  Not done: singing the phrases that are new in ONE run of `neutrino.exe`
  (a score of 8 new phrases pays 8 loads).
- Nothing of NEUTRINO is redistributed. Cantoria only needs the folder.

### 3.2 `diffsinger` backend (second; sings since 2026-11-08)

In-process inference of DiffSinger voices in the OpenUtau format through
`Microsoft.ML.OnnxRuntime`, following OpenUtau's `OpenUtau.Core/DiffSinger/`
(MIT, written anew in `src/Cantoria.Core/DiffSinger/`). The voice is a folder
the user installs (as OpenUtau installs it); Cantoria reads it, never bundles it:

- `DiffSingerVoice`: the root `dsconfig.yaml` (acoustic model, phoneme list,
  speakers, embeds and mel settings), the modules `dsdur/`, `dspitch/`,
  `dsvariance/` — each with its own config, phoneme list, `.emb` speaker files and
  dictionaries (`dsdict-ja.yaml`, else `dsdict.yaml`) — and the vocoder (the
  voice's `dsvocoder/`, else a folder named in the options). The ONNX sessions are
  created on first use and kept.
- `KanaPhonemizer`: a syllable's kana morae to the dictionary's graphemes — the
  kana itself (TIGER's dictionaries) or its romaji — then to the model's symbols
  (with the `ja/` prefix where a voice uses one); a long-vowel mark repeats the
  vowel; a melisma note is an extra note of its word.
- `DiffSingerSinger`: the duration model's timing aligned to the notes (the vowel
  starts the note; the consonants before it are the model's and may begin before
  the note), the pitch model (else the notes' pitches flat), the variance model
  where the acoustic model wants its curves, the acoustic model (`depth`/`steps` or
  `speedup`, `gender`, `velocity`, `spk_embed`, `languages` as the voice declares),
  the vocoder; a breath flag becomes the voice's `AP` in the gap after the note;
  the audio is cut or padded so its 0 is the score's 0.
- `DsMath`: the frame rounding, word division, duration fitting, sampling speedup
  and the model-input check (inputs the model has not are dropped; one it wants
  and was not given is an error naming it).

**Providers** (`roadmap.md` B3, 2026-11-08): `DiffSingerOptions.Provider` is `Auto`
(DirectML where this build and this machine have it, else the CPU), `Cpu`, or
`DirectML` (or an error). The Windows program is built on the DirectML package of
ONNX Runtime (`Microsoft.ML.OnnxRuntime.DirectML`, `UseDirectML=true` in the
publish; its native library runs the CPU provider too), so one program serves a
machine with a DirectX 12 device and one without: `OnnxProviders.Create` appends the
DirectML provider (sequential execution, no memory pattern — what the provider asks),
and where that fails (no entry point in a CPU build, no device) or the first session
cannot be created, the voice falls back to the CPU and says why
(`DiffSingerVoice.ProviderNote`; `cantoria sing` prints "on DirectML" / "on the
CPU (…)", `serve` answers `provider`). The CLI's `--cpu` (the extension's
`cantoria.general.useGPU` off) is `Cpu`. CUDA is not wired: it needs the user's CUDA
and cuDNN and a package of hundreds of megabytes. CoreML comes in the standard package
on macOS, and `Auto` takes it there (2026-11-28, after three runs on the macOS runner, below):
with `OnnxProviders.CoreMLDefaults` (the vocoder alone, as an ML Program, on all compute
units; the other models on the CPU), each pair of `CANTORIA_COREML` (`key=value;…`, ONNX Runtime's CoreML
provider options) put over them. Nobody here has a Mac; `.github/workflows/mac.yml`
sings Qixuan on GitHub's macOS runner through `tools/bench-providers.ps1` (the CPU and
CoreML settings; first and warm seconds, the WAVs as an artifact). That runner is a VM
with no Metal, so it proves CoreML sings right, not how fast a real Mac's GPU is.
`CANTORIA_COREML`'s one key of Cantoria's own, `Models=acoustic,vocoder` (also `dur`,
`pitch`, `variance`, `linguistic`), runs only those on CoreML and the rest on the CPU
(`DiffSingerVoice.Open`'s role). `CANTORIA_TRACE` makes `serve` print each answer's
log (each model's seconds) on stderr. First run there (2026-11-28, M1 VM, 3 cores, Qixuan,
the 7 s example, 20 steps): the CPU 20.0 s first / 18.6 s warm; MLProgram on every model
fails in the duration predictor's linguistic encoder (an attention slice of zero elements,
which the CoreML provider refuses); NeuralNetwork sings, 17.9 / 14.4 s, with the graphs cut
into 45–68 CoreML partitions covering about half the nodes. Second run: the acoustic
model's encoder fails MLProgram the same way (the zero-element slice is in the models, and
Qixuan's terms forbid changing them), and in NeuralNetwork it is no faster than the CPU
(18.7–23.5 s against 17.0); the vocoder on CoreML NeuralNetwork took 2.35 s against the CPU's
7.74. The runner's seconds swing by a third between runs, so the bench now reports the
fastest of three warm requests. Third run, the vocoder alone on CoreML: 1.54 s (MLProgram,
ALL), 1.65 (NeuralNetwork), 1.17 (CPUAndNeuralEngine), 0.96 (CPUOnly) against the CPU's 7.29;
the whole warm song 14.8–18.8 s against 27.7; the WAVs' levels alike (RMS −25.5 to −26.6 dB, no
clipping). The VM has no GPU, so CoreML's own CPU path (Accelerate) is what wins there;
the default stays ALL so that a real Mac's GPU and Neural Engine can be chosen. On the CPU a 7 s phrase renders in 4–18 s at 10–20 steps; on DirectML
the acoustic model takes 0.3 s and the phrase under a second once the models are
resident. The variance path is written after OpenUtau but untested (the voice at
hand has no `dsvariance`).

Spike done 2026-11-08 (`roadmap.md` B1): the whole pipeline ran in C# on a real voice and sang
the README's score in tune (mean 0.19 semitones off the written notes, every note voiced). On the
CPU a 7 s phrase took 5–18 s; on DirectML, with the models resident, 3 s for the process and
0.3 s for the acoustic model — so this backend wants `serve`'s resident engine as much as
NEUTRINO does, and the GPU provider is worth its separate package.

The models run a phrase at a time (2026-11-10). On DirectML one run sang 90 s and failed at 140 s:
a stack overflow in the pitch model, and with a larger stack `80070057` from the acoustic model's
GRU. `PhraseCut` cuts at each rest of 0.3 s or more, where the cut cannot be heard and no edit moves
a cut outside its own phrase, and a run still longer than 60 s in its widest gap. Each phrase is
sung from its own time 0 and kept in `PhraseCache` (in memory, 256 MiB a voice), so an edit sings
only its phrase and a phrase moved or repeated is not sung again. Harukaze, 187 s in 11 phrases on
DirectML: 28 s the first time with the models' loading, 2.6 s after changing one syllable, 0.12 s
for the same notes two bars later. On the CPU the first time costs more than one long run (77 s
against 48 s).

`PhrasedSinger` does the same around a singer of whole scores, each phrase given 0.5 s of rest before
it for its first consonant; a melisma, a word that goes on, or a breath into the next note is never
cut. VOICEVOX and NEUTRINO sing through it. Harukaze: VOICEVOX 29.4 s whole, 18.0 s in phrases, 1.8 s
after changing one syllable; NEUTRINO 29.6 s whole, 24.0 s in phrases, 1.5 s after one syllable. The
owner compared the renders by ear and heard no problem.

With `serve --cache-mb <n>` the phrases are also kept on disk (`PhraseDisk`, under `<out-dir>/phrases`,
16-bit as the WAVs are), so an engine started again sings only what changed: 5.3 s for one syllable
in a fresh process, the models' loading included, against 29 s before. One budget holds the rendered
WAVs and the phrases together, the longest unused going first; a WAV removed is laid together again
from its phrases in a tenth of a second, so the two need no limits of their own. The extension's
`cantoria.general.cacheSizeMB` is that budget.

### 3.4 `sinsy` backend (fourth; sings since 2026-11-28)

[Sinsy](http://sinsy.sourceforge.net/) 0.92 and hts_engine API 1.10 (Modified BSD; r9y9's git
forks, fetched at pinned commits by SHA-256) compiled with a C entry point of Cantoria's into one
shared library, `cantoria_sinsy` (`native/sinsy`; `tools/build-native.ps1` builds it for the machine
it runs on into `native/out/<rid>/`, which `Cantoria.Core`'s project copies beside the program with
Sinsy's Japanese dictionary in `sinsy-dic/`). 666 KB on Windows. Called in process through
`LibraryImport` (`SinsyNative`). Roadmap E's "fallback that always sings": no GPU, no other program,
a voice of a few MB (`VoiceOffers`: NIT SONG070 F001, CC BY 3.0).

- **The score goes in note by note** (`Sinsy::addNote`/`addRest`, no MusicXML): 60 to the quarter
  and Sinsy's 960 ticks a quarter, so a tick is 1/960 s; each note starts and ends on the tick
  nearest its time, so rounding does not add up. A second's rest leads in (a first consonant may
  start before its note) and is cut, as is the rest Sinsy fills the last 4/4 bar with (0.5 s of
  release kept). Melisma is `ー` (the dictionary's MACRON), a breath or an inhale a breath mark,
  the pitch the nearest semitone (Sinsy's pitch is MIDI − 12). Kana only (`NotKanaProblem`, shared
  with NEUTRINO). Level, vibrato, bend and voice settings are not Sinsy's.
- **The shim drives hts_engine itself**: Sinsy only turns the score into full-context labels
  (`createLabelData(false, 1, 2)`, what its own synthesize uses) and the shim's `HTS_Engine`, with
  the voice loaded once, sings them with phoneme alignment. This is for the range: an HMM voice
  sings only what it was trained on, and NIT SONG070 F001 sang every note under G#3 at G#3 (C3 and
  F3 alike, 2026-11-28), A#3 a little flat. `SinsySinger.Shift` moves a phrase whose lowest note is
  under C4 (`SinsyOptions.Low`) up to it, or one over E5 down, and hts_engine's additional half tone
  moves the pitch back: the spectrum is sung where the voice knows it, the excitation where the
  score is. A bass line from E2 is sung in tune. A phrase wider than the range (16 semitones) is
  centred on it and its ends are not reached; through `PhrasedSinger` each phrase has its own shift.
- **Sinsy's log goes to `std::cout`**, which `serve` speaks JSON on: the shim takes `cout` and `cerr`
  for the length of each call and returns the text as the error (one call at a time in the process).
  hts_engine's own warnings are C `stderr` and stay there.
- Voices: every `.htsvoice` in the Sinsy voices folder or one folder down (a release unpacked as it
  comes), named by the file; female or male by the Nitech name (`_f001`). Measured: the 8-second
  example in 0.4 s, the process's start included.
- One build per platform: the CI builds the library on each and fetches the voice as a user does,
  so the Sinsy tests and the published program sing on all three; the release builds Linux's and
  macOS's on their runners for the Windows job that packs.

### 3.5 `utau` backend (fifth; sings since 2026-11-28)

UTAU voicebanks — a folder of recordings with `oto.ini` — sung by Cantoria's own resampler and
wavtool (`UtauSinger`) over [WORLD](https://github.com/mmorise/World) (Modified BSD; fetched at a
pinned commit by SHA-256), compiled with a C entry point of Cantoria's into `cantoria_world`
(`native/world`, 245 KB on Windows; `native/` is one CMake project for both libraries and
`tools/build-native.ps1` builds them). Called through `LibraryImport` (`WorldNative`) over flat
arrays. Like Sinsy, no GPU and no other program; unlike Sinsy, thousands of free voicebanks exist.
Which can be offered for download is in [utau-voices-2026-11-28.md](utau-voices-2026-11-28.md);
none is offered yet (the owner's choice: a voicebank the user installs needs no terms of ours).

- **Reading a voicebank** (`UtauVoicebank`): every `oto.ini` in the folder and below (the folder's
  own first, then its subfolders' by name; the first line of an alias wins), `prefix.map` (the
  prefix and suffix an alias takes at each pitch: how a bank recorded at several pitches picks one)
  and `character.txt`'s `name=` (the label). UTF-8 where the bytes decode as UTF-8, else Shift_JIS,
  as UTAU writes. A bank is a folder of the UTAU voices folder with `oto.ini` in it or a folder down.
- **Choosing the recording** (`UtauPhonemizer`): each kana mora is one recording; tried in order,
  each with the pitch's prefix and suffix first: `a か` (VCV from the vowel sounding before; `n`
  after ん), `- か` after silence (a rest, a breath, the start), then `か` (CV), and each as the kana
  then its romaji. A syllable of several morae (`こん`) gives each mora after the first at most
  0.12 s of the note's end. A melisma note or `ー` holds the sound before on; `っ` is silence the
  next mora starts out of. A mora with none of its aliases is `has no sound for "…" (note N`, the
  words Lily# points at a note by. Japanese is kana only (`NotKanaProblem`).
- **Languages**: a bank sings what its aliases say (`UtauVoicebank.LanguagesOf`, the extension's
  `utauLanguagesOf` the same): `ja` where an alias is kana, `en` where it has `- @`, `@ -`, `- {` or
  `{ -` (an English CVVC bank in X-SAMPA), `ja` where neither (romaji). A score in another language
  is refused before singing, and English words by a bank that has no English.
- **English** (`UtauEnglishPhonemizer`, for X-SAMPA CVVC banks laid out as Kasane Teto English): a
  word is read as the CMU dictionary lists it, else by the model trained on it (`EnglishG2p`, what a
  DiffSinger voice falls back on), dealt over its notes a vowel each (`EnglishPhonemizer.Deal`), and
  turned into X-SAMPA (`ah` `V`, the schwa `@`, `er` `3`). Each vowel is sung on the recording of
  its consonant running into it: two consonants where the bank has them (`kju`), else one (`wI`),
  with `- ` after silence; the vowel alone (`- @`, `@`) or from the vowel before (`u A`) where no
  consonant comes between. Consonants before that one take the longest cluster the bank has,
  through to it (`- tw` before `wI`, `st` before `tA`), else single ones. Between two vowels: from
  the vowel into the first consonant (`I N`, `@ l`), one consonant into the next where there is one
  (`r d`), a word's last consonant beginning the next word where that has none of its own ("hand
  out": `daU`). These pieces are laid before the vowel's beat. A piece the next one begins on the
  same consonant as (`A k` then `kA`, `str` then `rI`) lasts the next one's preutterance, which
  sounds that consonant again; one followed by another consonant (`@ l` then `tw`, `I N` then
  `k@`) first sounds its own (as much of its recording as follows its preutterance, 0.03-0.2 s),
  then the next one's preutterance less its overlap. All within 0.6 of the sound before, or 0.4 s
  of the rest before a phrase. Laid the first way throughout, the owner heard "twinkle twinkle
  little star" as "we-ketz we-ketz we- testou" (2026-11-28): the N of twin and the l of kle and tle
  were cut off within 30 ms by the consonant after. A recording after such a bridge (`Bridge`, the
  Japanese CVVC `a k` too) may take nine tenths of it with its preutterance, not half
  (`UtauSinger.Timings`); a recording that begins on a vowel (`a k`, `@ l`, `FromVowel`) whose
  preutterance is cut loses the front of that vowel, so its consonant still begins on its beat.
- **Syllabic consonants**: a schwa before `l`, `n` or `m` that ends its syllable (twinkle, little,
  button) takes at most 0.1 s (0.35 of the syllable) and the consonant's piece holds the rest,
  its recording stretched; before `l` the vowel is the bank's `U` where it has `U l` (`kU`, `U l`).
  Held before another consonant (twinkle twinkle: the l before `tw`) it is sung on the word-final
  recording (`U l-`), which has the l held: Teto's `U l` has 17 ms of l, and stretched it was
  heard as a muffled, rolled "kru".
  Held as Teto sings `@`, the owner heard "win-kEtz", "littEl", and asked for "twin-kULe"
  (2026-11-28).
- The sung audio starts at the score's 0 s, so a consonant before a first note at 0 (`- tw`) is
  cut off: the test's Twinkle starts at 0.5 s. A phrase ends on `A -` where it ends on a vowel and has a rest; else on its last
  consonants (`A r-`; `{ l` then `l mz-`, else pair by pair), each as long as its recording after its
  preutterance, ending up to 0.15 s into the rest and beginning no sooner than halfway through the
  vowel, so a breath with no rest still has them. A consonant the bank lacks is passed over; a vowel
  is refused with the note named. Not yet: VCCV (Hitsune Kumi), ARPAsing, a note's `phonemes`.
- **CVVC banks**: where the mora took a CV recording after a vowel and the bank has the bridge from
  that vowel into its consonant (`a k` before か; `a sh`, else `a s`, before し; `n` for ん), the
  bridge takes the end of the sound before: as long as the CV recording's preutterance, at most
  0.25 s and half the sound before (as UTAU users lay a VC note). A vowel mora has no bridge.
- **Endings**: where the bank has one (`a -`, else `a R`), a phrase with a rest after it ends on its
  last vowel dying into silence, for up to 0.3 s of the rest; a rest under 80 ms (a breath) has none.
- **Laying it in** (as UTAU's wavtool): a recording starts its preutterance before the note's beat
  and its first overlap crossfades with the sound before; where preutterance minus overlap is more
  than half the note before, both are cut to fit. A sound runs to where the next one's overlap ends,
  or 30 ms past its note's end. A second before the score leads in and is cut.
- **Singing it** (as a resampler): the recording's stretch (offset to cutoff) is analysed once a
  singer (Harvest 60-1100 Hz, CheapTrick, D4C with its own voicing judgement off, 5 ms frames: its
  LoveTrain called the loud second half of the い in Teto's VCV `u き` aperiodic, sung as breath); the output frames take the consonant
  (offset + consonant) as recorded and stretch the rest evenly where the note is longer, the
  spectral envelope and aperiodicity interpolated between frames; the pitch is the score's — each
  note's, held through the rest after it, gliding 40 ms either side of the start of a note that
  follows without a rest, with the written vibrato and bend added as `Vibrato`/`Bend` add them to
  a DiffSinger voice's curve — where the recording is voiced, 0 where it is not. The recording's
  own pitch wobble is not kept. The audio is at the voicebank's rate (a file at another rate is
  resampled to the first's).
- Measured (2026-11-28): the 7-second example in about 2 s, every long note within half a semitone,
  by Kasane Teto's 単独音 (CV) and 統合 (CV + 連続音) and Namine Ritsu's まばゆ (CVVC, three pitches by
  `prefix.map`); Kasane Teto English sang "Twinkle, twinkle, little star" (8.3 s) in 2.3 s, every
  note within 0.1 semitone. A synthetic buzz held exactly at 220 Hz was analysed into an
  envelope that sang E4 an octave low; the test's voice wobbles as a singer's does.
- Tests: `UtauTests` makes its own voicebank (a buzz with vowel-like resonances, noise for a
  consonant) and sings it wherever `cantoria_world` is built, so the CI sings on all three
  platforms with no voicebank fetched; `CANTORIA_UTAU_VOICE` (a voicebank's folder) adds a real one,
  and `CANTORIA_UTAU_ENGLISH_VOICE` an English one singing Twinkle (its recordings in the test's output).
- Not yet: other English styles (VCCV, ARPAsing), the bank's own pitch modulation, flags, a disk cache of the
  analyses (each recording is analysed again in a new process), offering a voicebank for download.

## 4. CLI

```
cantoria sing score.json -o out.wav --voice neutrino/MERROW --neutrino-dir C:\NEUTRINO
cantoria sing score.json -o out.wav --voice diffsinger/TIGER --voices-dir C:\voices
cantoria sing score.json -o out.wav --voice C:\voices\TIGER        # a path holding dsconfig.yaml
cantoria voices [--neutrino-dir …] [--voices-dir …]                 # what is installed, as engine/name, with each voice's gender and languages
cantoria voices --language en [--json]                              # those that sing it; as the API's entries
cantoria plan score.json [--voice …] [--gender …] [--json]          # who would sing it here, without singing (§4.3)
cantoria providers [--json]                                         # where DiffSinger models can run here (CPU, DirectML, CoreML; what auto picks), the hardware GPUs; --json: {gpus, auto, note}
cantoria serve [the same]                                           # a resident singer for an editor (below)
cantoria --version
```

A voice is `engine/name`; a bare name is NEUTRINO's. The folders come from the
options, else the environment (`CANTORIA_NEUTRINO_DIR`, `CANTORIA_VOICES_DIR`,
`CANTORIA_VOCODER_DIR`), else `cantoria.json` in the user's config folder
(`%APPDATA%\cantoria` on Windows, `~/.config/cantoria` elsewhere: `neutrino.folder`,
`diffsinger.voices`, `diffsinger.vocoder`). `--provider auto|cpu|directml|coreml` picks where
a DiffSinger voice's models run (§3.2; `--cpu` is `cpu` for every engine). NEUTRINO runs on
Windows only. Exit codes: 0 sung, 1 the backend failed (its message on stderr), 2 bad
arguments, score or settings, 3 (`plan` only) something asked for has no singer. `sing`
refuses words in a language the voice says it does not sing (exit 1) with the reason `plan` gives.

### 4.1 `serve` — the resident singer

An editor that renders a voice on every Play cannot pay the engine's warm-up
each time (NEUTRINO's server binds to the process that started it, and a cold
start is seconds to a minute), so `cantoria serve` keeps the engine for the
life of one process and talks **JSON lines** on stdin/stdout, UTF-8 both ways:

```
→ {"id":"1","score":{ …the README's score… },"voice":"MERROW","output":"C:\\x.wav"}
→ {"id":"2","scoreFile":"C:\\score.json"}
→ {"command":"shutdown"}
← {"ready":true,"backend":"neutrino","voices":["MERROW"],"outDir":"…"}        (first, once)
← {"id":"1","wav":"C:\\x.wav","duration":8.0,"sampleRate":48000,"seconds":0.42}
← {"id":"2","error":"note 0 at 0 s: no lyric (write one, or mark the note melisma)"}
```

- `score`, `voice` (an `engine/name`; default the `--voice` option, else
  `neutrino/MERROW`) and `output` (default `<out-dir>\<id>.wav`, `--out-dir`
  default `%TEMP%\cantoria`) are optional; `score` or `scoreFile` is required.
  An `id` is echoed; a request without one gets its ordinal. The answer names
  the `voice` that sang. `{"command":"voices"}` answers `{"voices":[…]}`.
- `{"command":"plan","asked":[{"part":"alto","gender":"female","language":"la"}],"defaults":{"female":"","male":""}}`
  answers `{"id":…,"plan":{"ready":false,"singers":[…]}}` — §4.3's JSON, inside `plan` so that its
  `ready` is not taken for the first line's. Instead of `asked`, a `score` or `scoreFile` with
  `voice` and `gender` asks for that score's one voice. `defaults` are the editor's default voices
  (absent: the program's own); `voicevoxUrl` is where VOICEVOX answers, and `""` says the editor
  asked and nothing does, so that the program does not wait for it again.
- Requests are answered **in order, one at a time**; the caller may pipeline
  them. A bad request is an `error` line, never a dead process. End of stdin,
  or `shutdown`, ends the process and the engines' servers and sessions with it.
- One singer per voice named, created on first use (NEUTRINO: one server
  each — the voice is an argument of the render, so this could be one server;
  not yet. DiffSinger: one set of sessions per voice).
- Measured 2026-11-08 (RTX 5070 Ti, the 8 s example): first request 3.8 s,
  the next 0.4 s.

### 4.2 The VS Code extension (`editors/vscode`, `yotsuda.cantoria`)

The engine's home for editors (the owner's decision 2026-11-08, the shape the Lily#
OMR reader took): it carries the `cantoria` program (under `bin/<rid>/`: one VSIX per
platform target — win-x64, linux-x64, osx-arm64 — since 0.4.0, roadmap B5), holds the settings an engine needs — `cantoria.neutrino.installFolder`,
`cantoria.general.defaultFemaleVoice`, `cantoria.general.defaultMaleVoice`, `cantoria.general.useGPU`, `cantoria.neutrino.threads`, and `cantoria.developer.program` for a
developer's build — with pickers (**Choose NEUTRINO Folder…**, **Choose Default Female Voice…**, **Choose Default Male Voice…**) and a
**Check Setup**, and sings for other extensions through the API its `activate` returns:

```ts
{
  version: string, apiVersion: 1,
  status(): Promise<{ ready, program, backend: 'neutrino', neutrinoFolder, voice, voices, problems }>,
  voices(): Promise<{ id, engine, name, label, language?, licence? }[]>,    // id = 'engine/name' (0.2.0)
  sing(score, { voice?, signal? }): Promise<{ wav, duration, sampleRate, cached }>,   // the README's score; voice an id; signal lets go at the next phrase
  release(): void,                                                          // let the engine go
  resolveSinger(singer, { language?, gender? }): Promise<{ voice, note? }>, // the voice a score's singer comes to here
  plan(score | { asked }, { voice?, gender? }): Promise<{ ready, singers }>, // who would sing it, without singing (§4.3)
}
```

`apiVersion` is raised only when a call changes incompatibly; a method added beside the others
(`parameters`, `resolveSinger`, `plan`) leaves it at 1, and a caller looks for the method.

A voice is named **`engine/name`** everywhere — `neutrino/MERROW` — in the setting, the picker,
`status().voice` and `sing`'s `voice` (a bare name is NEUTRINO's, the first engine's), so a
second engine's voice needs no new setting and the rendered WAVs are keyed by it.

Inside, one resident `serve` (§4.1) per extension host (`src/engine.ts`), started on the first
`sing`, its WAVs named by the key of the score and the voice under the extension's storage, so
a phrase is sung once per machine (**Forget Rendered Voices** clears them); a changed engine
setting lets the engine go. `sing` throws one sentence when it cannot (no program, no NEUTRINO
folder, a score the engine refused). Lily# calls it from its preview (its
`docs/singing-design.md` §5); any other notation extension can. `tools/pack-extension.ps1`
publishes the program self-contained into `editors/vscode/bin/<rid>` and packs one VSIX per
target (`vsce package --target win32-x64 | linux-x64 | darwin-arm64`, each with its own program
only; win-x64 on the DirectML package, the others on the CPU package); publishing to the
Marketplace is the owner's. A VSIX packed on Windows carries no file modes, so the extension
gives the program its execute bit before the first start on Linux and macOS. NEUTRINO is
Windows-only: off Windows the program reads its folder as not set and the extension's Check
Setup says so; `.github/workflows/ci.yml` builds and tests on the three platforms and runs the
published program (`--version`, `check`, `providers`) on each — no voice is installed there, so
nothing is sung in CI.

### 4.3 `plan` — who would sing it

A score can ask for a singer by name, by gender alone, in a language; which installed voice that
comes to is known only on the machine. A Latin piece played in silence because no installed
voice sings Latin, and nothing said why (2026-11-13). The owner's decision: a caller's compiler
stays ignorant of what is installed — its output is the same on every machine — and Cantoria,
which knows, answers **who would sing this** without singing:

```
cantoria plan score.json [--voice neutrino/MERROW | --gender female] [--language la] [--json]
cantoria plan asked.json        # {"asked":[{ "part"?, "key"?, "voice"?, "gender"?, "language"? }, …]}
alto: female, la -> no installed voice sings Latin (la); installed voices sing ja, en, zh, ko

{ "ready": false,
  "singers": [ { "part": "alto", "asked": { "gender": "female", "language": "la" },
                 "voice": null, "reason": "no installed voice sings Latin (la); installed voices sing ja, en, zh, ko",
                 "languages": ["ja", "en", "zh", "ko"] } ] }
```

- A score asks for one voice in its `language` (absent: `ja`, as it is sung); `asked` lists
  several, each field optional, `part` and `key` the caller's own and given back. `voice` is what
  a `singer` line writes: an `engine/name` or a gender's word. One voice an ask: a `+` joined
  several until 2026-11-18 (each sung in turn and mixed), which gave nothing two asks do not.
- `voice` is the `engine/name` that would sing — what to hand `sing` — with a `note` when it is
  not the one asked for (`neutrino/NOPE is not installed; singing with neutrino/MERROW`); or
  `null`, and `reason` says why in a score writer's words: no installed voice sings the language
  and what they do sing, a default voice that does not sing the words, no voice installed and why
  each engine has none (its folder is not set, VOICEVOX is not running), what the engine's own
  check of the voice says (no vocoder). `languages`: what the voice sings; with none, what the
  installed voices sing between them. `ready`: each has a singer. Exit code 0 then, else 3.
- **The rule has one home**, `SingerResolver` (`src/Cantoria.Core/SingerPlan.cs`): a voice named
  and installed sings; else the default voice of the gender the line says (or the caller's
  `gender`, else female) — the one set (`--default-female-voice`, the extension's
  `cantoria.general.defaultFemaleVoice`), else the first voice listed that says it is that gender and
  sings the language; with none, the other gender's, else any voice, and the `note` says so. A
  default that is set sings whatever the language, so it can be the `reason`. Then what `sing`
  refuses: a voice that says what it sings and does not sing these words (one that does not say
  is let try). The order of the list is part of the answer, so `VoiceEntries` reads the voices
  as the extension lists them: NEUTRINO's (`info.toml`'s speaker), the DiffSinger folder's (the
  languages of its `dsdict-*.yaml`, its speakers after it), the running VOICEVOX's; a DiffSinger
  voice's and a VOICEVOX character's gender from `VoiceGenders`.
- Nothing is loaded and nothing started: folders and small files are read, and VOICEVOX is asked
  for its singers (two seconds when nothing answers).
- The extension's `resolveSinger` (`editors/vscode/src/core.ts`: `resolveSinger`,
  `defaultVoiceFor`, `firstOfGender`, `languageProblem`, and the readers `voiceEntriesIn`,
  `diffSingerEntriesIn`, `voicevoxEntriesFrom` with `diffSingerGenders.ts` and
  `voicevoxGenders.ts`) is the same rule in TypeScript, kept while its callers move to `plan`;
  the tests hold the two gender tables to each other and run the same cases through both rules.
- `sing` itself resolves nothing: it takes a voice (`--voice`, default `neutrino/MERROW`) and
  fails when it is not there. A plan's `voice` is what to give it.

## 5. How a caller uses it (Lily#'s plan)

1. The editor resolves the part that sings (the one the score's `lyrics`
   follow) into the JSON score: seconds from its own tempo map, the syllable
   each note carries, `melisma` where the lyric extends, `breath` at a breath
   mark or a long rest.
2. It keeps one `cantoria serve` child (§4.1) and sends each voice's score
   with a cache key over the score's bytes, the voice and the speed, keeps the
   WAV, and plays it alongside its sound-font synth at the same clock; muting
   the part mutes the voice; exports mix the WAV in. (Lily#'s side of this is
   its `docs/singing-design.md`.)
3. The setting names the backend and voice the way a sound font is named:
   a path the user chose, nothing bundled.

## 6. What comes next

The order of work after the `neutrino` backend — the `diffsinger` backend, languages,
expression hints, more engines, platform builds, a downloadable first voice — is
[roadmap.md](roadmap.md).

## 7. Licensing boundaries

- Cantoria: Apache-2.0. May include MIT code (OpenUtau) with notices; may
  reference Apache/MIT packages.
- Must **not** depend on Lily# (GPL-3.0): the dependency runs one way.
- Must **not** include any voice, model, NEUTRINO file or dataset. Public
  singing datasets are mostly research-only; training a voice for
  distribution needs recordings made for it with the singer's consent.
- NEUTRINO's agreement forbids its use in ML voice conversion; Cantoria
  synthesizes from scores only.

# Cantilena — design

## 1. Purpose

A notation program plays its score through a sound font; the lyrics stay
silent. Cantilena takes the notes a part sings, with their syllables, and
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
  Cantilena understand tempo, meter, repeats and fermatas would copy the
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

## 3. Pipeline

Every modern singing synthesizer is these four stages; Cantilena's backends
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
  RTX 5070 Ti. Cantilena therefore keeps the server alive for the process's
  lifetime and shuts it down on dispose.
- The command lines are **configuration**, with these as defaults: NEUTRINO
  has changed its layout between versions (`Run.bat` → server/client) and
  will again.
- Nothing of NEUTRINO is redistributed. Cantilena only needs the folder.

### 3.2 `diffsinger` backend (second)

In-process inference of OpenVPI DiffSinger ONNX exports through
`Microsoft.ML.OnnxRuntime`, following OpenUtau's `OpenUtau.Core/DiffSinger/`
(MIT): the voice's `dsconfig.yaml`, the phoneme dictionary, the linguistic,
duration, pitch and variance models, the acoustic model, the vocoder. Japanese
phonemization after OpenUtau's phonemizers. GPU through the CUDA or DirectML
provider when present; CPU otherwise (seconds, not sub-second).

The voice is a folder the user installs. Cantilena reads it, never bundles it.

## 4. CLI

```
cantilena sing score.json -o out.wav --backend neutrino --voice MERROW
cantilena sing score.json -o out.wav --backend diffsinger --voice C:\voices\xxx
cantilena voices --backend neutrino          # what is installed
cantilena serve --backend neutrino           # a resident singer for an editor (below)
cantilena --version
```

Settings (the NEUTRINO folder, the voices folder, command templates) come from
`cantilena.json` beside the executable, the user's config directory, or
options. Exit codes: 0 sung, 1 the backend failed (its message on stderr),
2 bad arguments or score.

### 4.1 `serve` — the resident singer

An editor that renders a voice on every Play cannot pay the engine's warm-up
each time (NEUTRINO's server binds to the process that started it, and a cold
start is seconds to a minute), so `cantilena serve` keeps the engine for the
life of one process and talks **JSON lines** on stdin/stdout, UTF-8 both ways:

```
→ {"id":"1","score":{ …the README's score… },"voice":"MERROW","output":"C:\\x.wav"}
→ {"id":"2","scoreFile":"C:\\score.json"}
→ {"command":"shutdown"}
← {"ready":true,"backend":"neutrino","voices":["MERROW"],"outDir":"…"}        (first, once)
← {"id":"1","wav":"C:\\x.wav","duration":8.0,"sampleRate":48000,"seconds":0.42}
← {"id":"2","error":"note 0 at 0 s: no lyric (write one, or mark the note melisma)"}
```

- `score`, `voice` (default the `--voice` option, else `MERROW`) and `output`
  (default `<out-dir>\<id>.wav`, `--out-dir` default `%TEMP%\cantilena`) are
  optional; `score` or `scoreFile` is required. An `id` is echoed; a request
  without one gets its ordinal.
- Requests are answered **in order, one at a time**; the caller may pipeline
  them. A bad request is an `error` line, never a dead process. End of stdin,
  or `shutdown`, ends the process and the engine's server with it.
- One singer per voice named, created on first use (NEUTRINO: one server
  each — the voice is an argument of the render, so this could be one server;
  not yet).
- Measured 2026-11-08 (RTX 5070 Ti, the 8 s example): first request 3.8 s,
  the next 0.4 s.

### 4.2 The VS Code extension (`editors/vscode`, `yotsuda.cantilena`)

The engine's home for editors (the owner's decision 2026-11-08, the shape the Lily#
OMR reader took): it carries the `cantilena` program (win-x64 to begin with, under
`bin/<rid>/`), holds the settings an engine needs — `cantilena.neutrinoFolder`,
`cantilena.voice`, `cantilena.gpu`, `cantilena.threads`, and `cantilena.program` for a
developer's build — with pickers (**Choose NEUTRINO Folder…**, **Choose Voice…**) and a
**Check Setup**, and sings for other extensions through the API its `activate` returns:

```ts
{
  version: string, apiVersion: 1,
  status(): Promise<{ ready, program, backend: 'neutrino', neutrinoFolder, voice, voices, problems }>,
  voices(): Promise<{ id, engine, name, label, language?, licence? }[]>,    // id = 'engine/name' (0.2.0)
  sing(score, { voice? }): Promise<{ wav, duration, sampleRate, cached }>,   // the README's score; voice an id
  release(): void,                                                          // let the engine go
}
```

A voice is named **`engine/name`** everywhere — `neutrino/MERROW` — in the setting, the picker,
`status().voice` and `sing`'s `voice` (a bare name is NEUTRINO's, the first engine's), so a
second engine's voice needs no new setting and the rendered WAVs are keyed by it.

Inside, one resident `serve` (§4.1) per extension host (`src/engine.ts`), started on the first
`sing`, its WAVs named by the key of the score and the voice under the extension's storage, so
a phrase is sung once per machine (**Forget Rendered Voices** clears them); a changed engine
setting lets the engine go. `sing` throws one sentence when it cannot (no program, no NEUTRINO
folder, a score the engine refused). Lily# calls it from its preview (its
`docs/singing-design.md` §5); any other notation extension can. `tools/pack-extension.ps1`
publishes the program self-contained into `editors/vscode/bin/win-x64` and packs the VSIX
(`vsce package --target win32-x64`); publishing to the Marketplace is the owner's.

## 5. How a caller uses it (Lily#'s plan)

1. The editor resolves the part that sings (the one the score's `lyrics`
   follow) into the JSON score: seconds from its own tempo map, the syllable
   each note carries, `melisma` where the lyric extends, `breath` at a breath
   mark or a long rest.
2. It keeps one `cantilena serve` child (§4.1) and sends each voice's score
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

- Cantilena: Apache-2.0. May include MIT code (OpenUtau) with notices; may
  reference Apache/MIT packages.
- Must **not** depend on Lily# (GPL-3.0): the dependency runs one way.
- Must **not** include any voice, model, NEUTRINO file or dataset. Public
  singing datasets are mostly research-only; training a voice for
  distribution needs recordings made for it with the singer's consent.
- NEUTRINO's agreement forbids its use in ML voice conversion; Cantilena
  synthesizes from scores only.

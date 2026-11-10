# Changelog

## Unreleased

- **A DiffSinger voice sings a phrase at a time**: the score is cut at its rests, and each phrase is sung once and kept while the engine runs, so changing a note or a word sings only that phrase again. A score longer than about two minutes no longer stops the engine on DirectML, and a model's failure is now reported as that voice's error instead of ending the engine.
- **VOICEVOX and NEUTRINO voices sing a phrase at a time too**, kept the same way.
- **An English word whose dictionary entry the voice cannot say is read from its letters**, and named in the log, where it was sung as silence: TIGER's dictionary spells `hear` with a phoneme the voice does not have.
- **A VOICEVOX voice no longer fails with `Internal Server Error` on notes that touch**: two notes rounded a hundredth of a second apart were sent with a rest between them too short for the engine.
- **VOICEVOX voices sing below E3 in tune**: the singing style that writes the pitch came 13 to 31 semitones too high there, on every voice, so a low phrase is now asked for an octave or two up and its pitch brought back down.
- **A render no longer wanted can be let go of**: `sing` takes an `AbortSignal` in `options.signal`, and `serve` a `{"command":"cancel","id":…}`. The engine finishes the phrase it is on, keeps what it sang, and goes on to the next request.
- **Phrases are kept on disk**, so an edit after the engine or VS Code restarts sings only what changed. The new setting `cantoria.cacheSizeMB` (1000 by default) holds the rendered voices and the phrases together to that size, the longest unused going first; the rendered voices had no limit before. **Forget Rendered Voices** clears the phrases too.
- **VOICEVOX voices say their gender**, from a table in `src/voicevoxGenders.ts` (VOICEVOX does not): `singer male` and `singer female` can pick them, and the default-voice lists show it. A character the table does not list says nothing, as before.
- **DiffSinger voices say their gender**, from a table in `src/diffSingerGenders.ts` (the voices do not): `singer male` and `singer female` can pick them, and the default-voice lists show it. A voice the table does not list says nothing, as before.
- **DiffSinger voices sing Chinese and Korean**, where the voice has a `dsdict-zh.yaml` or `dsdict-ko.yaml`: Han characters by their customary reading or pinyin, Hangul syllables or their romanisation.

## 0.4.0

- **A score can name its singer, or just its gender**: the settings `cantoria.defaultFemaleVoice` and `cantoria.defaultMaleVoice` take the place of `cantoria.voice`, with a picker each; empty is the first voice of that gender for the words' language. The API has `resolveSinger`. `sing` refuses words in a language the voice says it does not sing. `voices()` gives a voice's `gender` where its `info.toml` says.
- **NEUTRINO says which words it cannot read**: a score whose syllables are not kana is stopped with those syllables named, instead of the engine's "Failed to load phrase data".
- **A DiffSinger voice's speakers are voices**: each speaker its `dsconfig.yaml` lists after the first is offered as `diffsinger/TIGER/tiger_disco`, named by the voice's `character.yaml`; they share the voice's models.
- **Several voices can sing one part**: `sing` takes voices joined by `+` (`neutrino/MERROW+diffsinger/TIGER`), each singing the score, their sounds laid together.
- **Cantoria can start VOICEVOX**: with `cantoria.voicevoxFolder` set, its engine starts when one of its voices is needed and none is running, on a port of its own, and stops with VS Code. A VOICEVOX you opened yourself is used and left alone.
- **VOICEVOX sings**: start VOICEVOX and its singing voices are listed as `voicevox/name-style`, Japanese only. The new setting `cantoria.voicevoxUrl` says where it answers.
- **An English word the dictionary lacks is sung from its spelling**: the guess can be wrong, and the render's log names each word sung this way.
- **DiffSinger voices sing English**, where the voice has a `dsdict-en.yaml`: words written in syllables are looked up whole and dealt back to their notes, and a score's `language` picks the dictionary. A voice lists every language it can sing in `languages`.
- **A score's dynamics are heard**: a note's `level` and `levelEnd` scale what the engine sang (NEUTRINO reads a dynamic and sings no louder for it), eased between notes.
- **The settings are grouped**: General, DiffSinger, NEUTRINO, VOICEVOX and Developer.
- A **Choose the folder…** link for the vocoder folder.
- **A breath mark between two notes is heard**: a note's `inhale` lets NEUTRINO sing its own breath there (a rest stays silent).
- **A held ん moves on the beat**: where a note starts on the very phoneme the last one ended on, NEUTRINO's timing could put the join up to 0.4 s late, and the pitch moved late with it. The join now goes where the score has it.
- **DiffSinger voices sing on the GPU**: the bundled program is built on the DirectML build of ONNX
  Runtime, so a DiffSinger voice's models run on any DirectX 12 GPU (a phrase in a second or two
  once the models are resident, against ten or more on the CPU) and on the CPU where there is none —
  the Cantoria output channel says which (`diffsinger: the models run on DirectML`). `cantoria.gpu`
  off keeps every engine on the CPU. About 21 MB more in the VSIX (DirectML's redistributable).
- **A score whose notes touch is not refused for rounding**: a note starting less than a millisecond
  before the previous one ends (a caller's start and duration rounded apart) has the earlier note
  trimmed to it, where the engine used to answer "starts before note N ends" and sing nothing.
- **Linux x64 and macOS arm64**: the extension is published for those platforms too, with the
  program bundled (DiffSinger voices on the CPU there; NEUTRINO is Windows-only, and Check Setup
  says so if its folder is set). Not yet heard on either — the first phrase there is yours.

## 0.3.0

- **DiffSinger voices**: the setting `cantoria.voicesFolder` (and **Choose Voices Folder… (DiffSinger)**)
  names a folder whose subfolders are DiffSinger voices in the OpenUtau format; they appear in
  **Choose Voice…** and `voices()` as `diffsinger/<folder>` (labelled by their `character.yaml`), and the
  bundled program sings them in-process over ONNX Runtime (the CPU so far). `cantoria.vocoderFolder`
  for a voice that bundles no vocoder. Either engine alone is a complete setup: a NEUTRINO folder is no
  longer required.
- `status().voices` lists every voice as `engine/name` (it listed NEUTRINO's folder names).

## 0.2.0

- **A voice is named `engine/name`**: `neutrino/MERROW` in the setting `cantoria.voice` (a bare name
  still means a NEUTRINO voice), in **Choose Voice…** (with the speaker's name and language beside it),
  in `status().voice` and in `sing`'s `voice`, so a second engine's voice will need no new setting. An
  engine that is not there is said so: `no engine named foo in foo/MERROW (there is: neutrino)`.
- **`voices()` on the API**: the voices installed, each `{ id, engine, name, label, language? }` (the
  label and the language from NEUTRINO's `info.toml`). `apiVersion` stays 1.

## 0.1.0

- First release: the `cantoria` program bundled (Windows x64), the settings (`cantoria.neutrinoFolder`,
  `cantoria.voice`, `cantoria.gpu`, `cantoria.threads`, `cantoria.program`), the commands
  **Choose NEUTRINO Folder…**, **Choose Voice…**, **Check Setup**, **Forget Rendered Voices**, and the API
  (`status`, `sing`, `release`; `apiVersion` 1) that Lily# sings its lyrics through.

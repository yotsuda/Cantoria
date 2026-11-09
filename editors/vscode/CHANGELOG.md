# Changelog

## 0.4.0

- **A score can name its singer, or just its gender**: the settings `cantilena.defaultFemaleVoice` and `cantilena.defaultMaleVoice` take the place of `cantilena.voice`, with a picker each; empty is the first voice of that gender for the words' language. The API has `resolveSinger`. `sing` refuses words in a language the voice says it does not sing. `voices()` gives a voice's `gender` where its `info.toml` says.
- **Several voices can sing one part**: `sing` takes voices joined by `+` (`neutrino/MERROW+diffsinger/TIGER`), each singing the score, their sounds laid together.
- **Cantilena can start VOICEVOX**: with `cantilena.voicevoxFolder` set, its engine starts when one of its voices is needed and none is running, on a port of its own, and stops with VS Code. A VOICEVOX you opened yourself is used and left alone.
- **VOICEVOX sings**: start VOICEVOX and its singing voices are listed as `voicevox/name-style`, Japanese only. The new setting `cantilena.voicevoxUrl` says where it answers.
- **An English word the dictionary lacks is sung from its spelling**: the guess can be wrong, and the render's log names each word sung this way.
- **DiffSinger voices sing English**, where the voice has a `dsdict-en.yaml`: words written in syllables are looked up whole and dealt back to their notes, and a score's `language` picks the dictionary. A voice lists every language it can sing in `languages`.
- **A score's dynamics are heard**: a note's `level` and `levelEnd` scale what the engine sang (NEUTRINO reads a dynamic and sings no louder for it), eased between notes.
- **The settings are grouped**: General, DiffSinger, NEUTRINO and Developer.
- A **Choose the folder…** link for the vocoder folder.
- **A breath mark between two notes is heard**: a note's `inhale` lets NEUTRINO sing its own breath there (a rest stays silent).
- **A held ん moves on the beat**: where a note starts on the very phoneme the last one ended on, NEUTRINO's timing could put the join up to 0.4 s late, and the pitch moved late with it. The join now goes where the score has it.
- **DiffSinger voices sing on the GPU**: the bundled program is built on the DirectML build of ONNX
  Runtime, so a DiffSinger voice's models run on any DirectX 12 GPU (a phrase in a second or two
  once the models are resident, against ten or more on the CPU) and on the CPU where there is none —
  the Cantilena output channel says which (`diffsinger: the models run on DirectML`). `cantilena.gpu`
  off keeps every engine on the CPU. About 21 MB more in the VSIX (DirectML's redistributable).
- **A score whose notes touch is not refused for rounding**: a note starting less than a millisecond
  before the previous one ends (a caller's start and duration rounded apart) has the earlier note
  trimmed to it, where the engine used to answer "starts before note N ends" and sing nothing.
- **Linux x64 and macOS arm64**: the extension is published for those platforms too, with the
  program bundled (DiffSinger voices on the CPU there; NEUTRINO is Windows-only, and Check Setup
  says so if its folder is set). Not yet heard on either — the first phrase there is yours.

## 0.3.0

- **DiffSinger voices**: the setting `cantilena.voicesFolder` (and **Choose Voices Folder… (DiffSinger)**)
  names a folder whose subfolders are DiffSinger voices in the OpenUtau format; they appear in
  **Choose Voice…** and `voices()` as `diffsinger/<folder>` (labelled by their `character.yaml`), and the
  bundled program sings them in-process over ONNX Runtime (the CPU so far). `cantilena.vocoderFolder`
  for a voice that bundles no vocoder. Either engine alone is a complete setup: a NEUTRINO folder is no
  longer required.
- `status().voices` lists every voice as `engine/name` (it listed NEUTRINO's folder names).

## 0.2.0

- **A voice is named `engine/name`**: `neutrino/MERROW` in the setting `cantilena.voice` (a bare name
  still means a NEUTRINO voice), in **Choose Voice…** (with the speaker's name and language beside it),
  in `status().voice` and in `sing`'s `voice`, so a second engine's voice will need no new setting. An
  engine that is not there is said so: `no engine named foo in foo/MERROW (there is: neutrino)`.
- **`voices()` on the API**: the voices installed, each `{ id, engine, name, label, language? }` (the
  label and the language from NEUTRINO's `info.toml`). `apiVersion` stays 1.

## 0.1.0

- First release: the `cantilena` program bundled (Windows x64), the settings (`cantilena.neutrinoFolder`,
  `cantilena.voice`, `cantilena.gpu`, `cantilena.threads`, `cantilena.program`), the commands
  **Choose NEUTRINO Folder…**, **Choose Voice…**, **Check Setup**, **Forget Rendered Voices**, and the API
  (`status`, `sing`, `release`; `apiVersion` 1) that Lily# sings its lyrics through.

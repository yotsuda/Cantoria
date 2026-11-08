# Changelog

## 0.4.0

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

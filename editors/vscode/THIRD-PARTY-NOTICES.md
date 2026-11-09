# Third-party notices — Cantoria Singing Voice (the VS Code extension)

The extension's own code (`out/extension.js`) and the bundled `cantoria` program are
Cantoria's, Apache License 2.0 (LICENSE).

## Bundled

The licenses and third-party notices these works ship with are in `bin/<rid>/licenses/`.

- **.NET runtime** (Microsoft, MIT): the bundled program is published self-contained, so the
  runtime's libraries are under `bin/<rid>/`.
- **ONNX Runtime** (Microsoft, MIT): runs the DiffSinger voices' models.
- **DirectML** (Microsoft Software License Terms, the Windows package only): runs those models on
  a DirectX 12 GPU. It is not open source and is not under this extension's license; its terms are
  in `bin/win-x64/licenses/DirectML-LICENSE.txt`.
- **YamlDotNet** (Antoine Aubry and contributors, MIT): reads the voices' configuration and dictionaries.
- **OpenUtau** (MIT): the program's DiffSinger backend follows OpenUtau's C# DiffSinger inference.
  Its notice:

      MIT License

      Copyright (c) 2019 Stakira

      Permission is hereby granted, free of charge, to any person obtaining a copy
      of this software and associated documentation files (the "Software"), to deal
      in the Software without restriction, including without limitation the rights
      to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
      copies of the Software, and to permit persons to whom the Software is
      furnished to do so, subject to the following conditions:

      The above copyright notice and this permission notice shall be included in all
      copies or substantial portions of the Software.

      THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
      IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
      FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
      AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
      LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
      OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
      SOFTWARE.

## Invoked, not included

- **NEUTRINO** (STUDIO NEUTRINO / SHACHI, closed-source freeware) — installed by the user and
  named in `cantoria.neutrinoFolder`; nothing of it is redistributed. Its agreement forbids use
  for machine-learning voice conversion; Cantoria synthesizes from scores only.
- **VOICEVOX** (Hiroshiba Kazuyuki and contributors) — installed by the user, its engine started from
  `cantoria.voicevoxFolder` or reached at `cantoria.voicevoxUrl`; nothing of it is redistributed.
  It is used under <https://voicevox.hiroshiba.jp/term/>; each character has its own terms, and a
  published work credits `VOICEVOX:name`.
- **Voices** (MERROW ships with NEUTRINO; others are their authors') — read from the user's
  NEUTRINO folder, never included.

The repository's THIRD-PARTY-NOTICES.md lists the works the library and the other backends use.

## The icon

The extension's icon is the C clef of Emmentaler, LilyPond's music font.
Copyright (C) 1997-2023 Han-Wen Nienhuys, Jan Nieuwenhuizen, Werner Lemberg, and the LilyPond
project contributors. The font is dual-licensed; the icon uses it under the SIL Open Font License.
<https://gitlab.com/lilypond/lilypond>

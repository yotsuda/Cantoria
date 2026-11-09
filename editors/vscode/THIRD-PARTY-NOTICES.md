# Third-party notices — Cantoria Singing Voice (the VS Code extension)

The extension's own code (`out/extension.js`) and the bundled `cantoria` program are
Cantoria's, Apache License 2.0 (LICENSE).

## Bundled

- **.NET runtime** (Microsoft, MIT) — the bundled program is published self-contained, so the
  runtime's libraries are under `bin/<rid>/`. <https://github.com/dotnet/runtime/blob/main/LICENSE.TXT>

## Invoked, not included

- **NEUTRINO** (STUDIO NEUTRINO / SHACHI, closed-source freeware) — installed by the user and
  named in `cantoria.neutrinoFolder`; nothing of it is redistributed. Its agreement forbids use
  for machine-learning voice conversion; Cantoria synthesizes from scores only.
- **Voices** (MERROW ships with NEUTRINO; others are their authors') — read from the user's
  NEUTRINO folder, never included.

The repository's THIRD-PARTY-NOTICES.md lists the works the library and the other backends use.

## The icon

The extension's icon is the C clef of Emmentaler, LilyPond's music font.
Copyright (C) 1997-2023 Han-Wen Nienhuys, Jan Nieuwenhuizen, Werner Lemberg, and the LilyPond
project contributors. The font is dual-licensed; the icon uses it under the SIL Open Font License.
<https://gitlab.com/lilypond/lilypond>

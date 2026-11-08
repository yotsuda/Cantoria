# Third-party notices — Cantilena Singing Voice (the VS Code extension)

The extension's own code (`out/extension.js`) and the bundled `cantilena` program are
Cantilena's, Apache License 2.0 (LICENSE).

## Bundled

- **.NET runtime** (Microsoft, MIT) — the bundled program is published self-contained, so the
  runtime's libraries are under `bin/<rid>/`. <https://github.com/dotnet/runtime/blob/main/LICENSE.TXT>

## Invoked, not included

- **NEUTRINO** (STUDIO NEUTRINO / SHACHI, closed-source freeware) — installed by the user and
  named in `cantilena.neutrinoFolder`; nothing of it is redistributed. Its agreement forbids use
  for machine-learning voice conversion; Cantilena synthesizes from scores only.
- **Voices** (MERROW ships with NEUTRINO; others are their authors') — read from the user's
  NEUTRINO folder, never included.

The repository's THIRD-PARTY-NOTICES.md lists the works the library and the other backends use.

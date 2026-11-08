# Cantilena Singing Voice

Sings the lyrics of a score for notation extensions such as
[Lily#](https://marketplace.visualstudio.com/items?itemName=yotsuda.lilysharp): the extension
carries the `cantilena` program, holds the settings — where your NEUTRINO is, which voice — and
sings for other extensions through its API. It has no editor of its own.

Open source, Apache-2.0: [github.com/yotsuda/cantilena](https://github.com/yotsuda/cantilena).
Windows x64 for now (the bundled program); other platforms can point `cantilena.program` at a
build of their own.

## Setup

1. Install **NEUTRINO** (Tau, v3.2 or later) from [studio-neutrino.com](https://studio-neutrino.com/).
   It is free software of STUDIO NEUTRINO under its own terms, not part of this extension.
   Its bundled voice is MERROW; other voices are their authors' work, installed under its `model\` folder.
2. **Cantilena: Choose NEUTRINO Folder…** — the folder holding `bin\`, `model\` and `settings\`.
3. **Cantilena: Choose Voice…** (MERROW by default), and **Cantilena: Check Setup** to see that all is well.

Then, in Lily#, the preview's Play sings every part a lyrics row sings (hiragana or katakana on
each syllable), in time with the other parts. The first phrase takes a while (NEUTRINO loads its
models — up to a minute); the same phrase is sung once and kept (**Cantilena: Forget Rendered
Voices** clears them).

## For extension authors

```ts
const ext = vscode.extensions.getExtension('yotsuda.cantilena');
const api = ext && (ext.isActive ? ext.exports : await ext.activate());
const { ready, problems } = await api.status();
const { wav, duration } = await api.sing({
    sampleRate: 48000, language: 'ja',
    notes: [
        { start: 0.0, duration: 0.5, pitch: 60, lyric: 'ゆ' },
        { start: 0.5, duration: 0.25, pitch: 62, melisma: true },
        { start: 0.75, duration: 0.5, pitch: 60, lyric: 'き', breath: true },
    ],
});
```

- Times are **seconds**, resolved by the caller (tempo, repeats, a practice speed); gaps are
  rests. `pitch` is MIDI (fractional allowed). `lyric` is the syllable as pronounced (kana for
  the Japanese voices); a `melisma` note carries the previous vowel on and has no lyric;
  `breath` marks a phrase end after the note.
- `sing` answers the WAV's path (48 kHz mono) and its duration; the file stays until the voices
  are forgotten. It throws with one sentence when it cannot sing (no program, no NEUTRINO folder,
  a score the engine refused). `release()` lets the engine go; `apiVersion` is `1`.
- Everything is local: the engine is a resident child process of this extension, spoken to in
  JSON lines; nothing leaves the machine.

## Notices

NEUTRINO is invoked as installed by the user and is not redistributed; its agreement forbids use
for voice conversion — Cantilena synthesizes from scores only. The bundled program is Cantilena's
own code (Apache-2.0) published with the .NET runtime (MIT); see THIRD-PARTY-NOTICES.md.

# Neural instruments: what exists (2026-11-28)

The question: is there an "AI instrument" (a model trained on players that turns MIDI into a
played performance, the way DiffSinger turns a score into singing) that Cantoria or Lily# could
use? Prompted by ACE Studio's *AI Instruments*. Read from the vendors' and repositories' own
pages; nothing here was installed or heard.

## Commercial

| Product | Instruments | Runs | Price | Reachable from a program |
|---|---|---|---|---|
| ACE Studio *AI Instruments* | now: strings, saxophone, trumpet, duduk; "coming soon": oboe, suona, slap bass, dizi | cloud ("AI instruments require cloud processing") | subscription, about $16.58–22/month | its experimental MCP server / `acestudio-cli`, with the app open |
| Dreamtonics *Instrument X* (2026-08/09) | 15: violin, viola, cello, contrabass, flute, piccolo, oboe, clarinet in A, bassoon, trumpet, trombone, horn, tuba (+ jazz alto and tenor sax sold singly) | local, "no internet connection or dedicated GPU required" | $99 per instrument, $249–279 per family, $649 everything | none found: a standalone editor and VST3/AU/AAX plugin |

## Open

| Project | Instruments | Licence of the code / of the weights | Runtime | State |
|---|---|---|---|---|
| [magenta/midi-ddsp](https://github.com/magenta/midi-ddsp) | 13 (URMP): violin, viola, cello, double bass, flute, oboe, clarinet, saxophone, bassoon, trumpet, horn, trombone, tuba | Apache-2.0 / weights' terms not stated (URMP dataset's own terms apply) | Python, TensorFlow 2.7; CLI `midi_ddsp_synthesize --midi_path` | archived 2024-02, last commit 2022-11 |
| [magenta/ddsp-vst](https://github.com/magenta/ddsp-vst) | not stated on the page (models fetched by `repo-init.sh`) | Apache-2.0 / not stated | C++ (JUCE) plugin, TFLite inside, local | archived 2024-10, last commit 2023-05 |
| [daewoung/ViolinDiff](https://github.com/daewoung/ViolinDiff) (ICASSP 2025) | violin (21 performers) | no licence file; "non-commercial, research use only" | Python, PyTorch; CPU flag exists | last commit 2025-02 |
| FluidSynth | anything with a SoundFont | LGPL | C | alive (2.4.7, 2025-07) — samples, not a model |

## Reading

- Nothing open is both alive and free to ship. MIDI-DDSP is the closest (13 instruments,
  Apache code, a CLI that takes a MIDI file) but is archived, needs TensorFlow in Python, and
  says nothing about its weights' terms; its quality is research-grade (2021).
- The quality bar is set by the commercial two. Of those, only ACE Studio can be driven by a
  program, and only through its experimental agent access.
- If Lily# ever wants realistic accompaniment, the cheap route is the user's own copy of ACE
  Studio (MCP) or Instrument X (render in their DAW from Lily#'s MIDI), not a model Cantoria ships.

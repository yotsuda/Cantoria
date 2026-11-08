# Third-party notices

Cantilena is Apache-2.0. The works below are, or are planned to be, used by it.
Each entry says how it is used, because the obligations differ: code copied
into this repository keeps its copyright notice here and in the file; a
library referenced as a package keeps its own license in the package; a
program invoked as a separate process is not part of Cantilena at all.

## Included or referenced (none yet)

Nothing has been copied or referenced yet. When code is taken from OpenUtau,
or a package is referenced, its entry moves up here with the verbatim notice.

## Planned

### OpenUtau — MIT

https://github.com/stakira/OpenUtau

The C# DiffSinger inference (`OpenUtau.Core/DiffSinger/`) and the Japanese
phonemizers are the model for Cantilena's own. Code copied or adapted from
OpenUtau carries this notice at the top of the file and here:

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

(The copyright year and holder are to be checked against the file copied from.)

### DiffSinger (OpenVPI) — Apache-2.0

https://github.com/openvpi/DiffSinger

The model architecture and the ONNX export format Cantilena's in-process
backend will run. Cantilena does not include DiffSinger's code; it reads
models exported with it. Model files (voices) trained with DiffSinger belong
to whoever trained them, under that person's terms — Cantilena ships none.

### ONNX Runtime — MIT

https://github.com/microsoft/onnxruntime

Referenced as the NuGet package `Microsoft.ML.OnnxRuntime` (and the GPU
variant where available). Not copied.

## Invoked, not included

### NEUTRINO (STUDIO NEUTRINO)

https://studio-neutrino.com/

Closed-source freeware under its own license agreement. Cantilena's NEUTRINO
backend runs the user's own installation as a separate process
(`musicXMLtoLabel.exe`, `neutrino_client.exe`) and never redistributes it.
The sound produced is subject to the terms of the voice library used. The
agreement forbids using NEUTRINO in machine-learning voice-conversion tools;
Cantilena does nothing of the kind.

## Voices

No voice (sound bank, singing library, trained model) is included in this
repository or its releases. Each is the work of its author and the user
installs it under that author's terms.

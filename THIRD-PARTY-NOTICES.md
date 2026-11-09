# Third-party notices

Cantoria is Apache-2.0. The works below are, or are planned to be, used by it.
Each entry says how it is used, because the obligations differ: code copied
into this repository keeps its copyright notice here and in the file; a
library referenced as a package keeps its own license in the package; a
program invoked as a separate process is not part of Cantoria at all.

## Included or referenced

### OpenUtau — MIT (adapted)

https://github.com/stakira/OpenUtau

The `diffsinger` backend (`src/Cantoria.Core/DiffSinger/`) follows OpenUtau's
C# DiffSinger inference (`OpenUtau.Core/DiffSinger/`: the configuration keys,
the frame arithmetic, the word division, the duration alignment, the inputs of
the duration, pitch, variance, acoustic and vocoder models) and its Japanese
phonemizer's reading of a voice's dictionary. It was written anew in Cantoria's
own shape, not copied line for line; each file that follows OpenUtau says so at
its top. OpenUtau's notice:

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

### ONNX Runtime — MIT (referenced)

https://github.com/microsoft/onnxruntime

Referenced by `Cantoria.Core` as the NuGet package `Microsoft.ML.OnnxRuntime`
(the CPU build: every platform, the tests), or — the Windows program, published with
`UseDirectML=true` — `Microsoft.ML.OnnxRuntime.DirectML` (the same runtime with the
DirectML execution provider; its native library runs the CPU provider too). Not
copied. The license is in each package.

### DirectML — Microsoft Software License Terms (redistributed with the Windows program)

https://www.nuget.org/packages/Microsoft.AI.DirectML

`DirectML.dll`, brought in by `Microsoft.ML.OnnxRuntime.DirectML` as the NuGet package
`Microsoft.AI.DirectML`, ships beside the Windows program (and so inside the VS Code
extension's VSIX). It is not open source: Microsoft's license terms for DirectML
(`LICENSE.txt` in the package) allow its installation, use and distribution within
applications developed with machine-learning frameworks that run on Windows, which is
how it is used here. Nothing of it is modified. Its own third-party notices are in
the package's `ThirdPartyNotices.txt`.

### YamlDotNet — MIT (referenced)

https://github.com/aaubry/YamlDotNet

Referenced by `Cantoria.Core` as the NuGet package `YamlDotNet`, to read the
voices' `dsconfig.yaml`, `vocoder.yaml` and dictionaries. Not copied. Its
license is in the package.

### DiffSinger (OpenVPI) — Apache-2.0 (format only)

https://github.com/openvpi/DiffSinger

The model architecture and the ONNX export format the `diffsinger` backend
runs. Cantoria does not include DiffSinger's code; it reads models exported
with it. Model files (voices) trained with DiffSinger belong to whoever trained
them, under that person's terms — Cantoria ships none.

## Planned

Nothing further is planned to be copied.

## Invoked, not included

### NEUTRINO (STUDIO NEUTRINO)

https://studio-neutrino.com/

Closed-source freeware under its own license agreement. Cantoria's NEUTRINO
backend runs the user's own installation as a separate process
(`musicXMLtoLabel.exe`, `neutrino_client.exe`) and never redistributes it.
The sound produced is subject to the terms of the voice library used. The
agreement forbids using NEUTRINO in machine-learning voice-conversion tools;
Cantoria does nothing of the kind.

## Voices

No voice (sound bank, singing library, trained model) is included in this
repository or its releases. Each is the work of its author and the user
installs it under that author's terms.

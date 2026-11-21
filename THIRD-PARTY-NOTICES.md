# Third-party notices

Cantoria is Apache-2.0. The works below are, or are planned to be, used by it.
Each entry says how it is used, because the obligations differ: code copied
into this repository keeps its copyright notice here and in the file; a
library referenced as a package keeps its own license in the package
(the VS Code extension carries a copy of each under `bin/<rid>/licenses/`); a
program invoked as a separate process is not part of Cantoria at all.

## Included or referenced

### Unihan database — Unicode License v3 (data derived)

https://www.unicode.org/reports/tr38/

`src/Cantoria.Core/DiffSinger/pinyin.txt` is generated from the `kMandarin` field of
`Unihan_Readings.txt` (Unicode 18.0.0) by `tools/Generate-Pinyin.ps1`: each character's reading,
tone removed. © 1991-2026 Unicode, Inc. Terms: https://www.unicode.org/license.txt

### phrase-pinyin-data — MIT (data derived)

https://github.com/mozillazg/phrase-pinyin-data

`src/Cantoria.Core/DiffSinger/pinyin-phrases.txt` is generated from its `pinyin.txt` (0.20.0,
commit 4afa322) by `tools/Generate-PinyinPhrases.ps1`: the words whose characters are read otherwise
than alone, or that hold a character read two ways, tone removed; and a few corrections of
Cantoria's own, which the script lists. Its notice:

    MIT License

    Copyright (c) 2017 mozillazg

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

### OpenUtau's ARPABET pack — MIT (included as data), with CMUdict inside it

https://github.com/stakira/OpenUtau (`OpenUtau.Core/G2p/Data/g2p-arpabet.zip`)

`src/Cantoria.Core/DiffSinger/g2p-arpabet.zip` is that file, carried unchanged and embedded in
`Cantoria.Core`: the CMU Pronouncing Dictionary 0.7 (`dict.txt`), and a model trained on it that
reads a word it does not list from its letters (`g2p.onnx`). `EnglishG2p.cs`, which reads the
two, follows OpenUtau's `ArpabetG2p` and `G2pPack` and says so at its top. An English word a
voice's own dictionary does not list is read by them. The pack is OpenUtau's (the MIT notice
above); the dictionary in it keeps its own notice:

    Copyright (C) 1993-2015 Carnegie Mellon University. All rights reserved.

    Redistribution and use in source and binary forms, with or without
    modification, are permitted provided that the following conditions
    are met:

    1. Redistributions of source code must retain the above copyright
       notice, this list of conditions and the following disclaimer.
       The contents of this file are deemed to be source code.

    2. Redistributions in binary form must reproduce the above copyright
       notice, this list of conditions and the following disclaimer in
       the documentation and/or other materials provided with the
       distribution.

    This work was supported in part by funding from the Defense Advanced
    Research Projects Agency, the Office of Naval Research and the National
    Science Foundation of the United States of America, and by member
    companies of the Carnegie Mellon Sphinx Speech Consortium. We acknowledge
    the contributions of many volunteers to the expansion and improvement of
    this dictionary.

    THIS SOFTWARE IS PROVIDED BY CARNEGIE MELLON UNIVERSITY ``AS IS'' AND
    ANY EXPRESSED OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO,
    THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR
    PURPOSE ARE DISCLAIMED.  IN NO EVENT SHALL CARNEGIE MELLON UNIVERSITY
    NOR ITS EMPLOYEES BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
    SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
    LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
    DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
    THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
    (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
    OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

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
(`musicXMLtoLabel.exe`, and `neutrino_server.exe` with `neutrino_client.exe`
or `neutrino.exe`) and never redistributes it.
The sound produced is subject to the terms of the voice library used. The
agreement forbids using NEUTRINO in machine-learning voice-conversion tools;
Cantoria does nothing of the kind.

### VOICEVOX

https://voicevox.hiroshiba.jp/

Installed by the user and never redistributed. Cantoria starts the user's own
engine, or talks to a VOICEVOX already running, over its local API. It is used
under https://voicevox.hiroshiba.jp/term/, which asks for a credit that
VOICEVOX was used; each character has its own terms.

## Voices

No voice (sound bank, singing library, trained model) is included in this
repository or its releases. Each is the work of its author and the user
installs it under that author's terms.

**Download a Voice** (`cantoria fetch`) fetches, at the user's request and
after showing the terms, a voice whose terms allow it from its author's own
release, into the user's voices folder; nothing is copied into Cantoria:

- Qixuan 绮萱 2.7.0, by the DiffSinger community (data 颜绮萱; training
  YQ之神), https://github.com/yqzhishen/qixuan-diffsinger — its own terms
  (`Terms_of_Use.zh-CN`, which come with it): free, not to be sold; works made
  with it may be published and sold, marked as AI-synthesised and credited
  "绮萱" and "DiffSinger"; the model files not to be changed.
- OpenVPI pc_nsf_hifigan 44.1k hop512 128bin 2025.02, the vocoder it names,
  https://github.com/openvpi/vocoders — CC BY-NC-SA 4.0 (its `NOTICE.txt`
  comes with it); what it synthesises is not restricted.

## The icon

The extension's icon is the C clef of Emmentaler, LilyPond's music font.
Copyright (C) 1997-2023 Han-Wen Nienhuys, Jan Nieuwenhuizen, Werner Lemberg, and the LilyPond
project contributors. The font is dual-licensed; the icon uses it under the SIL Open Font License.
<https://gitlab.com/lilypond/lilypond>

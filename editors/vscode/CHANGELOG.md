# Changelog

## Unreleased

- **A Chinese character is read by the word it stands in**: 长 in 长大 is `zhang`, in 长城 `chang`; 行 in 银行 `hang`, in 行人 `xing`; 乐 in 音乐 `yue`, in 快乐 `le`. The words are found across the notes, one character a note as a song has them, from a table of 24,000 words after phrase-pinyin-data (pypinyin's). A character in no word is read as before, alone, except a few whose customary reading no word has (佛 `fo`, 长 `chang`). Pinyin written out is sung as written.
- **A Korean final consonant goes on to a following vowel**: 밥이 sings 바비, 닭이 달기, 값이 갑씨, 같이 가치, and a final ㅎ is not sung before a vowel (좋아 조아, 많이 마니). Within one note's text, or on to the next note when it follows with no rest and no breath between (or a hyphen joins them): a Korean song's syllables are written one to a note with spaces, so the words are not marked. Before a syllable that begins a word rather than a particle or an ending, the final goes on as it is said at a word's end (꽃 위 꼬뒤, 옷 안 오단), and not at all before ㅣ or a y- vowel (꽃잎이 is not 꼬치피). A carry the voice's dictionary cannot spell is not made, and a phrase is never cut between the two notes.

## 0.5.0

- **Find Engines on This Computer**: a new command that looks where the engines' own programs put them and offers what stands there for the settings: VOICEVOX as its installer leaves it — the engine in its folder, or, with the installer that downloads the engine at first launch, under its user data's `vvpp-engines`, where *Choose VOICEVOX Folder…* had to be pointed by hand — and OpenUtau's `Singers` folder with a vocoder among its dependencies. Nothing is set unasked. *Check Setup* offers it when nothing can sing.
- **How to Install a Voice opens the steps inside VS Code**: this extension's own README, as a page beside the editor, where it opened the same text on GitHub.
- **A folder that cannot be used is not set**: choosing a folder that is not the engine's (a folder that is no NEUTRINO folder, one with no DiffSinger voice, no VOICEVOX engine, no `vocoder.yaml`) showed a note whose one button was *Use it anyway*, which set a folder nothing could sing from. It now asks, with what is missing, between *Choose Another Folder…* — the folder dialog again, opened where the last choice was — and *Cancel*, which leaves the setting as it was.
- **One voice a sing: `+` no longer joins voices.** `sing`, `parameters` and `plan` took voices joined by `+` (`neutrino/MERROW+diffsinger/TIGER`) and sang each in turn, mixing the sounds. It was no faster than asking for each, and a caller that asks for each keeps what it sang of one when the other changes, and sets their levels itself. A name with a `+` is now a voice's whole name.
- **NEUTRINO on a computer with no GPU**: with no NVIDIA GPU NEUTRINO sings on the processor — 37 s of song took 5 min 40 s on a laptop with Intel graphics — and a phrase that took longer than five minutes was stopped and its work thrown away, every time. A render now has the song's length at the pace NEUTRINO was last timed at, three times over, with room for the voice to load (a processor's pace until it is timed). The package with the tuning tool is the faster there: a 36-second part in 135 s, where the plain package took 240 s.
- **A render let go of stops at once**: the phrase it was on goes on being sung only until the next request says whether it sings it — kept and used if it does, stopped if it does not (a request in any voice). Left alone, it is finished and kept. Before, the engine always finished the phrase it was on, which on a processor is minutes. A stopped render of the package with the tuning tool stops its server too, which otherwise went on and refused the next render with "BUSY: inference is running"; the next render starts it again.
- **Questions are answered while a score is sung**: `serve` answers `voices`, `plan` and `parameters` at once instead of after the score being sung. An editor that asks who would sing before it sends a cancel no longer waits minutes for both.
- **The plain NEUTRINO package sings too**: its folder was refused ("neutrino_server.exe is missing") because only the package with the tuning tool (調声支援ツール同梱版) has the server. Without a server each phrase is its own run of `neutrino.exe`, which loads the voice every time: about 3.5 s a phrase on a fast GPU, where the server sings one in well under a second. Where the folder has the server, the server is used as before.
- **Who would sing a score, asked without singing it**: the new `plan(score)` (the program's `cantoria plan`; `serve`: `{"command":"plan","asked":[…]}`) answers, for each voice a score asks for — by name, by gender alone, in a language — the installed voice that would sing it, or that none can and why: `no installed voice sings Latin (la); installed voices sing ja, en`. No model is loaded for it. The program now holds the rule `resolveSinger` goes by, so that a compiler can ask it too; `cantoria voices` says each voice's gender and languages and takes `--language <tag>`; and the program's own `sing` refuses words in a language the voice says it does not sing, with that reason.
- **How to Install a Voice**: where Cantoria says it cannot sing yet or has no voice to list, the first button opens the steps for each engine (this README's Setup: what to download, where to unpack it, which command to run). It took the place of "Get NEUTRINO", which led to one engine's site. The DiffSinger steps now say how to get a vocoder for a voice that bundles none.
- **A DiffSinger voice sings a phrase at a time**: the score is cut at its rests, and each phrase is sung once and kept while the engine runs, so changing a note or a word sings only that phrase again. A score longer than about two minutes no longer stops the engine on DirectML, and a model's failure is now reported as that voice's error instead of ending the engine.
- **VOICEVOX and NEUTRINO voices sing a phrase at a time too**, kept the same way.
- **An English word whose dictionary entry the voice cannot say is read from its letters**, and named in the log, where it was sung as silence: TIGER's dictionary spells `hear` with a phoneme the voice does not have.
- **A VOICEVOX voice no longer fails with `Internal Server Error` on notes that touch**: two notes rounded a hundredth of a second apart were sent with a rest between them too short for the engine.
- **VOICEVOX voices sing below E3 in tune**: the singing style that writes the pitch came 13 to 31 semitones too high there, on every voice, so a low phrase is now asked for an octave or two up and its pitch brought back down.
- **A score can say how the voice sings it**: `sing` in the score — `{ "breathiness": 3, "tension": -2 }` — sets what the voice takes, and the new `parameters(voice)` (`serve`: `{"command":"parameters","voice":…}`) lists it: the engine's own inputs, by name, with where each can change and its range. A DiffSinger voice takes what its model was trained with, a NEUTRINO voice its program's own two (`transpose`, `support`); a setting a voice does not take is left out and said in the answer's `notes`. A note's own `sing` changes a setting from there on, and its `singTo` arrives at a value gliding.
- **A render says how far it is**: `sing` takes `options.onProgress`, told `{ toSing, sung }` in seconds of music — what has to be sung anew, the phrases kept from before left out, and how much of it is done — and `serve` writes a `{"progress":{"id":…,"toSing":…,"sung":…}}` line before the first phrase and after each. A caller's bar can begin empty and be full when the voice is, whatever part of the score was already sung.
- **A render no longer wanted can be let go of**: `sing` takes an `AbortSignal` in `options.signal`, and `serve` a `{"command":"cancel","id":…}`. The engine keeps what it sang and goes on to the next request (the phrase it was on: see *A render let go of stops at once*, above).
- **Phrases are kept on disk**, so an edit after the engine or VS Code restarts sings only what changed. The new setting `cantoria.cacheSizeMB` (1000 by default) holds the rendered voices and the phrases together to that size, the longest unused going first; the rendered voices had no limit before. **Forget Rendered Voices** clears the phrases too.
- **VOICEVOX voices say their gender**, from a table in `src/voicevoxGenders.ts` (VOICEVOX does not): `singer male` and `singer female` can pick them, and the default-voice lists show it. A character the table does not list says nothing, as before.
- **DiffSinger voices say their gender**, from a table in `src/diffSingerGenders.ts` (the voices do not): `singer male` and `singer female` can pick them, and the default-voice lists show it. A voice the table does not list says nothing, as before.
- **DiffSinger voices sing Chinese and Korean**, where the voice has a `dsdict-zh.yaml` or `dsdict-ko.yaml`: Han characters by their customary reading or pinyin, Hangul syllables or their romanisation.
- **A syllable a DiffSinger voice has no sound for is said before it sings**: where the voice's dictionary spells a syllable with a phoneme one of its models was not taught (TIGER spells 思 `s iz`, and its pitch model has no `iz`), the render is refused at once with the syllable, its note and time, the model and the phoneme — `diffsinger/TIGER has no sound for "思" (note 92 at 108.853 s): …` — where it failed minutes in on a phoneme alone, naming no word.
- **A syllable not in a DiffSinger voice's dictionary is said to the caller**: it is still a silent note, but `sing` now says so with each syllable's note and time — `diffsinger/TIGER does not sing "岸" (note 46 at 57.25 s): the syllable is not in its dictionary (dsdict-zh.yaml), and its note is silent` — in `serve`'s `notes` and on the program's stderr, where only the log knew. What the program writes is UTF-8 whatever the console's code page.
- **An English word a DiffSinger voice's dictionary lacks is read as the CMU Pronouncing Dictionary lists it**, else by a model trained on that dictionary, and only then by the letters' rules, which read "folded", "worries" and "heavy" as `f aa l d eh d`, `w aa r iy z` and `hh iy v iy`. The voice's own dictionary still comes first. The dictionary and the model are OpenUtau's ARPABET pack, carried in the program.

## 0.4.0

- **A score can name its singer, or just its gender**: the settings `cantoria.defaultFemaleVoice` and `cantoria.defaultMaleVoice` take the place of `cantoria.voice`, with a picker each; empty is the first voice of that gender for the words' language. The API has `resolveSinger`. `sing` refuses words in a language the voice says it does not sing. `voices()` gives a voice's `gender` where its `info.toml` says.
- **NEUTRINO says which words it cannot read**: a score whose syllables are not kana is stopped with those syllables named, instead of the engine's "Failed to load phrase data".
- **A DiffSinger voice's speakers are voices**: each speaker its `dsconfig.yaml` lists after the first is offered as `diffsinger/TIGER/tiger_disco`, named by the voice's `character.yaml`; they share the voice's models.
- **Several voices can sing one part**: `sing` takes voices joined by `+` (`neutrino/MERROW+diffsinger/TIGER`), each singing the score, their sounds laid together.
- **Cantoria can start VOICEVOX**: with `cantoria.voicevoxFolder` set, its engine starts when one of its voices is needed and none is running, on a port of its own, and stops with VS Code. A VOICEVOX you opened yourself is used and left alone.
- **VOICEVOX sings**: start VOICEVOX and its singing voices are listed as `voicevox/name-style`, Japanese only. The new setting `cantoria.voicevoxUrl` says where it answers.
- **An English word the dictionary lacks is sung from its spelling**: the guess can be wrong, and the render's log names each word sung this way.
- **DiffSinger voices sing English**, where the voice has a `dsdict-en.yaml`: words written in syllables are looked up whole and dealt back to their notes, and a score's `language` picks the dictionary. A voice lists every language it can sing in `languages`.
- **A score's dynamics are heard**: a note's `level` and `levelEnd` scale what the engine sang (NEUTRINO reads a dynamic and sings no louder for it), eased between notes.
- **The settings are grouped**: General, DiffSinger, NEUTRINO, VOICEVOX and Developer.
- A **Choose the folder…** link for the vocoder folder.
- **A breath mark between two notes is heard**: a note's `inhale` lets NEUTRINO sing its own breath there (a rest stays silent).
- **A held ん moves on the beat**: where a note starts on the very phoneme the last one ended on, NEUTRINO's timing could put the join up to 0.4 s late, and the pitch moved late with it. The join now goes where the score has it.
- **DiffSinger voices sing on the GPU**: the bundled program is built on the DirectML build of ONNX
  Runtime, so a DiffSinger voice's models run on any DirectX 12 GPU (a phrase in a second or two
  once the models are resident, against ten or more on the CPU) and on the CPU where there is none —
  the Cantoria output channel says which (`diffsinger: the models run on DirectML`). `cantoria.gpu`
  off keeps every engine on the CPU. About 21 MB more in the VSIX (DirectML's redistributable).
- **A score whose notes touch is not refused for rounding**: a note starting less than a millisecond
  before the previous one ends (a caller's start and duration rounded apart) has the earlier note
  trimmed to it, where the engine used to answer "starts before note N ends" and sing nothing.
- **Linux x64 and macOS arm64**: the extension is published for those platforms too, with the
  program bundled (DiffSinger voices on the CPU there; NEUTRINO is Windows-only, and Check Setup
  says so if its folder is set). Not yet heard on either — the first phrase there is yours.

## 0.3.0

- **DiffSinger voices**: the setting `cantoria.voicesFolder` (and **Choose Voices Folder… (DiffSinger)**)
  names a folder whose subfolders are DiffSinger voices in the OpenUtau format; they appear in
  **Choose Voice…** and `voices()` as `diffsinger/<folder>` (labelled by their `character.yaml`), and the
  bundled program sings them in-process over ONNX Runtime (the CPU so far). `cantoria.vocoderFolder`
  for a voice that bundles no vocoder. Either engine alone is a complete setup: a NEUTRINO folder is no
  longer required.
- `status().voices` lists every voice as `engine/name` (it listed NEUTRINO's folder names).

## 0.2.0

- **A voice is named `engine/name`**: `neutrino/MERROW` in the setting `cantoria.voice` (a bare name
  still means a NEUTRINO voice), in **Choose Voice…** (with the speaker's name and language beside it),
  in `status().voice` and in `sing`'s `voice`, so a second engine's voice will need no new setting. An
  engine that is not there is said so: `no engine named foo in foo/MERROW (there is: neutrino)`.
- **`voices()` on the API**: the voices installed, each `{ id, engine, name, label, language? }` (the
  label and the language from NEUTRINO's `info.toml`). `apiVersion` stays 1.

## 0.1.0

- First release: the `cantoria` program bundled (Windows x64), the settings (`cantoria.neutrinoFolder`,
  `cantoria.voice`, `cantoria.gpu`, `cantoria.threads`, `cantoria.program`), the commands
  **Choose NEUTRINO Folder…**, **Choose Voice…**, **Check Setup**, **Forget Rendered Voices**, and the API
  (`status`, `sing`, `release`; `apiVersion` 1) that Lily# sings its lyrics through.

// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.
//
// The pipeline — words to phonemes, the duration model's timing aligned to the notes, the pitch
// and variance predictors, the acoustic model, the vocoder — follows OpenUtau's
// OpenUtau.Core/DiffSinger (DiffSingerBasePhonemizer.cs, DiffSingerPitch.cs, DiffSingerVariance.cs,
// DiffSingerRenderer.cs; MIT License, Copyright (c) 2019 Stakira); see THIRD-PARTY-NOTICES.md.

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using static Cantoria.DiffSinger.DsMath;

namespace Cantoria.DiffSinger;

/// <summary>
/// Sings through a DiffSinger voice in the OpenUtau format, in this process over ONNX Runtime.
/// The models stay loaded for this object's life (the first render pays for loading them).
/// </summary>
public sealed class DiffSingerSinger : ISinger, IDisposable
{
    private const int HeadFrames = 8;
    private const int TailFrames = 8;
    private const double LeadPaddingMs = 500;   // room before a phrase's first note for its consonants

    /// <summary>A rest at least this long (seconds) ends a phrase: shorter, the next consonant reaches into it.</summary>
    private const double RestBetweenPhrases = 0.3;

    /// <summary>The longest one run of the models (seconds). DirectML sang 90 s and failed at 140 s,
    /// on the pitch model's stack first and on the acoustic model's GRU (80070057) after that.</summary>
    private const double LongestPhrase = 60;

    private const double BreathMinMs = 150;     // a shorter gap takes no breath
    private const double BreathMaxMs = 400;
    private const double BreathClearMs = 20;    // kept clear between a breath and what follows it
    private const double ConsonantRoomMs = 150; // of the rest after a phrase, left to the next phrase's consonants

    /// <summary>What the phrases cost: 4 bytes a sample, 256 MiB a voice, about 24 minutes of singing.</summary>
    private const int PhrasesKept = 64 * 1024 * 1024;

    private readonly DiffSingerVoice _voice;
    private readonly PhraseCache _phrases;      // used under _oneAtATime
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private bool _disposed;

    /// <exception cref="ArgumentException">The options name no usable voice (the message lists the problems).</exception>
    public DiffSingerSinger(DiffSingerOptions options)
    {
        _voice = new DiffSingerVoice(options);
        _phrases = new PhraseCache(PhrasesKept, options.PhraseFolder is { } folder ? new PhraseDisk(folder, $"diffsinger/{options.Name}@{options.Steps}") : null);
    }

    public string Name => $"diffsinger/{_voice.Options.Name}";

    /// <summary>The voice this singer sings with.</summary>
    public DiffSingerVoice Voice => _voice;

    /// <summary>What the last render said: the provider, the syllables with no entry, the stages' seconds.</summary>
    public string LastLog { get; private set; } = "";

    /// <summary>Where the models run — <c>DirectML</c> or <c>CPU</c> once the first model is open
    /// (<see cref="DiffSingerVoice.Provider"/>).</summary>
    public string Provider => _voice.Provider;

    public Task<SungAudio> SingAsync(SungScore score, CancellationToken ct = default) => SingAsync(score, _voice.Options.Speaker, ct);

    /// <summary>Sings the score as one of the voice's speakers.</summary>
    public async Task<SungAudio> SingAsync(SungScore score, string? speaker, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var problems = score.Validate();
        if (problems.Count > 0)
        {
            throw new SingerException("the score is not sound: " + string.Join("; ", problems));
        }
        await _oneAtATime.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => { _voice.UseSpeaker(speaker); return Render(score, speaker, ct); }, ct).ConfigureAwait(false);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    private sealed class Word
    {
        public Word(SungNote first, List<string> phonemes) { Notes = [first]; Phonemes = phonemes; }
        public List<SungNote> Notes { get; }
        public List<string> Phonemes { get; }
        public double StartMs => Notes[0].Start * 1000;
        public double EndMs => Notes[^1].End * 1000;
        public int Tone => (int)Math.Round(Notes[0].Pitch);
    }

    private sealed record Phone(string Symbol, double StartMs, double EndMs);

    private SungAudio Render(SungScore score, string? speaker, CancellationToken ct)
    {
        var log = new System.Text.StringBuilder();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var voice = _voice;
        string language = score.Language.Split('-')[0];
        if (language is not ("ja" or "en" or "zh" or "ko"))
        {
            throw new SingerException($"{Name} cannot sing {score.Language} yet (it reads Japanese, English, Chinese and Korean)");
        }
        voice.UseLanguage(language);
        var dur = voice.Dur;
        var notes = score.Notes;
        var english = language == "en"
            ? EnglishPhonemizer.Syllables(notes.Where(n => !n.Melisma).Select(n => (n.Lyric!, n.Hyphen)).ToList(), dur.Dictionary, dur.Symbol)
            : null;
        int syllable = 0;

        // ---- words: a note with words, and the melisma notes that carry its vowel on ----
        var words = new List<Word>();
        var unknown = new List<string>();
        var guessed = new List<string>();
        for (int i = 0; i < notes.Count; i++)
        {
            var n = notes[i];
            if (n.Melisma)
            {
                words[^1].Notes.Add(n);
                continue;
            }
            List<string> phonemes;
            if (english is not null)
            {
                var sung = english[syllable++];
                if (sung.Phonemes.Count == 0 && !sung.Missing && words.Count > 0)
                {
                    words[^1].Notes.Add(n);     // a word with fewer vowels than notes: this one holds the last
                    continue;
                }
                phonemes = sung.Phonemes;
                if (sung.Guessed)
                {
                    guessed.Add(n.Lyric!);
                }
                if (sung.Missing)
                {
                    unknown.Add(n.Lyric!);
                }
            }
            else
            {
                var missing = new List<string>();
                phonemes = language switch
                {
                    "zh" => ChinesePhonemizer.Phonemes(n.Lyric!, dur.Dictionary, dur.Symbol, missing),
                    "ko" => KoreanPhonemizer.Phonemes(n.Lyric!, dur.Dictionary, dur.Symbol, missing),
                    _ => KanaPhonemizer.Phonemes(n.Lyric!, dur.Dictionary, dur.Symbol, missing),
                };
                if (missing.Count > 0)
                {
                    unknown.Add($"{n.Lyric} ({string.Join(" ", missing)})");
                }
            }
            if (phonemes.Count == 0)
            {
                phonemes.Add("SP");
            }
            words.Add(new Word(n, phonemes));
        }
        if (guessed.Count > 0)
        {
            log.AppendLine($"not in {dur.DictionaryFile}, pronounced from the spelling: {string.Join(", ", guessed.Distinct())}");
        }
        if (unknown.Count > 0)
        {
            if (unknown.Count == words.Count)
            {
                throw new SingerException($"no syllable of the score is in the voice's {dur.DictionaryFile}: {string.Join(", ", unknown.Take(5))}{(unknown.Count > 5 ? ", …" : "")}");
            }
            log.AppendLine($"not in {dur.DictionaryFile}, sung as silence: {string.Join(", ", unknown)}");
        }

        // ---- the phrases: each sung from its own time 0, once, and laid at its place in the score ----
        var cuts = PhraseCut.Split(words.Select(w => (w.StartMs / 1000, w.EndMs / 1000)).ToList(), RestBetweenPhrases, LongestPhrase);
        int rate = voice.Vocoder.SampleRate;
        var placed = new List<(double Seconds, SungPhrase Phrase)>();
        int fresh = 0;
        foreach (var (first, count) in cuts)
        {
            ct.ThrowIfCancellationRequested();
            double startSeconds = words[first].Notes[0].Start;
            var phrase = words.GetRange(first, count).Select(w => Shifted(w, startSeconds)).ToList();
            double? breathRoom = first + count < words.Count ? EndBreathRoom(words[first + count - 1], words[first + count]) : null;
            var key = new PhraseKey(speaker, language,
                phrase.Select(w => $"{string.Join(' ', w.Phonemes)}|{w.Notes.Count}").ToList(),
                phrase.SelectMany(w => w.Notes).Select(n => new PhraseNote(n.Start, n.Duration, n.Pitch, n.Breath)).ToList(),
                breathRoom);
            if (!_phrases.TryGet(key, out var sung))
            {
                sung = SingPhrase(phrase, breathRoom, log);
                _phrases.Add(key, sung);
                fresh++;
            }
            placed.Add((startSeconds, sung));
        }
        log.AppendLine($"phrases: {cuts.Count}, {fresh} sung, {cuts.Count - fresh} kept from before");
        log.AppendLine($"provider: {voice.Provider}" + (voice.ProviderNote is { } note ? $" ({note})" : ""));
        LastLog = log.ToString();
        return new SungAudio(SungPhrase.Lay(placed, rate), rate);
    }

    /// <summary>The word with its notes' times counted from <paramref name="seconds"/>.</summary>
    private static Word Shifted(Word w, double seconds)
    {
        var moved = new Word(w.Notes[0] with { Start = w.Notes[0].Start - seconds }, w.Phonemes);
        moved.Notes.AddRange(w.Notes.Skip(1).Select(n => n with { Start = n.Start - seconds }));
        return moved;
    }

    /// <summary>The time a breath at a phrase's end has before the next phrase's consonants, in ms;
    /// null where no breath is taken. Past what a breath can use it no longer matters, so a longer
    /// rest after the phrase does not make it a different phrase.</summary>
    private static double? EndBreathRoom(Word last, Word next)
    {
        double room = next.StartMs - last.EndMs - ConsonantRoomMs;
        return last.Notes[^1].Breath && room >= BreathMinMs ? Math.Min(room, BreathMaxMs + BreathClearMs) : null;
    }

    /// <summary>One phrase, its first note at time 0, through every model. A phrase begun is finished: asked to stop, a render stops at the next phrase.</summary>
    private SungPhrase SingPhrase(List<Word> words, double? breathRoom, System.Text.StringBuilder log)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var voice = _voice;
        var dur = voice.Dur;
        var notes = words.SelectMany(w => w.Notes).ToList();

        // ---- timing: the duration model, one run of notes that touch at a time ----
        var phrases = new List<List<Word>>();
        foreach (var w in words)
        {
            if (phrases.Count > 0 && Math.Abs(phrases[^1][^1].EndMs - w.StartMs) < 1)
            {
                phrases[^1].Add(w);
            }
            else
            {
                phrases.Add([w]);
            }
        }
        var phones = new List<Phone>();
        foreach (var phrase in phrases)
        {
            phones.AddRange(Time(phrase, dur));
        }
        // A breath: the voice's AP in the gap after the note, where there is a gap and the voice has AP.
        if (voice.AcousticSymbol("AP") is { } ap)
        {
            var withBreaths = new List<Phone>();
            for (int i = 0; i < phones.Count; i++)
            {
                withBreaths.Add(phones[i]);
                double? room = i + 1 < phones.Count ? phones[i + 1].StartMs - phones[i].EndMs : breathRoom;
                if (room >= BreathMinMs && notes.Any(n => n.Breath && Math.Abs(n.End * 1000 - phones[i].EndMs) < 1))
                {
                    double breathMs = Math.Min(room.Value - BreathClearMs, BreathMaxMs);
                    withBreaths.Add(new Phone(ap, phones[i].EndMs, phones[i].EndMs + breathMs));
                }
            }
            phones = withBreaths;
        }
        log.AppendLine($"timing: {phones.Count} phonemes in {clock.Elapsed.TotalSeconds:0.00} s");

        // ---- the padded segments the acoustic, pitch and variance models share ----
        double frameMs = voice.FrameMs;
        var segments = new List<(string Symbol, double Ms, int PhoneIndex)> { ("SP", HeadFrames * frameMs, -1) };
        for (int i = 0; i < phones.Count; i++)
        {
            if (i > 0)
            {
                double gap = phones[i].StartMs - phones[i - 1].EndMs;
                if (gap > 0)
                {
                    segments.Add(("SP", gap, -1));
                }
            }
            segments.Add((phones[i].Symbol, phones[i].EndMs - phones[i].StartMs, i));
        }
        segments.Add(("SP", TailFrames * frameMs, -1));
        var phDur = Frames(segments.Select(s => s.Ms), frameMs);
        int totalFrames = phDur.Sum();
        double renderStartMs = phones[0].StartMs - HeadFrames * frameMs;
        // The sung vowels (a breath AP counts), not the head, tail or gap silences: the word division's anchors.
        var isVowel = segments.Select(s => s.PhoneIndex >= 0 && voice.Dictionary.IsVowel(s.Symbol)).ToList();

        // ---- pitch ----
        float[] f0Midi = voice.Pitch is { } pitch
            ? PredictPitch(pitch, segments, phDur, totalFrames, notes, phones, renderStartMs, isVowel, log)
            : FlatPitch(segments, phDur, totalFrames, notes, phones);

        // ---- variance ----
        var acoustic = voice.Acoustic;
        bool wantsVariance = acoustic.UseEnergyEmbed || acoustic.UseBreathinessEmbed || acoustic.UseVoicingEmbed || acoustic.UseTensionEmbed;
        var variance = wantsVariance ? PredictVariance(voice.Variance, segments, phDur, totalFrames, f0Midi, isVowel, log) : new Dictionary<string, float[]>();

        // ---- acoustic ----
        var f0Hz = f0Midi.Select(ToHz).ToArray();
        var inputs = new List<NamedOnnxValue>
        {
            Row("tokens", segments.Select(s => AcousticToken(s.Symbol)).ToArray()),
            Row("durations", phDur.Select(x => (long)x).ToArray()),
            Row("f0", f0Hz),
        };
        int steps = voice.Options.Steps;
        double depth = acoustic.UseVariableDepth ? acoustic.MaxDepth : 1.0;
        if (acoustic.UseContinuousAcceleration)
        {
            if (acoustic.UseVariableDepth)
            {
                inputs.Add(Scalar("depth", (float)depth));
            }
            inputs.Add(Scalar("steps", (long)steps));
        }
        else if (acoustic.UseVariableDepth)
        {
            long d = (long)Math.Round(depth * 1000);
            long speedup = Math.Max(1, d / steps);
            d = d / speedup * speedup;
            inputs.Add(Scalar("depth", d));
            inputs.Add(Scalar("speedup", speedup));
        }
        else
        {
            inputs.Add(Scalar("speedup", Speedup(steps)));
        }
        if (acoustic.UseLangId)
        {
            inputs.Add(Row("languages", segments.Select(s => (long)voice.AcousticLanguages.GetValueOrDefault(LanguageOf(s.Symbol), 0)).ToArray()));
        }
        if (voice.AcousticSpeaker is { } embed)
        {
            inputs.Add(DiffSingerVoice.SpeakerRows(embed, totalFrames));
        }
        if (acoustic.UseKeyShiftEmbed)
        {
            inputs.Add(Row("gender", new float[totalFrames]));
        }
        if (acoustic.UseSpeedEmbed)
        {
            inputs.Add(Row("velocity", Enumerable.Repeat(1f, totalFrames).ToArray()));
        }
        foreach (var (name, wanted) in new[] { ("energy", acoustic.UseEnergyEmbed), ("breathiness", acoustic.UseBreathinessEmbed), ("voicing", acoustic.UseVoicingEmbed), ("tension", acoustic.UseTensionEmbed) })
        {
            if (!wanted)
            {
                continue;
            }
            if (!variance.TryGetValue(name, out var curve))
            {
                throw new SingerException($"the acoustic model wants {name} but the voice's variance model does not predict it");
            }
            inputs.Add(Row(name, curve.Length == totalFrames ? curve : Resample(curve, totalFrames)));
        }
        var acousticOut = Run(voice.AcousticSession, inputs, "acoustic");
        var mel = acousticOut.First().AsTensor<float>().Clone();
        log.AppendLine($"acoustic: {totalFrames} frames at {steps} steps in {clock.Elapsed.TotalSeconds:0.00} s");
        if (voice.Vocoder.MelBase != acoustic.MelBase)
        {
            float k = voice.Vocoder.MelBase == "e" ? 2.30259f : 0.434294f;
            for (int t = 0; t < mel.Dimensions[1]; t++)
            {
                for (int c = 0; c < mel.Dimensions[2]; c++)
                {
                    mel[0, t, c] *= k;
                }
            }
        }

        // ---- vocoder ----
        var waveform = Run(voice.VocoderSession, [NamedOnnxValue.CreateFromTensor("mel", mel), Row("f0", f0Hz)], "vocoder");
        var samples = waveform.First().AsTensor<float>().ToArray();
        int rate = voice.Vocoder.SampleRate;
        log.AppendLine($"vocoder: {samples.Length / (double)rate:0.00} s at {rate} Hz in {clock.Elapsed.TotalSeconds:0.00} s");
        return new SungPhrase(samples, renderStartMs, rate);
    }

    private long AcousticToken(string symbol)
    {
        var known = _voice.AcousticSymbol(symbol) ?? throw new SingerException($"the phoneme \"{symbol}\" is not in the acoustic model's {_voice.Acoustic.Phonemes}");
        return _voice.AcousticPhonemes[known];
    }

    private static string LanguageOf(string symbol) => symbol.Contains('/') ? symbol[..symbol.IndexOf('/')] : "";

    /// <summary>
    /// One phrase through the duration model: the vowel of each syllable starts its note, the
    /// consonants before it are placed by the model and may begin before the note; the first
    /// consonants keep the model's length, every later group is stretched to end where its note ends.
    /// </summary>
    private static List<Phone> Time(List<Word> phrase, DiffSingerVoice.Module dur)
    {
        var dict = dur.Dictionary;
        // Entries: (position ms, tone, phonemes); the first holds SP and the consonants before the first note.
        var entries = new List<(double Pos, int Tone, List<string> Phs)> { (phrase[0].StartMs - LeadPaddingMs, phrase[0].Tone, ["SP"]) };
        foreach (var w in phrase)
        {
            var syms = w.Phonemes;
            var start = new bool[syms.Count];
            if (!syms.Any(dict.IsVowel))
            {
                start[0] = true;
            }
            for (int k = 0; k < syms.Count; k++)
            {
                if (!dict.IsVowel(syms[k]))
                {
                    continue;
                }
                // In consonant-glide-vowel the glide is the first phoneme in the note.
                if (k >= 2 && dict.IsGlide(syms[k - 1]) && !dict.IsVowel(syms[k - 2]))
                {
                    start[k - 1] = true;
                }
                else
                {
                    start[k] = true;
                }
            }
            int noteIndex = 0;
            var perNote = new List<(double Pos, int Tone, List<string> Phs)> { (-1, w.Tone, []) };
            for (int k = 0; k < syms.Count; k++)
            {
                if (start[k] && noteIndex < w.Notes.Count)
                {
                    var nn = w.Notes[noteIndex++];
                    perNote.Add((nn.Start * 1000, (int)Math.Round(nn.Pitch), []));
                }
                perNote[^1].Phs.Add(syms[k]);
            }
            entries[^1].Phs.AddRange(perNote[0].Phs);
            entries.AddRange(perNote.Skip(1));
        }
        entries.Add((phrase[^1].EndMs, phrase[^1].Tone, []));
        entries[0] = (entries[1].Pos - LeadPaddingMs, entries[0].Tone, entries[0].Phs);

        var all = entries.SelectMany(e => e.Phs).ToList();
        var tokens = all.Select(dur.Token).ToArray();
        var wordDiv = entries.Take(entries.Count - 1).Select(e => (long)e.Phs.Count).ToArray();
        var wordDur = Frames(entries.Zip(entries.Skip(1), (a, b) => b.Pos - a.Pos), dur.FrameMs).Select(x => (long)x).ToArray();
        var linguisticIn = new List<NamedOnnxValue> { Row("tokens", tokens), Row("word_div", wordDiv), Row("word_dur", wordDur) };
        if (dur.Config.UseLangId)
        {
            linguisticIn.Add(Row("languages", all.Select(dur.LanguageId).ToArray()));
        }
        var linguisticOut = Run(dur.Linguistic, linguisticIn, "duration linguistic");
        var encoderOut = linguisticOut.First(o => o.Name == "encoder_out").AsTensor<float>();
        var xMasks = linguisticOut.First(o => o.Name == "x_masks").AsTensor<bool>();
        var durIn = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("encoder_out", encoderOut),
            NamedOnnxValue.CreateFromTensor("x_masks", xMasks),
            Row("ph_midi", entries.SelectMany(e => Enumerable.Repeat((long)e.Tone, e.Phs.Count)).ToArray()),
            Row("word_div", wordDiv),
            Row("word_dur", wordDur),
        };
        if (dur.Speaker is { } embed)
        {
            durIn.Add(DiffSingerVoice.SpeakerRows(embed, tokens.Length));
        }
        var frames = Run(dur.Main, durIn, "duration").First().AsTensor<float>().Select(x => (double)x).ToList();

        // Alignment: align point k = (index of the first phoneme of entry k+1, its ms).
        var alignPoints = new List<(int Index, double Ms)>();
        int acc = 0;
        for (int k = 0; k < entries.Count - 1; k++)
        {
            acc += entries[k].Phs.Count;
            alignPoints.Add((acc, entries[k + 1].Pos));
        }
        var positions = new List<double>();
        positions.AddRange(Stretch(frames.GetRange(1, alignPoints[0].Index - 1), dur.FrameMs, alignPoints[0].Ms));
        for (int k = 0; k + 1 < alignPoints.Count; k++)
        {
            var group = frames.GetRange(alignPoints[k].Index, alignPoints[k + 1].Index - alignPoints[k].Index);
            double sum = group.Sum();
            double ratio = sum > 0 ? (alignPoints[k + 1].Ms - alignPoints[k].Ms) / sum : 0;
            positions.AddRange(Stretch(group, ratio, alignPoints[k + 1].Ms));
        }
        var phones = new List<Phone>();
        double endMs = entries[^1].Pos;
        for (int j = 1; j < all.Count; j++)   // all[0] is the SP; positions[j-1] is where all[j] begins
        {
            double from = positions[j - 1];
            double to = j < all.Count - 1 ? positions[j] : endMs;
            if (to > from)
            {
                phones.Add(new Phone(all[j], from, to));
            }
        }
        return phones;
    }

    private static float[] PredictPitch(DiffSingerVoice.Module pitch, List<(string Symbol, double Ms, int PhoneIndex)> segments, int[] phDur, int totalFrames,
        IReadOnlyList<SungNote> notes, List<Phone> phones, double renderStartMs, List<bool> isVowel, System.Text.StringBuilder log)
    {
        if (Math.Abs(pitch.FrameMs - segments[0].Ms / HeadFrames) > 1e-6)
        {
            throw new SingerException($"the pitch model's frame ({pitch.FrameMs:0.###} ms) differs from the acoustic model's ({segments[0].Ms / HeadFrames:0.###} ms); not handled yet");
        }
        var tokens = segments.Select(s => pitch.Token(pitch.Symbol(s.Symbol) ?? s.Symbol)).ToArray();
        var linguisticIn = new List<NamedOnnxValue> { Row("tokens", tokens) };
        if (pitch.Config.PredictDur)
        {
            var (div, durs) = WordDivAndDur(isVowel, phDur);
            linguisticIn.Add(Row("word_div", div));
            linguisticIn.Add(Row("word_dur", durs));
        }
        else
        {
            linguisticIn.Add(Row("ph_dur", phDur.Select(x => (long)x).ToArray()));
        }
        if (pitch.Config.UseLangId)
        {
            linguisticIn.Add(Row("languages", segments.Select(s => pitch.LanguageId(s.Symbol)).ToArray()));
        }
        var encoderOut = Run(pitch.Linguistic, linguisticIn, "pitch linguistic").First(o => o.Name == "encoder_out").AsTensor<float>();

        // The notes with rests in the gaps, a head and a tail; a rest takes the tone of the nearest sung note.
        var noteDurMs = new List<double>();
        var noteMidi = new List<float>();
        var noteRest = new List<bool>();
        noteDurMs.Add(Math.Max(0, notes[0].Start * 1000 - renderStartMs));
        noteMidi.Add((float)notes[0].Pitch);
        noteRest.Add(true);
        double prevEnd = notes[0].Start * 1000;
        foreach (var n in notes)
        {
            double gap = n.Start * 1000 - prevEnd;
            if (gap > 0)
            {
                noteDurMs.Add(gap);
                noteMidi.Add((float)n.Pitch);
                noteRest.Add(true);
            }
            noteDurMs.Add(n.Duration * 1000);
            noteMidi.Add((float)n.Pitch);
            if (n.Melisma)
            {
                noteRest.Add(noteRest[^1]);
            }
            else
            {
                var inNote = phones.Where(p => p.EndMs > n.Start * 1000 + 1 && p.StartMs < n.End * 1000 - 1).ToList();
                noteRest.Add(inNote.Count == 0 || inNote.All(p => p.Symbol is "AP" or "SP" || !pitch.Dictionary.IsVowel(p.Symbol)));
            }
            prevEnd = n.End * 1000;
        }
        noteDurMs.Add(TailFrames * pitch.FrameMs);
        noteMidi.Add((float)notes[^1].Pitch);
        noteRest.Add(true);
        var midi = noteMidi.ToArray();
        for (int i = 0; i < noteRest.Count; i++)
        {
            if (!noteRest[i])
            {
                continue;
            }
            int j = i;
            while (j < noteRest.Count && noteRest[j])
            {
                j++;
            }
            if (i == 0)
            {
                if (j < midi.Length)
                {
                    Array.Fill(midi, midi[j], 0, j);
                }
            }
            else if (j == noteRest.Count)
            {
                Array.Fill(midi, midi[i - 1], i, j - i);
            }
            else
            {
                int mid = (i + j + 1) / 2;
                Array.Fill(midi, midi[i - 1], i, mid - i);
                Array.Fill(midi, midi[j], mid, j - mid);
            }
            i = j;
        }
        var noteDur = FitSum(Frames(noteDurMs, pitch.FrameMs), totalFrames);
        var pitchIn = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("encoder_out", encoderOut),
            Row("note_midi", midi),
            Row("note_dur", noteDur.Select(x => (long)x).ToArray()),
            Row("ph_dur", phDur.Select(x => (long)x).ToArray()),
            Row("pitch", Enumerable.Repeat(60f, totalFrames).ToArray()),
            Row("retake", Enumerable.Repeat(true, totalFrames).ToArray()),
        };
        int steps = 10;
        if (pitch.Config.UseContinuousAcceleration)
        {
            pitchIn.Add(Scalar("steps", (long)steps));
        }
        else
        {
            pitchIn.Add(Scalar("speedup", Speedup(steps)));
        }
        if (pitch.Config.UseExpr)
        {
            pitchIn.Add(Row("expr", Enumerable.Repeat(1f, totalFrames).ToArray()));
        }
        if (pitch.Speaker is { } embed)
        {
            pitchIn.Add(DiffSingerVoice.SpeakerRows(embed, totalFrames));
        }
        if (pitch.Config.UseNoteRest)
        {
            pitchIn.Add(Row("note_rest", noteRest.ToArray()));
        }
        var f0 = Run(pitch.Main, pitchIn, "pitch").First().AsTensor<float>().ToArray();
        log.AppendLine($"pitch: {f0.Length} frames, {f0.Min():0.0}..{f0.Max():0.0} semitones");
        return f0;
    }

    /// <summary>Without a pitch model: each frame at its note's pitch.</summary>
    private static float[] FlatPitch(List<(string Symbol, double Ms, int PhoneIndex)> segments, int[] phDur, int totalFrames, IReadOnlyList<SungNote> notes, List<Phone> phones)
    {
        var f0 = new float[totalFrames];
        int f = 0;
        for (int s = 0; s < segments.Count; s++)
        {
            double at = segments[s].PhoneIndex >= 0 ? phones[segments[s].PhoneIndex].StartMs : -1;
            var note = notes.LastOrDefault(n => n.Start * 1000 <= at + 1) ?? notes[0];
            for (int k = 0; k < phDur[s] && f < totalFrames; k++)
            {
                f0[f++] = (float)note.Pitch;
            }
        }
        return f0;
    }

    private static Dictionary<string, float[]> PredictVariance(DiffSingerVoice.Module? variance, List<(string Symbol, double Ms, int PhoneIndex)> segments, int[] phDur, int totalFrames,
        float[] f0Midi, List<bool> isVowel, System.Text.StringBuilder log)
    {
        if (variance is null)
        {
            throw new SingerException("the acoustic model wants variance curves (energy, breathiness, …) but the voice has no dsvariance model");
        }
        if (Math.Abs(variance.FrameMs - segments[0].Ms / HeadFrames) > 1e-6)
        {
            throw new SingerException($"the variance model's frame ({variance.FrameMs:0.###} ms) differs from the acoustic model's ({segments[0].Ms / HeadFrames:0.###} ms); not handled yet");
        }
        var tokens = segments.Select(s => variance.Token(variance.Symbol(s.Symbol) ?? s.Symbol)).ToArray();
        var linguisticIn = new List<NamedOnnxValue> { Row("tokens", tokens) };
        if (variance.Config.PredictDur)
        {
            var (div, durs) = WordDivAndDur(isVowel, phDur);
            linguisticIn.Add(Row("word_div", div));
            linguisticIn.Add(Row("word_dur", durs));
        }
        else
        {
            linguisticIn.Add(Row("ph_dur", phDur.Select(x => (long)x).ToArray()));
        }
        if (variance.Config.UseLangId)
        {
            linguisticIn.Add(Row("languages", segments.Select(s => variance.LanguageId(s.Symbol)).ToArray()));
        }
        var encoderOut = Run(variance.Linguistic, linguisticIn, "variance linguistic").First(o => o.Name == "encoder_out").AsTensor<float>();
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("encoder_out", encoderOut),
            Row("ph_dur", phDur.Select(x => (long)x).ToArray()),
            Row("pitch", f0Midi),
        };
        var predicted = new List<string>();
        foreach (var (name, wanted) in new[] { ("energy", variance.Config.PredictEnergy), ("breathiness", variance.Config.PredictBreathiness), ("voicing", variance.Config.PredictVoicing), ("tension", variance.Config.PredictTension) })
        {
            if (wanted)
            {
                inputs.Add(Row(name, new float[totalFrames]));
                predicted.Add(name);
            }
        }
        inputs.Add(NamedOnnxValue.CreateFromTensor("retake", new DenseTensor<bool>(Enumerable.Repeat(true, totalFrames * predicted.Count).ToArray(), [1, totalFrames, predicted.Count])));
        int steps = 20;
        if (variance.Config.UseContinuousAcceleration)
        {
            inputs.Add(Scalar("steps", (long)steps));
        }
        else
        {
            inputs.Add(Scalar("speedup", Speedup(steps)));
        }
        if (variance.Speaker is { } embed)
        {
            inputs.Add(DiffSingerVoice.SpeakerRows(embed, totalFrames));
        }
        var outputs = Run(variance.Main, inputs, "variance");
        var result = new Dictionary<string, float[]>();
        foreach (var name in predicted)
        {
            var o = outputs.FirstOrDefault(o => o.Name == name + "_pred");
            if (o is not null)
            {
                result[name] = o.AsTensor<float>().ToArray();
            }
        }
        log.AppendLine($"variance: {string.Join(", ", result.Keys)}");
        return result;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _voice.Dispose();
        _oneAtATime.Dispose();
    }
}

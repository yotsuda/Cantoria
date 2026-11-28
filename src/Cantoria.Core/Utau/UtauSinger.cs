// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Cantoria.Audio;
using Cantoria.DiffSinger;
using Cantoria.Neutrino;

namespace Cantoria.Utau;

/// <summary>
/// Sings with an UTAU voicebank in this process, as UTAU's resampler and wavtool do between them:
/// each mora's recording (<see cref="UtauPhonemizer"/>) is analysed by WORLD into its pitch, spectral
/// envelope and aperiodicity, made as long as its note (the consonant as recorded, the rest stretched
/// evenly where the note is longer than the recording), synthesised again on the score's pitch, and
/// laid in place with its preutterance before the beat and its overlap crossfaded into the sound before.
/// No GPU and no model: it sings on any machine. Japanese from kana, and English with an English CVVC
/// voicebank (<see cref="UtauEnglishPhonemizer"/>); a voicebank sings the languages its aliases say
/// (<see cref="UtauVoicebank.Languages"/>).
/// <para>The pitch is the score's own: each note's, gliding over <see cref="GlideSeconds"/> either side of
/// the start of a note that follows without a rest, with the written vibrato and bend
/// (<see cref="Vibrato"/>, <see cref="Bend"/>) as DiffSinger voices sing them. Where the recording is
/// unvoiced it stays unvoiced. Where a recording's preutterance minus its overlap is more than half the
/// note before, both are cut to fit, as UTAU does. A second of time before the score leads in (a first
/// consonant sounds before its note) and is cut from what is sung. The audio is at the voicebank's
/// sample rate.</para>
/// </summary>
public sealed class UtauSinger : ISinger, IDisposable
{
    /// <summary>WORLD's frame period, ms.</summary>
    public const double FrameMs = 5;

    /// <summary>The time before the score's start that is rendered and cut.</summary>
    public const double LeadInSeconds = 1;

    /// <summary>What is kept after the score's end.</summary>
    public const double TailSeconds = 0.3;

    /// <summary>The fade after a sound's end when no sound follows it.</summary>
    public const double ReleaseSeconds = 0.03;

    /// <summary>The fade into a sound that follows silence.</summary>
    public const double AttackSeconds = 0.005;

    /// <summary>Half the glide between two notes that follow without a rest.</summary>
    public const double GlideSeconds = 0.04;

    private const double F0Floor = 60, F0Ceil = 1100;

    private readonly UtauOptions _options;
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private readonly Dictionary<string, SungAudio> _recordings = new(StringComparer.Ordinal);
    private readonly Dictionary<(string File, double From, double To, int Rate), Analysis> _analyses = new();
    private UtauVoicebank? _bank;
    private bool _disposed;

    public UtauSinger(UtauOptions options)
    {
        _options = options;
    }

    public string Name => $"utau/{_options.Name}";

    public async Task<SungAudio> SingAsync(SungScore score, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var problems = score.Validate();
        if (problems.Count > 0)
        {
            throw new SingerException("the score is not sound: " + string.Join("; ", problems));
        }
        if (LanguageProblem(score) is { } language)
        {
            throw new SingerException(language);
        }
        await _oneAtATime.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => Sing(score, ct), ct).ConfigureAwait(false);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    /// <summary>Why the score's words are not ones the engine reads (kana, or English), or null.</summary>
    public static string? LanguageProblem(SungScore score) => score.Language switch
    {
        "ja" => NeutrinoMusicXml.NotKanaProblem(score, "UTAU"),
        "en" => null,
        _ => $"UTAU sings Japanese (kana) and English, and these words are {score.Language}",
    };

    private SungAudio Sing(SungScore score, CancellationToken ct)
    {
        var bank = Bank();
        if (!bank.Languages.Contains(score.Language))
        {
            throw new SingerException($"{Name} sings {string.Join(", ", bank.Languages)}, and these words are {score.Language}");
        }
        var segments = UtauPhonemizer.Segments(score, bank, Name);
        int fs = segments.Count > 0 ? Recording(segments[0].Oto.File).SampleRate : score.SampleRate;
        double lead = LeadInSeconds;
        var output = new float[(int)Math.Ceiling((lead + score.End + TailSeconds) * fs)];
        if (segments.Count == 0)
        {
            return new SungAudio(output[(int)(lead * fs)..], fs);
        }

        var curve = PitchCurve(score, lead, (lead + score.End + TailSeconds) * 1000);
        Vibrato.Apply(curve, FrameMs, -lead * 1000, Vibrato.Of(score.Notes));
        Bend.Apply(curve, FrameMs, -lead * 1000, Bend.Of(score.Notes));

        var timings = Timings(segments);
        int count = segments.Count;
        for (int k = 0; k < count; k++)
        {
            ct.ThrowIfCancellationRequested();
            var s = segments[k];
            var (from, to, fadeIn, fadeOut, skip) = timings[k];
            var y = Render(s.Oto, fs, from, to, curve, lead, skip * 1000);
            int at = (int)Math.Round((from + lead) * fs);
            int inLen = Math.Max(1, (int)(fadeIn * fs)), outLen = Math.Max(1, (int)(fadeOut * fs));
            for (int i = 0; i < y.Length; i++)
            {
                int o = at + i;
                if (o < 0 || o >= output.Length)
                {
                    continue;
                }
                double gain = Math.Min(1, Math.Min((i + 1.0) / inLen, (y.Length - i) / (double)outLen));
                output[o] += (float)(y[i] * gain);
            }
        }
        int start = (int)Math.Round(lead * fs);
        return new SungAudio(output[start..], fs);
    }

    /// <summary>
    /// When each recording sounds, in seconds on the score's clock: from its preutterance before its beat to where
    /// the next one's overlap ends (or 30 ms past its end, with silence after), and the fades in and out. Where a
    /// recording's preutterance minus its overlap is more than half the sound before, both are cut to fit, as UTAU
    /// does; after a bridge (<see cref="UtauSegment.Bridge"/>), laid for that preutterance to sound over it, more
    /// than nine tenths.
    /// </summary>
    public static (double From, double To, double FadeIn, double FadeOut, double Skip)[] Timings(IReadOnlyList<UtauSegment> segments)
    {
        int count = segments.Count;
        var pre = new double[count];
        var over = new double[count];
        var skip = new double[count];
        var joined = new bool[count];   // the sound follows the one before without silence
        for (int k = 0; k < count; k++)
        {
            var s = segments[k];
            double p = Math.Max(0, s.Oto.Preutter / 1000), o = Math.Clamp(s.Oto.Overlap / 1000, 0, Math.Max(0, s.Oto.Preutter / 1000));
            joined[k] = k > 0 && s.Start - segments[k - 1].End < UtauPhonemizer.Touching;
            if (joined[k])
            {
                var b = segments[k - 1];
                double room = (s.Start - b.Start) * (b.Bridge ? 0.9 : 0.5);
                if (p - o > room && p - o > 0)
                {
                    double cut = room / (p - o);
                    // A recording from a vowel loses the front of that vowel; another starts later in its recording's time.
                    skip[k] = s.FromVowel ? p * (1 - cut) : 0;
                    p *= cut;
                    o *= cut;
                }
            }
            pre[k] = p;
            over[k] = o;
        }
        var timings = new (double, double, double, double, double)[count];
        for (int k = 0; k < count; k++)
        {
            var s = segments[k];
            double from = s.Start - pre[k];
            bool next = k + 1 < count && joined[k + 1];
            double to = next ? segments[k + 1].Start - pre[k + 1] + over[k + 1] : s.End + ReleaseSeconds;
            to = Math.Max(to, from + 2 * FrameMs / 1000);
            double fadeIn = joined[k] ? Math.Max(over[k], AttackSeconds) : AttackSeconds;
            double fadeOut = next ? Math.Max(over[k + 1], AttackSeconds) : ReleaseSeconds;
            timings[k] = (from, to, fadeIn, fadeOut, skip[k]);
        }
        return timings;
    }

    /// <summary>
    /// The score's pitch in MIDI semitones a frame from <c>-lead</c> s: each note's, held through the
    /// rest after it (and the first note's before it), gliding over <see cref="GlideSeconds"/> either
    /// side of the start of a note that follows without a rest (no more than half of either note).
    /// </summary>
    public static float[] PitchCurve(SungScore score, double lead, double lengthMs)
    {
        var curve = new float[(int)Math.Ceiling(lengthMs / FrameMs) + 1];
        var notes = score.Notes;
        if (notes.Count == 0)
        {
            return curve;
        }
        int n = 0;
        for (int f = 0; f < curve.Length; f++)
        {
            double t = f * FrameMs / 1000 - lead;
            while (n + 1 < notes.Count && notes[n + 1].Start <= t)
            {
                n++;
            }
            curve[f] = (float)notes[n].Pitch;
        }
        for (int i = 1; i < notes.Count; i++)
        {
            var a = notes[i - 1];
            var b = notes[i];
            if (b.Start - a.End >= UtauPhonemizer.Touching || a.Pitch == b.Pitch)
            {
                continue;
            }
            double g = Math.Min(GlideSeconds, Math.Min(a.Duration, b.Duration) / 2);
            int f0 = (int)Math.Ceiling((b.Start - g + lead) * 1000 / FrameMs), f1 = (int)Math.Floor((b.Start + g + lead) * 1000 / FrameMs);
            for (int f = Math.Max(0, f0); f <= Math.Min(curve.Length - 1, f1); f++)
            {
                double u = (f * FrameMs / 1000 - lead - (b.Start - g)) / (2 * g);
                curve[f] = (float)(a.Pitch + (b.Pitch - a.Pitch) * (1 - Math.Cos(Math.PI * Math.Clamp(u, 0, 1))) / 2);
            }
        }
        return curve;
    }

    /// <summary>A recording made as long as from..to (seconds on the score's clock) on the curve's pitch, its first
    /// <paramref name="skipMs"/> left out.</summary>
    private double[] Render(Oto oto, int fs, double from, double to, float[] curve, double lead, double skipMs = 0)
    {
        var src = Analyse(oto, fs);
        skipMs = Math.Clamp(skipMs, 0, Math.Max(0, src.LengthMs - 2 * FrameMs));
        double fixedMs = Math.Clamp(oto.Consonant - skipMs, 0, src.LengthMs - skipMs);
        double outMs = (to - from) * 1000;
        int frames = (int)(outMs / FrameMs) + 1;
        int width = src.Width;
        var f0 = new double[frames];
        var sp = new double[frames * width];
        var ap = new double[frames * width];
        double rest = outMs - fixedMs, recorded = src.LengthMs - skipMs - fixedMs;
        for (int j = 0; j < frames; j++)
        {
            double t = j * FrameMs;
            double srcMs = skipMs + (t <= fixedMs ? t : fixedMs + (rest <= recorded || rest <= 0 ? t - fixedMs : (t - fixedMs) * recorded / rest));
            double x = Math.Clamp(srcMs / FrameMs, 0, src.Frames - 1);
            int i0 = (int)x, i1 = Math.Min(i0 + 1, src.Frames - 1);
            double w = x - i0;
            for (int b = 0; b < width; b++)
            {
                sp[j * width + b] = src.Sp[i0 * width + b] * (1 - w) + src.Sp[i1 * width + b] * w;
                ap[j * width + b] = src.Ap[i0 * width + b] * (1 - w) + src.Ap[i1 * width + b] * w;
            }
            bool voiced = (w < 0.5 ? src.F0[i0] : src.F0[i1]) > 0;
            double c = Math.Clamp((from + lead) * 1000 / FrameMs + j, 0, curve.Length - 1);
            int c0 = (int)c, c1 = Math.Min(c0 + 1, curve.Length - 1);
            double midi = curve[c0] + (curve[c1] - curve[c0]) * (c - c0);
            f0[j] = voiced ? 440 * Math.Pow(2, (midi - 69) / 12) : 0;
        }
        var y = new double[Math.Max(1, (int)(outMs / 1000 * fs))];
        WorldNative.Synthesize(f0, frames, sp, ap, src.FftSize, FrameMs, fs, y.Length, y);
        return y;
    }

    /// <summary>What WORLD made of a recording's stretch: <see cref="Frames"/> frames of pitch (0 where
    /// unvoiced), and rows of <see cref="Width"/> values of spectral envelope and aperiodicity.</summary>
    private sealed record Analysis(double[] F0, double[] Sp, double[] Ap, int Frames, int Width, int FftSize, double LengthMs);

    /// <summary>The alias's stretch of its recording at <paramref name="fs"/>, analysed (once a singer).</summary>
    private Analysis Analyse(Oto oto, int fs)
    {
        var audio = Recording(oto.File);
        double totalMs = audio.Duration * 1000;
        double fromMs = Math.Clamp(oto.Offset, 0, totalMs);
        double toMs = Math.Clamp(oto.Cutoff >= 0 ? totalMs - oto.Cutoff : oto.Offset - oto.Cutoff, fromMs, totalMs);
        var key = (oto.File, fromMs, toMs, fs);
        if (_analyses.TryGetValue(key, out var known))
        {
            return known;
        }
        var x = Stretch(audio, fromMs, toMs, fs);
        if (x.Length < fs * 3 * FrameMs / 1000)
        {
            throw new SingerException($"{Name}: the alias \"{oto.Alias}\" is too short to sing ({toMs - fromMs:0} ms of {oto.File}); its oto.ini line needs a look");
        }
        int frames = WorldNative.Frames(fs, x.Length, FrameMs);
        int fft = WorldNative.FftSize(fs);
        int width = fft / 2 + 1;
        var f0 = new double[frames];
        var sp = new double[frames * width];
        var ap = new double[frames * width];
        if (WorldNative.Analyze(x, x.Length, fs, FrameMs, F0Floor, F0Ceil, frames, f0, sp, ap) != frames)
        {
            throw new SingerException($"{Name}: WORLD could not analyse {oto.File}");
        }
        return _analyses[key] = new Analysis(f0, sp, ap, frames, width, fft, x.Length * 1000.0 / fs);
    }

    /// <summary>The samples of fromMs..toMs of a recording, at fs (linearly resampled where the file's rate differs).</summary>
    private static double[] Stretch(SungAudio audio, double fromMs, double toMs, int fs)
    {
        int length = (int)((toMs - fromMs) / 1000 * fs);
        var x = new double[Math.Max(0, length)];
        double ratio = (double)audio.SampleRate / fs, start = fromMs / 1000 * audio.SampleRate;
        for (int i = 0; i < x.Length; i++)
        {
            double p = start + i * ratio;
            int p0 = (int)p;
            if (p0 + 1 >= audio.Samples.Length)
            {
                x[i] = p0 < audio.Samples.Length ? audio.Samples[p0] : 0;
                continue;
            }
            double w = p - p0;
            x[i] = audio.Samples[p0] * (1 - w) + audio.Samples[p0 + 1] * w;
        }
        return x;
    }

    /// <summary>A recording of the voicebank, read once a singer.</summary>
    private SungAudio Recording(string file)
    {
        if (_recordings.TryGetValue(file, out var audio))
        {
            return audio;
        }
        if (!File.Exists(file))
        {
            throw new SingerException($"{Name}: the recording {file} that its oto.ini names does not exist");
        }
        try
        {
            audio = Wav.Read(File.ReadAllBytes(file));
        }
        catch (InvalidDataException e)
        {
            throw new SingerException($"{Name}: {file} could not be read: {e.Message}", e);
        }
        return _recordings[file] = audio;
    }

    /// <summary>The voicebank, read on first use and kept.</summary>
    private UtauVoicebank Bank()
    {
        if (_bank is not null)
        {
            return _bank;
        }
        var problems = _options.Validate();
        if (problems.Count > 0)
        {
            throw new SingerException(string.Join("; ", problems));
        }
        return _bank = UtauVoicebank.Load(_options.Folder);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _oneAtATime.Dispose();
    }
}

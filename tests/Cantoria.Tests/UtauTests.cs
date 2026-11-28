// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text;
using Cantoria.Audio;
using Cantoria.Utau;
using Xunit;

namespace Cantoria.Tests;

/// <summary>
/// The UTAU engine: reading a voicebank, choosing each mora's recording, and singing. The singing
/// facts use a voicebank the test makes (a buzz with vowel-like resonances, and a noise burst for a
/// consonant), so they run wherever the engine is built (cantoria_world beside the tests, by
/// tools/build-native.ps1; elsewhere they pass without doing anything). A real voicebank is sung too
/// where CANTORIA_UTAU_VOICE names its folder.
/// </summary>
public sealed class UtauTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cantoria-utau-" + Guid.NewGuid().ToString("N"));

    public UtauTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
        }
    }

    private static Encoding ShiftJis()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932);
    }

    /// <summary>A voicebank folder with these oto.ini lines (written in Shift_JIS, as UTAU writes them), in <paramref name="sub"/> if given.</summary>
    private string Bank(string name, string oto, string? sub = null, string? prefixMap = null, string? character = null)
    {
        var folder = Path.Combine(_dir, name);
        var otoFolder = sub is null ? folder : Path.Combine(folder, sub);
        Directory.CreateDirectory(otoFolder);
        File.WriteAllBytes(Path.Combine(otoFolder, "oto.ini"), ShiftJis().GetBytes(oto.Replace("\n", "\r\n")));
        if (prefixMap is not null)
        {
            File.WriteAllBytes(Path.Combine(folder, "prefix.map"), ShiftJis().GetBytes(prefixMap));
        }
        if (character is not null)
        {
            File.WriteAllBytes(Path.Combine(folder, "character.txt"), ShiftJis().GetBytes(character));
        }
        return folder;
    }

    [Fact]
    public void OtoLines_AreRead_WithTheirFileBesideThem()
    {
        var entries = UtauVoicebank.ParseOto("_か.wav=か,10.5,120,-650,70,20\r\nあ.wav=,20\r\nnot a line\r\n", "B").ToList();
        Assert.Equal(2, entries.Count);
        Assert.Equal(new Oto(Path.Combine("B", "_か.wav"), "か", 10.5, 120, -650, 70, 20), entries[0]);
        Assert.Equal("あ", entries[1].Alias);   // an empty alias is the file's name
        Assert.Equal(20, entries[1].Offset);
        Assert.Equal(0, entries[1].Preutter);
    }

    [Fact]
    public void NoteNames_AreUtausMidi()
    {
        Assert.Equal(60, UtauVoicebank.Midi("C4"));
        Assert.Equal(58, UtauVoicebank.Midi("A#3"));
        Assert.Equal(61, UtauVoicebank.Midi("C#4"));
        Assert.Null(UtauVoicebank.Midi("H4"));
    }

    [Fact]
    public void AVoicebank_IsRead_FromShiftJis_ItsSubfolders_AndItsPrefixMap()
    {
        var folder = Bank("テト", "_か.wav=か,10,120,-650,70,20\n_か↑.wav=か↑,10,120,-650,70,20\n", sub: "単独音",
            prefixMap: "C4\t\t\nC5\t\t↑\n", character: "name=重音テト\r\nimage=teto.bmp\r\n");
        Assert.True(UtauOptions.IsVoicebank(folder));
        Assert.Equal(["テト"], UtauOptions.Voices(_dir));
        Assert.Equal(folder, UtauOptions.Find(_dir, "テト"));
        Assert.Null(UtauOptions.Find(_dir, "../テト"));
        var bank = UtauVoicebank.Load(folder);
        Assert.Equal("重音テト", bank.Label);
        Assert.Equal(Path.Combine(folder, "単独音", "_か.wav"), bank.Find("か", 60)!.File);
        Assert.Equal("か↑", bank.Find("か", 72)!.Alias);    // C5's suffix
        Assert.Equal("か", bank.Find("か", 65)!.Alias);     // no line for F4: as written
        Assert.Equal("重音テト", VoiceEntries.Utau(_dir).Single().Label);
    }

    private UtauVoicebank Vcv() => UtauVoicebank.Load(Bank("vcv", "a.wav=- か,0,100,-500,60,20\na.wav=a か,0,100,-500,60,20\na.wav=a こ,0,100,-500,60,20\na.wav=- こ,0,100,-500,60,20\na.wav=o ん,0,100,-500,60,20\na.wav=- あ,0,100,-500,60,20\na.wav=n あ,0,100,-500,60,20\na.wav=a -,0,100,-500,60,20\n"));

    private static IEnumerable<string> Aliases(List<UtauSegment> segments) => segments.Select(s => s.Oto.Alias);

    [Fact]
    public void AVcvBank_SingsFromTheVowelBefore_AndFromSilenceAfterARest()
    {
        var score = new SungScore([
            new SungNote(0, 1, 60, "か"),
            new SungNote(1, 1, 62, "か"),
            new SungNote(3, 1, 64, "か"),
            new SungNote(4, 1, 64, "カ", Breath: true),
            new SungNote(5, 1, 64, "か"),
        ]);
        var segments = UtauPhonemizer.Segments(score, Vcv(), "utau/vcv");
        // A phrase that has a rest after it ends on "a -"; one cut by a breath has no time for it.
        Assert.Equal(["- か", "a か", "a -", "- か", "a か", "- か", "a -"], Aliases(segments));
        Assert.Equal((1.0, 2.0), (segments[1].Start, segments[1].End));
        Assert.Equal((2.0, 2.0 + UtauPhonemizer.EndingSeconds), (segments[2].Start, segments[2].End));
    }

    [Fact]
    public void ACvBank_SingsEachKanaAsItIs()
    {
        // あ's line names no alias: it is its file's name.
        var bank = UtauVoicebank.Load(Bank("cv", "_か.wav=か,0,100,-500,60,20\nあ.wav=,0,100,-500,60,20\n"));
        var score = new SungScore([new SungNote(0, 1, 60, "か"), new SungNote(1, 1, 60, "あ"), new SungNote(2, 1, 60, "か")]);
        Assert.Equal(["か", "あ", "か"], Aliases(UtauPhonemizer.Segments(score, bank, "utau/cv")));
    }

    [Fact]
    public void ACvvcBank_BridgesFromTheVowelBefore_IntoTheConsonant()
    {
        var bank = UtauVoicebank.Load(Bank("cvvc", "a.wav=- か,0,100,-500,60,20\na.wav=か,0,100,-500,60,20\na.wav=あ,0,100,-500,60,20\na.wav=し,0,100,-500,60,20\na.wav=a k,0,100,-500,250,80\na.wav=a s,0,100,-500,250,80\n"));
        var score = new SungScore([
            new SungNote(0, 1, 60, "か"),
            new SungNote(1, 1, 62, "か"),
            new SungNote(2, 1, 62, "あ"),   // a vowel: no consonant to bridge into
            new SungNote(3, 1, 62, "し"),   // "a sh" is not there, "a s" is
        ]);
        var segments = UtauPhonemizer.Segments(score, bank, "utau/cvvc");
        Assert.Equal(["- か", "a k", "か", "あ", "a s", "し"], Aliases(segments));
        // The bridge takes the end of the note before, as long as か's preutterance (60 ms).
        Assert.Equal(0.94, segments[0].End, 9);
        Assert.Equal((0.94, 1.0), (Math.Round(segments[1].Start, 9), segments[1].End));
        Assert.Equal(["sh", "s"], UtauPhonemizer.Consonants("し"));
        Assert.Equal(["ky"], UtauPhonemizer.Consonants("きゃ"));
        Assert.Empty(UtauPhonemizer.Consonants("あ"));
    }

    [Fact]
    public void ASyllableOfTwoMorae_GivesTheSecondATail_AndAMelismaHoldsTheSoundOn()
    {
        var score = new SungScore([
            new SungNote(0, 1, 60, "こん"),
            new SungNote(1, 0.5, 62, "あ"),
            new SungNote(1.5, 0.5, 64, null, Melisma: true),
        ]);
        var segments = UtauPhonemizer.Segments(score, Vcv(), "utau/vcv");
        Assert.Equal(["- こ", "o ん", "n あ", "a -"], Aliases(segments));
        Assert.Equal(1 - UtauPhonemizer.TailSeconds, segments[1].Start, 9);
        Assert.Equal(1.0, segments[1].End, 9);
        Assert.Equal(2.0, segments[2].End, 9);   // the melisma holds あ on
    }

    [Fact]
    public void AKanaTheBankHasNot_IsNamed_InTheWordsLilySharpReads()
    {
        var score = new SungScore([new SungNote(0, 1, 60, "か"), new SungNote(1.5, 1, 60, "ぴ")]);
        var e = Assert.Throws<SingerException>(() => UtauPhonemizer.Segments(score, Vcv(), "utau/vcv"));
        Assert.Contains("utau/vcv has no sound for \"ぴ\" (note 1 at 1.5 s)", e.Message);
        Assert.Contains("\"- ぴ\"", e.Message);
    }

    [Fact]
    public void ThePitchCurve_HoldsThroughRests_AndGlidesBetweenNotesThatTouch()
    {
        var score = new SungScore([new SungNote(0, 1, 60, "あ"), new SungNote(1, 1, 64, "か"), new SungNote(3, 1, 67, "あ")]);
        var curve = UtauSinger.PitchCurve(score, 1, 5000);
        float At(double seconds) => curve[(int)Math.Round((seconds + 1) * 1000 / UtauSinger.FrameMs)];
        Assert.Equal(60f, At(-0.5));             // the first note's before it
        Assert.Equal(60f, At(0.9));
        Assert.Equal(62f, At(1.0), 1);           // halfway through the glide
        Assert.Equal(64f, At(1.2));
        Assert.Equal(64f, At(2.5));              // held through the rest
        Assert.Equal(67f, At(3.0));              // no glide out of a rest
    }

    // ---- singing ----

    /// <summary>A buzz at 220 Hz with resonances near 700 and 1200 Hz (an "a"), after <paramref name="noiseMs"/> of noise.</summary>
    private static float[] Sound(double seconds, double noiseMs, int rate = 44100)
    {
        var x = new float[(int)(seconds * rate)];
        var random = new Random(7);
        double phase = 0;
        int noise = (int)(noiseMs / 1000 * rate);
        for (int i = 0; i < x.Length; i++)
        {
            if (i < noise)
            {
                x[i] = (float)((random.NextDouble() * 2 - 1) * 0.2);
                continue;
            }
            // A singer's pitch is never quite still (a buzz held exactly at 220 Hz was analysed into
            // an envelope that sang E4 an octave low): a slow wobble of a couple of percent.
            double t = (double)i / rate, f0 = 220 * (1 + 0.015 * Math.Sin(2 * Math.PI * 5.3 * t) + 0.006 * Math.Sin(2 * Math.PI * 1.7 * t)), v = 0;
            phase += 2 * Math.PI * f0 / rate;
            for (int k = 1; k * f0 < 5000; k++)
            {
                double f = k * f0;
                double gain = 1.0 / k * (1 + 3 * Math.Exp(-Math.Pow((f - 700) / 150, 2)) + 2 * Math.Exp(-Math.Pow((f - 1200) / 200, 2)));
                v += gain * Math.Sin(k * phase);
            }
            x[i] = (float)(v * 0.08);
        }
        return x;
    }

    private string SungBank()
    {
        var folder = Bank("buzz", "_あ.wav=あ,20,100,-700,10,5\n_か.wav=か,20,140,-700,80,20\n");
        File.WriteAllBytes(Path.Combine(folder, "_あ.wav"), Wav.Write(new SungAudio(Sound(1, 0), 44100)));
        File.WriteAllBytes(Path.Combine(folder, "_か.wav"), Wav.Write(new SungAudio(Sound(1, 70), 44100)));
        return folder;
    }

    [Fact]
    public async Task AVoicebank_IsSung_InTune_WhereWritten()
    {
        if (!UtauOptions.Built)
        {
            return;
        }
        using var singer = new UtauSinger(new UtauOptions(SungBank()));
        var score = new SungScore([
            new SungNote(0, 1, 60, "あ"),
            new SungNote(1, 1, 64, "か"),
            new SungNote(2.6, 1.4, 67, "あ", Vibrato: 30),
        ]);
        var audio = await singer.SingAsync(score);
        Assert.Equal("utau/buzz", singer.Name);
        Assert.Equal(44100, audio.SampleRate);
        Assert.True(audio.Duration >= score.End, $"{audio.Duration:0.00} s covers the score's {score.End:0.00} s");
        Assert.True(audio.Duration <= score.End + UtauSinger.TailSeconds + 0.01);
        foreach (var r in PitchCheck.Of(audio, score))
        {
            Assert.True(r.Voiced >= 0.7, $"note {r.Index} is voiced {r.Voiced:0%}");
            Assert.NotNull(r.Sung);
            Assert.InRange(r.Sung!.Value, r.Written - 0.5, r.Written + 0.5);
        }
        double Loudest(double from, double to) => audio.Samples[(int)(from * audio.SampleRate)..(int)(to * audio.SampleRate)].Max(Math.Abs);
        Assert.True(Loudest(2.1, 2.5) < 0.01, "silence in the rest");
        Assert.True(Loudest(0.2, 0.8) > 0.05, "the first note is heard");
        // The consonant sounds before the beat: the noise of か starts some 80 ms before 1 s.
        var again = await singer.SingAsync(score);
        Assert.Equal(audio.Samples, again.Samples);   // no chance in it: the phrase cache can trust it
    }

    [Fact]
    public async Task ARecordingTheOtoNamesNotThere_IsSaidSo()
    {
        if (!UtauOptions.Built)
        {
            return;
        }
        using var singer = new UtauSinger(new UtauOptions(Bank("empty", "_あ.wav=あ,20,100,-700,10,5\n")));
        var e = await Assert.ThrowsAsync<SingerException>(() => singer.SingAsync(new SungScore([new SungNote(0, 1, 60, "あ")])));
        Assert.Contains("_あ.wav that its oto.ini names does not exist", e.Message);
    }

    [Fact]
    public async Task ARealVoicebank_SingsTheExample_InTune()
    {
        var voice = Environment.GetEnvironmentVariable("CANTORIA_UTAU_VOICE");
        if (voice is null || !UtauOptions.Built)
        {
            return;
        }
        using var singer = new UtauSinger(new UtauOptions(voice));
        var score = ScoreJson.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "yukiyakonko.json")));
        var audio = await singer.SingAsync(score);
        var held = PitchCheck.Of(audio, score).Where(r => !score.Notes[r.Index].Melisma && score.Notes[r.Index].Duration >= 0.4).ToList();
        Assert.True(held.Count >= 6, "the example has at least six long syllables");
        foreach (var r in held)
        {
            Assert.True(r.Voiced >= 0.7, $"note {r.Index} is voiced {r.Voiced:0%}");
            Assert.InRange(r.Sung!.Value, r.Written - 0.5, r.Written + 0.5);
        }
    }
}

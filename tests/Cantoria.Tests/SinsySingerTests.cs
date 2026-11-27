// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Cantoria.Sinsy;
using Xunit;

namespace Cantoria.Tests;

/// <summary>
/// The facts that sing run only where a Sinsy voice is: set CANTORIA_SINSY_VOICE to its
/// <c>.htsvoice</c> (the CI fetches nitech_jp_song070_f001). Elsewhere they pass without doing
/// anything, as DiffSingerSingerTests do. The engine itself (cantoria_sinsy beside the tests) is
/// built by tools/build-sinsy.ps1.
/// </summary>
public class SinsySingerTests
{
    private static string? Voice => Environment.GetEnvironmentVariable("CANTORIA_SINSY_VOICE");

    [Fact]
    public async Task Sings_TheExample_InTune_FromTheScoresZero()
    {
        if (Voice is null)
        {
            return;
        }
        using var singer = new SinsySinger(new SinsyOptions(Voice));
        var score = ScoreJson.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "yukiyakonko.json")));
        var audio = await singer.SingAsync(score);
        Assert.Equal("sinsy/" + Path.GetFileNameWithoutExtension(Voice), singer.Name);
        Assert.True(audio.Duration >= score.End, $"{audio.Duration:0.00} s covers the score's {score.End:0.00} s");
        Assert.True(audio.Duration <= score.End + SinsySinger.TailSeconds + 0.01, $"{audio.Duration:0.00} s is not much more than the score's {score.End:0.00} s");
        // The audio's 0 is the score's 0: the lead-in is cut, and the first note is heard at once.
        int firstLoud = Array.FindIndex(audio.Samples, s => Math.Abs(s) > 0.02f);
        Assert.InRange(firstLoud / (double)audio.SampleRate, 0, 0.15);
        var results = PitchCheck.Of(audio, score);
        var held = results.Where(r => !score.Notes[r.Index].Melisma && score.Notes[r.Index].Duration >= 0.4).ToList();
        Assert.True(held.Count >= 6, "the example has at least six long syllables");
        foreach (var r in held)
        {
            // (Sinsy's consonants take more of a note than DiffSinger's: 80 % voiced was seen.)
            Assert.True(r.Voiced >= 0.7, $"note {r.Index} is voiced {r.Voiced:0%}");
            Assert.NotNull(r.Sung);
            Assert.InRange(r.Sung!.Value, r.Written - 0.5, r.Written + 0.5);
        }
        // Sung twice, the same: HMM synthesis has no sampling, and the handle keeps no score.
        var again = await singer.SingAsync(score);
        Assert.Equal(audio.Samples, again.Samples);
    }

    // A note across a 4/4 bar line (Sinsy counts bars from the lead-in) and a long rest are sung where written.
    [Fact]
    public async Task ANoteAcrossABarLine_AndALongRest_AreSungWhereWritten()
    {
        if (Voice is null)
        {
            return;
        }
        using var singer = new SinsySinger(new SinsyOptions(Voice));
        var score = new SungScore([
            new SungNote(2.5, 1.5, 67, "あ"),   // 3.5 to 5 s after the lead-in: across the bar at 4 s
            new SungNote(9.0, 1.0, 64, "ら"),
        ]);
        var audio = await singer.SingAsync(score);
        var results = PitchCheck.Of(audio, score);
        Assert.InRange(results[0].Sung!.Value, 66.5, 67.5);
        Assert.InRange(results[1].Sung!.Value, 63.5, 64.5);
        double Loudest(double from, double to) => audio.Samples[(int)(from * audio.SampleRate)..(int)(to * audio.SampleRate)].Max(Math.Abs);
        Assert.True(Loudest(0, 2.2) < 0.01, "silence before the first note");
        Assert.True(Loudest(4.5, 8.7) < 0.01, "silence in the rest");
    }

    // Under the voice's range every note came out at its floor (C3 and F3 alike at G#3); sung higher
    // and moved back down (SinsySinger.Shift), a bass line from E2 is sung where written.
    [Fact]
    public async Task NotesUnderTheVoicesRange_AreSungWhereWritten()
    {
        if (Voice is null)
        {
            return;
        }
        using var singer = new SinsySinger(new SinsyOptions(Voice));
        var score = new SungScore(Enumerable.Range(0, 8).Select(i => new SungNote(i * 1.0, 0.8, 40 + i * 2, "ら")).ToList());
        foreach (var r in PitchCheck.Of(await singer.SingAsync(score), score))
        {
            Assert.NotNull(r.Sung);
            Assert.InRange(r.Sung!.Value, r.Written - 0.5, r.Written + 0.5);
        }
    }

    [Fact]
    public void TheShift_BringsAScoreInsideTheRange_AndCentresOneWiderThanIt()
    {
        SungScore Of(params int[] pitches) => new(pitches.Select((p, i) => new SungNote(i, 1, p, "ら")).ToList());
        Assert.Equal(0, SinsySinger.Shift(Of(60, 70), 58, 76));
        Assert.Equal(5, SinsySinger.Shift(Of(53, 62), 58, 76));    // the lowest to the floor
        Assert.Equal(-4, SinsySinger.Shift(Of(70, 80), 58, 76));   // the highest to the ceiling
        Assert.Equal(0, SinsySinger.Shift(Of(52, 82), 58, 76));    // 30 semitones in 18: centred
        Assert.Equal(0, SinsySinger.Shift(Of(), 58, 76));
    }

    [Fact]
    public async Task WordsNotInKana_AreRefused_InSinsysName()
    {
        using var singer = new SinsySinger(new SinsyOptions("nowhere.htsvoice"));
        var e = await Assert.ThrowsAsync<SingerException>(() => singer.SingAsync(new SungScore([new SungNote(0, 1, 60, "la")])));
        Assert.StartsWith("Sinsy sings kana", e.Message);
    }

    [Fact]
    public void TheVoices_AreTheHtsvoiceFiles_InTheFolderAndOneDown_ByName()
    {
        var folder = Directory.CreateTempSubdirectory("cantoria-sinsy-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "b_voice_m001.htsvoice"), "");
            Directory.CreateDirectory(Path.Combine(folder, "hts_voice_nitech_jp_song070_f001-0.90"));
            File.WriteAllText(Path.Combine(folder, "hts_voice_nitech_jp_song070_f001-0.90", "nitech_jp_song070_f001.htsvoice"), "");
            File.WriteAllText(Path.Combine(folder, "hts_voice_nitech_jp_song070_f001-0.90", "COPYING"), "");
            Directory.CreateDirectory(Path.Combine(folder, "deeper", "deeper"));
            File.WriteAllText(Path.Combine(folder, "deeper", "deeper", "too_deep.htsvoice"), "");

            Assert.Equal(["b_voice_m001", "nitech_jp_song070_f001"], SinsyOptions.Voices(folder));
            Assert.Equal(Path.Combine(folder, "hts_voice_nitech_jp_song070_f001-0.90", "nitech_jp_song070_f001.htsvoice"), SinsyOptions.Find(folder, "nitech_jp_song070_f001"));
            Assert.Null(SinsyOptions.Find(folder, "too_deep"));

            var entries = VoiceEntries.Sinsy(folder);
            Assert.Equal(["sinsy/b_voice_m001", "sinsy/nitech_jp_song070_f001"], entries.Select(e => e.Id));
            Assert.Equal(["male", "female"], entries.Select(e => e.Gender));
            Assert.All(entries, e => Assert.Equal("ja", e.Language));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Ticks_AreSinsys960AQuarter_AtOneQuarterASecond()
    {
        Assert.Equal(960, SinsySinger.Ticks(1));
        Assert.Equal(480, SinsySinger.Ticks(0.5));
        Assert.Equal(1, SinsySinger.Ticks(0.001));
    }
}

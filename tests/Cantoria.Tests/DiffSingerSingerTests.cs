// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Cantoria.DiffSinger;
using Xunit;

namespace Cantoria.Tests;

/// <summary>
/// Runs only where a DiffSinger voice is installed: set CANTORIA_DIFFSINGER_VOICE to its folder
/// (and CANTORIA_VOCODER_DIR if it bundles no vocoder). Elsewhere the facts pass without doing
/// anything (xunit 2 cannot skip at run time), so CI stays green.
/// </summary>
public class DiffSingerSingerTests
{
    private static string? Voice => Environment.GetEnvironmentVariable("CANTORIA_DIFFSINGER_VOICE");
    private static string? Vocoder => Environment.GetEnvironmentVariable("CANTORIA_VOCODER_DIR");

    [Fact]
    public async Task Sings_TheExample_InTune_FromTheScoresZero()
    {
        if (Voice is null)
        {
            return;
        }
        using var singer = new DiffSingerSinger(new DiffSingerOptions(Voice) { VocoderFolder = Vocoder, Steps = 10 });
        var score = ScoreJson.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "yukiyakonko.json")));
        var audio = await singer.SingAsync(score);
        Assert.StartsWith("diffsinger/", singer.Name);
        // The audio covers the score (6.819 s) and the tail silence, not much more.
        Assert.InRange(audio.Duration, score.End, score.End + 0.5);
        Assert.Contains(audio.Samples, s => Math.Abs(s) > 0.05f);
        // The audio's 0 is the score's 0: the first note starts at 0, so sound is heard at once (its
        // consonant, placed before the beat, is cut), not after a lead.
        int firstLoud = Array.FindIndex(audio.Samples, s => Math.Abs(s) > 0.02f);
        Assert.InRange(firstLoud / (double)audio.SampleRate, 0, 0.15);
        // Every syllable note is voiced and sung within half a semitone of what is written; the
        // melisma and the very short notes before a consonant may be mostly consonant, so they are not held to it.
        var results = PitchCheck.Of(audio, score);
        var held = results.Where(r => !score.Notes[r.Index].Melisma && score.Notes[r.Index].Duration >= 0.4).ToList();
        Assert.True(held.Count >= 6, "the example has at least six long syllables");
        foreach (var r in held)
        {
            Assert.True(r.Voiced > 0.8, $"note {r.Index} is voiced {r.Voiced:0%}");
            Assert.NotNull(r.Sung);
            Assert.InRange(r.Sung!.Value, r.Written - 0.5, r.Written + 0.5);
        }
        // Sung twice, the same voice says the same thing (the sessions are resident; no state leaks).
        var again = await singer.SingAsync(score);
        Assert.Equal(audio.Samples.Length, again.Samples.Length);
    }

    [Fact]
    public async Task TheProvider_IsSettledByTheFirstModel_AndTheCpuIsAlwaysThere()
    {
        if (Voice is null)
        {
            return;
        }
        var score = ScoreJson.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "yukiyakonko.json")));
        // Auto: DirectML where this build and this machine have it, else the CPU — and the log says which.
        using (var auto = new DiffSingerSinger(new DiffSingerOptions(Voice) { VocoderFolder = Vocoder, Steps = 10 }))
        {
            Assert.Equal("DirectML?", auto.Provider);
            await auto.SingAsync(score);
            Assert.Contains(auto.Provider, new[] { "DirectML", "CPU" });
            Assert.Contains("provider: " + auto.Provider, auto.LastLog);
            if (auto.Provider == "CPU")
            {
                Assert.Contains("DirectML", auto.Voice.ProviderNote);
            }
        }
        // Cpu: the CPU, with nothing to explain.
        using (var cpu = new DiffSingerSinger(new DiffSingerOptions(Voice) { VocoderFolder = Vocoder, Steps = 10, Provider = OnnxProvider.Cpu }))
        {
            await cpu.SingAsync(score);
            Assert.Equal("CPU", cpu.Provider);
            Assert.Null(cpu.Voice.ProviderNote);
        }
    }

    [Fact]
    public async Task Sings_OnlyThePhraseThatChanged()
    {
        if (Voice is null)
        {
            return;
        }
        using var singer = new DiffSingerSinger(new DiffSingerOptions(Voice) { VocoderFolder = Vocoder, Steps = 10, Provider = OnnxProvider.Cpu });
        static SungNote[] Phrase(double at, string a, string b) => [new(at, 0.5, 64, a), new(at + 0.5, 0.5, 66, b)];
        var first = new SungScore([.. Phrase(0.5, "か", "き"), .. Phrase(2.5, "さ", "し"), .. Phrase(4.5, "た", "ち")]);
        var whole = await singer.SingAsync(first);
        Assert.Contains("phrases: 3, 3 sung, 0 kept", singer.LastLog);

        // A syllable of the second phrase changed: only that one is sung, and the first sounds as before.
        var edited = new SungScore([.. Phrase(0.5, "か", "き"), .. Phrase(2.5, "な", "し"), .. Phrase(4.5, "た", "ち")]);
        var again = await singer.SingAsync(edited);
        Assert.Contains("phrases: 3, 1 sung, 2 kept", singer.LastLog);
        int rate = whole.SampleRate;
        Assert.Equal(whole.Samples.Take(rate * 2), again.Samples.Take(rate * 2));
        Assert.NotEqual(whole.Samples.Skip(rate * 2).Take(rate * 2), again.Samples.Skip(rate * 2).Take(rate * 2));

        // Everything a bar later, and the same phrases at another place are the phrases already sung.
        var later = new SungScore(first.Notes.Select(n => n with { Start = n.Start + 2 }).ToList());
        var moved = await singer.SingAsync(later);
        Assert.Contains("phrases: 3, 0 sung, 3 kept", singer.LastLog);
        Assert.InRange(moved.Duration - whole.Duration, 1.99, 2.01);
    }

    [Fact]
    public async Task ABreath_AtAPhrasesEnd_IsStillTaken()
    {
        if (Voice is null)
        {
            return;
        }
        using var singer = new DiffSingerSinger(new DiffSingerOptions(Voice) { VocoderFolder = Vocoder, Steps = 10, Provider = OnnxProvider.Cpu });
        if (singer.Voice.AcousticSymbol("AP") is null)
        {
            return;
        }
        static SungScore Score(bool breath) => new([new(0.5, 1, 64, "か", Breath: breath), new(3, 1, 66, "さ")]);
        var held = await singer.SingAsync(Score(false));
        var breathed = await singer.SingAsync(Score(true));
        static double Peak(SungAudio a) => a.Samples.Skip((int)(1.6 * a.SampleRate)).Take((int)(0.3 * a.SampleRate)).Max(s => (double)Math.Abs(s));
        Assert.True(Peak(breathed) > 2 * Peak(held), $"after the note: {Peak(breathed):0.0000} with a breath, {Peak(held):0.0000} without");
    }

    [Fact]
    public async Task AModelThatFails_IsASingerFailure_NotACrash()
    {
        if (Voice is null)
        {
            return;
        }
        using var singer = new DiffSingerSinger(new DiffSingerOptions(Voice) { VocoderFolder = Vocoder, Steps = 10, Provider = OnnxProvider.Cpu });
        await singer.SingAsync(new SungScore([new SungNote(0.5, 0.5, 64, "か")]));     // settles the voice's language and opens the models
        var session = singer.Voice.VocoderSession;
        var inputs = session.InputNames.Select(name => DsMath.Row(name, new float[3]));     // rank 2: the mel wants 3
        var e = Assert.Throws<SingerException>(() => DsMath.Run(session, inputs, "vocoder"));
        Assert.Contains("vocoder", e.Message);
    }

    [Fact]
    public async Task Refuses_ASyllableTheDictionaryHasNot_WhenNothingIsLeft()
    {
        if (Voice is null)
        {
            return;
        }
        using var singer = new DiffSingerSinger(new DiffSingerOptions(Voice) { VocoderFolder = Vocoder });
        var score = new SungScore([new SungNote(0, 0.5, 60, "漢")]);
        var e = await Assert.ThrowsAsync<SingerException>(() => singer.SingAsync(score));
        Assert.Contains("no syllable", e.Message);
    }
}

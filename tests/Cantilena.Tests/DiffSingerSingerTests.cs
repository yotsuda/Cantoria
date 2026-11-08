// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Cantilena.DiffSinger;
using Xunit;

namespace Cantilena.Tests;

/// <summary>
/// Runs only where a DiffSinger voice is installed: set CANTILENA_DIFFSINGER_VOICE to its folder
/// (and CANTILENA_VOCODER_DIR if it bundles no vocoder). Elsewhere the facts pass without doing
/// anything (xunit 2 cannot skip at run time), so CI stays green.
/// </summary>
public class DiffSingerSingerTests
{
    private static string? Voice => Environment.GetEnvironmentVariable("CANTILENA_DIFFSINGER_VOICE");
    private static string? Vocoder => Environment.GetEnvironmentVariable("CANTILENA_VOCODER_DIR");

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

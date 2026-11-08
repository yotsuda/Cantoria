// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Cantilena.Neutrino;
using Xunit;

namespace Cantilena.Tests;

/// <summary>
/// Runs only where NEUTRINO is installed: set CANTILENA_NEUTRINO_DIR to its folder. Elsewhere the
/// facts pass without doing anything (xunit 2 cannot skip at run time), so CI stays green.
/// </summary>
public class NeutrinoSingerTests
{
    private static string? Folder => Environment.GetEnvironmentVariable("CANTILENA_NEUTRINO_DIR");

    [Fact]
    public void Options_NameTheProblems()
    {
        var problems = new NeutrinoOptions(@"C:\no\such\folder").Validate();
        Assert.Single(problems);
        Assert.Contains("not found", problems[0]);
    }

    [Fact]
    public async Task Sings_TheExample_WithTheLeadInTrimmed()
    {
        if (Folder is null)
        {
            return;
        }
        using var singer = new NeutrinoSinger(new NeutrinoOptions(Folder));
        var score = ScoreJson.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "yukiyakonko.json")));
        var audio = await singer.SingAsync(score);
        // The score ends at 6.819 s; the file is padded to the 8 s bar, lead-in trimmed.
        Assert.InRange(audio.Duration, score.End, 8.5);
        Assert.Contains(audio.Samples, s => Math.Abs(s) > 0.05f);
        // Silence before the first note: the lead-in is gone, so sound comes within the first 0.3 s
        // (the consonant may start a little before the beat).
        int firstLoud = Array.FindIndex(audio.Samples, s => Math.Abs(s) > 0.02f);
        Assert.InRange(firstLoud / (double)audio.SampleRate, 0, 0.3);
    }
}

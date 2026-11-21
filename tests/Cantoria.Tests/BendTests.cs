// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Cantoria.DiffSinger;
using Xunit;

namespace Cantoria.Tests;

/// <summary>A pitch bend written on a note (a trial, 2026-11-21): where it is, how it moves, what it leaves alone.</summary>
public class BendTests
{
    private static Bend.Written One(SungNote n) => Assert.Single(Bend.Of([n]));

    [Fact]
    public void AScoop_StartsBelow_MovesSmoothly_AndHoldsOnPitch()
    {
        var b = One(new SungNote(1, 1, 60, "a", Bend: [(0, -200), (0.2, 0)]));
        Assert.Equal(-200, Bend.At(b, 1000));
        Assert.Equal(-100, Bend.At(b, 1100), 6);               // half way in time is half way in pitch
        Assert.InRange(Bend.At(b, 1020), -200, -195);          // eased: no corner where it leaves…
        Assert.InRange(Bend.At(b, 1180), -5, 0);               // …or where it arrives
        Assert.Equal(0, Bend.At(b, 1500));
    }

    [Fact]
    public void Outside_TheNote_ItIsNothing_AndBeforeTheFirstPointItHolds()
    {
        var b = One(new SungNote(1, 1, 60, "a", Bend: [(0.5, 0), (1, -700)]));
        Assert.Equal(0, Bend.At(b, 999));
        Assert.Equal(0, Bend.At(b, 1200));
        Assert.Equal(-700, Bend.At(b, 2000));
        Assert.Equal(0, Bend.At(b, 2001));                     // the next note is the model's
    }

    [Fact]
    public void Points_AreSorted_KeptInTheNote_AndClamped()
    {
        var b = One(new SungNote(0, 1, 60, "a", Bend: [(2, 5000), (-1, 0)]));
        Assert.Equal([(0.0, 0.0), (1000.0, Bend.MaxCents)], b.Points);
        Assert.Empty(Bend.Of([new SungNote(0, 1, 60, "a"), new SungNote(1, 1, 60, "b", Bend: [])]));
    }

    [Fact]
    public void Apply_AddsSemitones_OnTheNotesFramesOnly()
    {
        var f0 = Enumerable.Repeat(60f, 30).ToArray();         // frames of 10 ms from 0
        Bend.Apply(f0, 10, 0, [One(new SungNote(0.1, 0.1, 60, "a", Bend: [(0, -100)]))]);
        Assert.Equal(60f, f0[9]);
        Assert.All(f0[10..21], v => Assert.Equal(59f, v));
        Assert.Equal(60f, f0[21]);
    }

    [Fact]
    public void TheKey_IsEmptyWithNoBend_AndTellsBendsApart()
    {
        SungNote[] plain = [new(0, 1, 60, "a")];
        Assert.Equal("", Bend.KeyOf(plain));
        var a = Bend.KeyOf([plain[0] with { Bend = [(0, -200), (0.1, 0)] }]);
        var b = Bend.KeyOf([plain[0] with { Bend = [(0, -100), (0.1, 0)] }]);
        Assert.StartsWith("|bend1 ", a);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void TheScore_ReadsAndWritesIt_AndValidateNamesABadPoint()
    {
        var score = ScoreJson.Read("""{ "notes": [ { "start": 0, "duration": 1, "pitch": 60, "lyric": "a", "bend": [[0, -200], [0.12, 0]] } ] }""");
        Assert.Equal([(0.0, -200.0), (0.12, 0.0)], score.Notes[0].Bend!);
        string json = ScoreJson.Write(score);
        Assert.Equal(score.Notes[0].Bend!, ScoreJson.Read(json).Notes[0].Bend!);
        Assert.Throws<System.Text.Json.JsonException>(() => ScoreJson.Read("""{ "notes": [ { "start": 0, "duration": 1, "pitch": 60, "lyric": "a", "bend": [[0]] } ] }"""));
        var bad = new SungScore([new SungNote(0, 1, 60, "a", Bend: [(0.5, 0), (0.2, 100), (3, 0)])]);
        var problems = bad.Validate();
        Assert.Contains(problems, p => p.Contains("bend point 1 at 0.2 s comes before point 0"));
        Assert.Contains(problems, p => p.Contains("bend point 2 at 3 s is outside the note"));
    }
}

// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Cantoria.DiffSinger;
using Xunit;

namespace Cantoria.Tests;

/// <summary>A vibrato written on a note, laid on the pitch curve (a trial, DiffSinger voices).</summary>
public class VibratoTests
{
    private const double FrameMs = 10;

    /// <summary>A flat curve at 60 of <paramref name="ms"/> ms, its first frame at 0.</summary>
    private static float[] Flat(double ms) => Enumerable.Repeat(60f, (int)(ms / FrameMs)).ToArray();

    [Fact]
    public void ANote_BeginsStraight_SwingsToItsDepth_AndEndsOnItsPitch()
    {
        var f0 = Flat(2000);
        Vibrato.Apply(f0, FrameMs, 0, [new Vibrato.Written(0, 2000, 40, 5)]);
        // Straight for the first quarter of a second.
        Assert.All(f0.Take(25), v => Assert.Equal(60f, v));
        // In the middle it swings 40 cents each way, and no further.
        var middle = f0.Skip(60).Take(100).ToArray();
        Assert.InRange(middle.Max(), 60.38f, 60.4001f);
        Assert.InRange(middle.Min(), 59.5999f, 59.62f);
        // It lets go before the note ends: the last frame, 10 ms from the end, is within 5 cents.
        Assert.InRange(f0[^1], 59.95f, 60.05f);
        // Five swings a second: the curve crosses the pitch ten times a second (upwards five).
        int up = 0;
        for (int i = 61; i < 160; i++)
        {
            if (f0[i - 1] < 60 && f0[i] >= 60)
            {
                up++;
            }
        }
        Assert.Equal(5, up);
    }

    [Fact]
    public void OnlyTheNoteThatWritesIt_IsMoved_AndAShortNoteIsLeftPlain()
    {
        var f0 = Flat(3000);
        Vibrato.Apply(f0, FrameMs, 0, [new Vibrato.Written(1000, 2000, 40, 5.5)]);
        Assert.All(f0.Take(100), v => Assert.Equal(60f, v));
        Assert.All(f0.Skip(201), v => Assert.Equal(60f, v));
        Assert.Contains(f0.Skip(100).Take(100), v => Math.Abs(v - 60f) > 0.3f);

        // An eighth note at a quick tempo: no room for a swing and a half.
        var quick = Flat(1000);
        Vibrato.Apply(quick, FrameMs, 0, [new Vibrato.Written(0, 250, 40, 5.5)]);
        Assert.All(quick, v => Assert.Equal(60f, v));
    }

    [Fact]
    public void TheCurvesClock_IsThePhrases_WithItsFramesBeforeTheFirstNote()
    {
        // The curve begins 80 ms before the phrase's 0 (the head frames): the note at 0 is frame 8 on.
        var f0 = Flat(2080);
        Vibrato.Apply(f0, FrameMs, -80, [new Vibrato.Written(0, 2000, 40, 5)]);
        Assert.All(f0.Take(8 + 25), v => Assert.Equal(60f, v));
        Assert.Contains(f0.Skip(8 + 60), v => Math.Abs(v - 60f) > 0.3f);
    }

    [Fact]
    public void WhatANoteWrites_IsReadFromTheScore_WithItsDefaults()
    {
        var score = ScoreJson.Read("""
            { "notes": [
              { "start": 0, "duration": 1, "pitch": 60, "lyric": "ら", "vibrato": 40 },
              { "start": 1, "duration": 1, "pitch": 62, "lyric": "ら", "vibrato": 500, "vibratoRate": 6 },
              { "start": 2, "duration": 1, "pitch": 64, "lyric": "ら" } ] }
            """);
        var written = Vibrato.Of(score.Notes);
        Assert.Equal(2, written.Count);
        Assert.Equal(new Vibrato.Written(0, 1000, 40, Vibrato.DefaultRate), written[0]);
        // A swing wider than a whole tone is a whole tone.
        Assert.Equal(new Vibrato.Written(1000, 2000, Vibrato.MaxDepth, 6), written[1]);
        // …and it goes back out as written.
        var again = ScoreJson.Read(ScoreJson.Write(score));
        Assert.Equal(40, again.Notes[0].Vibrato);
        Assert.Equal(6, again.Notes[1].VibratoRate);
        Assert.Null(again.Notes[2].Vibrato);
    }

    [Fact]
    public void APhraseWithAVibrato_IsAnotherPhrase_AndOneWithoutIsTheOneKeptBefore()
    {
        SungNote[] plain = [new(0, 1, 60, "ら"), new(1, 1, 62, "ら")];
        Assert.Equal("", Vibrato.KeyOf(plain));
        string one = Vibrato.KeyOf([plain[0], plain[1] with { Vibrato = 40 }]);
        Assert.NotEqual("", one);
        Assert.NotEqual(one, Vibrato.KeyOf([plain[0] with { Vibrato = 40 }, plain[1]]));
        Assert.NotEqual(one, Vibrato.KeyOf([plain[0], plain[1] with { Vibrato = 60 }]));
        Assert.NotEqual(one, Vibrato.KeyOf([plain[0], plain[1] with { Vibrato = 40, VibratoRate = 6 }]));
    }
}
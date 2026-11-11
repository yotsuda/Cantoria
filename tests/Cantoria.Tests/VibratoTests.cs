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
        // It grows in: a third of a second after it begins it is not yet half its width.
        Assert.All(f0.Skip(25).Take(30), v => Assert.InRange(v, 59.8f, 60.2f));
        // Once full it swings about 40 cents each way - its width wanders an eighth - and no wider.
        var full = f0.Skip(110).Take(70).ToArray();
        Assert.InRange(full.Max(), 60.33f, 60.46f);
        Assert.InRange(full.Min(), 59.54f, 59.67f);
        // It lets go before the note ends: the last frame, 10 ms from the end, is within 5 cents.
        Assert.InRange(f0[^1], 59.95f, 60.05f);
        // About five swings a second, never quite even: in the second where it is full, 4 to 6.
        int up = 0;
        var crossings = new List<int>();
        for (int i = 91; i < 190; i++)
        {
            if (f0[i - 1] < 60 && f0[i] >= 60)
            {
                up++;
                crossings.Add(i);
            }
        }
        Assert.InRange(up, 4, 6);
        // The same note is the same curve every time: nothing in it is chance.
        var again = Flat(2000);
        Vibrato.Apply(again, FrameMs, 0, [new Vibrato.Written(0, 2000, 40, 5)]);
        Assert.Equal(f0, again);
    }

    // Added to a curve that wavers by itself, the written swing came out half as wide again one
    // way: under the vibrato the curve is evened out first, and stays the model's own at its edges.
    [Fact]
    public void Steady_TheCurvesOwnWavering_GivesWayUnderTheVibrato_NotAtTheNotesEdges()
    {
        // A slow drift of 20 cents, twice a second, about 60.
        float[] Wavering() => Enumerable.Range(0, 200).Select(i => 60f + 0.2f * MathF.Sin(2 * MathF.PI * 2 * i * 0.01f)).ToArray();
        var added = Wavering();
        Vibrato.Apply(added, FrameMs, 0, [new Vibrato.Written(0, 2000, 25, 5)]);
        var steady = Wavering();
        Vibrato.Apply(steady, FrameMs, 0, [new Vibrato.Written(0, 2000, 25, 5)], steady: true);
        // Where the vibrato is full: added, the two swings stack; steady, the swing is the one written.
        float Widest(float[] f0) => f0.Skip(110).Take(70).Max(v => Math.Abs(v - 60f));
        Assert.InRange(Widest(added), 0.38f, 0.5f);
        Assert.InRange(Widest(steady), 0.2f, 0.33f);
        // Before the vibrato begins the curve is untouched either way.
        Assert.Equal(Wavering().Take(25), steady.Take(25));
        Assert.Equal(Wavering().Take(25), added.Take(25));
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
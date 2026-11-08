// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Xunit;

namespace Cantilena.Tests;

public class ScoreJsonTests
{
    // The README's example: 雪やこんこ's first bar at 132 to the quarter.
    private const string Yuki = """
        {
          "sampleRate": 44100,
          "language": "ja",
          "notes": [
            { "start": 0.000, "duration": 0.682, "pitch": 60, "lyric": "ゆ" },
            { "start": 0.682, "duration": 0.227, "pitch": 62, "melisma": true },
            { "start": 0.909, "duration": 0.682, "pitch": 60, "lyric": "き" },
            { "start": 1.591, "duration": 0.227, "pitch": 62, "lyric": "や", "breath": true }
          ]
        }
        """;

    [Fact]
    public void Reads_TheReadmeExample()
    {
        var score = ScoreJson.Read(Yuki);
        Assert.Equal(4, score.Notes.Count);
        Assert.Equal("ja", score.Language);
        Assert.Equal(44100, score.SampleRate);
        Assert.Equal("ゆ", score.Notes[0].Lyric);
        Assert.True(score.Notes[1].Melisma);
        Assert.Null(score.Notes[1].Lyric);
        Assert.True(score.Notes[3].Breath);
        Assert.Equal(1.818, score.End, 3);
        Assert.Empty(score.Validate());
    }

    [Fact]
    public void Writes_WhatItReads_LyricsUnescaped()
    {
        var score = ScoreJson.Read(Yuki);
        string json = ScoreJson.Write(score);
        Assert.Contains("\"lyric\": \"ゆ\"", json);        // not ゆ
        Assert.DoesNotContain("\"melisma\": false", json); // defaults left out
        Assert.Equal(score, ScoreJson.Read(json) with { Notes = score.Notes });
        Assert.Equal(score.Notes, ScoreJson.Read(json).Notes);
    }

    [Fact]
    public void Defaults_WhenFieldsAreAbsent()
    {
        var score = ScoreJson.Read("""{ "notes": [ { "start": 0, "duration": 1, "pitch": 60, "lyric": "a" } ] }""");
        Assert.Equal("ja", score.Language);
        Assert.Equal(44100, score.SampleRate);
        Assert.False(score.Notes[0].Melisma);
        Assert.False(score.Notes[0].Breath);
    }

    [Fact]
    public void Validate_NamesEachProblem()
    {
        var score = new SungScore([
            new SungNote(0, 1, 60, Melisma: true),           // first note melisma
            new SungNote(0.5, 0, 60, "a"),                   // overlaps, zero duration
            new SungNote(2, 1, 200),                         // pitch out of range, no lyric
            new SungNote(3, 1, 60, "b", Melisma: true),      // melisma with a lyric
        ]);
        var problems = score.Validate();
        Assert.Contains(problems, p => p.Contains("first note cannot be a melisma"));
        Assert.Contains(problems, p => p.Contains("starts before note 0 ends"));
        Assert.Contains(problems, p => p.Contains("duration 0 is not positive"));
        Assert.Contains(problems, p => p.Contains("pitch 200 is outside"));
        Assert.Contains(problems, p => p.Contains("no lyric"));
        Assert.Contains(problems, p => p.Contains("melisma note carries no lyric"));
    }

    [Fact]
    public void EmptyScore_IsValid_AndEndsAtZero()
    {
        var score = ScoreJson.Read("""{ "notes": [] }""");
        Assert.Empty(score.Validate());
        Assert.Equal(0, score.End);
    }
}

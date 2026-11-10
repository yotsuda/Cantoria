// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Cantoria.DiffSinger;
using Xunit;

namespace Cantoria.Tests;

/// <summary>Where a line is cut into phrases and what makes two phrases the same, without a voice or a model.</summary>
public class PhraseTests
{
    private static List<(double Start, double End)> Touching(int count, double length, double from = 0)
        => Enumerable.Range(0, count).Select(i => (from + i * length, from + (i + 1) * length)).ToList();

    [Fact]
    public void Split_CutsAtARestLongEnough_AndNotAtAShorterOne()
    {
        (double, double)[] words = [(0, 1), (1.125, 2), (2.25, 3), (3, 4)];
        // 0.125 s is too short; 0.25 s is a phrase's end.
        Assert.Equal(new[] { (0, 2), (2, 2) }, PhraseCut.Split(words, 0.25, 60));
    }

    [Fact]
    public void Split_OfNothing_IsNothing_AndOneWordIsOnePhrase()
    {
        Assert.Empty(PhraseCut.Split([], 0.3, 60));
        Assert.Equal(new[] { (0, 1) }, PhraseCut.Split([(5, 6)], 0.3, 60));
    }

    [Fact]
    public void Split_ALongRun_NeverPassesTheLongest_AndLosesNoWord()
    {
        var words = Touching(500, 0.5);     // 250 s with no rest
        var cuts = PhraseCut.Split(words, 0.3, 60);
        int next = 0;
        foreach (var (first, count) in cuts)
        {
            Assert.Equal(next, first);
            Assert.True(words[first + count - 1].End - words[first].Start <= 60);
            next = first + count;
        }
        Assert.Equal(words.Count, next);
    }

    [Fact]
    public void Split_ALongRun_IsCutInItsWidestGap_InTheLaterHalf()
    {
        var words = Touching(200, 0.5);     // 100 s
        // A gap too short to end a phrase, in the later half of the first 60 s, and a wider one in the earlier half.
        words[120 / 2] = (words[120 / 2].Start + 0.2, words[120 / 2].End);
        words[20] = (words[20].Start + 0.25, words[20].End);
        var cuts = PhraseCut.Split(words, 0.3, 60);
        Assert.Equal((0, 60), cuts[0]);
    }

    [Fact]
    public void Split_AWordLongerThanTheLongest_StandsAlone()
    {
        (double, double)[] words = [(0, 100), (100, 101)];
        Assert.Equal(new[] { (0, 1), (1, 1) }, PhraseCut.Split(words, 0.3, 60));
    }

    [Fact]
    public void Split_AnEditMovesNoCutOutsideItsPhrase()
    {
        (double Start, double End)[] before = [(0, 1), (2, 3), (4, 5), (6, 7)];
        (double Start, double End)[] after = [(0, 1), (2, 2.5), (2.5, 3), (4, 5), (6, 7)];     // a note split in two
        var a = PhraseCut.Split(before, 0.3, 60);
        var b = PhraseCut.Split(after, 0.3, 60);
        Assert.Equal(a[0], b[0]);
        Assert.Equal(1, a[2].First - a[1].First);
        Assert.Equal(2, b[2].First - b[1].First);
    }

    private static PhraseKey Key(double start = 0, double duration = 1, double pitch = 60, bool breath = false, string word = "k a|1",
        string? speaker = null, string language = "ja", double? room = null)
        => new(speaker, language, [word], [new PhraseNote(start, duration, pitch, breath)], room);

    [Fact]
    public void Key_IgnoresTheCallersRounding()
    {
        Assert.True(Key().SameAs(Key(start: 0.0004, duration: 1.0004)));
    }

    [Theory]
    [InlineData(0.002, 1, 60, false, "k a|1", null, "ja")]      // start
    [InlineData(0, 1.002, 60, false, "k a|1", null, "ja")]      // duration
    [InlineData(0, 1, 60.5, false, "k a|1", null, "ja")]        // pitch
    [InlineData(0, 1, 60, true, "k a|1", null, "ja")]           // breath
    [InlineData(0, 1, 60, false, "s a|1", null, "ja")]          // words
    [InlineData(0, 1, 60, false, "k a|1", "other", "ja")]       // speaker
    [InlineData(0, 1, 60, false, "k a|1", null, "en")]          // language
    public void Key_DiffersIn_WhatChangesTheSinging(double start, double duration, double pitch, bool breath, string word, string? speaker, string language)
    {
        Assert.False(Key().SameAs(Key(start, duration, pitch, breath, word, speaker, language)));
    }

    [Fact]
    public void Key_DiffersIn_TheBreathAfterIt()
    {
        Assert.False(Key(room: 300).SameAs(Key()));
        Assert.False(Key(room: 300).SameAs(Key(room: 200)));
        Assert.True(Key(room: 300).SameAs(Key(room: 300)));
    }

    [Fact]
    public void Cache_GivesBackWhatWasPut_AndFailsOnAnotherPhrase()
    {
        var cache = new PhraseCache(100);
        var sung = new SungPhrase(new float[10], -100, 1000);
        cache.Add(Key(), sung);
        Assert.True(cache.TryGet(Key(), out var found));
        Assert.Same(sung, found);
        Assert.False(cache.TryGet(Key(pitch: 62), out _));
    }

    [Fact]
    public void Cache_LetsTheLongestUnusedGo()
    {
        var cache = new PhraseCache(20);
        cache.Add(Key(pitch: 60), new SungPhrase(new float[10], 0, 1000));
        cache.Add(Key(pitch: 61), new SungPhrase(new float[10], 0, 1000));
        Assert.True(cache.TryGet(Key(pitch: 60), out _));           // 60 is now the most recent
        cache.Add(Key(pitch: 62), new SungPhrase(new float[10], 0, 1000));
        Assert.Equal(2, cache.Count);
        Assert.False(cache.TryGet(Key(pitch: 61), out _));
        Assert.True(cache.TryGet(Key(pitch: 60), out _));
    }

    [Fact]
    public void Cache_KeepsAPhraseLargerThanItAll_UntilTheNextOne()
    {
        var cache = new PhraseCache(5);
        cache.Add(Key(pitch: 60), new SungPhrase(new float[10], 0, 1000));
        Assert.True(cache.TryGet(Key(pitch: 60), out _));
    }
}

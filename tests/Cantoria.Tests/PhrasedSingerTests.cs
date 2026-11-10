// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Xunit;

namespace Cantoria.Tests;

/// <summary>Singing a phrase at a time through a singer that only knows whole scores.</summary>
public class PhrasedSingerTests
{
    private const int Rate = 1000;

    /// <summary>Audio from the score's 0 to its end, each note's samples at its pitch; every score it was asked to sing.</summary>
    private sealed class Fake : ISinger
    {
        public List<SungScore> Sung { get; } = [];
        public string Name => "fake/voice";

        public Task<SungAudio> SingAsync(SungScore score, CancellationToken ct = default)
        {
            Sung.Add(score);
            var samples = new float[(int)Math.Round(score.End * Rate)];
            foreach (var n in score.Notes)
            {
                Array.Fill(samples, (float)n.Pitch, (int)Math.Round(n.Start * Rate), (int)Math.Round(n.Duration * Rate));
            }
            return Task.FromResult(new SungAudio(samples, Rate));
        }
    }

    private static SungNote[] Phrase(double at, string a, string b, double pitch = 60) => [new(at, 0.5, pitch, a), new(at + 0.5, 0.5, pitch + 1, b)];

    [Fact]
    public async Task EachPhrase_IsSungApart_AndStandsWhereTheScoreHasIt()
    {
        var fake = new Fake();
        using var singer = new PhrasedSinger(fake);
        var audio = await singer.SingAsync(new SungScore([.. Phrase(1, "か", "き"), .. Phrase(3, "さ", "し", 70)]));
        Assert.Equal(2, fake.Sung.Count);
        Assert.Equal(Rate, audio.SampleRate);
        Assert.Equal(0f, audio.Samples[(int)(0.9 * Rate)]);
        Assert.Equal(60f, audio.Samples[(int)(1.2 * Rate)]);
        Assert.Equal(61f, audio.Samples[(int)(1.7 * Rate)]);
        Assert.Equal(0f, audio.Samples[(int)(2.5 * Rate)]);
        Assert.Equal(70f, audio.Samples[(int)(3.2 * Rate)]);
        Assert.Equal(4.0, audio.Duration, 3);
    }

    [Fact]
    public async Task APhrase_IsGivenRoomBeforeIt_ForItsFirstConsonant()
    {
        var fake = new Fake();
        using var singer = new PhrasedSinger(fake);
        await singer.SingAsync(new SungScore(Phrase(7, "か", "き")));
        Assert.Equal(PhrasedSinger.Lead, fake.Sung[0].Notes[0].Start, 6);
        Assert.Equal(PhrasedSinger.Lead + 0.5, fake.Sung[0].Notes[1].Start, 6);
    }

    [Fact]
    public async Task TheFirstConsonant_OfTheScore_BeforeItsZero_IsCut_NotTheScoreMoved()
    {
        var fake = new Fake();
        using var singer = new PhrasedSinger(fake);
        var audio = await singer.SingAsync(new SungScore(Phrase(0, "か", "き")));
        Assert.Equal(60f, audio.Samples[0]);
        Assert.Equal(1.0, audio.Duration, 3);
    }

    [Fact]
    public async Task AnEdit_SingsOnlyItsPhrase()
    {
        var fake = new Fake();
        using var singer = new PhrasedSinger(fake);
        await singer.SingAsync(new SungScore([.. Phrase(0, "か", "き"), .. Phrase(2, "さ", "し"), .. Phrase(4, "た", "ち")]));
        fake.Sung.Clear();
        await singer.SingAsync(new SungScore([.. Phrase(0, "か", "き"), .. Phrase(2, "な", "し"), .. Phrase(4, "た", "ち")]));
        var only = Assert.Single(fake.Sung);
        Assert.Equal("な", only.Notes[0].Lyric);
    }

    [Fact]
    public async Task APhraseMoved_OrRepeated_IsNotSungAgain()
    {
        var fake = new Fake();
        using var singer = new PhrasedSinger(fake);
        var audio = await singer.SingAsync(new SungScore([.. Phrase(0, "か", "き"), .. Phrase(2, "か", "き")]));
        Assert.Single(fake.Sung);
        Assert.Equal(60f, audio.Samples[(int)(2.2 * Rate)]);
        await singer.SingAsync(new SungScore(Phrase(9.2, "か", "き")));
        Assert.Single(fake.Sung);
    }

    [Theory]
    [InlineData(false, false, false, 2)]
    [InlineData(true, false, false, 1)]         // a melisma goes on with the vowel before it
    [InlineData(false, true, false, 1)]         // the word goes on
    [InlineData(false, false, true, 1)]         // a breath into the next note
    public async Task NoPhraseBegins_WhereTheNoteBeforeGoesOn(bool melisma, bool hyphen, bool inhale, int phrases)
    {
        var fake = new Fake();
        using var singer = new PhrasedSinger(fake);
        SungNote second = melisma ? new(2, 0.5, 62, null, Melisma: true) : new(2, 0.5, 62, "き");
        await singer.SingAsync(new SungScore([new(0, 0.5, 60, "か", Hyphen: hyphen, Inhale: inhale), second]));
        Assert.Equal(phrases, fake.Sung.Count);
    }

    [Fact]
    public async Task ALongVowelMark_DoesNotBeginAPhrase()
    {
        var fake = new Fake();
        using var singer = new PhrasedSinger(fake);
        await singer.SingAsync(new SungScore([new(0, 0.5, 60, "か"), new(2, 0.5, 62, "ー")]));
        Assert.Single(fake.Sung);
    }

    [Fact]
    public async Task ABreathOrAnotherLanguage_IsAnotherPhrase()
    {
        var fake = new Fake();
        using var singer = new PhrasedSinger(fake);
        await singer.SingAsync(new SungScore(Phrase(0, "か", "き")));
        await singer.SingAsync(new SungScore(Phrase(0, "か", "き"), "en"));
        await singer.SingAsync(new SungScore([new(0, 0.5, 60, "か"), new(0.5, 0.5, 61, "き", Breath: true)]));
        Assert.Equal(3, fake.Sung.Count);
    }

    [Fact]
    public async Task ALevel_IsNotTheSingersBusiness()
    {
        var fake = new Fake();
        using var singer = new PhrasedSinger(fake);
        await singer.SingAsync(new SungScore(Phrase(0, "か", "き")));
        await singer.SingAsync(new SungScore(Phrase(0, "か", "き").Select(n => n with { Level = 0.5 }).ToList()));
        Assert.Single(fake.Sung);
    }

    [Fact]
    public async Task TheCheck_SeesTheWholeScore_BeforeAnyPhraseIsSung()
    {
        var fake = new Fake();
        SungScore? seen = null;
        using var singer = new PhrasedSinger(fake, s => { seen = s; throw new SingerException("no"); });
        var score = new SungScore([.. Phrase(0, "か", "き"), .. Phrase(2, "さ", "し")]);
        await Assert.ThrowsAsync<SingerException>(() => singer.SingAsync(score));
        Assert.Same(score, seen);
        Assert.Empty(fake.Sung);
    }

    [Fact]
    public async Task AnUnsoundScore_IsRefused()
    {
        var fake = new Fake();
        using var singer = new PhrasedSinger(fake);
        await Assert.ThrowsAsync<SingerException>(() => singer.SingAsync(new SungScore([new(0, -1, 60, "か")])));
        Assert.Empty(fake.Sung);
    }

    [Fact]
    public async Task NothingToSing_IsNoAudio()
    {
        using var singer = new PhrasedSinger(new Fake());
        Assert.Empty((await singer.SingAsync(new SungScore([]))).Samples);
    }
}

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

    // The caller's bar: what there is to sing ANEW is told before the first phrase, and what is
    // done after each - so it begins empty and is full when the score is, and a phrase kept from
    // before is in neither number.
    [Fact]
    public async Task HowFarItIs_IsToldInTheSecondsToSingAnew()
    {
        var fake = new Fake();
        using var singer = new PhrasedSinger(fake);
        var score = new SungScore([.. Phrase(0, "か", "き"), .. Phrase(2, "さ", "し"), .. Phrase(4, "た", "ち")]);
        var told = new List<SingProgress>();
        using (SingReport.Listen(told.Add))
        {
            await singer.SingAsync(score);
        }
        Assert.Equal([new(3, 0), new(3, 1), new(3, 2), new(3, 3)], told);

        // One phrase rewritten: one second to sing, counted from 0.
        told.Clear();
        using (SingReport.Listen(told.Add))
        {
            await singer.SingAsync(new SungScore([.. Phrase(0, "か", "き"), .. Phrase(2, "さ", "す"), .. Phrase(4, "た", "ち")]));
        }
        Assert.Equal([new(1, 0), new(1, 1)], told);

        // Nothing changed: nothing to sing, said once.
        told.Clear();
        using (SingReport.Listen(told.Add))
        {
            await singer.SingAsync(score);
        }
        Assert.Equal([new(0, 0)], told);

        // Nobody listening changes nothing, and a listener is heard only while it listens.
        await singer.SingAsync(new SungScore(Phrase(9, "な", "に")));
        Assert.Single(told);
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

    /// <summary>Asked to stop while singing its first phrase; says whether it was asked to stop mid-phrase.</summary>
    private sealed class StoppedWhileSinging(CancellationTokenSource stop) : ISinger
    {
        public int Sung { get; private set; }
        public bool ToldMidPhrase { get; private set; }
        public string Name => "fake/voice";

        public Task<SungAudio> SingAsync(SungScore score, CancellationToken ct = default)
        {
            Sung++;
            stop.Cancel();
            ToldMidPhrase |= ct.IsCancellationRequested;
            return Task.FromResult(new SungAudio(new float[(int)(score.End * Rate)], Rate));
        }
    }

    [Fact]
    public async Task AskedToStop_ItFinishesThePhraseItIsOn_KeepsIt_AndGoesNoFurther()
    {
        using var stop = new CancellationTokenSource();
        var stopped = new StoppedWhileSinging(stop);
        using var singer = new PhrasedSinger(stopped);
        var score = new SungScore([.. Phrase(0, "か", "き"), .. Phrase(2, "さ", "し")]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => singer.SingAsync(score, stop.Token));
        Assert.Equal(1, stopped.Sung);
        Assert.False(stopped.ToldMidPhrase);

        await singer.SingAsync(score);
        Assert.Equal(2, stopped.Sung);      // the first phrase was kept: only the second is sung
    }

    /// <summary>Sings a phrase only when let (<see cref="Let"/>); stopped meanwhile, it throws as an engine killed would.</summary>
    private sealed class Slow : ISinger
    {
        private readonly TaskCompletionSource _let = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string?> Begun { get; } = [];
        public List<string?> Stopped { get; } = [];
        public string Name => "fake/slow";

        public void Let() => _let.TrySetResult();

        public async Task<SungAudio> SingAsync(SungScore score, CancellationToken ct = default)
        {
            lock (Begun) { Begun.Add(score.Notes[0].Lyric); }
            try
            {
                await _let.Task.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                lock (Stopped) { Stopped.Add(score.Notes[0].Lyric); }
                throw;
            }
            return new SungAudio(new float[(int)(score.End * Rate)], Rate);
        }
    }

    private static async Task Until(Func<bool> done)
    {
        for (int i = 0; i < 500 && !done(); i++)
        {
            await Task.Delay(10);
        }
        Assert.True(done());
    }

    // Owner's report 2026-11-18: NEUTRINO on a CPU took minutes over a phrase, and every edit
    // meanwhile waited for it. Let go of mid-phrase, the score ends at once; the phrase is the
    // next score's to take, if it sings it.
    [Fact]
    public async Task LetGoMidPhrase_TheNextScoreThatSingsIt_TakesIt()
    {
        var slow = new Slow();
        using var singer = new PhrasedSinger(slow);
        using var stop = new CancellationTokenSource();
        var first = singer.SingAsync(new SungScore([.. Phrase(0, "か", "き"), .. Phrase(2, "さ", "し")]), stop.Token);
        await Until(() => slow.Begun.Count == 1);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

        // The same first phrase, another second one: the first is finished, not sung again.
        var second = singer.SingAsync(new SungScore([.. Phrase(0, "か", "き"), .. Phrase(2, "た", "ち")]));
        slow.Let();
        await second;
        Assert.Equal(["か", "た"], slow.Begun);
        Assert.Empty(slow.Stopped);
    }

    [Fact]
    public async Task LetGoMidPhrase_TheNextScoreThatDoesNotSingIt_StopsIt()
    {
        var slow = new Slow();
        using var singer = new PhrasedSinger(slow);
        using var stop = new CancellationTokenSource();
        var first = singer.SingAsync(new SungScore(Phrase(0, "か", "き")), stop.Token);
        await Until(() => slow.Begun.Count == 1);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

        var second = singer.SingAsync(new SungScore(Phrase(0, "な", "に")));
        await Until(() => slow.Begun.Count == 2);
        Assert.Equal(["か"], slow.Stopped);
        slow.Let();
        await second;

        // Stopped, it was not kept: a score that sings it again sings it.
        await singer.SingAsync(new SungScore(Phrase(0, "か", "き")));
        Assert.Equal(["か", "な", "か"], slow.Begun);
    }

    // The engine sings one score at a time: a score in another voice stops the phrase too, rather
    // than be sung beside it.
    [Fact]
    public async Task LetGoMidPhrase_AScoreInAnotherVoice_StopsIt()
    {
        var left = new LeftPhrase();
        var slow = new Slow();
        using var one = new PhrasedSinger(slow, left: left);
        using var other = new PhrasedSinger(new Fake(), left: left);
        using var stop = new CancellationTokenSource();
        var first = one.SingAsync(new SungScore(Phrase(0, "か", "き")), stop.Token);
        await Until(() => slow.Begun.Count == 1);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

        await other.SingAsync(new SungScore(Phrase(0, "か", "き")));
        Assert.Equal(["か"], slow.Stopped);
    }

    // Nobody asking after it, the phrase is finished and kept.
    [Fact]
    public async Task LetGoMidPhrase_AndLeftAlone_IsFinishedAndKept()
    {
        var slow = new Slow();
        using var singer = new PhrasedSinger(slow);
        using var stop = new CancellationTokenSource();
        var first = singer.SingAsync(new SungScore(Phrase(0, "か", "き")), stop.Token);
        await Until(() => slow.Begun.Count == 1);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        slow.Let();
        await Task.Delay(50);

        await singer.SingAsync(new SungScore([.. Phrase(0, "か", "き"), .. Phrase(2, "さ", "し")]));
        Assert.Equal(["か", "さ"], slow.Begun);
        Assert.Empty(slow.Stopped);
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

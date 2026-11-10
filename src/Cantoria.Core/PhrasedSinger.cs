// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Cantoria.DiffSinger;

namespace Cantoria;

/// <summary>
/// Sings a score a phrase at a time through a singer that sings whole scores, keeping each phrase,
/// so that an edit sings only the phrase it changed. (DiffSinger does this inside, after its
/// phonemes: <see cref="DiffSingerSinger"/>.)
/// </summary>
/// <param name="inner">The singer of each phrase.</param>
/// <param name="check">Throws <see cref="SingerException"/> for what is wrong with the whole score,
/// before any phrase is sung, so that the message speaks of the score and not of one phrase.</param>
/// <param name="disk">Where phrases are kept beyond this engine's life; null keeps them in memory only.</param>
public sealed class PhrasedSinger(ISinger inner, Action<SungScore>? check = null, PhraseDisk? disk = null) : ISinger, IDisposable
{
    private const double RestBetweenPhrases = 0.3;
    private const double LongestPhrase = 60;

    /// <summary>Seconds of rest before a phrase's first note: engines cut what sounds before the
    /// score's 0, and the first consonant does.</summary>
    public const double Lead = 0.5;

    /// <summary>4 bytes a sample, 256 MiB a voice.</summary>
    private const int PhrasesKept = 64 * 1024 * 1024;

    private readonly PhraseCache _phrases = new(PhrasesKept, disk);
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    public string Name => inner.Name;

    public async Task<SungAudio> SingAsync(SungScore score, CancellationToken ct = default)
    {
        var problems = score.Validate();
        if (problems.Count > 0)
        {
            throw new SingerException("the score is not sound: " + string.Join("; ", problems));
        }
        check?.Invoke(score);

        var notes = score.Notes;
        var words = new List<(int First, int Last)>();
        for (int i = 0; i < notes.Count; i++)
        {
            if (i > 0 && CarriesOn(notes[i - 1], notes[i]))
            {
                words[^1] = (words[^1].First, i);
            }
            else
            {
                words.Add((i, i));
            }
        }
        var cuts = PhraseCut.Split(words.Select(w => (notes[w.First].Start, notes[w.Last].End)).ToList(), RestBetweenPhrases, LongestPhrase);

        await _oneAtATime.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var placed = new List<(double Seconds, SungPhrase Phrase)>();
            foreach (var (first, count) in cuts)
            {
                ct.ThrowIfCancellationRequested();
                int from = words[first].First, to = words[first + count - 1].Last;
                double start = notes[from].Start;
                var phrase = Enumerable.Range(from, to - from + 1).Select(i => notes[i] with { Start = notes[i].Start - start + Lead }).ToList();
                var key = new PhraseKey(null, $"{score.Language}@{score.SampleRate}",
                    phrase.Select(n => $"{n.Lyric}|{n.Melisma}|{n.Inhale}|{n.Hyphen}").ToList(),
                    phrase.Select(n => new PhraseNote(n.Start, n.Duration, n.Pitch, n.Breath)).ToList(),
                    null);
                if (!_phrases.TryGet(key, out var sung))
                {
                    // A phrase begun is finished and kept: asked to stop, the score stops at the next phrase.
                    var audio = await inner.SingAsync(score with { Notes = phrase }, CancellationToken.None).ConfigureAwait(false);
                    sung = new SungPhrase(audio.Samples, -Lead * 1000, audio.SampleRate);
                    _phrases.Add(key, sung);
                }
                placed.Add((start, sung));
            }
            int rate = placed.Count > 0 ? placed[0].Phrase.SampleRate : score.SampleRate;
            return new SungAudio(SungPhrase.Lay(placed, rate), rate);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    /// <summary>True when <paramref name="next"/> cannot begin a phrase: it goes on with the vowel or
    /// the word of the note before, or the note before takes a breath into it.</summary>
    private static bool CarriesOn(SungNote before, SungNote next)
        => next.Melisma || next.Lyric?.StartsWith('ー') == true || before.Hyphen || before.Inhale;

    public void Dispose()
    {
        (inner as IDisposable)?.Dispose();
        _oneAtATime.Dispose();
    }
}

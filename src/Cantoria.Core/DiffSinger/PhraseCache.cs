// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Cantoria.DiffSinger;

/// <summary>A phrase's audio; its sample 0 stands <paramref name="StartMs"/> from the phrase's first note.</summary>
public sealed record SungPhrase(float[] Samples, double StartMs, int SampleRate)
{
    /// <summary>The phrases laid together, each with its first note at the seconds given; sample 0
    /// is the score's 0, and what sounds before it (the first consonant) is cut.</summary>
    public static float[] Lay(IReadOnlyList<(double Seconds, SungPhrase Phrase)> placed, int sampleRate)
    {
        var at = placed.Select(p => (int)Math.Round((p.Seconds * 1000 + p.Phrase.StartMs) / 1000 * sampleRate)).ToList();
        var all = new float[placed.Count == 0 ? 0 : Math.Max(0, placed.Select((p, k) => at[k] + p.Phrase.Samples.Length).Max())];
        for (int k = 0; k < placed.Count; k++)
        {
            var samples = placed[k].Phrase.Samples;
            for (int i = Math.Max(0, -at[k]); i < samples.Length; i++)
            {
                all[at[k] + i] += samples[i];
            }
        }
        return all;
    }
}

/// <summary>A note as the models hear it, its start counted from the phrase's first note.</summary>
public sealed record PhraseNote(double Start, double Duration, double Pitch, bool Breath);

/// <summary>
/// All that decides how a phrase sounds. Where it stands in the score is not part of it, nor is a
/// note's level, which is applied after the singing.
/// </summary>
/// <param name="Speaker">The voice's speaker; null for its first.</param>
/// <param name="Language">The language the voice sings the words in.</param>
/// <param name="Words">Each word's phonemes and how many notes it has.</param>
/// <param name="Notes">The phrase's notes.</param>
/// <param name="EndBreathRoom">The time a breath after the last note has, in ms; null for no breath.</param>
public sealed record PhraseKey(string? Speaker, string Language, IReadOnlyList<string> Words, IReadOnlyList<PhraseNote> Notes, double? EndBreathRoom)
{
    /// <summary>Times the caller rounded differently at another place in the score are the same time.</summary>
    private const double SameSeconds = 0.001;

    public bool SameAs(PhraseKey other)
    {
        if (Speaker != other.Speaker || Language != other.Language || !Words.SequenceEqual(other.Words) || Notes.Count != other.Notes.Count
            || EndBreathRoom.HasValue != other.EndBreathRoom.HasValue
            || Math.Abs((EndBreathRoom ?? 0) - (other.EndBreathRoom ?? 0)) >= SameSeconds * 1000)
        {
            return false;
        }
        for (int i = 0; i < Notes.Count; i++)
        {
            var (a, b) = (Notes[i], other.Notes[i]);
            if (Math.Abs(a.Start - b.Start) >= SameSeconds || Math.Abs(a.Duration - b.Duration) >= SameSeconds
                || Math.Abs(a.Pitch - b.Pitch) >= 1e-6 || a.Breath != b.Breath)
            {
                return false;
            }
        }
        return true;
    }
}

/// <summary>
/// The phrases already sung, so that an edit sings only the phrase it changed, and a phrase the piece
/// repeats is sung once. The ones used longest ago go first when <c>maxSamples</c> is passed. Not
/// thread-safe: the singer holds it under its one-at-a-time lock.
/// </summary>
public sealed class PhraseCache(long maxSamples)
{
    private readonly LinkedList<(PhraseKey Key, SungPhrase Phrase)> _recentFirst = new();
    private long _samples;

    public int Count => _recentFirst.Count;

    public bool TryGet(PhraseKey key, out SungPhrase phrase)
    {
        for (var node = _recentFirst.First; node is not null; node = node.Next)
        {
            if (node.Value.Key.SameAs(key))
            {
                _recentFirst.Remove(node);
                _recentFirst.AddFirst(node);
                phrase = node.Value.Phrase;
                return true;
            }
        }
        phrase = null!;
        return false;
    }

    public void Add(PhraseKey key, SungPhrase phrase)
    {
        _recentFirst.AddFirst((key, phrase));
        _samples += phrase.Samples.Length;
        while (_samples > maxSamples && _recentFirst.Count > 1)
        {
            _samples -= _recentFirst.Last!.Value.Phrase.Samples.Length;
            _recentFirst.RemoveLast();
        }
    }
}

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
/// <param name="left">Where a phrase let go of mid-song waits for the next score's word (<see cref="LeftPhrase"/>):
/// one shared by the singers of an engine process; null, this singer's own.</param>
public sealed class PhrasedSinger(ISinger inner, Action<SungScore>? check = null, PhraseDisk? disk = null, LeftPhrase? left = null) : ISinger, ISingParameters, IDisposable
{
    private readonly LeftPhrase _left = left ?? new LeftPhrase();

    /// <summary>The settings the engine behind takes: none, unless it says some.</summary>
    public IReadOnlyList<SingParameter> Parameters => (inner as ISingParameters)?.Parameters ?? [];

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
        // What the score sets and this voice does not take is said, and the score is sung without
        // it; what it does take goes to the engine with every phrase, and into every phrase's key.
        // A setting written on a note is said too: this engine has one value a phrase at most.
        var whole = SingSettings.Taken(score.Sing, Parameters, Name);
        // …and what the music itself writes, for a setting that is one value a phrase: each
        // phrase is sung with the value at its first note (SingCurves.PhraseValues).
        score = SingCurves.Resolve(score, Parameters, Name);

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
            var phrases = new List<(double Start, double Seconds, List<SungNote> Notes, PhraseKey Key, Dictionary<string, SingValue> Sing)>();
            foreach (var (first, count) in cuts)
            {
                int from = words[first].First, to = words[first + count - 1].Last;
                double start = notes[from].Start;
                var phrase = Enumerable.Range(from, to - from + 1).Select(i => notes[i] with { Start = notes[i].Start - start + Lead }).ToList();
                var sing = new Dictionary<string, SingValue>(whole, StringComparer.Ordinal);
                foreach (var (name, value) in SingCurves.PhraseValues(phrase, Parameters, Name))
                {
                    sing[name] = SingValue.Of(value);
                }
                var key = new PhraseKey(null, $"{score.Language}@{score.SampleRate}{SingSettings.KeyOf(sing)}",
                    phrase.Select(n => $"{n.Lyric}|{n.Melisma}|{n.Inhale}|{n.Hyphen}").ToList(),
                    phrase.Select(n => new PhraseNote(n.Start, n.Duration, n.Pitch, n.Breath)).ToList(),
                    null);
                phrases.Add((start, notes[to].End - start, phrase, key, sing));
            }

            // A phrase an earlier score was let go of in the middle of: finished and kept if this
            // score sings it, stopped if not (LeftPhrase).
            await _left.Settle(this, held => phrases.Any(p => p.Key.SameAs(held))).ConfigureAwait(false);

            // Every phrase is looked up first, so that what there is to sing ANEW is known before
            // the first is sung (SingReport): the phrases kept from before are held here meanwhile.
            // A phrase the score has twice is sung once: its second place waits for the first (Again).
            var asked = new List<(double Start, double Seconds, List<SungNote> Notes, PhraseKey Key, SungPhrase? Kept, bool Again, Dictionary<string, SingValue> Sing)>();
            var fresh = new List<PhraseKey>();
            foreach (var (start, seconds, phrase, key, sing) in phrases)
            {
                var kept = Kept(key);
                bool again = kept is null && fresh.Any(k => k.SameAs(key));
                if (kept is null && !again)
                {
                    fresh.Add(key);
                }
                asked.Add((start, seconds, phrase, key, kept, again, sing));
            }
            double toSing = asked.Where(a => a.Kept is null && !a.Again).Sum(a => a.Seconds), done = 0;
            SingReport.Tell(toSing, 0);

            var placed = new List<(double Seconds, SungPhrase Phrase)>();
            foreach (var (start, seconds, phrase, key, kept, again, sing) in asked)
            {
                ct.ThrowIfCancellationRequested();
                var sung = kept ?? (again ? Kept(key) : null);
                if (sung is null)
                {
                    // Asked to stop mid-phrase, the score stops now and the phrase goes on being
                    // sung, for the next score to take or stop (LeftPhrase): an edit elsewhere
                    // still finds it kept, and an engine minutes over a phrase is not kept on it
                    // for a text nobody asks for (owner's decision 2026-11-18; until then a phrase
                    // begun was always finished).
                    var stop = new CancellationTokenSource();
                    var singing = SingPhrase(key, score with { Notes = phrase, Sing = sing.Count > 0 ? sing : null }, stop.Token);
                    bool held = false;
                    try
                    {
                        sung = await singing.WaitAsync(ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        _left.Hold(this, key, singing, stop);
                        held = true;
                        throw;
                    }
                    finally
                    {
                        if (!held)
                        {
                            stop.Dispose();
                        }
                    }
                    done = Math.Min(toSing, done + seconds);
                    SingReport.Tell(toSing, done);
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

    /// <summary>The phrase kept for <paramref name="key"/>, or null. (A phrase let go of is added
    /// from its own thread: the cache is taken a caller at a time.)</summary>
    private SungPhrase? Kept(PhraseKey key)
    {
        lock (_phrases)
        {
            return _phrases.TryGet(key, out var found) ? found : null;
        }
    }

    /// <summary>Sings one phrase and keeps it; <paramref name="stop"/> stops it unkept.</summary>
    private async Task<SungPhrase> SingPhrase(PhraseKey key, SungScore phrase, CancellationToken stop)
    {
        var audio = await inner.SingAsync(phrase, stop).ConfigureAwait(false);
        var sung = new SungPhrase(audio.Samples, -Lead * 1000, audio.SampleRate);
        lock (_phrases)
        {
            _phrases.Add(key, sung);
        }
        return sung;
    }

    /// <summary>True when <paramref name="next"/> cannot begin a phrase: it goes on with the vowel or
    /// the word of the note before, or the note before takes a breath into it.</summary>
    private static bool CarriesOn(SungNote before, SungNote next)
        => next.Melisma || next.Lyric?.StartsWith('ー') == true || before.Hyphen || before.Inhale;

    public void Dispose()
    {
        // A phrase of this singer's still being sung is stopped before its engine goes.
        _left.Settle(this, _ => false, onlyMine: true).GetAwaiter().GetResult();
        (inner as IDisposable)?.Dispose();
        _oneAtATime.Dispose();
    }
}

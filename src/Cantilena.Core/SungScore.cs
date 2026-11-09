// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Cantilena;

/// <summary>
/// One sung note: where it sounds, how long, at what pitch, and the syllable it carries.
/// Times are seconds from the start of the score; the caller has already resolved tempo,
/// meter and repeats. A gap between notes is a rest.
/// </summary>
/// <param name="Start">Seconds from the score's start.</param>
/// <param name="Duration">Seconds the note sounds; positive.</param>
/// <param name="Pitch">MIDI note number (60 = C4); fractional values are allowed.</param>
/// <param name="Lyric">The syllable as pronounced, in the language's own script; null on a melisma note.</param>
/// <param name="Melisma">True when the note continues the previous syllable's vowel and carries no lyric.</param>
/// <param name="Breath">True when the singer breathes after this note (a phrase ends).</param>
/// <param name="Level">The note's loudness as a factor on the sung audio, 1 for as sung; null is 1.
/// The engines do not sing louder or softer for a marking, so it is applied to what they sang.</param>
/// <param name="LevelEnd">The factor at the note's end, for a swell across it; null is <see cref="Level"/>.</param>
public sealed record SungNote(
    double Start,
    double Duration,
    double Pitch,
    string? Lyric = null,
    bool Melisma = false,
    bool Breath = false,
    double? Level = null,
    double? LevelEnd = null)
{
    /// <summary>Seconds at which the note ends.</summary>
    public double End => Start + Duration;
}

/// <summary>
/// What a singer is asked to sing: the notes in time order, the language the lyrics are
/// written in, and the sample rate wanted back.
/// </summary>
/// <param name="Notes">Notes in ascending <see cref="SungNote.Start"/>, non-overlapping.</param>
/// <param name="Language">A BCP-47 language tag, e.g. "ja".</param>
/// <param name="SampleRate">Samples per second of the audio wanted, e.g. 44100.</param>
public sealed record SungScore(
    IReadOnlyList<SungNote> Notes,
    string Language = "ja",
    int SampleRate = 44100)
{
    /// <summary>Seconds at which the last note ends; 0 for an empty score.</summary>
    public double End => Notes.Count == 0 ? 0 : Notes[^1].End;

    /// <summary>An overlap shorter than this is a caller's rounding, not music (<see cref="Tidy"/>).</summary>
    public const double RoundingOverlap = 0.001;

    /// <summary>
    /// The score with a caller's rounding taken out: where a note starts less than
    /// <see cref="RoundingOverlap"/> before the note before it ends, that earlier note is cut to
    /// end where this one starts — the notes touched in the caller's music, and its start and
    /// duration were rounded apart (sixteenths at 132 to the quarter are repeating decimals; Lily#
    /// session 1144, owner's report "note 23 at 6.591 s starts before note 22 ends (6.591 s)"). A
    /// longer overlap is left for <see cref="Validate"/> to report. The same score when nothing
    /// needed cutting.
    /// </summary>
    public SungScore Tidy()
    {
        List<SungNote>? notes = null;
        for (int i = 1; i < Notes.Count; i++)
        {
            var prev = (notes ?? Notes)[i - 1];
            double overlap = prev.End - Notes[i].Start;
            if (overlap <= 0 || overlap >= RoundingOverlap)
            {
                continue;
            }
            notes ??= Notes.ToList();
            notes[i - 1] = prev with { Duration = Math.Max(RoundingOverlap, Notes[i].Start - prev.Start) };
        }
        return notes is null ? this : this with { Notes = notes };
    }

    /// <summary>
    /// The score's problems, in order, as messages a caller can show; empty when it is sound.
    /// Checked: notes in time order and not overlapping, durations positive, pitches in MIDI
    /// range, a lyric on every note that is not a melisma, and no melisma on the first note.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (SampleRate < 8000)
        {
            problems.Add($"sampleRate {SampleRate} is below 8000");
        }
        for (int i = 0; i < Notes.Count; i++)
        {
            var n = Notes[i];
            string at = $"note {i} at {n.Start:0.###} s";
            if (n.Duration <= 0)
            {
                problems.Add($"{at}: duration {n.Duration:0.###} is not positive");
            }
            if (n.Pitch < 0 || n.Pitch > 127)
            {
                problems.Add($"{at}: pitch {n.Pitch} is outside 0..127");
            }
            if (i > 0 && n.Start < Notes[i - 1].End - 1e-9)
            {
                problems.Add($"{at}: starts before note {i - 1} ends ({Notes[i - 1].End:0.###} s)");
            }
            if (n.Melisma)
            {
                if (i == 0)
                {
                    problems.Add($"{at}: the first note cannot be a melisma");
                }
                if (!string.IsNullOrEmpty(n.Lyric))
                {
                    problems.Add($"{at}: a melisma note carries no lyric (has \"{n.Lyric}\")");
                }
            }
            else if (string.IsNullOrWhiteSpace(n.Lyric))
            {
                problems.Add($"{at}: no lyric (write one, or mark the note melisma)");
            }
        }
        return problems;
    }
}

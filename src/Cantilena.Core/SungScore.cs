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
public sealed record SungNote(
    double Start,
    double Duration,
    double Pitch,
    string? Lyric = null,
    bool Melisma = false,
    bool Breath = false)
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

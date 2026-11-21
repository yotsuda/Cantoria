// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Cantoria.DiffSinger;

/// <summary>
/// A pitch bend WRITTEN on a note (<see cref="SungNote.Bend"/>), laid on the pitch curve the voice
/// is sung from: a scoop up into the note, a fall off its end (LilyPond's <c>\bendAfter</c>), a
/// slide. A trial (2026-11-21), to be heard before a caller writes it from a score.
/// <para>
/// The points are (seconds from the note's start, cents off the curve). Between two points the
/// bend moves on a smooth curve, not a straight line (a voice bends with no corner); before the
/// first and after the last it holds; outside the note it is nothing, so the next note is entered
/// as the model drew it. The model's own approach, wavering and vibrato stay under it.
/// </para>
/// </summary>
public static class Bend
{
    /// <summary>The farthest a bend goes either way, in cents: two octaves.</summary>
    public const double MaxCents = 2400;

    /// <summary>One note's bend on the phrase's clock: where the note sounds and its points, in ms
    /// on that clock and cents, in time order.</summary>
    public readonly record struct Written(double StartMs, double EndMs, IReadOnlyList<(double Ms, double Cents)> Points);

    /// <summary>The bends the notes write, on the clock their starts are counted on (seconds in, ms out).</summary>
    public static List<Written> Of(IEnumerable<SungNote> notes)
        => notes.Where(n => n.Bend is { Count: > 0 })
            .Select(n => new Written(n.Start * 1000, n.End * 1000,
                [.. n.Bend!.Where(p => double.IsFinite(p.At) && double.IsFinite(p.Cents))
                    .OrderBy(p => p.At)
                    .Select(p => ((n.Start + Math.Clamp(p.At, 0, n.Duration)) * 1000, Math.Clamp(p.Cents, -MaxCents, MaxCents)))]))
            .Where(w => w.Points.Count > 0)
            .ToList();

    /// <summary>The bend at <paramref name="ms"/> on the phrase's clock, in cents (0 outside the note).</summary>
    public static double At(Written bend, double ms)
    {
        if (ms < bend.StartMs || ms > bend.EndMs)
        {
            return 0;
        }
        var p = bend.Points;
        if (ms <= p[0].Ms)
        {
            return p[0].Cents;
        }
        for (int k = 1; k < p.Count; k++)
        {
            if (ms <= p[k].Ms)
            {
                double span = p[k].Ms - p[k - 1].Ms;
                double x = span <= 0 ? 1 : (ms - p[k - 1].Ms) / span;
                double eased = 0.5 - 0.5 * Math.Cos(Math.PI * x);
                return p[k - 1].Cents + (p[k].Cents - p[k - 1].Cents) * eased;
            }
        }
        return p[^1].Cents;
    }

    /// <summary>
    /// Adds each bend to <paramref name="f0Midi"/> (semitones, a value a frame; frame i is at
    /// <paramref name="startMs"/> + i × <paramref name="frameMs"/>).
    /// </summary>
    public static void Apply(float[] f0Midi, double frameMs, double startMs, IReadOnlyList<Written> bends)
    {
        foreach (var b in bends)
        {
            int first = Math.Max(0, (int)Math.Ceiling((b.StartMs - startMs) / frameMs));
            int last = Math.Min(f0Midi.Length - 1, (int)Math.Floor((b.EndMs - startMs) / frameMs));
            for (int i = first; i <= last; i++)
            {
                f0Midi[i] += (float)(At(b, startMs + i * frameMs) / 100);
            }
        }
    }

    /// <summary>What tells a phrase with these bends from the same notes without (the phrase
    /// cache's key); empty when none is written, so the phrases kept before are still found.</summary>
    public static string KeyOf(IEnumerable<SungNote> notes)
    {
        var written = notes.Select(n => n.Bend is { Count: > 0 } b ? string.Join(';', b.Select(p => $"{p.At:0.###}:{p.Cents:0.#}")) : "").ToList();
        return written.All(w => w.Length == 0) ? "" : "|bend1 " + string.Join(',', written);
    }
}

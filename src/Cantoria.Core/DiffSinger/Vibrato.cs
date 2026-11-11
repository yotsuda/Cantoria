// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Cantoria.DiffSinger;

/// <summary>
/// A vibrato WRITTEN on a note (<see cref="SungNote.Vibrato"/>), laid on the pitch curve the voice
/// is sung from. A trial (2026-11-12), to be heard and judged before the grammar and the other
/// engines are given it. The first shape was an even sine ADDED to what the pitch model drew, and
/// the owner heard it as put on; this one wavers as a throat does, and stands in place of the
/// model's own wavering under it (<c>steady</c>) instead of on top of it.
/// <para>
/// The shape is a singer's: the note begins straight, the vibrato comes in after a moment and
/// grows to its depth, and lets go just before the note ends, so that the next note is entered
/// on pitch. A note too short for a swing and a half is left plain.
/// </para>
/// </summary>
public static class Vibrato
{
    /// <summary>How far the pitch swings each way when the note says only "vibrato", in cents.
    /// (40 at first: the owner heard it as put on, 2026-11-12.)</summary>
    public const double DefaultDepth = 25;

    /// <summary>Swings a second when the note names none.</summary>
    public const double DefaultRate = 5.5;

    /// <summary>The widest swing sung, each way, in cents: a whole tone.</summary>
    public const double MaxDepth = 200;

    private const double DelayMs = 250;         // straight tone before it begins…
    private const double DelayShare = 0.3;      // …at most this share of the note
    private const double GrowMs = 350;          // from nothing to its depth, at least…
    private const double GrowShare = 0.45;      // …and this share of what sounds, where that is longer
    private const double LetGoMs = 120;         // back to the note's pitch before its end
    // What makes it a throat and not an oscillator (owner's ear, 2026-11-12: an even sine at one
    // depth sounded put on): it begins a little slower than it settles, and neither its pace nor
    // its width holds still. The drift is two slow waves of unrelated pace, not chance: the same
    // note is the same sound every time it is sung, as the phrase cache needs.
    private const double SlowStart = 0.12;      // the first swings are this much slower
    private const double PaceDrift = 0.04;      // the pace wanders this much either way
    private const double WidthDrift = 0.12;     // the width too

    /// <summary>One note's vibrato on the phrase's clock: where the note sounds, its depth each way
    /// in cents and its swings a second.</summary>
    public readonly record struct Written(double StartMs, double EndMs, double Cents, double Hz);

    /// <summary>The vibratos the notes write, on the clock their starts are counted on (seconds in, ms out).</summary>
    public static List<Written> Of(IEnumerable<SungNote> notes)
        => notes.Where(n => n.Vibrato is > 0)
            .Select(n => new Written(n.Start * 1000, n.End * 1000, Math.Min(MaxDepth, n.Vibrato!.Value),
                n.VibratoRate is > 0 and var hz ? Math.Clamp(hz, 1, 12) : DefaultRate))
            .ToList();

    /// <summary>
    /// Adds each vibrato to <paramref name="f0Midi"/> (semitones, a value a frame; frame i is at
    /// <paramref name="startMs"/> + i × <paramref name="frameMs"/>).
    /// </summary>
    /// <remarks><c>steady</c>: the curve's own wavering under the vibrato is taken out first, as
    /// far as the vibrato is in (the voice's slow drift - measured on Nishiren/2P, some 20 cents
    /// twice a second - and the written swing otherwise add up, unevenly). The note's pitch, its
    /// approach and its end stay the model's.</remarks>
    public static void Apply(float[] f0Midi, double frameMs, double startMs, IReadOnlyList<Written> vibratos, bool steady = false)
    {
        foreach (var v in vibratos)
        {
            double length = v.EndMs - v.StartMs;
            double from = v.StartMs + Math.Min(DelayMs, length * DelayShare);
            double sounding = v.EndMs - from;
            if (sounding < 1500 / v.Hz)
            {
                continue;   // no room for a swing and a half: a wobble, not a vibrato
            }
            int first = Math.Max(0, (int)Math.Ceiling((from - startMs) / frameMs));
            int last = Math.Min(f0Midi.Length - 1, (int)Math.Floor((v.EndMs - startMs) / frameMs));
            double grow = Math.Max(GrowMs, sounding * GrowShare);
            double phase = 0, before = first * frameMs + startMs - from;
            // The curve with its wavering averaged away, over the vibrato's frames: a window of
            // 0.6 s, longer than one swing of the voice's own drift.
            float[]? even = null;
            if (steady && last > first)
            {
                int half = Math.Max(1, (int)Math.Round(300 / frameMs));
                even = new float[last - first + 1];
                for (int i = first; i <= last; i++)
                {
                    int a = Math.Max(first, i - half), b = Math.Min(last, i + half);
                    double sum = 0;
                    for (int k = a; k <= b; k++)
                    {
                        sum += f0Midi[k];
                    }
                    even[i - first] = (float)(sum / (b - a + 1));
                }
            }
            for (int i = first; i <= last; i++)
            {
                double t = startMs + i * frameMs - from;                    // ms into the vibrato
                // Its width: in on a smooth curve (no corner where it begins or where it is full), out the same way.
                double grown = t >= grow ? 1 : 0.5 - 0.5 * Math.Cos(Math.PI * t / grow);
                double left = sounding - t;
                double held = left >= LetGoMs ? 1 : 0.5 - 0.5 * Math.Cos(Math.PI * Math.Max(0, left) / LetGoMs);
                double width = 1 + WidthDrift * Math.Sin(2 * Math.PI * 0.83 * t / 1000 + 1.3);
                // Its pace: slower at first, settling as the width does, and never quite even.
                double pace = v.Hz * (1 - SlowStart * (1 - grown)) * (1 + PaceDrift * Math.Sin(2 * Math.PI * 0.61 * t / 1000));
                phase += 2 * Math.PI * pace * (t - before) / 1000;
                before = t;
                if (even != null)
                {
                    // As far in as the vibrato is: at its edges the curve is still the model's own.
                    double share = grown * held;
                    f0Midi[i] = (float)(f0Midi[i] * (1 - share) + even[i - first] * share);
                }
                f0Midi[i] += (float)(v.Cents / 100 * grown * held * width * Math.Sin(phase));
            }
        }
    }

    /// <summary>What tells a phrase with these vibratos from the same notes without (the phrase
    /// cache's key); empty when none is written, so the phrases kept before the trial are still found.</summary>
    public static string KeyOf(IEnumerable<SungNote> notes)
    {
        var written = notes.Select(n => n.Vibrato is > 0 ? $"{n.Vibrato:0.##}@{n.VibratoRate ?? 0:0.##}" : "").ToList();
        // "2": the shape's own number, raised when the shape changes, so that a phrase kept with
        // an earlier shape is sung again.
        return written.All(w => w.Length == 0) ? "" : "|vib3 " + string.Join(',', written);
    }
}
// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Cantoria.DiffSinger;

/// <summary>
/// A vibrato WRITTEN on a note (<see cref="SungNote.Vibrato"/>), laid on the pitch curve the voice
/// is sung from. A trial (2026-11-12): it is ADDED to what the pitch model drew - the model's own
/// vibrato, where a voice has one, is still there under it - so that it can be heard and judged
/// before the grammar and the other engines are given it.
/// <para>
/// The shape is a singer's: the note begins straight, the vibrato comes in after a moment and
/// grows to its depth, and lets go just before the note ends, so that the next note is entered
/// on pitch. A note too short for a swing and a half is left plain.
/// </para>
/// </summary>
public static class Vibrato
{
    /// <summary>How far the pitch swings each way when the note says only "vibrato", in cents.</summary>
    public const double DefaultDepth = 40;

    /// <summary>Swings a second when the note names none.</summary>
    public const double DefaultRate = 5.5;

    /// <summary>The widest swing sung, each way, in cents: a whole tone.</summary>
    public const double MaxDepth = 200;

    private const double DelayMs = 250;         // straight tone before it begins…
    private const double DelayShare = 0.3;      // …at most this share of the note
    private const double GrowMs = 200;          // from nothing to its depth
    private const double LetGoMs = 80;          // back to the note's pitch before its end

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
    public static void Apply(float[] f0Midi, double frameMs, double startMs, IReadOnlyList<Written> vibratos)
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
            for (int i = first; i <= last; i++)
            {
                double t = startMs + i * frameMs - from;                    // ms into the vibrato
                double grown = Math.Min(1, t / GrowMs);
                double held = Math.Min(1, Math.Max(0, (sounding - t) / LetGoMs));
                f0Midi[i] += (float)(v.Cents / 100 * grown * held * Math.Sin(2 * Math.PI * v.Hz * t / 1000));
            }
        }
    }

    /// <summary>What tells a phrase with these vibratos from the same notes without (the phrase
    /// cache's key); empty when none is written, so the phrases kept before the trial are still found.</summary>
    public static string KeyOf(IEnumerable<SungNote> notes)
    {
        var written = notes.Select(n => n.Vibrato is > 0 ? $"{n.Vibrato:0.##}@{n.VibratoRate ?? 0:0.##}" : "").ToList();
        return written.All(w => w.Length == 0) ? "" : "|vib " + string.Join(',', written);
    }
}
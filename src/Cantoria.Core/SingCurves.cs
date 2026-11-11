// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Cantoria;

/// <summary>
/// The settings written INSIDE the music (<see cref="SungNote.Sing"/>, <see cref="SungNote.SingTo"/>),
/// made into what each note's frames are to be told. A value written on a note holds from that
/// note on, as a dynamic does, until the next one written for the same name; "default" gives the
/// name back to the score's own value (<see cref="SungScore.Sing"/>) or the voice's; a value written
/// as an ARRIVAL (<c>SingTo</c>) is reached at that note's start, gliding in time from where the
/// value before was written.
/// </summary>
public static class SingCurves
{
    /// <summary>
    /// The score with each note's <see cref="SungNote.Curve"/> filled: for every name the music
    /// changes, its value where the note starts and where it ends. Between two notes the value
    /// goes on in a line from the one's end to the other's start, so a glide crosses a rest as it
    /// crosses a note. Names the music never writes are not in it (the score's value stands for
    /// the whole of them); a score whose notes write nothing is given back as it is.
    /// <para>
    /// <paramref name="known"/> is what the voice takes: a name it does not take, a word where it
    /// takes a number, a value out of range and a setting written on a note that the voice can
    /// only take once a phrase or once a score are said (<see cref="SingReport.Note"/>) and left out.
    /// </para>
    /// </summary>
    public static SungScore Resolve(SungScore score, IReadOnlyList<SingParameter> known, string voice)
    {
        var notes = score.Notes;
        var names = notes.SelectMany(n => (n.Sing?.Keys ?? []).Concat(n.SingTo?.Keys ?? [])).Distinct(StringComparer.Ordinal).ToList();
        if (names.Count == 0)
        {
            return score;
        }
        var curves = new Dictionary<string, (double Start, double End)>?[notes.Count];
        foreach (string name in names)
        {
            var parameter = known.FirstOrDefault(p => p.Name == name);
            if (parameter is null)
            {
                string there = known.Count == 0 ? "it takes none" : "it takes " + string.Join(", ", known.Select(p => p.Name));
                SingReport.Note($"{voice} does not take the setting \"{name}\" ({there}); it sings without it");
                continue;
            }
            if (parameter.Scope != SingScope.Curve)
            {
                SingReport.Note($"{voice}: \"{name}\" is one value {(parameter.Scope == SingScope.Phrase ? "a phrase" : "for the voice")}; it cannot change inside the music, and is left as the singer line has it");
                continue;
            }
            double own = score.Sing is { } whole && whole.TryGetValue(name, out var set) && set.Number is { } number
                ? Clamp(number, parameter) : parameter.Default ?? 0;
            // The places the value is written: (the note, its value, whether it is arrived at).
            var marks = new List<(int Note, double Value, bool Arrives)>();
            for (int i = 0; i < notes.Count; i++)
            {
                if (notes[i].Sing is { } here && here.TryGetValue(name, out var value) && Number(value, own, parameter, name, voice) is { } v)
                {
                    marks.Add((i, v, false));
                }
                else if (notes[i].SingTo is { } to && to.TryGetValue(name, out var target) && Number(target, own, parameter, name, voice) is { } t)
                {
                    marks.Add((i, t, true));
                }
            }
            if (marks.Count == 0)
            {
                continue;
            }
            // The value at any time: the score's own before the first mark, each mark's from its
            // note's start, and on the way to an arrival the line from the mark before it.
            double At(double time)
            {
                double value = own, since = double.NegativeInfinity;
                foreach (var (note, v, arrives) in marks)
                {
                    double at = notes[note].Start;
                    if (time >= at)
                    {
                        value = v;
                        since = at;
                        continue;
                    }
                    if (arrives)
                    {
                        // Not there yet: from where the value before was written (the score's
                        // start for the first), in a line.
                        double from = double.IsNegativeInfinity(since) ? Math.Min(0, notes[0].Start) : since;
                        return at > from ? value + (v - value) * (time - from) / (at - from) : v;
                    }
                    return value;
                }
                return value;
            }
            for (int i = 0; i < notes.Count; i++)
            {
                (curves[i] ??= new(StringComparer.Ordinal))[name] = (At(notes[i].Start), At(notes[i].End - 1e-9));
            }
        }
        if (curves.All(c => c is null))
        {
            return score;
        }
        return score with { Notes = notes.Select((n, i) => curves[i] is { } c ? n with { Curve = c } : n).ToList() };
    }

    private static double Clamp(double value, SingParameter parameter)
        => Math.Clamp(value, parameter.Min ?? double.MinValue, parameter.Max ?? double.MaxValue);

    private static double? Number(SingValue value, double own, SingParameter parameter, string name, string voice)
    {
        if (value.IsDefault)
        {
            return own;
        }
        if (value.Number is not { } number)
        {
            SingReport.Note($"{voice}: \"{name}\" takes a number, not \"{value.Word}\"; it sings without it");
            return null;
        }
        double clamped = Clamp(number, parameter);
        if (clamped != number)
        {
            SingReport.Note($"{voice}: \"{name} {value}\" is outside {parameter.Min:0.##} to {parameter.Max:0.##}; sung as {clamped:0.##}");
        }
        return clamped;
    }

    /// <summary>
    /// The value a frame of a name's curve, for one phrase: <paramref name="notes"/> are the
    /// phrase's, their times on the clock the frames are counted on (frame i at
    /// <paramref name="startMs"/> + i × <paramref name="frameMs"/>); null when none of them carries
    /// the name. Inside a note the line from its start to its end; between two notes the line from
    /// the one's end to the other's start; before the first and after the last, their values.
    /// </summary>
    public static float[]? Frames(IReadOnlyList<SungNote> notes, string name, int frames, double frameMs, double startMs)
    {
        var points = new List<(double Ms, double Value)>();
        foreach (var n in notes)
        {
            if (n.Curve is { } curve && curve.TryGetValue(name, out var v))
            {
                points.Add((n.Start * 1000, v.Start));
                points.Add((n.End * 1000, v.End));
            }
        }
        if (points.Count == 0)
        {
            return null;
        }
        var values = new float[frames];
        int at = 0;
        for (int i = 0; i < frames; i++)
        {
            double ms = startMs + i * frameMs;
            while (at + 1 < points.Count && points[at + 1].Ms <= ms)
            {
                at++;
            }
            if (ms <= points[0].Ms)
            {
                values[i] = (float)points[0].Value;
            }
            else if (at + 1 >= points.Count)
            {
                values[i] = (float)points[^1].Value;
            }
            else
            {
                var (a, b) = (points[at], points[at + 1]);
                values[i] = (float)(b.Ms > a.Ms ? a.Value + (b.Value - a.Value) * (ms - a.Ms) / (b.Ms - a.Ms) : b.Value);
            }
        }
        return values;
    }

    /// <summary>What tells a phrase whose notes carry these curves from the same notes without (a
    /// phrase cache's key); empty when none does.</summary>
    public static string KeyOf(IEnumerable<SungNote> notes)
    {
        var written = notes.Select(n => n.Curve is { Count: > 0 } c
            ? string.Join(';', c.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => FormattableString.Invariant($"{p.Key}={p.Value.Start:0.###}>{p.Value.End:0.###}")))
            : "").ToList();
        return written.All(w => w.Length == 0) ? "" : "|curve " + string.Join(',', written);
    }
}
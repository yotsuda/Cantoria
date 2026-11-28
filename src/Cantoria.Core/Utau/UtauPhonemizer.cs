// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Cantoria.DiffSinger;

namespace Cantoria.Utau;

/// <summary>One recording a note sings: the alias's entry, and the seconds it sounds from the beat
/// (<see cref="Start"/>, where its preutterance ends) to <see cref="End"/>. <see cref="Note"/> is the
/// score's note it begins in.</summary>
public sealed record UtauSegment(Oto Oto, double Start, double End, int Note);

/// <summary>
/// Japanese kana to an UTAU voicebank's recordings. Each mora of a syllable is one recording: in a
/// 連続音 (VCV) bank the one that runs from the vowel before into it (<c>a か</c>), from silence
/// (<c>- か</c>) after a rest or a breath; else the 単独音 (CV) one (<c>か</c>); each tried with the
/// pitch's <c>prefix.map</c> prefix and suffix first, and as the kana then its romaji (<c>ka</c>). A
/// syllable of several morae (<c>こん</c>) gives the first most of the note and each one after it
/// at most <see cref="TailSeconds"/>. A melisma note, or <c>ー</c>, holds the sound before on; <c>っ</c>
/// is a silence that the next mora starts out of.
/// </summary>
public static class UtauPhonemizer
{
    /// <summary>The most each mora after a syllable's first takes of the note.</summary>
    public const double TailSeconds = 0.12;

    /// <summary>A gap between notes shorter than this is no rest.</summary>
    public const double Touching = 0.001;

    /// <summary>The recordings the score sings, in time order.</summary>
    /// <exception cref="SingerException">A mora the voicebank has no recording for; the message names the note
    /// in the words Lily# looks for (<c>has no sound for "…" (note N</c>).</exception>
    public static List<UtauSegment> Segments(SungScore score, UtauVoicebank bank, string voice)
    {
        var segments = new List<UtauSegment>();
        string? vowel = null;      // the vowel sounding at the end of the last mora; null after silence
        double end = double.NegativeInfinity;
        bool breath = false;
        for (int i = 0; i < score.Notes.Count; i++)
        {
            var n = score.Notes[i];
            bool joined = n.Start - end < Touching && !breath;
            if (!joined)
            {
                vowel = null;
            }
            breath = n.Breath || n.Inhale;
            end = n.End;
            if (n.Melisma)
            {
                if (joined && segments.Count > 0)
                {
                    segments[^1] = segments[^1] with { End = n.End };
                }
                continue;
            }
            var morae = KanaPhonemizer.Morae(n.Lyric ?? "");
            int count = morae.Count(m => m != "ー");
            double tail = count > 1 ? Math.Min(TailSeconds, n.Duration / count) : 0;
            double at = n.Start;
            int sung = 0;
            foreach (var mora in morae)
            {
                if (mora == "ー")
                {
                    continue;   // the sound before goes on: its end is moved as the next mora starts
                }
                double from = at, to = sung == 0 ? n.End - tail * (count - 1) : at + tail;
                at = to;
                sung++;
                if (segments.Count > 0 && segments[^1].Note == i)
                {
                    segments[^1] = segments[^1] with { End = from };
                }
                if (mora == "っ")
                {
                    vowel = null;
                    continue;
                }
                var tried = Candidates(mora, vowel).ToList();
                int midi = (int)Math.Round(n.Pitch);
                var oto = tried.Select(a => bank.Find(a, midi)).FirstOrDefault(o => o is not null)
                    ?? throw new SingerException($"{voice} has no sound for \"{n.Lyric}\" (note {i} at {n.Start:0.###} s): its voicebank has none of the aliases {string.Join(", ", tried.Select(a => $"\"{a}\""))}");
                segments.Add(new UtauSegment(oto, from, to, i));
                vowel = VowelOf(mora);
            }
            if (segments.Count > 0 && segments[^1].Note == i && vowel is not null)
            {
                segments[^1] = segments[^1] with { End = n.End };   // a syllable ending in っ ends silent
            }
        }
        return segments;
    }

    /// <summary>The aliases tried for a mora after <paramref name="vowel"/> (null after silence), in order.</summary>
    public static IEnumerable<string> Candidates(string mora, string? vowel)
    {
        var graphemes = KanaPhonemizer.Graphemes(mora).Where(g => g.Length > 0).ToList();
        foreach (var g in graphemes)
        {
            yield return (vowel ?? "-") + " " + g;
        }
        foreach (var g in graphemes)
        {
            yield return g;
        }
    }

    /// <summary>The vowel a mora ends on as a VCV alias writes it (<c>a</c> … <c>o</c>, <c>n</c> for ん), or null.</summary>
    public static string? VowelOf(string mora)
    {
        if (mora == "ん")
        {
            return "n";
        }
        var romaji = KanaPhonemizer.Graphemes(mora).Skip(1).FirstOrDefault();
        return romaji is { Length: > 0 } && "aiueo".Contains(romaji[^1]) ? romaji[^1].ToString() : null;
    }
}

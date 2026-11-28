// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Cantoria.DiffSinger;

namespace Cantoria.Utau;

/// <summary>One recording a note sings: the alias's entry, and the seconds it sounds from the beat
/// (<see cref="Start"/>, where its preutterance ends) to <see cref="End"/>. <see cref="Note"/> is the
/// score's note it begins in. <see cref="Bridge"/>: a consonant laid before the next recording's beat (a CVVC
/// <c>a k</c>, an English cluster), as long as it is so that the next recording's preutterance sounds over it.
/// <see cref="FromVowel"/>: its recording begins on the vowel before its consonant (<c>a k</c>, <c>@ l</c>), so a
/// preutterance cut to fit is cut from the recording's front and its consonant still begins on <see cref="Start"/>.
/// <see cref="Held"/>: a word-final recording (<c>U l-</c>) sung mid-phrase for its held consonant: used only to the
/// end of its unstretched part, so its release into silence is left out, and the consonant after its preutterance
/// stretched to fill.</summary>
public sealed record UtauSegment(Oto Oto, double Start, double End, int Note, bool Bridge = false, bool FromVowel = false, bool Held = false);

/// <summary>
/// Japanese kana to an UTAU voicebank's recordings. Each mora of a syllable is one recording: in a
/// 連続音 (VCV) bank the one that runs from the vowel before into it (<c>a か</c>), from silence
/// (<c>- か</c>) after a rest or a breath; else the 単独音 (CV) one (<c>か</c>); each tried with the
/// pitch's <c>prefix.map</c> prefix and suffix first, and as the kana then its romaji (<c>ka</c>). A
/// syllable of several morae (<c>こん</c>) gives the first most of the note and each one after it
/// at most <see cref="TailSeconds"/>. A melisma note, or <c>ー</c>, holds the sound before on; <c>っ</c>
/// is a silence that the next mora starts out of. Where the bank has them, a phrase ends on the recording of
/// its last vowel dying into silence (<c>a -</c>, else <c>a R</c>; <see cref="EndingSeconds"/> at most).
/// English words are <see cref="UtauEnglishPhonemizer"/>'s.
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
        if (score.Language == "en")
        {
            return UtauEnglishPhonemizer.Segments(score, bank, voice);
        }
        var segments = new List<UtauSegment>();
        string? vowel = null;      // the vowel sounding at the end of the last mora; null after silence
        double end = double.NegativeInfinity;
        bool breath = false;
        int lastMidi = 60, lastSegmentMidi = 60;
        for (int i = 0; i < score.Notes.Count; i++)
        {
            var n = score.Notes[i];
            bool joined = n.Start - end < Touching && !breath;
            if (!joined)
            {
                Ending(segments, bank, vowel, lastMidi, i - 1, end, n.Start - end);
                vowel = null;
            }
            lastMidi = (int)Math.Round(n.Pitch);
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
                var found = tried.Select(a => (Alias: a, Oto: bank.Find(a, midi))).FirstOrDefault(f => f.Oto is not null);
                var oto = found.Oto
                    ?? throw new SingerException($"{voice} has no sound for \"{n.Lyric}\" (note {i} at {n.Start:0.###} s): its voicebank has none of the aliases {string.Join(", ", tried.Select(a => $"\"{a}\""))}");
                if (vowel is not null && !found.Alias.StartsWith(vowel + " ", StringComparison.Ordinal) && segments.Count > 0)
                {
                    BridgeFromVowel(segments, bank, vowel, mora, oto, from, lastSegmentMidi);
                }
                segments.Add(new UtauSegment(oto, from, to, i));
                lastSegmentMidi = midi;
                vowel = VowelOf(mora);
            }
            if (segments.Count > 0 && segments[^1].Note == i && vowel is not null)
            {
                segments[^1] = segments[^1] with { End = n.End };   // a syllable ending in っ ends silent
            }
        }
        Ending(segments, bank, vowel, lastMidi, score.Notes.Count - 1, end, double.PositiveInfinity);
        return segments;
    }

    /// <summary>The most of the sound before that a CVVC bank's bridge (<c>a k</c>) takes.</summary>
    public const double BridgeSeconds = 0.25;

    /// <summary>
    /// In a CVVC bank, the recording from the vowel before into the mora's consonant (<c>a k</c> before
    /// <c>か</c>), laid at the end of the sound before as UTAU users lay it: as long as the CV recording's
    /// preutterance, at most <see cref="BridgeSeconds"/> and half the sound before. Nothing where the bank
    /// has none (a CV bank), or for a mora that is a vowel.
    /// </summary>
    private static void BridgeFromVowel(List<UtauSegment> segments, UtauVoicebank bank, string vowel, string mora, Oto cv, double at, int midi)
    {
        var before = segments[^1];
        if (before.End < at - Touching)
        {
            return;   // silence between them (っ): nothing to bridge
        }
        foreach (var consonant in Consonants(mora))
        {
            if (bank.Find(vowel + " " + consonant, midi) is not { } vc)
            {
                continue;
            }
            double length = Math.Min(Math.Min(BridgeSeconds, (at - before.Start) / 2), Math.Max(0.03, cv.Preutter / 1000));
            segments[^1] = before with { End = at - length };
            segments.Add(new UtauSegment(vc, at - length, at, before.Note, Bridge: true, FromVowel: true));
            return;
        }
    }

    /// <summary>A mora's consonant as a CVVC alias writes it, for each of its romaji spellings: <c>k</c> for か,
    /// <c>sh</c> then <c>s</c> for し, <c>ky</c> for きゃ, <c>n</c> for ん; none for a vowel.</summary>
    public static IEnumerable<string> Consonants(string mora)
    {
        if (mora == "ん")
        {
            yield return "n";
            yield break;
        }
        foreach (var romaji in KanaPhonemizer.Graphemes(mora).Skip(1))
        {
            var consonant = romaji.TrimEnd('a', 'i', 'u', 'e', 'o');
            if (consonant.Length > 0 && consonant.Length < romaji.Length)
            {
                yield return consonant;
            }
        }
    }

    /// <summary>The most a phrase's ending recording (<c>a -</c>, <c>a R</c>: the vowel dying into silence) takes of the rest after it.</summary>
    public const double EndingSeconds = 0.3;

    /// <summary>A rest shorter than this has no ending recording: the next phrase begins too soon.</summary>
    public const double EndingRoom = 0.08;

    /// <summary>Where a 連続音 bank has one, the recording of <paramref name="vowel"/> dying into silence,
    /// from the phrase's end into the <paramref name="room"/> after it.</summary>
    private static void Ending(List<UtauSegment> segments, UtauVoicebank bank, string? vowel, int midi, int note, double at, double room)
    {
        if (vowel is null || room < EndingRoom || segments.Count == 0)
        {
            return;
        }
        var oto = bank.Find(vowel + " -", midi) ?? bank.Find(vowel + " R", midi);
        if (oto is not null)
        {
            segments.Add(new UtauSegment(oto, at, at + Math.Min(EndingSeconds, room - EndingRoom / 4), note));
        }
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

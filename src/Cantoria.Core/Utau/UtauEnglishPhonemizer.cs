// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Cantoria.DiffSinger;

namespace Cantoria.Utau;

/// <summary>
/// English words to an UTAU voicebank's recordings, for an English CVVC bank in X-SAMPA (Kasane Teto
/// English): a word is read as the CMU Pronouncing Dictionary has it, else as the model trained on it
/// reads it (<see cref="EnglishG2p"/>, as the DiffSinger voices read a word their dictionary lacks), its
/// phonemes dealt over the word's notes a vowel each (<see cref="EnglishPhonemizer"/>), and each vowel
/// sung on the recording of the consonant before it running into it (<c>b@</c>; <c>- b@</c> after
/// silence; the vowel alone, or from the vowel before, <c>A @</c>, where no consonant comes between).
/// <para>Between two vowels the consonants are bridged as such a bank is laid out: from the vowel into the
/// first consonant (<c>A d</c>), a consonant into the next where the bank has one (<c>r d</c>), a cluster
/// before the vowel's own consonant (<c>str</c> before <c>rI</c>; <c>- str</c> after silence), each laid
/// before the vowel's beat as long as the recording after it has as its preutterance, all within half
/// the sound before. A phrase ends on its vowel dying into silence (<c>A -</c>), or on its last
/// consonants (<c>A t-</c>; <c>l mz-</c> after <c>{ l</c>), which end as the note does or a little into
/// the rest after it. A consonant the bank has no recording for is passed over; a vowel it has none for
/// is refused with the note named.</para>
/// </summary>
public static class UtauEnglishPhonemizer
{
    /// <summary>ARPABET (as <see cref="EnglishG2p"/> spells it, <c>ax</c> the schwa) in the X-SAMPA of an
    /// English CVVC bank's aliases.</summary>
    public static readonly IReadOnlyDictionary<string, string> XSampa = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["aa"] = "A", ["ae"] = "{", ["ah"] = "V", ["ax"] = "@", ["ao"] = "O", ["aw"] = "aU", ["ay"] = "aI",
        ["eh"] = "E", ["er"] = "3", ["ey"] = "eI", ["ih"] = "I", ["iy"] = "i", ["ow"] = "oU", ["oy"] = "OI",
        ["uh"] = "U", ["uw"] = "u",
        ["b"] = "b", ["ch"] = "tS", ["d"] = "d", ["dh"] = "D", ["f"] = "f", ["g"] = "g", ["hh"] = "h", ["jh"] = "dZ",
        ["k"] = "k", ["l"] = "l", ["m"] = "m", ["n"] = "n", ["ng"] = "N", ["p"] = "p", ["r"] = "r", ["s"] = "s",
        ["sh"] = "S", ["t"] = "t", ["th"] = "T", ["v"] = "v", ["w"] = "w", ["y"] = "j", ["z"] = "z", ["zh"] = "Z",
    };

    private static readonly HashSet<string> Vowels = ["A", "{", "V", "@", "O", "aU", "aI", "E", "3", "eI", "I", "i", "oU", "OI", "U", "u"];

    private static readonly DsDictionary Arpabet = DsDictionary.OfSymbols(XSampa.Keys, p => Vowels.Contains(XSampa[p]));

    /// <summary>The aliases that say a voicebank sings English this way: the schwa or the vowel of "cat" from silence, or into it.</summary>
    public static readonly string[] Markers = ["- @", "@ -", "- {", "{ -"];

    /// <summary>The most of the time before a phrase that its first consonants take.</summary>
    public const double StartSeconds = 0.4;

    /// <summary>The most a phrase's last consonants run into the rest after it.</summary>
    public const double ReleaseSeconds = 0.15;

    /// <summary>One vowel and the consonants about it: those before it in its syllable, and those after
    /// it that end its word's syllable.</summary>
    private sealed record Unit(List<string> Onset, string Vowel, List<string> Coda, double Start, double End, int Note, int Midi, bool Syllabic = false);

    /// <summary>The consonants a schwa before them is all but swallowed by (little, button, rhythm).</summary>
    private static readonly HashSet<string> SyllabicConsonants = ["l", "n", "m"];

    /// <summary>The most of a syllabic consonant's syllable (<c>kle</c>: k, a short vowel, then l) its vowel takes,
    /// and the share of the syllable at most.</summary>
    public const double SyllabicVowelSeconds = 0.1, SyllabicVowelShare = 0.35;

    /// <summary>Where a syllabic consonant begins in its syllable.</summary>
    private static double SyllabicAt(Unit u) => u.Start + Math.Min(SyllabicVowelSeconds, SyllabicVowelShare * (u.End - u.Start));

    /// <summary>The recordings the score sings, in time order.</summary>
    /// <exception cref="SingerException">A word that cannot be read, or a vowel the voicebank has no recording for;
    /// the message names the note (<c>has no sound for "…" (note N</c>).</exception>
    public static List<UtauSegment> Segments(SungScore score, UtauVoicebank bank, string voice)
    {
        var notes = score.Notes;
        var sungNotes = notes.Select((n, i) => (Note: n, Index: i)).Where(x => !x.Note.Melisma).ToList();
        var words = sungNotes.Select(x =>
        {
            int last = x.Index;
            while (last + 1 < notes.Count && notes[last + 1].Melisma)
            {
                last++;
            }
            return (x.Note.Lyric ?? "", notes.Skip(x.Index).Take(last - x.Index + 1).Any(m => m.Hyphen));
        }).ToList();
        var syllables = EnglishPhonemizer.Syllables(words, Arpabet, p => XSampa.ContainsKey(p) ? p : null);

        // Each phrase's vowels: a syllable from its note to the end of the melisma after it.
        var phrases = new List<List<Unit>>();
        double end = double.NegativeInfinity;
        bool breath = false;
        int syllable = 0;
        for (int i = 0; i < notes.Count; i++)
        {
            var n = notes[i];
            bool joined = n.Start - end < UtauPhonemizer.Touching && !breath;
            breath = n.Breath || n.Inhale;
            end = n.End;
            var units = joined && phrases.Count > 0 ? phrases[^1] : null;
            if (n.Melisma)
            {
                if (units is { Count: > 0 })
                {
                    units[^1] = units[^1] with { End = n.End };
                }
                continue;
            }
            var sung = syllables[syllable++];
            if (sung.Missing)
            {
                throw new SingerException($"{voice} has no sound for \"{n.Lyric}\" (note {i} at {n.Start:0.###} s): the word cannot be read");
            }
            if (sung.Phonemes.Count == 0)
            {
                if (units is { Count: > 0 })
                {
                    units[^1] = units[^1] with { End = n.End };   // a word with fewer vowels than notes: the vowel goes on
                }
                continue;
            }
            if (units is null)
            {
                phrases.Add(units = []);
            }
            AddUnits(units, sung.Phonemes.Select(p => XSampa[p]).ToList(), n, i);
        }

        var segments = new List<UtauSegment>();
        double before = double.PositiveInfinity;    // the silence before the phrase
        for (int p = 0; p < phrases.Count; p++)
        {
            var units = phrases[p];
            if (units.Count == 0)
            {
                continue;
            }
            double after = p + 1 < phrases.Count && phrases[p + 1].Count > 0 ? phrases[p + 1][0].Start - units[^1].End : double.PositiveInfinity;
            Phrase(segments, units, bank, voice, score, before, after);
            before = after;
        }
        return segments;
    }

    /// <summary>A syllable's vowels as units, the note's time shared evenly among them; consonants with no
    /// vowel of their own end the unit before.</summary>
    private static void AddUnits(List<Unit> units, List<string> phonemes, SungNote n, int note)
    {
        var vowels = Enumerable.Range(0, phonemes.Count).Where(k => Vowels.Contains(phonemes[k])).ToList();
        int midi = (int)Math.Round(n.Pitch);
        if (vowels.Count == 0)
        {
            if (units.Count > 0)
            {
                units[^1].Coda.AddRange(phonemes);
                units[^1] = units[^1] with { End = n.End };
            }
            return;
        }
        double share = n.Duration / vowels.Count;
        for (int v = 0; v < vowels.Count; v++)
        {
            int from = v == 0 ? 0 : vowels[v - 1] + 1;
            var onset = phonemes.GetRange(from, vowels[v] - from);
            var coda = v == vowels.Count - 1 ? phonemes.GetRange(vowels[v] + 1, phonemes.Count - vowels[v] - 1) : [];
            units.Add(new Unit(onset, phonemes[vowels[v]], coda, n.Start + v * share, n.Start + (v + 1) * share, note, midi));
        }
    }

    private static void Phrase(List<UtauSegment> segments, List<Unit> units, UtauVoicebank bank, string voice, SungScore score, double before, double after)
    {
        // A schwa before l, n or m that ends its syllable (twinkle, little): the consonant is most of the syllable,
        // and before l the vowel is the bank's U where it has "U l" (owner's report 2026-11-28: the schwa held
        // as Teto sings it was heard as "win-kEtz", "littEl"; "twin-kULe" was what was wanted).
        for (int k = 0; k < units.Count; k++)
        {
            var u = units[k];
            if (u.Vowel == "@" && u.Coda.Count > 0 && SyllabicConsonants.Contains(u.Coda[0]))
            {
                units[k] = u with { Vowel = u.Coda[0] == "l" && Find(bank, "U l", u.Midi) is not null ? "U" : "@", Syllabic = true };
            }
        }
        for (int k = 0; k < units.Count; k++)
        {
            var u = units[k];
            var pieces = new List<Piece>();
            Piece cv;
            double room;
            int pieceNote;
            double? firstFrom = null;
            if (k == 0)
            {
                cv = Onset(pieces, u, u.Onset, null, true, bank, voice, score);
                room = Math.Min(StartSeconds, Math.Max(before - UtauPhonemizer.EndingRoom / 4, 0.04 * pieces.Count));
                pieceNote = u.Note;
            }
            else
            {
                var p = units[k - 1];
                List<string> coda = p.Coda, onset = u.Onset;
                if (coda.Count > 0 && onset.Count == 0)
                {
                    (coda, onset) = (coda[..^1], [coda[^1]]);   // "hand out": the d begins the next vowel
                }
                if (coda.Count > 0 && Find(bank, p.Vowel + " " + coda[0], p.Midi) is { } vc)
                {
                    pieces.Add(new Piece(vc, null, coda[0]));
                }
                for (int j = 1; j < coda.Count; j++)
                {
                    if (Find(bank, coda[j - 1] + " " + coda[j], p.Midi) is { } pair)
                    {
                        pieces.Add(new Piece(pair, coda[j - 1], coda[j]));
                    }
                    else if (Find(bank, coda[j], p.Midi) is { } single)
                    {
                        pieces.Add(new Piece(single, coda[j], coda[j]));
                    }
                }
                if (coda.Count == 0 && onset.Count > 0 && Find(bank, p.Vowel + " " + onset[0], p.Midi) is { } bridge)
                {
                    pieces.Add(new Piece(bridge, null, onset[0]));
                }
                cv = Onset(pieces, u, onset, coda.Count == 0 ? p.Vowel : null, false, bank, voice, score);
                room = (u.Start - segments[^1].Start) * RoomShare;
                pieceNote = p.Note;
                if (p.Syllabic && pieces.Count > 0 && pieces[0].First is null)
                {
                    firstFrom = SyllabicAt(p);
                }
            }
            LayBefore(segments, pieces, cv, u.Start, room, pieceNote, firstFrom);
            segments.Add(new UtauSegment(cv.Oto, u.Start, u.End, u.Note));
        }
        End(segments, units[^1], bank, after);
    }

    /// <summary>The recording the vowel is sung on, the cluster recordings before it added to <paramref name="pieces"/>.
    /// <paramref name="vowelBefore"/>: the vowel sounding just before, with no consonant between; null after silence or a consonant.</summary>
    private static Piece Onset(List<Piece> pieces, Unit u, List<string> onset, string? vowelBefore, bool start, UtauVoicebank bank, string voice, SungScore score)
    {
        var tried = new List<string>();
        Oto? Try(string alias)
        {
            tried.Add(alias);
            return Find(bank, alias, u.Midi);
        }
        Oto? cv = null;
        int used = 0;
        if (onset.Count == 0)
        {
            cv = (vowelBefore is not null ? Try(vowelBefore + " " + u.Vowel) : null)
                ?? (start ? Try("- " + u.Vowel) : null)
                ?? Try(u.Vowel);
        }
        for (int s = Math.Min(2, onset.Count); s >= 1 && cv is null; s--)
        {
            string alias = string.Concat(onset.Skip(onset.Count - s)) + u.Vowel;
            cv = (start && s == onset.Count ? Try("- " + alias) : null) ?? Try(alias);
            used = s;
        }
        if (cv is null && u.Syllabic && u.Vowel == "U")
        {
            return Onset(pieces, u with { Vowel = "@" }, onset, vowelBefore, start, bank, voice, score);
        }
        if (cv is null)
        {
            var n = score.Notes[u.Note];
            throw new SingerException($"{voice} has no sound for \"{n.Lyric}\" (note {u.Note} at {n.Start:0.###} s): its voicebank has none of the aliases {string.Join(", ", tried.Select(a => $"\"{a}\""))}");
        }
        if (onset.Count > used)
        {
            // The consonants before the vowel's own: the longest cluster the bank has from each, through to that consonant.
            var q = onset.Take(onset.Count - used + 1).ToList();
            bool first = start;
            for (int at = 0; at < q.Count - 1;)
            {
                Oto? found = null;
                int to = q.Count;
                for (; to >= at + 2 && found is null; to--)
                {
                    string cluster = string.Concat(q.Skip(at).Take(to - at));
                    found = (first ? Find(bank, "- " + cluster, u.Midi) : null) ?? Find(bank, cluster, u.Midi);
                }
                if (found is not null)
                {
                    pieces.Add(new Piece(found, q[at], q[to]));
                    at = to;   // (one past where the loop stopped: the cluster's last consonant)
                }
                else
                {
                    if (((first ? Find(bank, "- " + q[at], u.Midi) : null) ?? Find(bank, q[at], u.Midi)) is { } single)
                    {
                        pieces.Add(new Piece(single, q[at], q[at]));
                    }
                    at++;
                }
                first = false;
            }
        }
        return new Piece(cv, used > 0 ? onset[onset.Count - used] : null, null);
    }

    /// <summary>The share of the sound before that the consonants between it and the next vowel may take.</summary>
    public const double RoomShare = 0.6;

    /// <summary>A recording and the consonants it begins and ends on (null: a vowel, or silence).</summary>
    private sealed record Piece(Oto Oto, string? First, string? Last);

    /// <summary>
    /// How long a piece lasts before the next one's beat. Where the next one begins on the consonant this one ends
    /// on (<c>A k</c> then <c>kA</c>, <c>str</c> then <c>rI</c>), the next one's preutterance sounds that consonant
    /// again: this one lasts that preutterance (0.03–0.25 s). Else (<c>@ l</c> then <c>tw</c>, <c>I N</c> then
    /// <c>k@</c>) its own consonant must sound first: as much of its recording as there is after its
    /// preutterance (0.03–0.2 s), then the next one's preutterance less its overlap. Owner's report 2026-11-28:
    /// laid the first way throughout, "twinkle twinkle little star" was heard as "we-ketz we-ketz we- testou",
    /// the N of twin and the l of kle and tle each cut off within 30 ms by the consonant after.
    /// </summary>
    private static double Before(Piece piece, Piece next)
    {
        double pre = next.Oto.Preutter / 1000;
        if (piece.Last is not null && piece.Last == next.First)
        {
            return Math.Clamp(pre, 0.03, UtauPhonemizer.BridgeSeconds);
        }
        return Math.Clamp(After(piece.Oto), 0.03, 0.2) + Math.Clamp(pre - next.Oto.Overlap / 1000, 0.03, UtauPhonemizer.BridgeSeconds);
    }

    /// <summary>The pieces laid before <paramref name="beat"/>, each as long as <see cref="Before"/> says, all within
    /// <paramref name="room"/>; the sound before cut where they begin.</summary>
    private static void LayBefore(List<UtauSegment> segments, List<Piece> pieces, Piece cv, double beat, double room, int note, double? firstFrom = null)
    {
        if (pieces.Count == 0)
        {
            return;
        }
        var lengths = new double[pieces.Count];
        for (int k = 0; k < pieces.Count; k++)
        {
            lengths[k] = Before(pieces[k], k + 1 < pieces.Count ? pieces[k + 1] : cv);
        }
        double total = lengths.Sum(), scale = total > room && room > 0 ? room / total : 1;
        double at = beat - total * scale;
        // A syllabic consonant's piece (little: "U l") begins early and holds its consonant on.
        double start = firstFrom is { } early && early < at ? early : at;
        if (segments.Count > 0 && segments[^1].End > start && segments[^1].Start < start)
        {
            segments[^1] = segments[^1] with { End = start };
        }
        for (int k = 0; k < pieces.Count; k++)
        {
            double to = at + lengths[k] * scale;
            segments.Add(new UtauSegment(pieces[k].Oto, k == 0 ? start : at, to, note, Bridge: true, FromVowel: pieces[k].First is null));
            at = to;
        }
    }

    /// <summary>The phrase's end: its vowel dying into silence where it ends on one (<c>A -</c>, else <c>A R</c>) and
    /// there is a rest to die into; else its last consonants, ending as the note does or a little into the rest.</summary>
    private static void End(List<UtauSegment> segments, Unit u, UtauVoicebank bank, double after)
    {
        if (u.Coda.Count == 0)
        {
            if (after >= UtauPhonemizer.EndingRoom && (Find(bank, u.Vowel + " -", u.Midi) ?? Find(bank, u.Vowel + " R", u.Midi)) is { } ending)
            {
                segments.Add(new UtauSegment(ending, u.End, u.End + Math.Min(UtauPhonemizer.EndingSeconds, after - UtauPhonemizer.EndingRoom / 4), u.Note));
            }
            return;
        }
        var k = u.Coda;
        var pieces = new List<Piece>();
        if (k.Count == 1)
        {
            if ((Find(bank, u.Vowel + " " + k[0] + "-", u.Midi) ?? Find(bank, u.Vowel + " " + k[0], u.Midi)) is { } vc)
            {
                pieces.Add(new Piece(vc, null, k[0]));
            }
        }
        else
        {
            if (Find(bank, u.Vowel + " " + k[0], u.Midi) is { } vc)
            {
                pieces.Add(new Piece(vc, null, k[0]));
            }
            if (Find(bank, k[0] + " " + string.Concat(k.Skip(1)) + "-", u.Midi) is { } rest)
            {
                pieces.Add(new Piece(rest, k[0], k[^1]));
            }
            else
            {
                for (int j = 1; j < k.Count; j++)
                {
                    bool last = j == k.Count - 1;
                    if (((last ? Find(bank, k[j - 1] + " " + k[j] + "-", u.Midi) : null) ?? Find(bank, k[j - 1] + " " + k[j], u.Midi)) is { } pair)
                    {
                        pieces.Add(new Piece(pair, k[j - 1], k[j]));
                    }
                    else if (Find(bank, k[j], u.Midi) is { } single)
                    {
                        pieces.Add(new Piece(single, k[j], k[j]));
                    }
                }
            }
        }
        if (pieces.Count == 0)
        {
            return;
        }
        // Each before the next as Before says, the last as long as its recording has after its preutterance
        // (0.05–0.25 s); ending a little into the rest, and beginning no sooner than halfway through the vowel.
        var lengths = pieces.Select((p, j) => j + 1 < pieces.Count ? Before(p, pieces[j + 1])
            : Math.Clamp(After(p.Oto), 0.05, UtauPhonemizer.BridgeSeconds)).ToArray();
        double total = lengths.Sum();
        double endAt = u.End + Math.Min(Math.Min(ReleaseSeconds, total / 2), Math.Max(0, after - UtauPhonemizer.EndingRoom / 4));
        var vowel = segments[^1];
        double earliest = vowel.Start + (u.End - vowel.Start) / 2;
        double scale = endAt - total < earliest ? Math.Max(0.01, endAt - earliest) / total : 1;
        double at = endAt - total * scale;
        double start = u.Syllabic ? Math.Min(at, SyllabicAt(u)) : at;   // "little": the l holds on
        segments[^1] = vowel with { End = start };
        for (int j = 0; j < pieces.Count; j++)
        {
            double to = at + lengths[j] * scale;
            segments.Add(new UtauSegment(pieces[j].Oto, j == 0 ? start : at, to, u.Note, Bridge: true, FromVowel: pieces[j].First is null));
            at = to;
        }
    }

    /// <summary>How long an alias's recording sounds after its preutterance (its consonant, for a VC), in seconds:
    /// from its length where <c>oto.ini</c> gives it (a negative cutoff), else a guess.</summary>
    private static double After(Oto oto) => oto.Cutoff < 0 ? (-oto.Cutoff - oto.Preutter) / 1000 : 0.15;

    private static Oto? Find(UtauVoicebank bank, string alias, int midi) => bank.Find(alias, midi);
}

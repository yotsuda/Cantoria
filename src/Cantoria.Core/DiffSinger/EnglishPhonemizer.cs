// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text;

namespace Cantoria.DiffSinger;

/// <summary>
/// English words to phonemes through a voice's dictionary. A score writes a word over its notes
/// in syllables (<c>Twin- kle</c>) and the dictionary spells whole words, so the syllables joined by
/// hyphens are looked up as one word and its phonemes dealt back to the notes, a vowel to each.
/// </summary>
public static class EnglishPhonemizer
{
    /// <summary>Second consonant of the two-consonant onsets English begins a syllable with, by first.</summary>
    private static readonly Dictionary<string, string[]> Onsets = new()
    {
        ["p"] = ["l", "r"], ["b"] = ["l", "r"], ["t"] = ["r", "w"], ["d"] = ["r", "w"], ["k"] = ["l", "r", "w"], ["g"] = ["l", "r", "w"],
        ["f"] = ["l", "r"], ["th"] = ["r"], ["sh"] = ["r"], ["s"] = ["p", "t", "k", "m", "n", "l", "w"],
    };

    /// <summary>What each syllable sings. <c>Missing</c>: its word is not in the dictionary. Empty and
    /// not missing: the word has fewer vowels than notes, so the note carries the vowel before it on.</summary>
    public readonly record struct Syllable(List<string> Phonemes, bool Missing, bool Guessed = false);

    /// <summary>The phonemes of each note's syllable, in order.</summary>
    /// <param name="notes">Each syllable, and whether a hyphen joins it to the next.</param>
    /// <param name="dictionary">The voice's English dictionary.</param>
    /// <param name="known">The symbol as the model spells it, or null when the model has none.</param>
    public static List<Syllable> Syllables(IReadOnlyList<(string Text, bool Hyphen)> notes, DsDictionary dictionary, Func<string, string?> known)
    {
        var result = new List<Syllable>(notes.Count);
        for (int first = 0; first < notes.Count;)
        {
            int last = first;
            while (notes[last].Hyphen && last + 1 < notes.Count)
            {
                last++;
            }
            var parts = notes.Skip(first).Take(last - first + 1).Select(n => Clean(n.Text)).ToList();
            string word = string.Concat(parts);
            var whole = Spell(word, dictionary, known);
            // A word the dictionary lacks: each syllable may still be a word of its own, and
            // failing that the letters give a guess.
            var alone = whole is null ? parts.Select(p => Spell(p, dictionary, known)).ToList() : null;
            bool guessed = alone is not null && alone.Contains(null);
            whole ??= guessed ? Spell(word, dictionary, known, guess: true) : null;
            if (whole is not null)
            {
                foreach (var dealt in Deal(whole, parts.Count, dictionary.IsVowel))
                {
                    result.Add(new Syllable(dealt, false, guessed));
                }
            }
            else
            {
                foreach (var one in alone!)
                {
                    result.Add(new Syllable(one ?? [], one is null));
                }
            }
            first = last + 1;
        }
        return result;
    }

    /// <summary>A syllable in the dictionary's spelling: lower case, letters, apostrophes, and the spaces of an elision.</summary>
    internal static string Clean(string text)
    {
        var s = new StringBuilder();
        foreach (char c in text.ToLowerInvariant())
        {
            if (char.IsLetter(c) || c == '\'')
            {
                s.Append(c);
            }
            else if (c == '’')
            {
                s.Append('\'');
            }
            else if (c is ' ' or '~' or '‿')
            {
                s.Append(' ');
            }
        }
        return s.ToString().Trim().Trim('\'');
    }

    /// <summary>The model's phonemes of a word or of an elision's words; null if one is not to be had.</summary>
    private static List<string>? Spell(string text, DsDictionary dictionary, Func<string, string?> known, bool guess = false)
    {
        var phonemes = new List<string>();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var raw = Lookup(word, dictionary) ?? (guess ? EnglishLetters.Guess(word) : null);
            if (raw is null)
            {
                return null;
            }
            foreach (var p in raw)
            {
                if (known(p) is not { } symbol)
                {
                    return null;
                }
                phonemes.Add(symbol);
            }
        }
        return phonemes.Count > 0 ? phonemes : null;
    }

    /// <summary>A word's entry, or its spelling from a listed form of it: the dictionary has
    /// <c>twinkles</c> and no <c>twinkle</c>.</summary>
    public static string[]? Lookup(string word, DsDictionary dictionary)
    {
        if (Inflected(word, dictionary) is { } direct)
        {
            return direct;
        }
        if (word.EndsWith("ing", StringComparison.Ordinal) && word.Length > 4)
        {
            string stem = word[..^3];
            if ((Inflected(stem, dictionary) ?? Inflected(stem + "e", dictionary)) is { } found)
            {
                return [.. found, "ih", "ng"];
            }
        }
        if (word.EndsWith("ed", StringComparison.Ordinal) && word.Length > 3
            && (Inflected(word[..^2], dictionary) ?? Inflected(word[..^1], dictionary)) is { } past)
        {
            return [.. past, past[^1] is "p" or "k" or "f" or "s" or "sh" or "ch" or "th" ? "t" : "d"];
        }
        return null;
    }

    private static string[]? Inflected(string word, DsDictionary dictionary)
    {
        if (dictionary.Query(word) is { Length: > 0 } entry)
        {
            return entry;
        }
        if (dictionary.Query(word + "s") is { Length: > 1 } plural && plural[^1] is "s" or "z")
        {
            return plural[..^1];
        }
        if (word.EndsWith('s') && word.Length > 2 && dictionary.Query(word[..^1]) is { Length: > 0 } singular)
        {
            return [.. singular, singular[^1] is "p" or "t" or "k" or "f" or "th" ? "s" : "z"];
        }
        return null;
    }

    /// <summary>A word's phonemes over its syllables, one vowel each, the consonants between two vowels
    /// going to the later syllable as far as English can begin one. More syllables than vowels
    /// leaves the last ones empty; more vowels than syllables leaves the last syllable with the rest.</summary>
    internal static List<List<string>> Deal(List<string> phonemes, int syllables, Func<string, bool> isVowel)
    {
        var vowels = Enumerable.Range(0, phonemes.Count).Where(i => isVowel(phonemes[i])).ToList();
        var dealt = new List<List<string>>(syllables);
        int shared = Math.Min(syllables, vowels.Count);
        int start = 0;
        for (int i = 0; i + 1 < shared; i++)
        {
            int end = OnsetStart(phonemes, vowels[i], vowels[i + 1]);
            dealt.Add(phonemes.GetRange(start, end - start));
            start = end;
        }
        dealt.Add(phonemes.GetRange(start, phonemes.Count - start));
        while (dealt.Count < syllables)
        {
            dealt.Add([]);
        }
        return dealt;
    }

    private static int OnsetStart(List<string> phonemes, int vowel, int nextVowel)
    {
        int consonants = nextVowel - vowel - 1;
        if (consonants <= 1)
        {
            return vowel + 1;
        }
        return Onsets.TryGetValue(phonemes[nextVowel - 2], out var seconds) && seconds.Contains(phonemes[nextVowel - 1])
            ? nextVowel - 2
            : nextVowel - 1;
    }
}

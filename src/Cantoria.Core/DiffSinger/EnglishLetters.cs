// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Cantoria.DiffSinger;

/// <summary>
/// A best guess at an English word's phonemes from its letters, for a word the voice's dictionary
/// does not list: English spelling is not regular, so a guess can be wrong, and the render's log
/// names every word sung this way.
/// </summary>
public static class EnglishLetters
{
    /// <summary>Letters read together, the longest first.</summary>
    private static readonly (string Letters, string[] Phonemes)[] Groups =
    [
        ("tion", ["sh", "ax", "n"]), ("ough", ["ow"]), ("augh", ["ao"]),
        ("tch", ["ch"]), ("igh", ["ay"]),
        ("ch", ["ch"]), ("sh", ["sh"]), ("ph", ["f"]), ("th", ["th"]), ("wh", ["w"]), ("ck", ["k"]),
        ("qu", ["k", "w"]), ("ng", ["ng"]), ("nk", ["ng", "k"]),
        ("ee", ["iy"]), ("ea", ["iy"]), ("oo", ["uw"]), ("ai", ["ey"]), ("ay", ["ey"]), ("oa", ["ow"]),
        ("ou", ["aw"]), ("ow", ["aw"]), ("oi", ["oy"]), ("oy", ["oy"]), ("au", ["ao"]), ("aw", ["ao"]),
        ("ei", ["ey"]), ("ey", ["ey"]), ("ie", ["iy"]), ("ue", ["uw"]), ("ew", ["uw"]),
    ];

    private static readonly Dictionary<char, string[]> Single = new()
    {
        ['b'] = ["b"], ['d'] = ["d"], ['f'] = ["f"], ['h'] = ["hh"], ['j'] = ["jh"], ['k'] = ["k"], ['l'] = ["l"], ['m'] = ["m"],
        ['n'] = ["n"], ['p'] = ["p"], ['q'] = ["k"], ['r'] = ["r"], ['t'] = ["t"], ['v'] = ["v"], ['w'] = ["w"], ['x'] = ["k", "s"], ['z'] = ["z"],
        ['a'] = ["ae"], ['e'] = ["eh"], ['i'] = ["ih"], ['o'] = ["aa"], ['u'] = ["ah"],
    };

    private static readonly Dictionary<char, string> Long = new() { ['a'] = "ey", ['e'] = "iy", ['i'] = "ay", ['o'] = "ow", ['u'] = "uw" };

    private static readonly Dictionary<char, string[]> BeforeR = new()
    {
        ['a'] = ["aa", "r"], ['o'] = ["ao", "r"], ['e'] = ["er"], ['i'] = ["er"], ['u'] = ["er"],
    };

    private static bool IsVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u';

    /// <summary>The word's phonemes, or null when it is not spelled in plain letters or has no vowel to sing.</summary>
    public static string[]? Guess(string word)
    {
        string w = word.Replace("'", "");
        if (w.Length == 0 || !w.All(c => c is >= 'a' and <= 'z'))
        {
            return null;
        }
        int end = w.Length;
        bool vowelBefore(int at) => w.Take(at).Any(c => IsVowel(c) || c == 'y');
        bool endsLe = end > 3 && w.EndsWith("le", StringComparison.Ordinal) && !IsVowel(w[end - 3]) && vowelBefore(end - 3);
        bool silentE = !endsLe && end > 2 && w[end - 1] == 'e' && vowelBefore(end - 1);
        if (endsLe)
        {
            end -= 2;
        }
        else if (silentE)
        {
            end--;
        }

        var phonemes = new List<string>();
        for (int i = 0; i < end;)
        {
            if (i == 0 && end > 2 && (w.StartsWith("kn", StringComparison.Ordinal) || w.StartsWith("gn", StringComparison.Ordinal)))
            {
                phonemes.Add("n");
                i = 2;
                continue;
            }
            if (i == 0 && end > 2 && w.StartsWith("wr", StringComparison.Ordinal))
            {
                phonemes.Add("r");
                i = 2;
                continue;
            }
            if (i + 2 == end && w[i] == 'm' && w[i + 1] == 'b')
            {
                phonemes.Add("m");
                break;
            }
            if (Group(w, i, end) is { } group)
            {
                phonemes.AddRange(group.Phonemes);
                i += group.Letters.Length;
                continue;
            }
            char c = w[i];
            char next = i + 1 < end ? w[i + 1] : '\0';
            char after = i + 2 < end ? w[i + 2] : '\0';
            if (IsVowel(c) && next == 'r' && !IsVowel(after) && after != 'r')
            {
                phonemes.AddRange(BeforeR[c]);
                i += 2;
                continue;
            }
            if (IsVowel(c) && silentE && i + 2 == end && !IsVowel(next))
            {
                phonemes.Add(Long[c]);
                i++;
                continue;
            }
            switch (c)
            {
                case 'c':
                    phonemes.Add(next is 'e' or 'i' or 'y' || (silentE && i + 1 == end) ? "s" : "k");
                    break;
                case 'g':
                    phonemes.Add(next is 'e' or 'i' or 'y' || (silentE && i + 1 == end) ? "jh" : "g");
                    break;
                case 's':
                    phonemes.Add(i > 0 && i + 1 == end && next != 's' && !(phonemes[^1] is "p" or "t" or "k" or "f" or "th") ? "z" : "s");
                    break;
                case 'y':
                    if (i == 0)
                    {
                        phonemes.Add("y");
                    }
                    else if (i + 1 == w.Length)
                    {
                        phonemes.Add(vowelBefore(i) ? "iy" : "ay");
                    }
                    else
                    {
                        phonemes.Add("ih");
                    }
                    break;
                default:
                    phonemes.AddRange(Single[c]);
                    break;
            }
            i += !IsVowel(c) && c == next ? 2 : 1;     // a doubled consonant is one sound
        }
        if (endsLe)
        {
            phonemes.AddRange(["ax", "l"]);
        }
        return phonemes.Any(p => p is "aa" or "ae" or "ah" or "ao" or "aw" or "ax" or "ay" or "eh" or "er" or "ey" or "ih" or "iy" or "ow" or "oy" or "uw")
            ? [.. phonemes]
            : null;
    }

    private static (string Letters, string[] Phonemes)? Group(string w, int at, int end)
    {
        foreach (var group in Groups)
        {
            if (at + group.Letters.Length <= end && string.CompareOrdinal(w, at, group.Letters, 0, group.Letters.Length) == 0)
            {
                return group;
            }
        }
        return null;
    }
}

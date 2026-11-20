// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text;

namespace Cantoria.DiffSinger;

/// <summary>
/// Mandarin words to phonemes through a voice's dictionary, which spells toneless pinyin. A syllable
/// is written in Han characters or in pinyin (tone marks and tone numbers are read and dropped, ü may
/// be v). A character is read by the word it stands in, found across the notes (<see cref="Read"/>:
/// 长大 zhang, 长城 chang), and alone by its customary reading: the caller who wants another writes
/// the pinyin.
/// </summary>
public static class ChinesePhonemizer
{
    private static readonly Lazy<Dictionary<char, string>> Readings = new(LoadReadings);
    private static readonly Lazy<(Dictionary<string, string[]> Words, int Longest)> Words = new(LoadWords);

    /// <summary>The toneless reading of a Han character alone, or null when the table has none.</summary>
    public static string? Reading(char hanzi)
        => Words.Value.Words.TryGetValue(hanzi.ToString(), out var own) ? own[0] : Readings.Value.GetValueOrDefault(hanzi);

    /// <summary>
    /// Each Han character of the syllables, in order, read by the word it stands in: the characters
    /// that follow one another, across syllables, are cut into the longest words the table has, the
    /// first longest first; a character in no word takes its reading alone. Anything else - pinyin,
    /// punctuation, a syllable with no Han character - ends a run. One array a syllable, a reading
    /// (or null, for a character the table does not read) for each of its Han characters.
    /// </summary>
    public static IReadOnlyList<string?[]> Read(IReadOnlyList<string?> syllables)
    {
        var read = syllables.Select(s => new string?[(s ?? "").Count(IsHan)]).ToList();
        var run = new List<(int Syllable, int Han, char C)>();
        for (int s = 0; s < syllables.Count; s++)
        {
            var text = syllables[s] ?? "";
            if (!text.Any(IsHan))
            {
                Flush();
                continue;
            }
            int han = 0;
            foreach (char c in text)
            {
                if (IsHan(c))
                {
                    run.Add((s, han++, c));
                }
                else
                {
                    Flush();
                }
            }
        }
        Flush();
        return read;

        void Flush()
        {
            var (words, longest) = Words.Value;
            for (int i = 0; i < run.Count;)
            {
                int length = Math.Min(longest, run.Count - i);
                string[]? sung = null;
                for (; length >= 1; length--)
                {
                    var word = string.Concat(run.Skip(i).Take(length).Select(r => r.C));
                    if (words.TryGetValue(word, out sung))
                    {
                        break;
                    }
                }
                if (sung is null)
                {
                    (length, sung) = (1, [Readings.Value.GetValueOrDefault(run[i].C)!]);
                }
                for (int k = 0; k < length; k++)
                {
                    read[run[i + k].Syllable][run[i + k].Han] = sung[k];
                }
                i += length;
            }
            run.Clear();
        }
    }

    /// <summary>
    /// The phonemes of one syllable, each known to the model, its Han characters read as
    /// <paramref name="readings"/> says (<see cref="Read"/>; null: each alone). What has no entry
    /// is reported in <paramref name="unknown"/> and skipped; an empty result is the caller's to handle.
    /// </summary>
    public static List<string> Phonemes(string syllable, DsDictionary dictionary, Func<string, string?> known, List<string> unknown, string?[]? readings = null)
    {
        var phonemes = new List<string>();
        var text = Toneless(syllable);
        int han = 0;
        for (int i = 0; i < text.Length;)
        {
            if (IsHan(text[i]))
            {
                var reading = readings is not null && han < readings.Length ? readings[han] : Reading(text[i]);
                han++;
                Add(reading is not null ? dictionary.Spell(reading, known) : null, text[i].ToString());
                i++;
            }
            else if (char.IsAsciiLetterLower(text[i]))
            {
                int start = i;
                while (i < text.Length && char.IsAsciiLetterLower(text[i]))
                {
                    i++;
                }
                var pinyin = text[start..i];
                Add(dictionary.Spell(pinyin, known), pinyin);
            }
            else
            {
                i++;    // a tone number, an apostrophe, punctuation
            }
        }
        return phonemes;

        void Add(string[]? spelled, string shown)
        {
            if (spelled is null)
            {
                unknown.Add(shown);
            }
            else
            {
                phonemes.AddRange(spelled);
            }
        }
    }

    private static bool IsHan(char c) => c is >= '㐀' and <= '鿿';

    /// <summary>Lower case, no tone marks, ü as v. Han characters pass through.</summary>
    internal static string Toneless(string text)
    {
        var s = new StringBuilder();
        foreach (char c in text.Normalize(NormalizationForm.FormD).Replace("ü", "v"))    // ü, decomposed
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                s.Append(char.ToLowerInvariant(c));
            }
        }
        return s.ToString();
    }

    private static Dictionary<char, string> LoadReadings()
    {
        using var reader = Resource("pinyin.txt");
        var table = new Dictionary<char, string>();
        while (reader.ReadLine() is { } line)
        {
            int space = line.IndexOf(' ');
            if (line.StartsWith('#') || space <= 0)
            {
                continue;
            }
            string reading = line[..space];
            for (int i = space + 1; i < line.Length; i++)
            {
                table[line[i]] = reading;
            }
        }
        return table;
    }

    /// <summary>The words whose characters are read otherwise than alone (pinyin-phrases.txt, from
    /// tools/Generate-PinyinPhrases.ps1): "长城 chang cheng".</summary>
    private static (Dictionary<string, string[]>, int) LoadWords()
    {
        using var reader = Resource("pinyin-phrases.txt");
        var words = new Dictionary<string, string[]>(StringComparer.Ordinal);
        int longest = 1;
        while (reader.ReadLine() is { } line)
        {
            int space = line.IndexOf(' ');
            if (line.StartsWith('#') || space <= 0)
            {
                continue;
            }
            var word = line[..space];
            var sung = line[(space + 1)..].Split(' ');
            if (sung.Length == word.Length)
            {
                words[word] = sung;
                longest = Math.Max(longest, word.Length);
            }
        }
        return (words, longest);
    }

    private static StreamReader Resource(string name)
        => new(typeof(ChinesePhonemizer).Assembly.GetManifestResourceStream($"Cantoria.DiffSinger.{name}")
            ?? throw new InvalidOperationException($"{name} is not in the assembly"), Encoding.UTF8);
}

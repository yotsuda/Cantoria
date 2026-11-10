// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text;

namespace Cantoria.DiffSinger;

/// <summary>
/// Mandarin words to phonemes through a voice's dictionary, which spells toneless pinyin. A syllable
/// is written in Han characters or in pinyin (tone marks and tone numbers are read and dropped, ü may
/// be v). A character takes its customary reading, whatever the word around it: the caller who wants
/// another writes the pinyin.
/// </summary>
public static class ChinesePhonemizer
{
    private static readonly Lazy<Dictionary<char, string>> Readings = new(LoadReadings);

    /// <summary>The toneless reading of a Han character, or null when the table has none.</summary>
    public static string? Reading(char hanzi) => Readings.Value.GetValueOrDefault(hanzi);

    /// <summary>
    /// The phonemes of one syllable, each known to the model. What has no entry is reported in
    /// <paramref name="unknown"/> and skipped; an empty result is the caller's to handle.
    /// </summary>
    public static List<string> Phonemes(string syllable, DsDictionary dictionary, Func<string, string?> known, List<string> unknown)
    {
        var phonemes = new List<string>();
        var text = Toneless(syllable);
        for (int i = 0; i < text.Length;)
        {
            if (IsHan(text[i]))
            {
                var spelled = Reading(text[i]) is { } reading ? dictionary.Spell(reading, known) : null;
                Add(spelled, text[i].ToString());
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
        foreach (char c in text.Normalize(NormalizationForm.FormD).Replace("ü", "v"))
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
        using var stream = typeof(ChinesePhonemizer).Assembly.GetManifestResourceStream("Cantoria.DiffSinger.pinyin.txt")
            ?? throw new InvalidOperationException("the reading table is not in the assembly");
        using var reader = new StreamReader(stream, Encoding.UTF8);
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
}

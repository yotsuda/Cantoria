// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Cantoria.DiffSinger;

/// <summary>
/// Korean words to phonemes through a voice's dictionary, which spells each Hangul syllable (and
/// its romanisation). Each syllable is read by itself: a final consonant is not carried over to a
/// following syllable that begins with a vowel, so 밥이 sings 밥 and 이, not 바 and 비.
/// </summary>
public static class KoreanPhonemizer
{
    /// <summary>
    /// The phonemes of one note's text, each known to the model. What has no entry is reported in
    /// <paramref name="unknown"/> and skipped; an empty result is the caller's to handle.
    /// </summary>
    public static List<string> Phonemes(string text, DsDictionary dictionary, Func<string, string?> known, List<string> unknown)
    {
        var phonemes = new List<string>();
        for (int i = 0; i < text.Length;)
        {
            int start = i;
            if (text[i] is >= '가' and <= '힣')
            {
                i++;
            }
            else if (char.IsAsciiLetter(text[i]))
            {
                while (i < text.Length && char.IsAsciiLetter(text[i]))
                {
                    i++;
                }
            }
            else
            {
                i++;    // punctuation
                continue;
            }
            var grapheme = text[start..i];
            if (dictionary.Spell(grapheme, known) is { } spelled)
            {
                phonemes.AddRange(spelled);
            }
            else
            {
                unknown.Add(grapheme);
            }
        }
        return phonemes;
    }
}

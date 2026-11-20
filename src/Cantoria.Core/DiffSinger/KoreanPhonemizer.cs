// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Cantoria.DiffSinger;

/// <summary>
/// Korean words to phonemes through a voice's dictionary, which spells each Hangul syllable (and
/// its romanisation). Within a word, a final consonant before a syllable that begins with a vowel
/// is sung as that syllable's first (<see cref="Carry"/>: 밥이 sings 바 and 비).
/// </summary>
public static class KoreanPhonemizer
{
    private const char First = '가', Last = '힣';
    private const int Silent = 11;      // ㅇ as a syllable's first: none
    private const int I = 20;           // the vowel ㅣ

    // A final (its index, 1 to 27) carried on: what stays on its syllable, and the next one's first
    // (-1: dropped, as ㅎ is before a vowel). ㄳ ㄽ ㅄ: the ㅅ goes on tense (값이 갑씨). ㄶ ㅀ: the
    // ㅎ is not sung and the other goes on (많이 마니, 싫어 시러).
    private static readonly (int Stays, int Goes)[] Carried =
    [
        (0, Silent),
        (0, 0), (0, 1), (1, 10), (0, 2), (4, 12), (0, 2), (0, 3), (0, 5), (8, 0), (8, 6), (8, 7),
        (8, 10), (8, 16), (8, 17), (0, 5), (0, 6), (0, 7), (17, 10), (0, 9), (0, 10), (21, Silent),
        (0, 12), (0, 14), (0, 15), (0, 16), (0, 17), (0, -1),
    ];

    /// <summary>
    /// The notes' Hangul as sung: where a syllable with a final consonant is followed in the same
    /// word by one that begins with a vowel - in one note's text, or in the next note's after a
    /// hyphen - the final is carried on (밥이 바비, 닭이 달기, 값이 갑씨); ㄷ and ㅌ so carried
    /// before ㅣ are ㅈ and ㅊ (같이 가치); a final ㅎ is not sung before a vowel (좋아 조아, 많이
    /// 마니); ㅇ stays. A pair is changed only where <paramref name="spelled"/> knows both
    /// syllables it becomes. Romanised text is left as written.
    /// </summary>
    /// <param name="syllables">Each note's text, and whether the next note's is of the same word.</param>
    /// <param name="spelled">Whether the voice's dictionary spells a syllable.</param>
    public static List<string> Carry(IReadOnlyList<(string Text, bool Hyphen)> syllables, Func<string, bool> spelled)
    {
        var sung = syllables.Select(s => s.Text.ToCharArray()).ToList();
        var none = (Note: -1, At: -1);
        var before = none;
        for (int n = 0; n < sung.Count; n++)
        {
            var text = sung[n];
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] is < First or > Last)
                {
                    before = none;
                    continue;
                }
                if (before is var (bn, bi) && bn >= 0)
                {
                    var (carried, next) = CarryOn(sung[bn][bi], text[i]);
                    if (carried != sung[bn][bi] && spelled(carried.ToString()) && spelled(next.ToString()))
                    {
                        (sung[bn][bi], text[i]) = (carried, next);
                    }
                }
                before = (n, i);
            }
            if (!syllables[n].Hyphen)
            {
                before = none;
            }
        }
        return sung.Select(s => new string(s)).ToList();
    }

    private static (char Before, char Next) CarryOn(char before, char next)
    {
        int b = before - First, x = next - First;
        int final = b % 28, first = x / 28 / 21, vowel = x / 28 % 21;
        if (final == 0 || first != Silent || Carried[final].Goes == Silent)
        {
            return (before, next);
        }
        var (stays, goes) = Carried[final];
        if (vowel == I)
        {
            goes = goes switch { 3 => 12, 16 => 14, _ => goes };    // ㄷ ㅌ before ㅣ: ㅈ ㅊ
        }
        return ((char)(before - final + stays), goes < 0 ? next : (char)(First + (goes * 21 + vowel) * 28 + x % 28));
    }

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
            if (text[i] is >= First and <= Last)
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

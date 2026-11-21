// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Cantoria.DiffSinger;

/// <summary>
/// Korean words to phonemes through a voice's dictionary, which spells each Hangul syllable (and
/// its romanisation). A final consonant before a syllable that begins with a vowel, in one note or
/// in the next note sung on from it, is sung as that syllable's first (<see cref="Carry"/>: 밥이
/// sings 바 and 비); before a consonant it changes as the standard pronunciation says (밥만 밤만).
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

    // A final as it is said at a word's end, as the first of the next syllable: ㄱ ㄴ ㄷ ㄹ ㅁ ㅂ
    // (a pair gives the one of its two that is said: 닭 앞 다갑, 넋 없이 너겁씨).
    private static readonly int[] AtAWordsEnd =
        [Silent, 0, 0, 0, 2, 2, 2, 3, 5, 0, 6, 5, 5, 5, 7, 5, 6, 7, 7, 3, 3, Silent, 3, 3, 0, 3, 7, 3];

    // The syllables that begin a particle or an ending after a final (밥이, 밥을, 밥은, 밥에, 밥의,
    // 밥으로, 먹어, 잡아, 먹었다, 잡았다, 웃음, 학생인데, 학생일까, 학생입니다, 학생임): the final
    // goes on to them as it is. Any other syllable is taken to begin a word. The notes do not say
    // which a syllable is, so a word that begins as an ending does (이름, 아래, 어깨) is read as one.
    private static readonly HashSet<char> Endings = [.. "이을은에의으어아었았음인일입임엔"];

    /// <summary>
    /// True when the singer goes from <paramref name="before"/> (a word's last note, its melisma
    /// included) to <paramref name="next"/> with nothing between: no rest, no breath. A final is
    /// carried across such notes whether a hyphen joins them or not: Korean set a syllable to a
    /// note seldom marks its words (Lily# parts them with spaces, as Korean scores print them), and
    /// in a sung line a final goes on to the vowel of the word after too.
    /// </summary>
    public static bool SungOn(SungNote before, SungNote next)
        => !before.Breath && !before.Inhale && next.Start - before.End < SungScore.RoundingOverlap;

    /// <summary>True when <see cref="Carry"/> would carry the last syllable of
    /// <paramref name="before"/> on to the first of <paramref name="next"/> (the dictionary aside).</summary>
    public static bool CarriesAcross(string? before, string? next)
        => before is [.., var b and >= First and <= Last] && next is [var x and >= First and <= Last, ..]
            && CarryOn(b, x) != (b, x);

    /// <summary>
    /// The notes' Hangul as sung: where a syllable with a final consonant is followed by one that
    /// begins with a vowel - in one note's text, or in the next note's when that is sung on from it
    /// (a hyphen, or <see cref="SungOn"/>) - the final is carried on (밥이 바비, 닭이 달기, 값이
    /// 갑씨); ㄷ and ㅌ so carried before ㅣ are ㅈ and ㅊ (같이 가치); a final ㅎ is not sung before
    /// a vowel (좋아 조아, 많이 마니); ㅇ stays. Before a syllable that is not a particle's or an
    /// ending's (<see cref="Endings"/>) the final goes on as said at a word's end (꽃 위 꼬뒤), and
    /// not at all before ㅣ or a y- vowel (꽃잎이 꽃이피). A pair is changed only where
    /// <paramref name="spelled"/> knows both syllables it becomes. Romanised text is left as written.
    /// </summary>
    /// <param name="syllables">Each note's text, and whether the next note's is sung on from it.</param>
    /// <param name="spelled">Whether the voice's dictionary spells a syllable.</param>
    public static List<string> Carry(IReadOnlyList<(string Text, bool SungOn)> syllables, Func<string, bool> spelled)
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
                    if ((carried, next) != (sung[bn][bi], text[i]) && spelled(carried.ToString()) && spelled(next.ToString()))
                    {
                        (sung[bn][bi], text[i]) = (carried, next);
                    }
                }
                before = (n, i);
            }
            if (!syllables[n].SungOn)
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
        if (final != 0 && first != Silent)
        {
            return Meet(before, next, final, first, vowel);
        }
        if (final == 0 || Carried[final].Goes == Silent)
        {
            return (before, next);
        }
        var (stays, goes) = Carried[final];
        if (!Endings.Contains(next))
        {
            // A word's own first syllable: the final goes on as it is said at a word's end (꽃 위
            // 꼬뒤, 옷 안 오단); before ㅣ and the y- vowels it is left (꽃잎 꼰닙 takes an ㄴ some
            // compounds have and others do not, 금요일 그묘일).
            if (vowel is I or 2 or 3 or 6 or 7 or 12 or 17)
            {
                return (before, next);
            }
            (stays, goes) = (0, AtAWordsEnd[final]);
        }
        else if (vowel == I)
        {
            goes = goes switch { 3 => 12, 16 => 14, _ => goes };    // ㄷ ㅌ before ㅣ: ㅈ ㅊ
        }
        return ((char)(before - final + stays), goes < 0 ? next : (char)(First + (goes * 21 + vowel) * 28 + x % 28));
    }

    // Finals by how they sound at a word's end: ㄱ (ㄱ ㄲ ㄳ ㄺ ㅋ), ㄷ (ㄷ ㅅ ㅆ ㅈ ㅊ ㅌ), ㅂ (ㅂ ㅄ ㄿ ㅍ).
    private static bool IsK(int f) => f is 1 or 2 or 3 or 9 or 24;
    private static bool IsT(int f) => f is 7 or 19 or 20 or 22 or 23 or 25;
    private static bool IsP(int f) => f is 14 or 17 or 18 or 26;

    /// <summary>
    /// A final meeting the consonant that begins the next syllable (the standard pronunciation's
    /// rules 12, 17 to 20 and 23): ㅎ with ㄱ ㄷ ㅈ is ㅋ ㅌ ㅊ either way round (좋다 조타, 축하 추카,
    /// 못하다 모타다, 굳히다 구치다); ㄱ ㄷ ㅂ before ㄴ ㅁ are ㅇ ㄴ ㅁ (국물 궁물, 밥만 밤만, 꽃말
    /// 꼰말, 없는 엄는); ㄹ after ㅁ ㅇ is ㄴ, and after ㄱ ㅂ too, which become ㅇ ㅁ (종로 종노,
    /// 독립 동닙, 협력 혐녁); ㄴ and ㄹ meeting are ㄹ ㄹ (신라 실라, 설날 설랄); ㄱ ㄷ ㅂ ㅅ ㅈ after
    /// ㄱ ㄷ ㅂ are tense (학교 학꾜, 국밥 국빱, 옷장 옷짱). The final is otherwise left as written.
    /// </summary>
    private static (char Before, char Next) Meet(char before, char next, int final, int first, int vowel)
    {
        int f = final, o = first;
        switch (final)
        {
            case 27 or 6 or 15:     // ㅎ ㄶ ㅀ
                int rest = final == 27 ? 0 : final == 6 ? 4 : 8;
                if (first is 0 or 3 or 12)
                {
                    (f, o) = (rest, first switch { 0 => 15, 3 => 16, _ => 14 });
                }
                else if (first == 9)
                {
                    (f, o) = (rest, 10);
                }
                else if (first == 2)
                {
                    (f, o) = final == 15 ? (8, 5) : (4, 2);     // 놓는 논는, 않는 안는, 싫네 실레
                }
                break;
            case var _ when first == 18 && (IsK(final) || IsT(final) || IsP(final) || final is 5 or 11):
                (f, o) = final switch
                {
                    9 => (8, 15),                               // 밝히다 발키다
                    11 => (8, 17),                              // 넓히다 널피다
                    5 => (4, 14),                               // 앉히다 안치다
                    _ when IsK(final) => (0, 15),
                    _ when IsP(final) => (0, 17),
                    7 or 25 when vowel == I => (0, 14),         // 굳히다 구치다
                    _ => (0, 16),
                };
                break;
            case var _ when first is 2 or 6 && (IsK(final) || IsT(final) || IsP(final)):
                f = IsK(final) ? 21 : IsT(final) ? 4 : 16;
                break;
            case var _ when first == 5:
                if (final is 16 or 21)
                {
                    o = 2;
                }
                else if (IsK(final) || IsT(final) || IsP(final))
                {
                    (f, o) = (IsK(final) ? 21 : IsT(final) ? 4 : 16, 2);
                }
                else if (final == 4)
                {
                    f = 8;
                }
                break;
            case 8 or 13 when first == 2:   // ㄹ ㄾ before ㄴ
                o = 5;
                break;
            case var _ when first is 0 or 3 or 7 or 9 or 12 && (IsK(final) || IsT(final) || IsP(final)):
                o = first switch { 0 => 1, 3 => 4, 7 => 8, 9 => 10, _ => 13 };
                break;
        }
        int x = next - First;
        return ((char)(before - final + f), (char)(First + (o * 21 + vowel) * 28 + x % 28));
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

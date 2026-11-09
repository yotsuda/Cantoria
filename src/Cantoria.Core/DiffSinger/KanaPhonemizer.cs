// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Cantoria.DiffSinger;

/// <summary>
/// Japanese syllables to a voice's phonemes: a syllable is one or several kana morae
/// (<c>こん</c> = こ + ん), each looked up as the kana itself (TIGER's dictionaries) or as its
/// romaji spellings (other voices); a long-vowel mark repeats the vowel before it. The symbols
/// the dictionary returns are checked against the model's phoneme list, with the language
/// prefix (<c>ja/</c>) tried where a voice uses one.
/// </summary>
public static class KanaPhonemizer
{
    private static readonly Dictionary<string, string[]> Romaji = new()
    {
        ["あ"] = ["a"], ["い"] = ["i"], ["う"] = ["u"], ["え"] = ["e"], ["お"] = ["o"],
        ["か"] = ["ka"], ["き"] = ["ki"], ["く"] = ["ku"], ["け"] = ["ke"], ["こ"] = ["ko"],
        ["さ"] = ["sa"], ["し"] = ["shi", "si"], ["す"] = ["su"], ["せ"] = ["se"], ["そ"] = ["so"],
        ["た"] = ["ta"], ["ち"] = ["chi", "ti"], ["つ"] = ["tsu", "tu"], ["て"] = ["te"], ["と"] = ["to"],
        ["な"] = ["na"], ["に"] = ["ni"], ["ぬ"] = ["nu"], ["ね"] = ["ne"], ["の"] = ["no"],
        ["は"] = ["ha"], ["ひ"] = ["hi"], ["ふ"] = ["fu", "hu"], ["へ"] = ["he"], ["ほ"] = ["ho"],
        ["ま"] = ["ma"], ["み"] = ["mi"], ["む"] = ["mu"], ["め"] = ["me"], ["も"] = ["mo"],
        ["や"] = ["ya"], ["ゆ"] = ["yu"], ["よ"] = ["yo"],
        ["ら"] = ["ra"], ["り"] = ["ri"], ["る"] = ["ru"], ["れ"] = ["re"], ["ろ"] = ["ro"],
        ["わ"] = ["wa"], ["を"] = ["o", "wo"], ["ん"] = ["N", "n"],
        ["が"] = ["ga"], ["ぎ"] = ["gi"], ["ぐ"] = ["gu"], ["げ"] = ["ge"], ["ご"] = ["go"],
        ["ざ"] = ["za"], ["じ"] = ["ji", "zi"], ["ず"] = ["zu"], ["ぜ"] = ["ze"], ["ぞ"] = ["zo"],
        ["だ"] = ["da"], ["ぢ"] = ["ji", "di"], ["づ"] = ["zu", "du"], ["で"] = ["de"], ["ど"] = ["do"],
        ["ば"] = ["ba"], ["び"] = ["bi"], ["ぶ"] = ["bu"], ["べ"] = ["be"], ["ぼ"] = ["bo"],
        ["ぱ"] = ["pa"], ["ぴ"] = ["pi"], ["ぷ"] = ["pu"], ["ぺ"] = ["pe"], ["ぽ"] = ["po"],
        ["きゃ"] = ["kya"], ["きゅ"] = ["kyu"], ["きょ"] = ["kyo"], ["しゃ"] = ["sha", "sya"], ["しゅ"] = ["shu", "syu"], ["しょ"] = ["sho", "syo"],
        ["ちゃ"] = ["cha", "tya"], ["ちゅ"] = ["chu", "tyu"], ["ちょ"] = ["cho", "tyo"], ["にゃ"] = ["nya"], ["にゅ"] = ["nyu"], ["にょ"] = ["nyo"],
        ["ひゃ"] = ["hya"], ["ひゅ"] = ["hyu"], ["ひょ"] = ["hyo"], ["みゃ"] = ["mya"], ["みゅ"] = ["myu"], ["みょ"] = ["myo"],
        ["りゃ"] = ["rya"], ["りゅ"] = ["ryu"], ["りょ"] = ["ryo"], ["ぎゃ"] = ["gya"], ["ぎゅ"] = ["gyu"], ["ぎょ"] = ["gyo"],
        ["じゃ"] = ["ja", "zya"], ["じゅ"] = ["ju", "zyu"], ["じょ"] = ["jo", "zyo"], ["びゃ"] = ["bya"], ["びゅ"] = ["byu"], ["びょ"] = ["byo"],
        ["ぴゃ"] = ["pya"], ["ぴゅ"] = ["pyu"], ["ぴょ"] = ["pyo"],
        ["ふぁ"] = ["fa"], ["ふぃ"] = ["fi"], ["ふぇ"] = ["fe"], ["ふぉ"] = ["fo"], ["てぃ"] = ["ti"], ["でぃ"] = ["di"],
        ["うぃ"] = ["wi"], ["うぇ"] = ["we"], ["うぉ"] = ["wo"], ["ゔ"] = ["vu"],
    };

    private const string SmallKana = "ゃゅょぁぃぅぇぉ";

    /// <summary>Katakana as hiragana (the dictionaries spell hiragana); other characters as they are.</summary>
    public static string ToHiragana(string s) => string.Concat(s.Select(c => c is >= 'ァ' and <= 'ヶ' ? (char)(c - 0x60) : c));

    /// <summary>The morae of a syllable: a kana with a small ゃゅょぁぃぅぇぉ after it is one mora.</summary>
    public static IReadOnlyList<string> Morae(string syllable)
    {
        var s = ToHiragana(syllable.Trim());
        var morae = new List<string>();
        for (int i = 0; i < s.Length; i++)
        {
            if (i + 1 < s.Length && SmallKana.Contains(s[i + 1]))
            {
                morae.Add(s.Substring(i, 2));
                i++;
            }
            else
            {
                morae.Add(s[i].ToString());
            }
        }
        return morae;
    }

    /// <summary>The graphemes to try for a mora, in order: the kana itself, then its romaji spellings. (A long-vowel mark has none: it repeats the vowel before it.)</summary>
    public static IReadOnlyList<string> Graphemes(string mora) => mora switch
    {
        "ー" => [],
        "っ" => ["っ", "cl", "q"],
        _ => [mora, .. Romaji.GetValueOrDefault(mora, [])],
    };

    /// <summary>
    /// The phonemes of one syllable, each known to the model (<paramref name="known"/> answers the
    /// symbol as the model spells it, or null). Morae with no entry are reported in
    /// <paramref name="unknown"/> and skipped; an empty result is the caller's to handle.
    /// </summary>
    public static List<string> Phonemes(string syllable, DsDictionary dictionary, Func<string, string?> known, List<string> unknown)
    {
        var phonemes = new List<string>();
        string? lastVowel = null;
        foreach (var mora in Morae(syllable))
        {
            if (mora == "ー")
            {
                if (lastVowel is null)
                {
                    unknown.Add(mora);
                }
                else
                {
                    phonemes.Add(lastVowel);
                }
                continue;
            }
            string[]? found = null;
            foreach (var g in Graphemes(mora))
            {
                var raw = dictionary.Query(g);
                if (raw is null || raw.Length == 0)
                {
                    continue;
                }
                var mapped = raw.Select(known).ToArray();
                if (mapped.All(m => m is not null))
                {
                    found = mapped!;
                    break;
                }
            }
            if (found is null)
            {
                unknown.Add(mora);
                continue;
            }
            phonemes.AddRange(found);
            lastVowel = found.LastOrDefault(dictionary.IsVowel) ?? lastVowel;
        }
        return phonemes;
    }
}

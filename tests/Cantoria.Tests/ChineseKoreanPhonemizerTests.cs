// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Cantoria.DiffSinger;
using Xunit;

namespace Cantoria.Tests;

/// <summary>Chinese and Korean lyrics through a voice's dictionary, without a voice or a model.</summary>
public class ChineseKoreanPhonemizerTests
{
    private const string Zh = """
        symbols:
        - {symbol: zh/d, type: stop}
        - {symbol: zh/uo, type: vowel}
        - {symbol: zh/l, type: liquid}
        - {symbol: zh/v, type: vowel}
        entries:
        - {grapheme: duo, phonemes: [zh/d, zh/uo]}
        - {grapheme: lv, phonemes: [zh/l, zh/v]}
        """;

    private const string Ko = """
        symbols:
        - {symbol: ko/d, type: stop}
        - {symbol: ko/o, type: vowel}
        entries:
        - {grapheme: "도", phonemes: [ko/d, ko/o]}
        - {grapheme: "do", phonemes: [ko/d, ko/o]}
        """;

    private static string? AnyKnown(string symbol) => symbol;

    [Theory]
    [InlineData("哆", "duo")]
    [InlineData("来", "lai")]
    [InlineData("咪", "mi")]
    [InlineData("发", "fa")]
    [InlineData("唆", "suo")]
    [InlineData("拉", "la")]
    [InlineData("西", "xi")]
    [InlineData("绿", "lv")]
    public void Reading_IsTonelessPinyin_WithVForU(string hanzi, string reading)
        => Assert.Equal(reading, ChinesePhonemizer.Reading(hanzi[0]));

    [Theory]
    [InlineData("哆")]
    [InlineData("duo")]
    [InlineData("duō")]
    [InlineData("duo1")]
    [InlineData("DUO")]
    public void Chinese_ReadsHanAndPinyinAlike(string syllable)
    {
        var unknown = new List<string>();
        Assert.Equal(["zh/d", "zh/uo"], ChinesePhonemizer.Phonemes(syllable, DsDictionary.Parse(Zh), AnyKnown, unknown));
        Assert.Empty(unknown);
    }

    [Theory]
    [InlineData("lü")]
    [InlineData("lǜ")]
    [InlineData("lv4")]
    [InlineData("绿")]
    public void Chinese_ReadsUmlautAsV(string syllable)
        => Assert.Equal(["zh/l", "zh/v"], ChinesePhonemizer.Phonemes(syllable, DsDictionary.Parse(Zh), AnyKnown, []));

    [Fact]
    public void Chinese_SeveralCharactersOnANote_AreSungInOrder_AndAnUnknownOneIsReported()
    {
        var unknown = new List<string>();
        Assert.Equal(["zh/d", "zh/uo", "zh/l", "zh/v"], ChinesePhonemizer.Phonemes("哆绿", DsDictionary.Parse(Zh), AnyKnown, unknown));
        Assert.Empty(unknown);
        Assert.Equal(["zh/d", "zh/uo"], ChinesePhonemizer.Phonemes("哆来", DsDictionary.Parse(Zh), AnyKnown, unknown));
        Assert.Equal(["来"], unknown);
    }

    [Fact]
    public void Chinese_ASymbolTheModelLacksIsUnknown()
    {
        var unknown = new List<string>();
        Assert.Empty(ChinesePhonemizer.Phonemes("哆", DsDictionary.Parse(Zh), _ => null, unknown));
        Assert.Equal(["哆"], unknown);
    }

    [Theory]
    [InlineData("도")]
    [InlineData("do")]
    [InlineData("도!")]
    public void Korean_ReadsHangulAndRomaji(string syllable)
    {
        var unknown = new List<string>();
        Assert.Equal(["ko/d", "ko/o"], KoreanPhonemizer.Phonemes(syllable, DsDictionary.Parse(Ko), AnyKnown, unknown));
        Assert.Empty(unknown);
    }

    [Fact]
    public void Korean_AnUnknownSyllableIsReported()
    {
        var unknown = new List<string>();
        Assert.Equal(["ko/d", "ko/o"], KoreanPhonemizer.Phonemes("도레", DsDictionary.Parse(Ko), AnyKnown, unknown));
        Assert.Equal(["레"], unknown);
    }
}

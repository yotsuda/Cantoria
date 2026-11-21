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

    // A character is read by the word it stands in, across notes; alone by its customary reading,
    // or by the table's own where Unihan's first is one almost no word has (佛 fo, 长 chang).
    [Theory]
    [InlineData(new[] { "长", "大" }, "zhang da")]
    [InlineData(new[] { "长", "城" }, "chang cheng")]
    [InlineData(new[] { "长" }, "chang")]
    [InlineData(new[] { "银", "行" }, "yin hang")]
    [InlineData(new[] { "行", "人" }, "xing ren")]
    [InlineData(new[] { "音", "乐" }, "yin yue")]
    [InlineData(new[] { "快", "乐" }, "kuai le")]
    [InlineData(new[] { "睡", "觉" }, "shui jiao")]
    [InlineData(new[] { "觉", "得" }, "jue de")]
    [InlineData(new[] { "重", "新" }, "chong xin")]
    [InlineData(new[] { "了", "解" }, "liao jie")]
    [InlineData(new[] { "大", "地" }, "da di")]
    [InlineData(new[] { "慢", "慢", "地" }, "man man de")]
    [InlineData(new[] { "佛" }, "fo")]
    [InlineData(new[] { "落", "下" }, "luo xia")]
    [InlineData(new[] { "长城" }, "chang cheng")]
    public void Chinese_ACharacterIsReadByItsWord(string[] notes, string read)
        => Assert.Equal(read, string.Join(" ", ChinesePhonemizer.Read(notes).SelectMany(r => r)));

    [Fact]
    public void Chinese_AWordDoesNotRunThroughPinyinOrPunctuation()
    {
        // 银 then "hang" written out, then 行 alone: xing, not the 银行 the two Han characters would be.
        Assert.Equal("yin|xing", string.Join("|", ChinesePhonemizer.Read(["银", "hang", "行"]).SelectMany(r => r)));
        Assert.Equal("yin|xing", string.Join("|", ChinesePhonemizer.Read(["银，", "行"]).SelectMany(r => r)));
        Assert.Equal("yin|xing", string.Join("|", ChinesePhonemizer.Read(["银", null, "行"]).SelectMany(r => r)));
    }

    [Fact]
    public void Chinese_TheReadingOfItsWordIsSpelled()
    {
        var zh = DsDictionary.Parse("""
            entries:
            - {grapheme: chang, phonemes: [ch, ang]}
            - {grapheme: zhang, phonemes: [zh, ang]}
            """);
        var read = ChinesePhonemizer.Read(["长", "城"]);
        Assert.Equal(["ch", "ang"], ChinesePhonemizer.Phonemes("长", zh, AnyKnown, [], read[0]));
        Assert.Equal(["zh", "ang"], ChinesePhonemizer.Phonemes("长", zh, AnyKnown, [], ["zhang"]));
    }

    // Within a word a final consonant goes on to a syllable that begins with a vowel.
    [Theory]
    [InlineData("밥이", false, "", "바비")]
    [InlineData("밥", true, "이", "바|비")]
    [InlineData("밥", false, "이", "밥|이")]              // another word: as written
    [InlineData("닭이", false, "", "달기")]
    [InlineData("값이", false, "", "갑씨")]
    [InlineData("같이", false, "", "가치")]
    [InlineData("굳이", false, "", "구지")]
    [InlineData("좋아", false, "", "조아")]
    [InlineData("많이", false, "", "마니")]
    [InlineData("사랑이", false, "", "사랑이")]            // ㅇ stays
    [InlineData("음악", false, "", "으막")]
    [InlineData("밥도", false, "", "밥도")]               // not before a vowel
    [InlineData("밥,이", false, "", "밥,이")]
    // Before a word's own first syllable the final goes on as said at a word's end, and before ㅣ
    // or a y- vowel not at all (꽃잎 꼰닙 takes an ㄴ that 금요일 그묘일 does not).
    [InlineData("꽃", true, "위에", "꼬|뒤에")]
    [InlineData("옷안", false, "", "오단")]
    [InlineData("맛없다", false, "", "마덦다")]
    [InlineData("닭앞", false, "", "다갚")]
    [InlineData("넋없이", false, "", "너겁씨")]
    [InlineData("꽃", true, "잎이", "꽃|이피")]
    [InlineData("금요일", false, "", "금요일")]
    public void Korean_AFinalIsCarriedOnWithinAWord(string first, bool hyphen, string second, string sung)
    {
        var notes = second == "" ? new[] { (first, hyphen) } : new[] { (first, hyphen), (second, false) };
        Assert.Equal(sung, string.Join("|", KoreanPhonemizer.Carry(notes, _ => true)));
    }

    [Fact]
    public void Korean_ACarryTheDictionaryCannotSpellIsNotMade()
        => Assert.Equal(["밥", "이"], KoreanPhonemizer.Carry([("밥", true), ("이", false)], s => s != "비"));

    // A final goes on to the next note when the singer goes on to it with nothing between.
    [Theory]
    [InlineData(1.0, false, false, true)]
    [InlineData(1.0005, false, false, true)]     // a caller's rounding
    [InlineData(1.2, false, false, false)]       // a rest
    [InlineData(1.0, true, false, false)]        // a breath
    [InlineData(1.0, false, true, false)]        // a breath taken into the next note
    public void Korean_ANoteIsSungOn_WithNoRestOrBreathBetween(double next, bool breath, bool inhale, bool on)
        => Assert.Equal(on, KoreanPhonemizer.SungOn(new SungNote(0.5, 0.5, 60, "밥", Breath: breath, Inhale: inhale), new SungNote(next, 0.5, 60, "이")));

    [Theory]
    [InlineData("밥", "이", true)]
    [InlineData("좋", "아", true)]              // the ㅎ goes
    [InlineData("꽃", "위", true)]              // 꼬뒤
    [InlineData("꽃", "잎", false)]
    [InlineData("사랑", "이", false)]           // ㅇ stays
    [InlineData("밥", "도", false)]
    [InlineData("바", "이", false)]
    [InlineData("밥", null, false)]
    [InlineData("bap", "이", false)]
    public void Korean_CarriesAcross_SaysWhetherAFinalGoesOn(string? before, string? next, bool carries)
        => Assert.Equal(carries, KoreanPhonemizer.CarriesAcross(before, next));

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

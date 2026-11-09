using Cantilena.Neutrino;
using Xunit;

namespace Cantilena.Tests;

public class NeutrinoKanaTests
{
    private static SungScore Score(params string?[] lyrics)
        => new(lyrics.Select((l, i) => new SungNote(i, 1, 60, l, Melisma: l is null)).ToList());

    [Fact]
    public void Kana_IsSung() => Assert.Null(NeutrinoMusicXml.NotKanaProblem(Score("か", "ガ", "きゃ", "ん", "っ", "ー", null)));

    [Fact]
    public void Punctuation_IsLeftToTheEngine() => Assert.Null(NeutrinoMusicXml.NotKanaProblem(Score("さ、", "く。")));

    [Fact]
    public void Other_Letters_AreNamed_Once_Each()
        => Assert.Equal("NEUTRINO sings kana, and 3 syllable(s) are not: fro, g, frog",
            NeutrinoMusicXml.NotKanaProblem(Score("fro", "g", "frog", "frog", "か")));

    [Fact]
    public void Kanji_IsNotKana() => Assert.Contains("漢", NeutrinoMusicXml.NotKanaProblem(Score("漢"))!);

    [Fact]
    public void ALongList_IsCut()
        => Assert.EndsWith("a, b, c, d, e, f, g, h, …", NeutrinoMusicXml.NotKanaProblem(Score("a", "b", "c", "d", "e", "f", "g", "h", "i")));
}

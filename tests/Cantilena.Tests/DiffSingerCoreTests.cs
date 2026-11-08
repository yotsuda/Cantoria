// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Cantilena.DiffSinger;
using Xunit;

namespace Cantilena.Tests;

/// <summary>The DiffSinger backend's arithmetic and reading, without a voice or a model.</summary>
public class DiffSingerCoreTests
{
    [Fact]
    public void Frames_RoundCumulatively_SoTheSumIsTheTotal()
    {
        // 11.61 ms frames: 100 ms is 8.61 frames; three of them are 25.84 frames, 26 in all.
        double frame = 1000.0 * 512 / 44100;
        var frames = DsMath.Frames([100, 100, 100], frame);
        Assert.Equal(3, frames.Length);
        Assert.Equal((int)Math.Round(300 / frame + 0.5, MidpointRounding.ToEven), frames.Sum());
        // A sequence of tiny durations never rounds each up: ten 1 ms items are 1 frame, not 10.
        Assert.Equal(1, DsMath.Frames(Enumerable.Repeat(1.0, 10), frame).Sum());
        Assert.Throws<InvalidDataException>(() => DsMath.Frames([-1], frame));
    }

    [Fact]
    public void FitSum_MovesTheDifferenceOntoTheTail()
    {
        Assert.Equal([3, 4, 6], DsMath.FitSum([3, 4, 5], 13));
        Assert.Equal([3, 2, 0], DsMath.FitSum([3, 4, 5], 5));
        Assert.Throws<InvalidDataException>(() => DsMath.FitSum([3, 4, 5], -1));
    }

    [Fact]
    public void Stretch_LaysTheItemsOutToEndAtThePoint()
    {
        // Three items of 10, 20, 30 scaled by 0.5 end at 100: they begin at 70, 75, 85.
        Assert.Equal([70, 75, 85], DsMath.Stretch([10, 20, 30], 0.5, 100));
    }

    [Fact]
    public void Speedup_DividesTheThousandSteps()
    {
        Assert.Equal(50, DsMath.Speedup(20));
        Assert.Equal(100, DsMath.Speedup(10));
        Assert.Equal(1, DsMath.Speedup(1000));
        Assert.Equal(1, DsMath.Speedup(5000));
        Assert.Equal(8, DsMath.Speedup(120));   // 1000/120 = 8, and 8 divides 1000
    }

    [Fact]
    public void WordDivision_RunsFromVowelToVowel_TheTailItsOwnWord()
    {
        // SP | k a | kx oo | SP (the silences are not sung vowels): the words are [SP k] [a kx] [oo] [SP] —
        // a word runs from a vowel to the one before the next vowel, the lead is the first word's.
        var vowels = new[] { false, false, true, false, true, false };
        var frames = new[] { 8, 3, 20, 4, 25, 8 };
        var (div, dur) = DsMath.WordDivAndDur(vowels, frames);
        Assert.Equal([2, 2, 1, 1], div);
        Assert.Equal([11, 24, 25, 8], dur);
        // No vowel at all: the last phoneme stands for one.
        var (div2, _) = DsMath.WordDivAndDur([false, false, false, false], [8, 3, 4, 8]);
        Assert.Equal([2, 1, 1], div2);
    }

    [Fact]
    public void Resample_KeepsTheEnds()
    {
        Assert.Equal([0, 5, 10], DsMath.Resample([0, 10], 3));
        Assert.Equal([1, 2, 3], DsMath.Resample([1, 2, 3], 3));
    }

    [Fact]
    public void Kana_SplitsIntoMorae_AndTriesTheKanaBeforeItsRomaji()
    {
        Assert.Equal(["こ", "ん"], KanaPhonemizer.Morae("こん"));
        Assert.Equal(["きょ", "う"], KanaPhonemizer.Morae("きょう"));
        Assert.Equal(["ゆ"], KanaPhonemizer.Morae("ユ"));
        Assert.Equal(["し", "shi", "si"], KanaPhonemizer.Graphemes("し"));
        Assert.Equal(["っ", "cl", "q"], KanaPhonemizer.Graphemes("っ"));
        Assert.Empty(KanaPhonemizer.Graphemes("ー"));
    }

    private const string Dict = """
        symbols:
        - {symbol: SP, type: vowel}
        - {symbol: a, type: vowel}
        - {symbol: o, type: vowel}
        - {symbol: N, type: vowel}
        - {symbol: k, type: stop}
        - {symbol: y, type: semivowel}
        entries:
        - {grapheme: "か", phonemes: [k, a]}
        - {grapheme: "ん", phonemes: [N]}
        - {grapheme: yo, phonemes: [y, o]}
        """;

    [Fact]
    public void Dictionary_ReadsSymbolsAndEntries()
    {
        var d = DsDictionary.Parse(Dict);
        Assert.Equal(3, d.Count);
        Assert.True(d.IsVowel("a"));
        Assert.False(d.IsVowel("k"));
        Assert.True(d.IsGlide("y"));
        Assert.True(d.IsVowel("AP"));   // always
        Assert.Equal(["k", "a"], d.Query("か")!);
        Assert.Equal(["y", "o"], d.Query("YO")!);   // case-insensitive fallback
        Assert.Null(d.Query("x"));
    }

    [Fact]
    public void Phonemes_OfASyllable_ByKanaThenRomaji_WithTheModelsSpelling()
    {
        var d = DsDictionary.Parse(Dict);
        var known = new HashSet<string> { "k", "a", "N", "ja/y", "o" };
        string? Known(string s) => known.Contains(s) ? s : known.Contains("ja/" + s) ? "ja/" + s : null;
        var unknown = new List<string>();
        Assert.Equal(["k", "a", "N"], KanaPhonemizer.Phonemes("かん", d, Known, unknown));
        Assert.Empty(unknown);
        // よ has no kana entry; its romaji "yo" has one, and the model spells y as ja/y.
        Assert.Equal(["ja/y", "o"], KanaPhonemizer.Phonemes("よ", d, Known, unknown));
        // A long vowel repeats the vowel phoneme before it (no entry needed); an unknown mora is reported and skipped.
        Assert.Equal(["k", "a", "a"], KanaPhonemizer.Phonemes("かーき", d, Known, unknown));
        Assert.Equal(["き"], unknown);
        Assert.Equal(["ja/y", "o", "o"], KanaPhonemizer.Phonemes("よー", d, Known, unknown));
        Assert.Empty(KanaPhonemizer.Phonemes("ー", d, Known, unknown));
        Assert.Equal(["き", "ー"], unknown);
    }

    [Fact]
    public void Config_ReadsSnakeCase_AndTheDepthForms()
    {
        var c = DsYaml.Parse<DsConfig>("""
            phonemes: dsacoustic/phonemes.txt
            acoustic: dsacoustic/acoustic.onnx
            use_key_shift_embed: true
            use_continuous_acceleration: true
            use_variable_depth: true
            max_depth: 0.6
            speakers:
              - dsacoustic/a
            unknown_key: 1
            """);
        Assert.Equal("dsacoustic/acoustic.onnx", c.Acoustic);
        Assert.True(c.UseKeyShiftEmbed);
        Assert.True(c.UseVariableDepth);
        Assert.Equal(0.6, c.MaxDepth, 6);
        Assert.Equal(["dsacoustic/a"], c.Speakers);
        Assert.Equal(1000.0 * 512 / 44100, c.FrameMs, 6);
        // The older spelling, and steps out of 1000.
        var old = DsYaml.Parse<DsConfig>("use_shallow_diffusion: true\nmax_depth: 600\n");
        Assert.True(old.UseVariableDepth);
        Assert.Equal(0.6, old.MaxDepth, 6);
        Assert.False(DsYaml.Parse<DsConfig>("acoustic: x\n").UseVariableDepth);
    }

    [Fact]
    public void Options_NameTheProblems_AndListTheVoices()
    {
        var problems = new DiffSingerOptions(@"C:\no\such\voice").Validate();
        Assert.Single(problems);
        Assert.Contains("not found", problems[0]);
        var tmp = Directory.CreateTempSubdirectory("cantilena-ds-");
        try
        {
            Directory.CreateDirectory(Path.Combine(tmp.FullName, "V1"));
            File.WriteAllText(Path.Combine(tmp.FullName, "V1", "dsconfig.yaml"), "acoustic: a.onnx\n");
            Directory.CreateDirectory(Path.Combine(tmp.FullName, "notavoice"));
            Assert.Equal(["V1"], DiffSingerOptions.Voices(tmp.FullName));
            var v1 = new DiffSingerOptions(Path.Combine(tmp.FullName, "V1")).Validate();
            Assert.Contains(v1, p => p.Contains("dsdur"));
            Assert.Contains(v1, p => p.Contains("vocoder"));
            Assert.Equal("V1", new DiffSingerOptions(Path.Combine(tmp.FullName, "V1")).Name);
        }
        finally
        {
            tmp.Delete(true);
        }
    }
}

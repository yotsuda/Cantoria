// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Cantoria.DiffSinger;
using Xunit;

namespace Cantoria.Tests;

/// <summary>
/// A syllable whose phoneme one of the voice's models was not taught is found before anything is
/// sung and said by its syllable and note (owner's report 2026-11-19: TIGER's dictionary gives 思
/// as "s iz", its pitch model's list has no "iz", and the song failed on a phoneme alone).
/// </summary>
public class NoSoundForTests
{
    private static (string Model, string File, Func<string, bool> Knows) Model(string name, params string[] phonemes) =>
        (name, $@"C:\voices\V\ds{name}\phonemes.txt", phonemes.Contains);

    private static (int, double, string, IReadOnlyList<string>) Word(int note, double start, string lyric, params string[] phonemes) =>
        (note, start, lyric, phonemes);

    [Fact]
    public void EverySyllableKnown_IsNull()
    {
        var words = new[] { Word(0, 0.5, "我", "w", "o"), Word(1, 1, "把", "b", "a") };
        Assert.Null(DiffSingerSinger.NoSoundFor("diffsinger/V", words, [Model("pitch", "w", "o", "b", "a"), Model("acoustic", "w", "o", "b", "a")]));
    }

    [Fact]
    public void ASyllableAModelLacks_IsNamedWithItsNoteItsSpellingTheModelAndThePhoneme()
    {
        var words = new[] { Word(0, 0.5, "我", "w", "o"), Word(2, 1.25, "思", "s", "iz"), Word(3, 2, "念", "n", "ian") };
        string? said = DiffSingerSinger.NoSoundFor("diffsinger/V", words,
            [Model("pitch", "w", "o", "s", "n", "ian"), Model("acoustic", "w", "o", "s", "iz", "n", "ian")]);
        Assert.Equal("diffsinger/V has no sound for \"思\" (note 2 at 1.25 s): its dictionary spells it \"s iz\", and its pitch model has no phoneme \"iz\" "
            + @"(C:\voices\V\dspitch\phonemes.txt). Write other words there, or take another voice", said);
    }

    // Owner's report 2026-11-19: TIGER's Chinese dictionary has no "an", 岸 was a silent note in a
    // song that sang, and only the log knew. It is said to the caller (SingReport.Note).
    [Fact]
    public void ASyllableNotInTheDictionary_IsSaidWithItsNote()
    {
        Assert.Equal("diffsinger/V does not sing \"岸\" (note 46 at 57.25 s): the syllable is not in its dictionary (dsdict-zh.yaml), and its note is silent. "
            + "Write other words there, or take another voice",
            DiffSingerSinger.NotInDictionary("diffsinger/V", @"C:\voices\V\dsdur\dsdict-zh.yaml", [(46, 57.25, "岸")]));
    }

    [Fact]
    public void SeveralSyllablesNotInTheDictionary_AreEachSaidWithTheirNote_TheFirstTwelve()
    {
        string said = DiffSingerSinger.NotInDictionary("diffsinger/V", "dsdict-zh.yaml", [(3, 1, "岸"), (9, 4.5, "村")]);
        Assert.Equal("diffsinger/V does not sing \"岸\" (note 3 at 1 s), \"村\" (note 9 at 4.5 s): these syllables are not in its dictionary (dsdict-zh.yaml), and their notes are silent. "
            + "Write other words there, or take another voice", said);
        var many = Enumerable.Range(0, 14).Select(i => (i, (double)i, "岸")).ToList();
        Assert.Contains("\"岸\" (note 11 at 11 s), …: these syllables", DiffSingerSinger.NotInDictionary("diffsinger/V", "d.yaml", many));
    }

    [Fact]
    public void TheOtherSyllables_AreSaidOnceEach_AndTheSameOneNotAgain()
    {
        var words = new[] { Word(0, 0, "思", "s", "iz"), Word(1, 1, "字", "dz", "iz"), Word(2, 2, "思", "s", "iz"), Word(3, 3, "字", "dz", "iz") };
        string? said = DiffSingerSinger.NoSoundFor("diffsinger/V", words, [Model("pitch", "s", "dz")]);
        Assert.Contains("has no sound for \"思\" (note 0 at 0 s)", said);
        Assert.Contains("; nor for \"字\" (note 1). Write other words", said);
    }
}

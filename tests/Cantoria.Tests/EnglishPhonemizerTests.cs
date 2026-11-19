using Cantoria.DiffSinger;
using Xunit;

namespace Cantoria.Tests;

public class EnglishPhonemizerTests
{
    private const string Yaml = """
        symbols:
        - {symbol: aa, type: vowel}
        - {symbol: ae, type: vowel}
        - {symbol: eh, type: vowel}
        - {symbol: ah, type: vowel}
        - {symbol: ax, type: vowel}
        - {symbol: ay, type: vowel}
        - {symbol: ih, type: vowel}
        - {symbol: ow, type: vowel}
        - {symbol: d, type: stop}
        - {symbol: dx, type: stop}
        - {symbol: g, type: stop}
        - {symbol: hh, type: fricative}
        - {symbol: k, type: stop}
        - {symbol: l, type: liquid}
        - {symbol: n, type: nasal}
        - {symbol: ng, type: nasal}
        - {symbol: p, type: stop}
        - {symbol: r, type: liquid}
        - {symbol: s, type: fricative}
        - {symbol: t, type: stop}
        - {symbol: w, type: semivowel}
        - {symbol: z, type: fricative}
        entries:
        - {grapheme: "twinkles", phonemes: [t, w, ih, ng, k, ax, l, z]}
        - {grapheme: "little", phonemes: [l, ih, dx, ax, l]}
        - {grapheme: "star", phonemes: [s, t, aa, r]}
        - {grapheme: "hello", phonemes: [hh, ah, l, ow]}
        - {grapheme: "don't", phonemes: [d, ow, n, t]}
        - {grapheme: "april", phonemes: [ay, p, r, ax, l]}
        - {grapheme: "the", phonemes: [dx, ax]}
        - {grapheme: "love", phonemes: [l, ah]}
        - {grapheme: "kiss", phonemes: [k, ih, s]}
        - {grapheme: "cat", phonemes: [k, aa, t]}
        - {grapheme: "had", phonemes: [h, ae, d]}
        """;

    private static readonly DsDictionary Dict = DsDictionary.Parse(Yaml);

    private static string?  Known(string symbol) => Dict.IsSymbol(symbol) ? symbol : null;

    private static List<EnglishPhonemizer.Syllable> Sing(params (string, bool)[] notes) => EnglishPhonemizer.Syllables(notes, Dict, Known);

    [Fact]
    public void AnEntryNamingAPhonemeTheVoiceLacks_IsReadFromItsLetters()
    {
        var had = Assert.Single(Sing(("had", false)));
        Assert.Equal(["hh", "ae", "d"], had.Phonemes);
        Assert.False(had.Missing);
        Assert.True(had.Guessed);       // so the render's log names it
    }

    private static string Show(EnglishPhonemizer.Syllable s) => string.Join(' ', s.Phonemes);

    [Fact]
    public void AWordOverItsSyllables_IsLookedUpWhole_AVowelToEach()
    {
        var sung = Sing(("Lit", true), ("tle", false));
        Assert.Equal("l ih", Show(sung[0]));
        Assert.Equal("dx ax l", Show(sung[1]));
    }

    [Fact]
    public void AnOnsetEnglishCanBegin_StaysTogether()
    {
        var sung = Sing(("A", true), ("pril", false));
        Assert.Equal("ay", Show(sung[0]));
        Assert.Equal("p r ax l", Show(sung[1]));
    }

    [Fact]
    public void AConsonantPairNoSyllableBegins_IsDivided()
    {
        var sung = Sing(("Twin", true), ("kle", false));
        Assert.Equal("t w ih ng", Show(sung[0]));
        Assert.Equal("k ax l", Show(sung[1]));
    }

    [Theory]
    [InlineData("twinkle", "t w ih ng k ax l")]      // only its plural is listed
    [InlineData("stars", "s t aa r z")]
    [InlineData("cats", "k aa t s")]
    [InlineData("twinkling", "t w ih ng k ax l ih ng")]
    [InlineData("loved", "l ah d")]
    [InlineData("kissed", "k ih s t")]
    public void AnInflectedForm_IsSpelledFromTheListedOne(string word, string expected)
        => Assert.Equal(expected, string.Join(' ', EnglishPhonemizer.Lookup(word, Dict)!));

    [Fact]
    public void CaseAndPunctuation_DoNotMatter()
    {
        Assert.Equal("s t aa r", Show(Sing(("Star,", false))[0]));
        Assert.Equal("d ow n t", Show(Sing(("Don’t", false))[0]));
    }

    [Fact]
    public void AnElision_SingsItsWordsOnOneNote()
        => Assert.Equal("dx ax s t aa r", Show(Sing(("the~star", false))[0]));

    [Fact]
    public void MoreNotesThanVowels_LeaveTheLastToCarryTheVowelOn()
    {
        var sung = Sing(("st", true), ("ar", false));
        Assert.Equal("s t aa r", Show(sung[0]));
        Assert.Empty(sung[1].Phonemes);
        Assert.False(sung[1].Missing);
    }

    [Fact]
    public void MoreVowelsThanNotes_StayOnTheNote()
        => Assert.Equal("hh ah l ow", Show(Sing(("hello", false))[0]));

    [Fact]
    public void AWordNotInTheDictionary_IsMissing_AndAKnownSyllableStillSings()
    {
        var sung = Sing(("star", true), ("ほげ", false));
        Assert.Equal("s t aa r", Show(sung[0]));
        Assert.True(sung[1].Missing);
    }

    [Theory]
    [InlineData("frog", "f r aa g")]
    [InlineData("blade", "b l ey d")]
    [InlineData("queen", "k w iy n")]
    [InlineData("night", "n ay t")]
    [InlineData("apple", "ae p ax l")]
    [InlineData("knock", "n aa k")]
    [InlineData("city", "s ih t iy")]
    [InlineData("frogs", "f r aa g z")]
    public void AWordTheDictionaryLacks_IsGuessedFromItsLetters(string word, string expected)
        => Assert.Equal(expected, string.Join(' ', EnglishLetters.Guess(word)!));

    [Theory]
    [InlineData("ほげ")]
    [InlineData("hmm")]
    public void NoGuess_ForOtherScripts_OrWithoutAVowel(string word)
        => Assert.Null(EnglishLetters.Guess(word));

    [Fact]
    public void AGuessedWord_SaysSo_AndIsDealtOverItsSyllables()
    {
        var sung = EnglishPhonemizer.Syllables([("blan", true), ("ket", false)], Dict, s => s);
        Assert.True(sung[0].Guessed);
        Assert.False(sung[0].Missing);
        Assert.Equal("b l ae ng", Show(sung[0]));
        // As the CMU dictionary lists the word (B L AE1 NG K AH0 T), its schwa the voice's ax -
        // the letters' rules alone said "k eh t" until 2026-11-19.
        Assert.Equal("k ax t", Show(sung[1]));
    }

    // Owner's report 2026-11-19 (Lily#'s Paper-Boats.lys under diffsinger/TIGER, whose dictionary has
    // ten thousand words): these were read from their letters - f aa l d eh d, w aa r iy z,
    // hh iy v iy, ae n ih m ao r. A word the voice's dictionary lacks is read as the CMU
    // dictionary lists it (EnglishG2p), in the voice's symbols.
    /// <summary>A voice with every ARPABET phoneme and the schwa, and no word of its own.</summary>
    private static readonly DsDictionary Bare = DsDictionary.Parse("symbols:\n"
        + string.Concat("aa ae ah ao aw ax ay eh er ey ih iy ow oy uh uw".Split(' ').Select(s => $"- {{symbol: {s}, type: vowel}}\n"))
        + string.Concat("b ch d dh f g hh jh k l m n ng p r s sh t th v w y z zh".Split(' ').Select(s => $"- {{symbol: {s}, type: stop}}\n"))
        + "entries: []\n");

    [Theory]
    [InlineData("fold- ed", "f ow l|d ax d")]
    [InlineData("wor- ries", "w er|iy z")]
    [InlineData("heav- y", "hh eh|v iy")]
    [InlineData("an- y- more", "eh|n iy|m ao r")]
    [InlineData("riv- er", "r ih|v er")]
    [InlineData("gen- tly", "jh eh n t|l iy")]
    public void AWordTheVoicesDictionaryLacks_IsReadAsTheCmuDictionaryListsIt(string written, string expected)
    {
        var notes = written.Split(' ').Select(s => (s.TrimEnd('-'), s.EndsWith('-'))).ToArray();
        var sung = EnglishPhonemizer.Syllables(notes, Bare, s => Bare.IsSymbol(s) ? s : null);
        Assert.Equal(expected, string.Join('|', sung.Select(Show)));
        Assert.All(sung, s => { Assert.True(s.Guessed); Assert.False(s.Missing); });   // (named in the render's log)
    }

    [Fact]
    public void TheSchwa_IsTheVoicesAh_WhereItHasNoAx()
        => Assert.Equal("f ow l|d ah d", string.Join('|',
            EnglishPhonemizer.Syllables([("fold", true), ("ed", false)], Bare, s => s != "ax" && Bare.IsSymbol(s) ? s : null).Select(Show)));

    [Fact]
    public void TheVoicesOwnDictionary_ComesFirst()
        => Assert.Equal("l ah", Show(Sing(("love", false))[0]));       // (the CMU dictionary says l ah v)

    [Fact]
    public void AWordNoDictionaryLists_IsReadByTheModel_ALetterAtATime()
    {
        Assert.Null(EnglishG2p.Listed("zundamon"));
        var read = EnglishG2p.Predicted("zundamon");
        Assert.NotNull(read);
        Assert.Equal("z", read![0]);
        Assert.Contains("m", read);
        // Not a word of another script, nor one with a letter that is not the model's.
        Assert.Null(EnglishG2p.Predicted("ほげ"));
        Assert.Null(EnglishG2p.Predicted("starほげ"));
    }

    [Fact]
    public void TheCmuDictionary_ListsAWordsFirstReading_WithoutItsStress()
    {
        Assert.Equal("d ao r", string.Join(' ', EnglishG2p.Listed("door")!));
        Assert.Equal("w ah n", string.Join(' ', EnglishG2p.Listed("One")!));
        Assert.Equal("dh ax", string.Join(' ', EnglishG2p.Listed("the")!));          // (AH0, the schwa)
        Assert.Null(EnglishG2p.Listed("zundamon"));
    }

    [Fact]
    public void AListedSyllable_IsPreferredToAGuess()
    {
        var sung = EnglishPhonemizer.Syllables([("star", true), ("cat", false)], Dict, s => s);
        Assert.False(sung[0].Guessed);
        Assert.Equal("s t aa r", Show(sung[0]));
        Assert.Equal("k aa t", Show(sung[1]));
    }

    [Fact]
    public void APhonemeTheModelLacks_MeansTheWordIsMissing()
        => Assert.True(EnglishPhonemizer.Syllables([("star", false)], Dict, s => s == "aa" ? null : s)[0].Missing);

    [Fact]
    public void WordsFollowEachOther()
    {
        var sung = Sing(("hel", true), ("lo", false), ("star", false));
        Assert.Equal(3, sung.Count);
        Assert.Equal("hh ah", Show(sung[0]));
        Assert.Equal("l ow", Show(sung[1]));
        Assert.Equal("s t aa r", Show(sung[2]));
    }

    [Fact]
    public void TheJson_CarriesTheHyphen()
        => Assert.True(ScoreJson.Read("""{ "notes": [ { "start": 0, "duration": 1, "pitch": 60, "lyric": "a", "hyphen": true } ] }""").Notes[0].Hyphen);
}

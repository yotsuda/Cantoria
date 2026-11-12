// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text.Json;
using System.Text.RegularExpressions;
using Cantoria.Voicevox;
using Xunit;

namespace Cantoria.Tests;

/// <summary>Who sings what a score asks for: the rule (SingerResolver), the plan over it, and the voices as they are listed.</summary>
public class SingerPlanTests
{
    private static VoiceEntry V(string id, string? gender = null, string? language = "ja", params string[] languages)
        => new(id, id[..id.IndexOf('/')], id[(id.IndexOf('/') + 1)..], id) { Gender = gender, Language = language, Languages = languages.Length > 0 ? languages : null };

    // The list of the extension's own tests (editors/vscode/test/core.test.ts): the two rules answer alike.
    private static readonly VoiceEntry[] List =
    [
        V("n/A", "female"), V("n/B"), V("n/C", "male", "en"), V("n/D", "male"), V("n/E", "male"), V("n/F", "female"),
    ];

    private static readonly DefaultVoices Unset = new();

    // This machine's kinds of voices: Japanese ones of each gender, one of several languages that
    // says no gender, a male one of several.
    private static readonly VoiceEntry[] Installed =
    [
        V("neutrino/CHANKO", "female"), V("neutrino/MERROW", "female"), V("neutrino/NAKUMO", "male"),
        V("diffsinger/Nishiren", null, "ja", "ja", "en", "zh", "ko"), V("diffsinger/Nishiren/Soft", null, "ja", "ja", "en", "zh", "ko"),
        V("diffsinger/TIGER", "male", "ja", "ja", "en", "zh"),
    ];

    private static PlannedSinger One(PlanAsk ask, DefaultVoices? defaults = null, VoiceEntry[]? voices = null)
        => Assert.Single(SingerPlan.For([ask], voices ?? Installed, defaults ?? Unset).Singers);

    [Fact]
    public void AVoiceNamedAndInstalled_Sings()
        => Assert.Equal(new ResolvedSinger("n/B"), SingerResolver.Resolve("n/B", List, Unset));

    [Fact]
    public void AGender_TakesTheSetting_ElseTheFirstSuchVoiceForTheWordsLanguage()
    {
        Assert.Equal(new ResolvedSinger("n/D"), SingerResolver.Resolve("male", List, Unset, "ja"));
        Assert.Equal(new ResolvedSinger("n/C"), SingerResolver.Resolve("male", List, Unset, "en"));
        Assert.Equal(new ResolvedSinger("n/A"), SingerResolver.Resolve("female", List, Unset, "ja"));
        Assert.Equal(new ResolvedSinger("n/E"), SingerResolver.Resolve("male", List, new DefaultVoices(Male: "n/E"), "ja"));
        Assert.Equal(new ResolvedSinger("n/F"), SingerResolver.Resolve("female", List, new DefaultVoices(Female: "n/F"), "ja"));
    }

    [Fact]
    public void APartsWord_SaysTheGender_AndWithNoSingerTheReaderDoes()
    {
        Assert.Equal("female", SingerResolver.GenderOfWord("soprano"));
        Assert.Equal("female", SingerResolver.GenderOfWord("alto"));
        Assert.Equal("male", SingerResolver.GenderOfWord("tenor"));
        Assert.Equal("male", SingerResolver.GenderOfWord("bass"));
        Assert.Null(SingerResolver.GenderOfWord("lead"));
        Assert.Equal(new ResolvedSinger("n/D"), SingerResolver.Resolve("tenor", List, Unset, "ja", "female"));
        Assert.Equal(new ResolvedSinger("n/D"), SingerResolver.Resolve("", List, Unset, "ja", "male"));
        Assert.Equal(new ResolvedSinger("n/A"), SingerResolver.Resolve("  ", List, Unset, "ja"));
        Assert.Equal(new ResolvedSinger("n/A"), SingerResolver.Resolve(null, List, Unset, "ja"));
    }

    [Fact]
    public void NoVoiceOfTheGender_TheOtherSings_AndASentenceSaysSo()
    {
        var femaleOnly = List.Where(v => v.Gender != "male").ToArray();
        var r = SingerResolver.Resolve("male", femaleOnly, Unset, "ja");
        Assert.Equal("n/A", r.Voice);
        Assert.Contains("no installed voice for these words says it is male", r.Note);
        var silent = SingerResolver.DefaultVoiceFor([V("x/Y", null, null)], Unset, "male");
        Assert.Equal("x/Y", silent.Voice);
        Assert.NotNull(silent.Note);
        Assert.Equal("neutrino/MERROW", SingerResolver.DefaultVoiceFor([], Unset, "female").Voice);
        Assert.Null(SingerResolver.FirstOfGender(List, "male", "ko"));
    }

    [Fact]
    public void ANameOrASettingNotThere_OrAWordThatNamesNoVoice_IsSaid()
    {
        var missing = SingerResolver.Resolve("diffsinger/NOPE", List, Unset, "ja", "male");
        Assert.Equal(new ResolvedSinger("n/D", "diffsinger/NOPE is not installed; singing with n/D"), missing);
        var setting = SingerResolver.Resolve("male", List, new DefaultVoices(Male: "n/GONE"), "ja");
        Assert.Equal(new ResolvedSinger("n/D", "n/GONE is not installed; singing with n/D"), setting);
        Assert.Equal("the singer \"lead\" names no voice; singing with n/A", SingerResolver.Resolve("lead", List, Unset, "ja").Note);
    }

    [Fact]
    public void VoicesJoined_EachSing_OnceEach()
    {
        VoiceEntry[] chorus = [V("n/A", null, null), V("d/B", null, null)];
        Assert.Equal(["a/b", "c/d"], SingerResolver.Pieces(" a/b + c/d+"));
        Assert.Equal(new ResolvedSinger("n/A+d/B"), SingerResolver.Resolve("n/A + d/B", chorus, Unset));
        Assert.Equal("n/A", SingerResolver.Resolve("n/A + n/A", chorus, Unset).Voice);
        Assert.Contains("x/Y is not installed", SingerResolver.Resolve("n/A + x/Y", chorus, Unset).Note);
    }

    [Fact]
    public void AVoice_IsWrittenAsEngineAndName()
    {
        Assert.Equal("neutrino/MERROW", SingerResolver.NormalVoice(""));
        Assert.Equal("neutrino/ZUNKO", SingerResolver.NormalVoice(" ZUNKO "));
        Assert.Equal("diffsinger/TIGER/tiger_disco", SingerResolver.NormalVoice("DiffSinger/TIGER/tiger_disco"));
        Assert.Equal("neutrino/MERROW", SingerResolver.NormalVoice("/"));
    }

    [Fact]
    public void WordsInALanguageTheVoiceDoesNotSing_AreRefused_AndASilentVoiceIsLetTry()
    {
        Assert.Equal("neutrino/MERROW sings ja, and these words are en", SingerResolver.LanguageProblem(V("neutrino/MERROW"), "en"));
        Assert.Null(SingerResolver.LanguageProblem(V("neutrino/MERROW"), "ja"));
        Assert.Null(SingerResolver.LanguageProblem(V("d/T", null, "ja", "ja", "en"), "en"));
        Assert.Equal("d/T sings ja, en, and these words are ko", SingerResolver.LanguageProblem(V("d/T", null, "ja", "ja", "en"), "ko"));
        Assert.Equal(new ResolvedSinger("d/M"), SingerResolver.Resolve("male", [V("d/M", "male", "ja", "ja", "en")], Unset, "en"));
        Assert.Null(SingerResolver.LanguageProblem(V("x/y", null, null), "en"));
        Assert.Null(SingerResolver.LanguageProblem(null, "en"));
        Assert.Null(SingerResolver.LanguageProblem(V("neutrino/MERROW"), null));
    }

    [Fact]
    public void ThePlan_ByName_IsThatVoice_AndWhatItSings()
    {
        var s = One(new PlanAsk("melody", Voice: "neutrino/MERROW", Language: "ja"));
        Assert.Equal("neutrino/MERROW", s.Voice);
        Assert.Null(s.Note);
        Assert.Null(s.Reason);
        Assert.Equal(["ja"], s.Languages);
    }

    [Fact]
    public void ThePlan_ByGender_IsTheFirstOfIt_ThatSingsTheLanguage_UnlessADefaultIsSet()
    {
        Assert.Equal("neutrino/CHANKO", One(new PlanAsk(Gender: "female", Language: "ja")).Voice);
        Assert.Equal("neutrino/NAKUMO", One(new PlanAsk(Gender: "male", Language: "ja")).Voice);
        Assert.Equal("diffsinger/TIGER", One(new PlanAsk(Gender: "male", Language: "en")).Voice);
        Assert.Equal("neutrino/CHANKO", One(new PlanAsk()).Voice);
        Assert.Equal("neutrino/MERROW", One(new PlanAsk(Gender: "female", Language: "ja"), new DefaultVoices("neutrino/MERROW", "diffsinger/TIGER")).Voice);
        Assert.Equal("diffsinger/TIGER", One(new PlanAsk(Voice: "male", Language: "ja"), new DefaultVoices("MERROW", "diffsinger/TIGER")).Voice);
    }

    [Fact]
    public void ThePlan_ForALanguageNoVoiceSings_HasNoVoice_AndSaysWhatTheyDoSing()
    {
        var s = One(new PlanAsk("alto", Gender: "female", Language: "la"));
        Assert.Null(s.Voice);
        Assert.Null(s.Note);
        Assert.Equal("no installed voice sings Latin (la); installed voices sing ja, en, zh, ko", s.Reason);
        Assert.Equal(["ja", "en", "zh", "ko"], s.Languages);
        Assert.Equal("no installed voice sings tlh; installed voices sing ja, en, zh, ko", One(new PlanAsk(Voice: "diffsinger/TIGER", Language: "tlh")).Reason);
    }

    [Fact]
    public void ThePlan_ForAGenderNoVoiceOfTheLanguageSays_IsTheOtherGenders_WithASentence()
    {
        var s = One(new PlanAsk("sop", Gender: "female", Language: "en"));
        Assert.Equal("diffsinger/TIGER", s.Voice);
        Assert.Equal("no installed voice for these words says it is female; singing with diffsinger/TIGER", s.Note);
        Assert.Null(s.Reason);
    }

    [Fact]
    public void ThePlan_ForADefaultVoiceThatDoesNotSingTheWords_HasNoVoice_AndNamesTheSetting()
    {
        var s = One(new PlanAsk(Gender: "female", Language: "en"), new DefaultVoices(Female: "neutrino/MERROW"));
        Assert.Null(s.Voice);
        Assert.Equal("the default female voice neutrino/MERROW sings ja, and these words are en (diffsinger/Nishiren, diffsinger/TIGER sing en)", s.Reason);
        // Named outright, it is the name that is wrong, not the setting.
        Assert.Equal("neutrino/MERROW sings ja, and these words are ko (diffsinger/Nishiren sings ko)",
            One(new PlanAsk(Voice: "neutrino/MERROW", Language: "ko"), new DefaultVoices(Female: "neutrino/MERROW")).Reason);
    }

    [Fact]
    public void ThePlan_ForAVoiceNotInstalled_IsTheDefault_AndSaysSo_WithWhyItsEngineHasNone()
    {
        var missing = new Dictionary<string, string> { ["voicevox"] = "VOICEVOX is not running or not installed: nothing answers at http://x" };
        var plan = SingerPlan.For([new PlanAsk(Voice: "neutrino/NOPE", Gender: "male", Language: "ja"), new PlanAsk(Voice: "voicevox/ずんだもん-ノーマル", Language: "ja")],
            Installed, Unset, setup: missing);
        Assert.True(plan.Ready);
        Assert.Equal("neutrino/NAKUMO", plan.Singers[0].Voice);
        Assert.Equal("neutrino/NOPE is not installed; singing with neutrino/NAKUMO", plan.Singers[0].Note);
        Assert.Equal("neutrino/CHANKO", plan.Singers[1].Voice);
        Assert.Equal("voicevox/ずんだもん-ノーマル is not installed; singing with neutrino/CHANKO; VOICEVOX is not running or not installed: nothing answers at http://x", plan.Singers[1].Note);
    }

    [Fact]
    public void ThePlan_WithNoVoiceInstalled_SaysWhyEachEngineHasNone()
    {
        var setup = new Dictionary<string, string> { ["neutrino"] = "NEUTRINO's folder is not set", ["voicevox"] = "VOICEVOX is not running" };
        var plan = SingerPlan.For([new PlanAsk(Gender: "female", Language: "ja")], [], Unset, setup: setup);
        Assert.False(plan.Ready);
        Assert.Null(plan.Singers[0].Voice);
        Assert.Equal("no voice is installed: NEUTRINO's folder is not set; VOICEVOX is not running", plan.Singers[0].Reason);
        Assert.Null(plan.Singers[0].Languages);
    }

    [Fact]
    public void ThePlan_ForAVoiceItsEngineCannotSingWith_HasNoVoice_AndTheEnginesReason()
    {
        int asked = 0;
        var plan = SingerPlan.For([new PlanAsk(Voice: "diffsinger/TIGER", Language: "en"), new PlanAsk(Voice: "diffsinger/TIGER", Language: "ja"), new PlanAsk(Voice: "neutrino/MERROW")],
            Installed, Unset, voice => { asked++; return voice.StartsWith("diffsinger/") ? ["the voice bundles no vocoder"] : null; });
        Assert.Equal("the voice bundles no vocoder", plan.Singers[0].Reason);
        Assert.Null(plan.Singers[1].Voice);
        Assert.Equal("neutrino/MERROW", plan.Singers[2].Voice);
        Assert.Equal(2, asked);     // a voice is looked at once a plan
    }

    [Fact]
    public void ThePlan_ForVoicesJoined_NeedsEachToSing()
    {
        Assert.Equal("neutrino/MERROW+diffsinger/TIGER", One(new PlanAsk(Voice: "neutrino/MERROW + diffsinger/TIGER", Language: "ja")).Voice);
        Assert.Equal(["ja", "en", "zh"], One(new PlanAsk(Voice: "neutrino/MERROW + diffsinger/TIGER", Language: "ja")).Languages);
        var s = One(new PlanAsk(Voice: "neutrino/MERROW+diffsinger/TIGER", Language: "en"));
        Assert.Null(s.Voice);
        Assert.StartsWith("neutrino/MERROW sings ja, and these words are en", s.Reason);
    }

    [Fact]
    public void ThePlan_AsLines_SaysWhatWasAsked_AndWhoSings()
    {
        var plan = SingerPlan.For(
        [
            new PlanAsk("melody", Voice: "neutrino/MERROW", Language: "ja"),
            new PlanAsk("alto", Gender: "female", Language: "la"),
            new PlanAsk(Key: "sop#2", Gender: "female", Language: "en"),
            new PlanAsk(),
        ], Installed, Unset);
        Assert.False(plan.Ready);
        Assert.Equal(
        [
            "melody: neutrino/MERROW, ja -> neutrino/MERROW",
            "alto: female, la -> no installed voice sings Latin (la); installed voices sing ja, en, zh, ko",
            "sop#2: female, en -> diffsinger/TIGER (no installed voice for these words says it is female; singing with diffsinger/TIGER)",
            "the default voice -> neutrino/CHANKO",
        ], plan.Lines());
    }

    [Fact]
    public void ThePlan_AsJson_NamesWhatTheScoreAndVoicesDo_AndWritesNoVoiceAsNull()
    {
        var plan = SingerPlan.For([new PlanAsk("melody", "m1", "neutrino/MERROW", null, "ja"), new PlanAsk("alto", Gender: "female", Language: "la"), new PlanAsk(Gender: "female", Language: "en")], Installed, Unset);
        Assert.Equal("""
            {"ready":false,"singers":[{"part":"melody","key":"m1","asked":{"voice":"neutrino/MERROW","language":"ja"},"voice":"neutrino/MERROW","reason":null,"languages":["ja"]},{"part":"alto","asked":{"gender":"female","language":"la"},"voice":null,"reason":"no installed voice sings Latin (la); installed voices sing ja, en, zh, ko","languages":["ja","en","zh","ko"]},{"asked":{"gender":"female","language":"en"},"voice":"diffsinger/TIGER","note":"no installed voice for these words says it is female; singing with diffsinger/TIGER","reason":null,"languages":["ja","en","zh"]}]}
            """, plan.ToJson().ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Assert.True(SingerPlan.For([new PlanAsk(Voice: "neutrino/MERROW")], Installed, Unset).ToJson()["ready"]!.GetValue<bool>());
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    [Fact]
    public void WhatIsAsked_IsReadFromAList_OrFromAScore_OrFromTheOptions()
    {
        var asked = SingerPlan.AskedOf(Json("""{"asked":[{"part":"alto","key":"a1","gender":"female","language":"la"},{"voice":"neutrino/MERROW"},{}]}"""), gender: "male", language: "ja");
        Assert.Equal([new PlanAsk("alto", "a1", null, "female", "la"), new PlanAsk(Voice: "neutrino/MERROW", Gender: "male", Language: "ja"), new PlanAsk(Gender: "male", Language: "ja")], asked);
        // A score asks for one voice, in its language — Japanese when it says none, as it is sung.
        Assert.Equal([new PlanAsk(Voice: "diffsinger/TIGER", Language: "en")], SingerPlan.AskedOf(Json("""{"language":"en","notes":[]}"""), "diffsinger/TIGER"));
        Assert.Equal([new PlanAsk(Gender: "male", Language: "ja")], SingerPlan.AskedOf(Json("""{"notes":[]}"""), gender: "male"));
        Assert.Equal([new PlanAsk(Language: "la")], SingerPlan.AskedOf(Json("""{"language":"en","notes":[]}"""), language: "la"));
        Assert.Equal([new PlanAsk(Voice: "female", Language: "la")], SingerPlan.AskedOf(null, "female", language: "la"));
        Assert.Throws<JsonException>(() => SingerPlan.AskedOf(Json("""{"voices":[]}""")));
        Assert.Throws<JsonException>(() => SingerPlan.AskedOf(Json("""{"asked":["alto"]}""")));
        Assert.Throws<JsonException>(() => SingerPlan.AskedOf(Json("[]")));
    }

    [Fact]
    public void ALanguage_IsNamedInASentence_AndAnUnknownTagShownAsItIs()
    {
        Assert.Equal(["Japanese", "English", "Chinese", "Korean", "Latin", "tlh"], new[] { "ja", "en", "zh", "ko", "la", "tlh" }.Select(SingerResolver.LanguageName));
    }

    private static string Temp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cantoria-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Write(string root, string relative, string text)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void NeutrinosVoices_AreReadForTheirSpeaker_InTheExtensionsOrder()
    {
        var root = Temp();
        try
        {
            Write(root, "model/b_lower/info.toml", "[model]\nname = \"x\"\n");
            Write(root, "model/MERROW/info.toml", "[model]\nname = \"m\"\n\n[speaker]\nname = \"Merrow\"\r\nlanguage = \"Japanese\"\ngender = \"Female\"\n[other]\ngender = \"male\"\n");
            Write(root, "model/NAKUMO/info.toml", "[speaker]\nlanguage = \"en-US\"\ngender = \"male\"\n");
            Directory.CreateDirectory(Path.Combine(root, "model", "not-a-voice"));
            var voices = VoiceEntries.Neutrino(root);
            // By UTF-16 code unit, as a JavaScript sort has them: capitals before small letters.
            Assert.Equal(["neutrino/MERROW", "neutrino/NAKUMO", "neutrino/b_lower"], voices.Select(v => v.Id));
            Assert.Equal(("neutrino", "MERROW", "Merrow", "ja", "female"), (voices[0].Engine, voices[0].Name, voices[0].Label, voices[0].Language, voices[0].Gender));
            Assert.Null(voices[0].Languages);
            Assert.Equal(("NAKUMO", "en-US", "male"), (voices[1].Label, voices[1].Language, voices[1].Gender));
            Assert.Equal(("b_lower", null, null), (voices[2].Label, voices[2].Language, voices[2].Gender));
            Assert.Empty(VoiceEntries.Neutrino(Path.Combine(root, "nowhere")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DiffSingerVoices_SayTheLanguagesOfTheirDictionaries_AndTheirSpeakersFollowThem()
    {
        var root = Temp();
        try
        {
            Write(root, "TIGER/dsconfig.yaml", "speakers:\n  - embeds/tiger_default\n  - embeds/tiger_disco\n");
            Write(root, "TIGER/character.yaml", "name: \"TIGER (虎)\"\nsubbanks:\n  - {color: \"01 Default\", suffix: \"embeds/tiger_default\"}\n  - {color: \"02 Disco\", suffix: \"embeds/tiger_disco\"}\n");
            foreach (var l in new[] { "ja", "en", "zh", "es" })
            {
                Write(root, $"TIGER/dsdur/dsdict-{l}.yaml", "entries: []\n");
            }
            Write(root, "Plain/dsconfig.yaml", "acoustic: acoustic.onnx\n");
            Write(root, "nsf_hifigan/vocoder.yaml", "name: nsf\n");
            var voices = VoiceEntries.DiffSinger(root);
            Assert.Equal(["diffsinger/Plain", "diffsinger/TIGER", "diffsinger/TIGER/tiger_disco"], voices.Select(v => v.Id));
            Assert.Equal(("Plain", "Plain", null, null), (voices[0].Name, voices[0].Label, voices[0].Language, voices[0].Gender));
            Assert.Null(voices[0].Languages);
            Assert.Equal(("TIGER (虎)", "ja", "male"), (voices[1].Label, voices[1].Language, voices[1].Gender));
            Assert.Equal(["ja", "en", "zh"], voices[1].Languages);
            Assert.Equal(("TIGER/tiger_disco", "TIGER (虎) (Disco)", "male"), (voices[2].Name, voices[2].Label, voices[2].Gender));
            Assert.Equal(["ja", "en", "zh"], voices[2].Languages);
            Assert.True(voices[2].Sings("en"));
            Assert.False(voices[2].Sings("ko"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void VoicevoxsStyles_SingJapanese_WithTheGenderOfTheTable_AndTheCreditItsTermsAsk()
    {
        var singers = VoicevoxSingers.Parse("""
            [{"name":"玄野武宏","styles":[{"name":"ノーマル","id":3011,"type":"frame_decode"},{"name":"喜び","id":39,"type":"talk"}]},
             {"name":"だれか","styles":[{"name":"ノーマル","id":6000,"type":"sing"}]}]
            """);
        var voices = VoiceEntries.Voicevox(singers);
        Assert.Equal(["voicevox/玄野武宏-ノーマル", "voicevox/だれか-ノーマル"], voices.Select(v => v.Id));
        Assert.Equal(("voicevox", "玄野武宏-ノーマル", "玄野武宏 (ノーマル)", "ja", "male", "Credit \"VOICEVOX:玄野武宏\" and follow the character's terms"),
            (voices[0].Engine, voices[0].Name, voices[0].Label, voices[0].Language, voices[0].Gender, voices[0].Licence));
        Assert.Null(voices[1].Gender);
        Assert.Equal("voicevox/玄野武宏-ノーマル", SingerResolver.FirstOfGender(voices, "male", "ja"));
    }

    /// <summary>The extension's source folder, found from where the tests run.</summary>
    private static string ExtensionSource()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var src = Path.Combine(dir.FullName, "editors", "vscode", "src");
            if (Directory.Exists(src))
            {
                return src;
            }
        }
        throw new DirectoryNotFoundException("editors/vscode/src above " + AppContext.BaseDirectory);
    }

    private static Dictionary<string, string> TableOf(string file)
        => Regex.Matches(File.ReadAllText(Path.Combine(ExtensionSource(), file)), @"^\s*'([^']+)':\s*'(female|male)',", RegexOptions.Multiline)
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);

    // The genders are written down twice, here and in the extension, until its callers ask the
    // program: a voice added to one table and not the other would be sung by another voice.
    [Fact]
    public void TheGenderTables_AreTheExtensionsOwn()
    {
        Assert.Equal(TableOf("diffSingerGenders.ts").OrderBy(p => p.Key, StringComparer.Ordinal), VoiceGenders.DiffSinger.OrderBy(p => p.Key, StringComparer.Ordinal));
        Assert.Equal(TableOf("voicevoxGenders.ts").OrderBy(p => p.Key, StringComparer.Ordinal), VoiceGenders.Voicevox.OrderBy(p => p.Key, StringComparer.Ordinal));
        Assert.True(VoiceGenders.Voicevox.Count > 40);
    }
}

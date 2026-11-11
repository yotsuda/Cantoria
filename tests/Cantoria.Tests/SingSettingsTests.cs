// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Xunit;

namespace Cantoria.Tests;

/// <summary>How a score asks a voice to sing (its "sing" object), and what a voice takes of it.</summary>
public class SingSettingsTests
{
    private static readonly SingParameter[] Known =
    [
        new("breathiness", SingScope.Curve, "breath", -12, 12, 0, "dB"),
        new("velocity", SingScope.Curve, "consonants", 0.5, 2, 1),
    ];

    [Fact]
    public void AScoresSettings_AreReadAsNumbersAndWords_AndWrittenBack()
    {
        var score = ScoreJson.Read("""
            { "sing": { "breathiness": 3.5, "support": "KIRITAN", "tension": "default" },
              "notes": [ { "start": 0, "duration": 1, "pitch": 60, "lyric": "ら" } ] }
            """);
        Assert.Equal(SingValue.Of(3.5), score.Sing!["breathiness"]);
        Assert.Equal(SingValue.Of("KIRITAN"), score.Sing["support"]);
        Assert.True(score.Sing["tension"].IsDefault);
        var again = ScoreJson.Read(ScoreJson.Write(score));
        Assert.Equal(score.Sing.OrderBy(p => p.Key), again.Sing!.OrderBy(p => p.Key));
        // A score that sets nothing has none, and says nothing of it when written.
        var plain = ScoreJson.Read("""{ "notes": [ { "start": 0, "duration": 1, "pitch": 60, "lyric": "ら" } ] }""");
        Assert.Null(plain.Sing);
        Assert.DoesNotContain("sing", ScoreJson.Write(plain));
        Assert.Throws<System.Text.Json.JsonException>(() => ScoreJson.Read("""{ "sing": { "breathiness": [1] }, "notes": [] }"""));
    }

    [Fact]
    public void AVoiceTakesWhatItKnows_AndSaysWhatItDoesNot_OnceEach()
    {
        var said = new List<string>();
        Dictionary<string, double> numbers;
        using (SingReport.ListenNotes(said.Add))
        {
            var sing = new Dictionary<string, SingValue>
            {
                ["breathiness"] = SingValue.Of(3), ["velocity"] = SingValue.Of(5), ["tension"] = SingValue.Of(2),
                ["support"] = SingValue.Of("KIRITAN"), ["energy"] = SingValue.Of(SingValue.DefaultWord),
            };
            numbers = SingSettings.Numbers(sing, Known, "diffsinger/X");
            SingSettings.Numbers(sing, Known, "diffsinger/X");      // a second phrase of the same sing
        }
        // What it knows, a value out of range brought within it; "default" sets nothing.
        Assert.Equal(new Dictionary<string, double> { ["breathiness"] = 3, ["velocity"] = 2 }, numbers);
        Assert.Equal(3, said.Count);
        Assert.Contains(said, s => s.Contains("\"velocity 5\" is outside 0.5 to 2; sung as 2"));
        Assert.Contains(said, s => s.Contains("does not take the setting \"tension\" (it takes breathiness, velocity)"));
        Assert.Contains(said, s => s.Contains("does not take the setting \"support\""));
        // A voice that takes nothing says so of every setting; nobody listening, nothing happens.
        said.Clear();
        using (SingReport.ListenNotes(said.Add))
        {
            Assert.Empty(SingSettings.Numbers(new Dictionary<string, SingValue> { ["breathiness"] = SingValue.Of(3) }, [], "neutrino/M"));
        }
        Assert.Contains("it takes none", Assert.Single(said));
        Assert.Empty(SingSettings.Numbers(null, Known, "x"));
        SingSettings.Numbers(new Dictionary<string, SingValue> { ["zzz"] = SingValue.Of(1) }, Known, "x");
    }

    // A setting whose value is a NAME (NEUTRINO's support voice): one of the names it lists.
    [Fact]
    public void ASettingThatTakesAName_TakesOneOfItsNames_WhateverTheCase()
    {
        SingParameter[] takes = [new("transpose", SingScope.Phrase, "key", -12, 12, 0), new("support", SingScope.Voice, "model", Word: true, Values: ["KIRITAN", "SOMA"])];
        var said = new List<string>();
        using (SingReport.ListenNotes(said.Add))
        {
            var taken = SingSettings.Taken(new Dictionary<string, SingValue> { ["support"] = SingValue.Of("kiritan"), ["transpose"] = SingValue.Of(3) }, takes, "neutrino/M");
            Assert.Equal(SingValue.Of("KIRITAN"), taken["support"]);
            Assert.Equal(SingValue.Of(3), taken["transpose"]);
            Assert.Equal("|sing support=KIRITAN transpose=3", SingSettings.KeyOf(taken));
            Assert.Empty(said);
            // Not one of its names, a number for a name: said, and left out. The numbers alone do not hold a name.
            Assert.Empty(SingSettings.Taken(new Dictionary<string, SingValue> { ["support"] = SingValue.Of("NOBODY") }, takes, "neutrino/M"));
            Assert.Empty(SingSettings.Taken(new Dictionary<string, SingValue> { ["support"] = SingValue.Of(2) }, takes, "neutrino/M"));
            Assert.Equal(["transpose"], SingSettings.Numbers(new Dictionary<string, SingValue> { ["support"] = SingValue.Of("SOMA"), ["transpose"] = SingValue.Of(-2) }, takes, "neutrino/M").Keys);
        }
        Assert.Contains(said, s => s.Contains("\"support NOBODY\" is none of KIRITAN, SOMA"));
        Assert.Contains(said, s => s.Contains("\"support\" takes a name, not 2"));
        Assert.Equal("", SingSettings.KeyOf(new Dictionary<string, SingValue>()));
    }

    [Fact]
    public void APhraseSungWithASetting_IsAnotherPhrase_AndOneWithNoneIsTheOneKeptBefore()
    {
        Assert.Equal("", SingSettings.KeyOf(new Dictionary<string, double>()));
        string a = SingSettings.KeyOf(new Dictionary<string, double> { ["breathiness"] = 3, ["velocity"] = 1.5 });
        Assert.Equal(a, SingSettings.KeyOf(new Dictionary<string, double> { ["velocity"] = 1.5, ["breathiness"] = 3 }));
        Assert.NotEqual(a, SingSettings.KeyOf(new Dictionary<string, double> { ["breathiness"] = 4, ["velocity"] = 1.5 }));
    }

    private static SungNote Note(double at, double length = 1) => new(at, length, 60, "ら");
    private static Dictionary<string, SingValue> Set(string name, double value) => new() { [name] = SingValue.Of(value) };

    // Written inside the music a setting holds, as a dynamic does, until the next one for its name.
    [Fact]
    public void AValueWrittenOnANote_HoldsUntilTheNextOne_AndDefaultGivesItBack()
    {
        var score = new SungScore(
        [
            Note(0), Note(1) with { Sing = Set("breathiness", 6) }, Note(2),
            Note(3) with { Sing = new Dictionary<string, SingValue> { ["breathiness"] = SingValue.Of(SingValue.DefaultWord) } }, Note(4),
        ], Sing: Set("breathiness", 2));
        var notes = SingCurves.Resolve(score, Known, "v").Notes;
        // The score's own value before it is written, the note's from its start, the score's again after "default".
        Assert.Equal([(2, 2), (6, 6), (6, 6), (2, 2), (2, 2)], notes.Select(n => n.Curve!["breathiness"]));
        // A score whose notes write nothing is the same score: nothing to tell its frames.
        var plain = new SungScore([Note(0), Note(1)], Sing: Set("breathiness", 2));
        Assert.Same(plain, SingCurves.Resolve(plain, Known, "v"));
        // With no value of the score's, the voice's own: the one that changes nothing.
        var bare = SingCurves.Resolve(new SungScore([Note(0), Note(1) with { Sing = Set("velocity", 1.5) }]), Known, "v").Notes;
        Assert.Equal([(1, 1), (1.5, 1.5)], bare.Select(n => n.Curve!["velocity"]));
    }

    // An arrival is reached at its note's start, in a line from where the value before was written.
    [Fact]
    public void AnArrival_GlidesFromTheValueBefore_ToItsNotesStart_AcrossNotesAndRests()
    {
        var score = new SungScore(
        [
            Note(0) with { Sing = Set("breathiness", 2) }, Note(1), Note(3),      // a rest from 2 to 3
            Note(4) with { SingTo = Set("breathiness", 10) }, Note(5),
        ]);
        var notes = SingCurves.Resolve(score, Known, "v").Notes;
        var curve = notes.Select(n => n.Curve!["breathiness"]).ToList();
        // From 2 at 0 s to 10 at 4 s: two a second.
        Assert.Equal(2, curve[0].Start, 6);
        Assert.Equal(4, curve[0].End, 4);
        Assert.Equal(4, curve[1].Start, 6);
        Assert.Equal(8, curve[2].Start, 6);
        Assert.Equal((10, 10), (Math.Round(curve[3].Start, 6), Math.Round(curve[3].End, 6)));
        Assert.Equal((10, 10), (Math.Round(curve[4].Start, 6), Math.Round(curve[4].End, 6)));

        // The frames of a phrase follow the notes, and cross the rest in the same line.
        var frames = SingCurves.Frames(notes, "breathiness", 50, 100, 0)!;
        Assert.Equal(2f, frames[0], 3);
        Assert.Equal(5f, frames[15], 3);        // 1.5 s, inside the second note
        Assert.Equal(7f, frames[25], 3);        // 2.5 s, in the rest
        Assert.Equal(10f, frames[45], 3);
        Assert.Null(SingCurves.Frames(notes, "velocity", 50, 100, 0));
        // An arrival with nothing written before it glides from the score's start, from the voice's own value.
        var first = SingCurves.Resolve(new SungScore([Note(0), Note(1), Note(2) with { SingTo = Set("breathiness", 6) }]), Known, "v").Notes;
        Assert.Equal(3, first[1].Curve!["breathiness"].Start, 6);
    }

    // A setting that is one value a phrase (NEUTRINO's transpose) steps where a phrase begins.
    [Fact]
    public async Task ASettingOfOneValueAPhrase_WrittenOnANote_IsThatPhrasesAndTheOnesAfter()
    {
        var engine = new Keyed();
        using var singer = new PhrasedSinger(engine);
        var said = new List<string>();
        using (SingReport.ListenNotes(said.Add))
        {
            // Each phrase at a pitch of its own, so that none is another one kept.
            await singer.SingAsync(new SungScore(
            [
                Note(0), Note(1),                                                                   // phrase 1: the singer line's value
                Note(3) with { Pitch = 62, Sing = Set("transpose", 2) }, Note(4),                   // phrase 2: 2 from its first note
                Note(6) with { Pitch = 64 }, Note(7) with { SingTo = Set("transpose", 5) },         // phrase 3: still 2; the 5 is the next one's, and cannot glide
                Note(9) with { Pitch = 65 },                                                        // phrase 4: 5
                Note(11) with { Pitch = 67, Sing = new Dictionary<string, SingValue> { ["transpose"] = SingValue.Of(SingValue.DefaultWord) } },   // phrase 5: the line's again
            ], Sing: Set("transpose", -1)));
        }
        Assert.Equal(new double?[] { -1, 2, 2, 5, -1 }, engine.Sung.Select(s => s.Sing?["transpose"].Number));
        Assert.Contains(said, s => s.Contains("\"transpose\" is one value a phrase and cannot glide"));
        Assert.Contains(said, s => s.Contains("written inside a phrase it is sung from the next phrase on"));
        // The phrases differ by their setting: the same notes at another value are sung anew, and at the same value kept.
        engine.Sung.Clear();
        await singer.SingAsync(new SungScore([Note(0), Note(1), Note(3) with { Pitch = 62 }, Note(4)], Sing: Set("transpose", -1)));
        Assert.Single(engine.Sung);     // phrase 1 was kept at -1; phrase 2's notes were sung at 2 before, not at -1
    }

    /// <summary>An engine with one setting a phrase, remembering what it was asked.</summary>
    private sealed class Keyed : ISinger, ISingParameters
    {
        public List<SungScore> Sung { get; } = [];
        public string Name => "keyed/voice";
        public IReadOnlyList<SingParameter> Parameters => [new("transpose", SingScope.Phrase, "key", -12, 12, 0)];
        public Task<SungAudio> SingAsync(SungScore score, CancellationToken ct = default)
        {
            Sung.Add(score);
            return Task.FromResult(new SungAudio(new float[(int)Math.Round(score.End * 1000)], 1000));
        }
    }

    [Fact]
    public void WhatTheVoiceCannotChangeInsideTheMusic_IsSaid_AndLeftOut()
    {
        SingParameter[] takes = [.. Known, new("transpose", SingScope.Phrase, "key", -12, 12, 0), new("support", SingScope.Voice, "model")];
        var said = new List<string>();
        SungScore resolved;
        using (SingReport.ListenNotes(said.Add))
        {
            resolved = SingCurves.Resolve(new SungScore(
            [
                Note(0) with { Sing = new Dictionary<string, SingValue> { ["transpose"] = SingValue.Of(2), ["nosuch"] = SingValue.Of(1), ["breathiness"] = SingValue.Of(99) } },
                Note(1) with { SingTo = Set("support", 1) },
            ]), takes, "v");
        }
        // One value a phrase steps with the notes (a phrased singer takes each phrase's first); one for the voice does not move.
        Assert.Equal(["breathiness", "transpose"], resolved.Notes[0].Curve!.Keys.OrderBy(k => k));
        Assert.Equal((12, 12), resolved.Notes[0].Curve!["breathiness"]);
        Assert.Equal((2, 2), resolved.Notes[1].Curve!["transpose"]);
        Assert.Contains(said, s => s.Contains("\"support\" is one value for the voice"));
        Assert.Contains(said, s => s.Contains("does not take the setting \"nosuch\""));
        Assert.Contains(said, s => s.Contains("\"breathiness 99\" is outside -12 to 12; sung as 12"));
    }

    [Fact]
    public void ANotesSettings_AreReadFromTheScore_AndAPhraseWithThemIsAnotherPhrase()
    {
        var score = ScoreJson.Read("""
            { "notes": [
              { "start": 0, "duration": 1, "pitch": 60, "lyric": "ら", "sing": { "breathiness": 6, "tension": "default" } },
              { "start": 1, "duration": 1, "pitch": 62, "lyric": "ら", "singTo": { "breathiness": 9 } } ] }
            """);
        Assert.Equal(SingValue.Of(6), score.Notes[0].Sing!["breathiness"]);
        Assert.True(score.Notes[0].Sing!["tension"].IsDefault);
        Assert.Equal(SingValue.Of(9), score.Notes[1].SingTo!["breathiness"]);
        var again = ScoreJson.Read(ScoreJson.Write(score));
        Assert.Equal(SingValue.Of(9), again.Notes[1].SingTo!["breathiness"]);
        // The key is of what the frames are told, not of how it was written.
        var notes = SingCurves.Resolve(score, Known, "v").Notes;
        Assert.NotEqual("", SingCurves.KeyOf(notes));
        Assert.Equal("", SingCurves.KeyOf(score.Notes));
        Assert.NotEqual(SingCurves.KeyOf(notes), SingCurves.KeyOf(SingCurves.Resolve(score with { Notes = [score.Notes[0], score.Notes[1] with { SingTo = Set("breathiness", 8) }] }, Known, "v").Notes));
    }

    // An engine that sings a score whole takes no setting: it says so, and sings.
    [Fact]
    public async Task AnEngineThatTakesNone_SaysSo_AndSings()
    {
        using var singer = new PhrasedSinger(new Whole());
        Assert.Empty(singer.Parameters);
        var said = new List<string>();
        SungAudio audio;
        using (SingReport.ListenNotes(said.Add))
        {
            audio = await singer.SingAsync(new SungScore([new(0, 1, 60, "ら")], Sing: new Dictionary<string, SingValue> { ["breathiness"] = SingValue.Of(3) }));
        }
        Assert.True(audio.Duration > 0.9);
        Assert.Contains("whole/voice does not take the setting \"breathiness\" (it takes none)", Assert.Single(said));
    }

    private sealed class Whole : ISinger
    {
        public string Name => "whole/voice";
        public Task<SungAudio> SingAsync(SungScore score, CancellationToken ct = default)
            => Task.FromResult(new SungAudio(new float[(int)Math.Round(score.End * 1000)], 1000));
    }
}
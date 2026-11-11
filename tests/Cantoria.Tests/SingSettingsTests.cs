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

    [Fact]
    public void APhraseSungWithASetting_IsAnotherPhrase_AndOneWithNoneIsTheOneKeptBefore()
    {
        Assert.Equal("", SingSettings.KeyOf(new Dictionary<string, double>()));
        string a = SingSettings.KeyOf(new Dictionary<string, double> { ["breathiness"] = 3, ["velocity"] = 1.5 });
        Assert.Equal(a, SingSettings.KeyOf(new Dictionary<string, double> { ["velocity"] = 1.5, ["breathiness"] = 3 }));
        Assert.NotEqual(a, SingSettings.KeyOf(new Dictionary<string, double> { ["breathiness"] = 4, ["velocity"] = 1.5 }));
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
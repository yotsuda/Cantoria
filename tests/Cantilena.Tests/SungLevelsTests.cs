using Xunit;

namespace Cantilena.Tests;

public class SungLevelsTests
{
    private const int Rate = 1000;

    private static SungAudio Flat(double seconds, float value = 0.1f)
        => new(Enumerable.Repeat(value, (int)(seconds * Rate)).ToArray(), Rate);

    private static SungScore Score(params SungNote[] notes) => new(notes, "ja", Rate);

    [Fact]
    public void AScoreWithNoLevels_IsTheSameAudio()
    {
        var audio = Flat(1);
        Assert.Same(audio, SungLevels.Apply(audio, Score(new SungNote(0, 1, 60, "a"))));
    }

    [Fact]
    public void EachNote_HoldsItsLevel()
    {
        var sung = SungLevels.Apply(Flat(2), Score(new SungNote(0, 1, 60, "a", Level: 0.5), new SungNote(1, 1, 60, "a", Level: 2)));
        Assert.Equal(0.05f, sung.Samples[500], 3);
        Assert.Equal(0.2f, sung.Samples[1500], 3);
    }

    [Fact]
    public void AChange_IsEased_NotAStep()
    {
        var sung = SungLevels.Apply(Flat(2), Score(new SungNote(0, 1, 60, "a", Level: 0.5), new SungNote(1, 1, 60, "a", Level: 2)));
        Assert.InRange(sung.Samples[1000], 0.06f, 0.19f);
    }

    [Fact]
    public void ASwell_GoesAcrossItsNote()
    {
        var sung = SungLevels.Apply(Flat(1), Score(new SungNote(0, 1, 60, "a", Level: 1, LevelEnd: 3)));
        Assert.Equal(0.2f, sung.Samples[500], 2);
        Assert.True(sung.Samples[750] > sung.Samples[250]);
    }

    [Fact]
    public void ARest_KeepsTheLevelBefore_AndTheConsonantBeforeTheFirstNote_HasTheFirsts()
    {
        var sung = SungLevels.Apply(Flat(3), Score(new SungNote(0.5, 0.5, 60, "a", Level: 0.5), new SungNote(2, 0.5, 60, "a", Level: 0.5)));
        Assert.Equal(0.05f, sung.Samples[100], 3);
        Assert.Equal(0.05f, sung.Samples[1500], 3);
    }

    [Fact]
    public void TooLoud_BringsTheWholeVoiceDown()
    {
        var sung = SungLevels.Apply(Flat(1, 0.8f), Score(new SungNote(0, 1, 60, "a", Level: 2)));
        Assert.Equal(0.98f, sung.Samples.Max(), 3);
    }

    [Fact]
    public void TheJson_CarriesTheLevels()
    {
        var note = ScoreJson.Read("""{ "notes": [ { "start": 0, "duration": 1, "pitch": 60, "lyric": "a", "level": 0.5, "levelEnd": 0.7 } ] }""").Notes[0];
        Assert.Equal(0.5, note.Level);
        Assert.Equal(0.7, note.LevelEnd);
    }
}

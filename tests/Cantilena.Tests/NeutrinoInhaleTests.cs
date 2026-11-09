using Cantilena.Neutrino;
using Xunit;

namespace Cantilena.Tests;

public class NeutrinoInhaleTests
{
    private static SungScore Score(params SungNote[] notes) => new(notes, "ja", 48000);

    [Fact]
    public void AnInhale_ClosesTheGap_SoTheBreathMarkHasNoRestToFallInto()
    {
        var closed = NeutrinoMusicXml.WithInhalesClosed(Score(
            new SungNote(0, 1.75, 60, "さ", Breath: true, Inhale: true),
            new SungNote(2, 1, 62, "く", Breath: true)));
        Assert.Equal(2, closed.Notes[0].Duration, 6);
        Assert.Equal(1, closed.Notes[1].Duration, 6);
    }

    [Fact]
    public void ABreathWithNoInhale_KeepsItsRest()
    {
        var score = Score(new SungNote(0, 1, 60, "さ", Breath: true), new SungNote(2, 1, 62, "く"));
        Assert.Same(score, NeutrinoMusicXml.WithInhalesClosed(score));
    }

    [Fact]
    public void TheLastNote_HasNothingToCloseTo()
    {
        var score = Score(new SungNote(0, 1, 60, "さ", Breath: true, Inhale: true));
        Assert.Equal(1, NeutrinoMusicXml.WithInhalesClosed(score).Notes[0].Duration, 6);
    }

    [Fact]
    public void TheScoreWritten_HasTheBreathMark_AndNoRestBetween()
    {
        string xml = NeutrinoMusicXml.Write(Score(
            new SungNote(0, 1.75, 60, "さ", Breath: true, Inhale: true),
            new SungNote(2, 2, 62, "く")));
        int mark = xml.IndexOf("<breath-mark", StringComparison.Ordinal);
        int next = xml.IndexOf("<note>", mark, StringComparison.Ordinal);
        Assert.True(mark > 0);
        Assert.DoesNotContain("<rest", xml[next..xml.IndexOf("</note>", next, StringComparison.Ordinal)]);
    }

    [Fact]
    public void TheJson_CarriesIt()
        => Assert.True(ScoreJson.Read("""{ "notes": [ { "start": 0, "duration": 1, "pitch": 60, "lyric": "a", "inhale": true } ] }""").Notes[0].Inhale);
}

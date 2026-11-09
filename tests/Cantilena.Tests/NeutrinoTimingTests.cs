using Cantilena.Neutrino;
using Xunit;

namespace Cantilena.Tests;

public class NeutrinoTimingTests
{
    private static string Full(params (long Start, long End, string Ph)[] rows)
    {
        string prev = "xx";
        var lines = new List<string>();
        foreach (var (start, end, ph) in rows)
        {
            lines.Add($"{start} {end} p@xx^{prev}-{ph}+x=x_xx/A:xx-xx");
            prev = ph;
        }
        return string.Join("\r\n", lines) + "\r\n";
    }

    private static string Timing(params (long Start, long End, string Ph)[] rows)
        => string.Join("\r\n", rows.Select(r => $"{r.Start} {r.End} {r.Ph}")) + "\r\n";

    [Fact]
    public void AJoinOfLikePhonemes_GoesToTheScoresPlace()
    {
        var full = Full((0, 13, "pau"), (13, 46, "N"), (46, 79, "N"), (79, 113, "N"), (113, 120, "pau"));
        var timing = Timing((0, 11, "pau"), (11, 46, "N"), (46, 83, "N"), (83, 111, "N"), (111, 120, "pau"));
        Assert.Equal("0 11 pau\n11 46 N\n46 79 N\n79 111 N\n111 120 pau\n", NeutrinoTiming.AlignRepeats(full, timing));
    }

    [Fact]
    public void AConsonant_KeepsTheModelsPlace()
    {
        var full = Full((0, 10, "pau"), (10, 20, "s"), (20, 40, "a"), (40, 50, "k"), (50, 70, "u"));
        var timing = Timing((0, 9, "pau"), (9, 19, "s"), (19, 38, "a"), (38, 48, "k"), (48, 70, "u"));
        Assert.Equal("0 9 pau\n9 19 s\n19 38 a\n38 48 k\n48 70 u\n", NeutrinoTiming.AlignRepeats(full, timing));
        Assert.False(NeutrinoTiming.HasRepeats(full));
    }

    [Fact]
    public void ALikeVowel_IsARepeat_ButSilenceIsNot()
    {
        Assert.True(NeutrinoTiming.HasRepeats(Full((0, 10, "a"), (10, 20, "a"))));
        Assert.False(NeutrinoTiming.HasRepeats(Full((0, 10, "pau"), (10, 20, "pau"))));
    }

    [Fact]
    public void LabelsThatDoNotLineUp_AreLeftAlone()
    {
        var full = Full((0, 10, "N"), (10, 20, "N"));
        var shorter = Timing((0, 20, "N"));
        Assert.Equal(shorter, NeutrinoTiming.AlignRepeats(full, shorter));
        var other = Timing((0, 12, "a"), (12, 20, "a"));
        Assert.Equal(other, NeutrinoTiming.AlignRepeats(full, other));
    }

    [Fact]
    public void AJoinThatWouldLeaveNoLength_Stays()
    {
        var full = Full((0, 30, "N"), (30, 40, "N"));
        var timing = Timing((0, 10, "N"), (10, 25, "N"));
        Assert.Equal("0 10 N\n10 25 N\n", NeutrinoTiming.AlignRepeats(full, timing));
    }
}

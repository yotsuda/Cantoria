using Xunit;

namespace Cantoria.Tests;

public class ChorusSingerTests
{
    private sealed class Fixed(string name, SungAudio audio) : ISinger, IDisposable
    {
        public string Name => name;
        public int Sang { get; private set; }
        public bool Disposed { get; private set; }

        public Task<SungAudio> SingAsync(SungScore score, CancellationToken ct = default)
        {
            Sang++;
            return Task.FromResult(audio);
        }

        public void Dispose() => Disposed = true;
    }

    [Fact]
    public async Task EachVoiceSings_AndTheNameSaysThemAll()
    {
        var a = new Fixed("a", new SungAudio([0.1f], 8000));
        var b = new Fixed("b", new SungAudio([0.1f], 8000));
        var chorus = new ChorusSinger([a, b]);
        await chorus.SingAsync(new SungScore([]));
        Assert.Equal((1, 1), (a.Sang, b.Sang));
        Assert.Equal("a+b", chorus.Name);
        chorus.Dispose();
        Assert.True(a.Disposed && b.Disposed);
    }

    [Fact]
    public void Voices_AddInPower_FromTheStart()
    {
        var mixed = ChorusSinger.Mix([new SungAudio([0.5f, 0.5f], 8000), new SungAudio([0.5f], 8000)]);
        Assert.Equal(2, mixed.Samples.Length);
        Assert.Equal(1f / MathF.Sqrt(2) * 1f / 1f * 1f, mixed.Samples[0] * 1f / 1f, 4);
        Assert.Equal(0.5f / MathF.Sqrt(2), mixed.Samples[1], 4);
    }

    [Fact]
    public void Rates_MeetAtTheHighest_AndTheTimesStayTogether()
    {
        var mixed = ChorusSinger.Mix([new SungAudio([0f, 1f, 0f], 8000), new SungAudio(new float[8], 16000)]);
        Assert.Equal(16000, mixed.SampleRate);
        Assert.Equal(1f / MathF.Sqrt(2), mixed.Samples[2], 4);     // sample 1 at 8 kHz is sample 2 at 16 kHz
    }

    [Fact]
    public void APeak_IsBroughtWithinRange()
    {
        var mixed = ChorusSinger.Mix([new SungAudio([1f], 8000), new SungAudio([1f], 8000)]);
        Assert.Equal(1f, mixed.Samples[0], 4);
    }
}

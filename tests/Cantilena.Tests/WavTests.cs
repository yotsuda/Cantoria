// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Cantilena.Audio;
using Xunit;

namespace Cantilena.Tests;

public class WavTests
{
    [Fact]
    public void RoundTrips_16BitMono()
    {
        var samples = Enumerable.Range(0, 1000).Select(i => MathF.Sin(i * 0.05f) * 0.8f).ToArray();
        var bytes = Wav.Write(new SungAudio(samples, 48000));
        Assert.Equal(44 + 2000, bytes.Length);
        var back = Wav.Read(bytes);
        Assert.Equal(48000, back.SampleRate);
        Assert.Equal(samples.Length, back.Samples.Length);
        for (int i = 0; i < samples.Length; i++)
        {
            Assert.Equal(samples[i], back.Samples[i], 1e-4f);
        }
    }

    [Fact]
    public void Clips_WhatIsOutOfRange()
    {
        var back = Wav.Read(Wav.Write(new SungAudio([2f, -2f, 0f], 8000)));
        Assert.Equal(1f, back.Samples[0], 1e-4f);
        Assert.Equal(-1f, back.Samples[1], 1e-4f);
    }

    [Fact]
    public void RejectsWhatIsNotWave()
    {
        Assert.Throws<InvalidDataException>(() => Wav.Read("hello, this is not a wave file"u8));
    }
}

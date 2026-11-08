// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Cantilena;

/// <summary>Mono audio a singer produced: samples in −1..1 at <see cref="SampleRate"/>.</summary>
public sealed record SungAudio(float[] Samples, int SampleRate)
{
    /// <summary>Seconds of audio.</summary>
    public double Duration => (double)Samples.Length / SampleRate;
}

/// <summary>
/// A voice that sings a <see cref="SungScore"/>. Each backend (NEUTRINO as a process,
/// DiffSinger in-process) is one implementation; the voice it sings with is chosen when
/// the singer is made.
/// </summary>
public interface ISinger
{
    /// <summary>The backend and voice, for messages: "neutrino/MERROW".</summary>
    string Name { get; }

    /// <summary>
    /// Sings the score. The audio starts at the score's time 0 and lasts at least to
    /// <see cref="SungScore.End"/>; a backend may add a release tail.
    /// </summary>
    /// <exception cref="SingerException">The backend could not sing; the message says why.</exception>
    Task<SungAudio> SingAsync(SungScore score, CancellationToken ct = default);
}

/// <summary>A backend's failure, with the backend's own words.</summary>
public sealed class SingerException(string message, Exception? inner = null) : Exception(message, inner);

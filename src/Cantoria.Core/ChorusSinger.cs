// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Cantoria;

/// <summary>
/// Several voices singing one score together (<c>neutrino/MERROW+diffsinger/TIGER</c>): each sings
/// it alone, one after another, and the sounds are laid over each other from the score's time 0.
/// </summary>
public sealed class ChorusSinger(IReadOnlyList<ISinger> singers) : ISinger, IDisposable
{
    public string Name => string.Join("+", singers.Select(s => s.Name));

    public async Task<SungAudio> SingAsync(SungScore score, CancellationToken ct = default)
    {
        // Each voice sings the whole score and counts from 0: the listener of the chorus hears one
        // count of them all (SingReport.Together).
        var sung = new List<SungAudio>(singers.Count);
        using var heard = SingReport.ListenTogether();
        foreach (var singer in singers)
        {
            sung.Add(await singer.SingAsync(score, ct).ConfigureAwait(false));
        }
        return Mix(sung);
    }

    /// <summary>The sounds together at the highest rate among them: voices that do not move as one add
    /// in power, so each is scaled by 1/√n to keep the level of one, and a peak is brought within −1..1.</summary>
    public static SungAudio Mix(IReadOnlyList<SungAudio> sounds)
    {
        int rate = sounds.Max(s => s.SampleRate);
        var at = sounds.Select(s => Resample(s, rate)).ToList();
        var mixed = new float[at.Max(s => s.Length)];
        float gain = 1f / MathF.Sqrt(sounds.Count);
        foreach (var samples in at)
        {
            for (int i = 0; i < samples.Length; i++)
            {
                mixed[i] += samples[i] * gain;
            }
        }
        float peak = mixed.Length == 0 ? 0 : mixed.Max(MathF.Abs);
        if (peak > 1f)
        {
            for (int i = 0; i < mixed.Length; i++)
            {
                mixed[i] /= peak;
            }
        }
        return new SungAudio(mixed, rate);
    }

    private static float[] Resample(SungAudio sound, int rate)
    {
        if (sound.SampleRate == rate || sound.Samples.Length == 0)
        {
            return sound.Samples;
        }
        double step = (double)sound.SampleRate / rate;
        var result = new float[(int)Math.Floor((sound.Samples.Length - 1) / step) + 1];
        for (int i = 0; i < result.Length; i++)
        {
            double position = i * step;
            int before = (int)position;
            int after = Math.Min(before + 1, sound.Samples.Length - 1);
            float t = (float)(position - before);
            result[i] = sound.Samples[before] * (1 - t) + sound.Samples[after] * t;
        }
        return result;
    }

    public void Dispose()
    {
        foreach (var singer in singers)
        {
            (singer as IDisposable)?.Dispose();
        }
    }
}

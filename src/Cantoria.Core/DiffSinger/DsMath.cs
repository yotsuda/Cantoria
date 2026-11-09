// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.
//
// The frame rounding, the word division, the duration fitting and the sampling speedup follow
// OpenUtau's OpenUtau.Core/DiffSinger/DiffSingerUtils.cs and DiffSingerBasePhonemizer.cs
// (MIT License, Copyright (c) 2019 Stakira); see THIRD-PARTY-NOTICES.md.

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Cantoria.DiffSinger;

/// <summary>The arithmetic the DiffSinger models share, as the training data had it.</summary>
public static class DsMath
{
    /// <summary>
    /// Milliseconds to whole frames by cumulative rounding — <c>round(sum / frame + 0.5)</c> at each
    /// boundary, differences between boundaries — so the frames of a sequence add up to the frames
    /// of its total, and never to one more.
    /// </summary>
    public static int[] Frames(IEnumerable<double> ms, double frameMs)
    {
        var result = new List<int>();
        double acc = 0;
        int prev = 0;
        foreach (var m in ms)
        {
            if (m < 0)
            {
                throw new InvalidDataException($"a negative duration: {m} ms");
            }
            acc += m;
            int frame = (int)Math.Round(acc / frameMs + 0.5, MidpointRounding.ToEven);
            result.Add(frame - prev);
            prev = frame;
        }
        return result.ToArray();
    }

    /// <summary>Makes <paramref name="durations"/> sum to <paramref name="total"/> by moving the difference onto the last (then earlier) entries.</summary>
    public static int[] FitSum(int[] durations, int total)
    {
        if (durations.Length == 0)
        {
            return durations;
        }
        var r = (int[])durations.Clone();
        r[^1] += total - r.Sum();
        if (r[^1] < 0)
        {
            int deficit = -r[^1];
            r[^1] = 0;
            for (int i = r.Length - 2; i >= 0 && deficit > 0; i--)
            {
                int take = Math.Min(r[i], deficit);
                r[i] -= take;
                deficit -= take;
            }
            if (deficit > 0)
            {
                throw new InvalidDataException($"cannot fit the durations into {total} frames");
            }
        }
        return r;
    }

    /// <summary>
    /// The start of each item when <paramref name="lengths"/> scaled by <paramref name="ratio"/>
    /// are laid end to end so that they finish at <paramref name="endPos"/>.
    /// </summary>
    public static List<double> Stretch(IReadOnlyList<double> lengths, double ratio, double endPos)
    {
        double sum = endPos - lengths.Sum() * ratio;
        var result = new List<double>(lengths.Count);
        foreach (var x in lengths)
        {
            result.Add(sum);
            sum += x * ratio;
        }
        return result;
    }

    /// <summary>The largest speedup that gives at least <paramref name="steps"/> steps and divides 1000.</summary>
    public static long Speedup(int steps)
    {
        long s = Math.Max(1, 1000 / Math.Max(1, steps));
        while (1000 % s != 0 && s > 1)
        {
            s--;
        }
        return s;
    }

    /// <summary>A curve resampled to another length, linearly.</summary>
    public static float[] Resample(float[] curve, int length)
    {
        if (curve.Length == length)
        {
            return curve;
        }
        var r = new float[length];
        for (int i = 0; i < length; i++)
        {
            double x = length > 1 ? (double)i / (length - 1) * (curve.Length - 1) : 0;
            int x0 = (int)x;
            int x1 = Math.Min(x0 + 1, curve.Length - 1);
            r[i] = (float)(curve[x0] + (curve[x1] - curve[x0]) * (x - x0));
        }
        return r;
    }

    /// <summary>
    /// The word division the linguistic encoder takes in word mode: a word runs from one vowel's
    /// segment to the next (the consonants before a vowel belong to it), the tail silence is a
    /// word of its own; <c>wordDur</c> sums the frames of each. <paramref name="isVowelSegment"/>
    /// is true for a sung phoneme that is a vowel — not for the head, tail or gap silences.
    /// </summary>
    public static (long[] WordDiv, long[] WordDur) WordDivAndDur(IReadOnlyList<bool> isVowelSegment, int[] frames)
    {
        int count = isVowelSegment.Count;
        if (count < 2 || frames.Length != count)
        {
            throw new InvalidDataException("word division needs the padded segments and one frame count each");
        }
        var vowels = Enumerable.Range(0, count - 1).Where(i => isVowelSegment[i]).ToArray();
        if (vowels.Length == 0)
        {
            vowels = [count - 2];
        }
        int tail = count - 1;
        var div = vowels.Zip(vowels.Skip(1), (a, b) => (long)(b - a)).Prepend(vowels[0]).Append(tail - vowels[^1]).Append(1L).ToArray();
        if (div.Any(d => d <= 0) || div.Sum() != count)
        {
            throw new InvalidDataException("the word division does not cover the segments");
        }
        var dur = new long[div.Length];
        int offset = 0;
        for (int i = 0; i < div.Length; i++)
        {
            for (int j = 0; j < div[i]; j++)
            {
                dur[i] += frames[offset + j];
            }
            offset += (int)div[i];
        }
        return (div, dur);
    }

    /// <summary>MIDI note number to Hz.</summary>
    public static float ToHz(float midi) => (float)(440.0 * Math.Pow(2, (midi - 69) / 12.0));

    public static NamedOnnxValue Row<T>(string name, T[] values) =>
        NamedOnnxValue.CreateFromTensor(name, new DenseTensor<T>(values, [1, values.Length]));

    public static NamedOnnxValue Scalar<T>(string name, T value) =>
        NamedOnnxValue.CreateFromTensor(name, new DenseTensor<T>(new[] { value }, [1]));

    /// <summary>
    /// Runs a model with the inputs it declares: inputs the model has not are dropped (a voice
    /// without speakers, say), an input it wants and was not given is an error naming it.
    /// </summary>
    public static List<NamedOnnxValue> Run(InferenceSession session, IEnumerable<NamedOnnxValue> inputs, string who)
    {
        var wanted = session.InputNames.ToHashSet();
        var given = inputs.Where(i => wanted.Contains(i.Name)).ToList();
        var missing = wanted.Except(given.Select(i => i.Name)).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
        {
            throw new SingerException($"the {who} model wants inputs this engine does not give: {string.Join(", ", missing)}");
        }
        return session.Run(given).Cast<NamedOnnxValue>().ToList();
    }
}

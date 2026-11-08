// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Cantilena.Tests;

/// <summary>
/// The ear of the tests: the pitch a rendered voice carries during each note (normalised
/// autocorrelation over 46 ms windows, the median), against the note written.
/// </summary>
public static class PitchCheck
{
    public sealed record NoteResult(int Index, double Written, double? Sung, double Voiced);

    public static List<NoteResult> Of(SungAudio audio, SungScore score)
    {
        var results = new List<NoteResult>();
        int rate = audio.SampleRate, window = 2048, hop = 512;
        for (int i = 0; i < score.Notes.Count; i++)
        {
            var n = score.Notes[i];
            int a = (int)((n.Start + 0.05) * rate), b = (int)((n.End - 0.02) * rate);
            var f0s = new List<double>();
            int windows = 0;
            for (int s = a; s + window <= Math.Min(b, audio.Samples.Length); s += hop)
            {
                windows++;
                double f = Pitch(audio.Samples, s, window, rate);
                if (f > 0)
                {
                    f0s.Add(f);
                }
            }
            double? sung = null;
            if (f0s.Count > 0)
            {
                f0s.Sort();
                sung = 69 + 12 * Math.Log2(f0s[f0s.Count / 2] / 440.0);
            }
            results.Add(new NoteResult(i, n.Pitch, sung, windows == 0 ? 0 : (double)f0s.Count / windows));
        }
        return results;
    }

    private static double Pitch(float[] x, int start, int n, int rate)
    {
        double mean = 0;
        for (int i = 0; i < n; i++)
        {
            mean += x[start + i];
        }
        mean /= n;
        double e = 0;
        for (int i = 0; i < n; i++)
        {
            double v = x[start + i] - mean;
            e += v * v;
        }
        if (Math.Sqrt(e / n) < 0.005)
        {
            return 0;
        }
        int minLag = rate / 1000, maxLag = rate / 70;
        double best = 0;
        int bestLag = 0;
        for (int lag = minLag; lag <= maxLag && lag < n / 2; lag++)
        {
            double r = Correlation(x, start, n, lag, mean);
            if (r > best)
            {
                best = r;
                bestLag = lag;
            }
        }
        if (best < 0.6 || bestLag == 0)
        {
            return 0;
        }
        int half = bestLag / 2;
        if (half >= minLag && Correlation(x, start, n, half, mean) > best * 0.9)
        {
            bestLag = half;   // the octave above, nearly as strong: the true period
        }
        return (double)rate / bestLag;
    }

    private static double Correlation(float[] x, int start, int n, int lag, double mean)
    {
        double c = 0, e1 = 0, e2 = 0;
        for (int i = 0; i + lag < n; i++)
        {
            double u = x[start + i] - mean, v = x[start + i + lag] - mean;
            c += u * v;
            e1 += u * u;
            e2 += v * v;
        }
        return c / Math.Sqrt(e1 * e2 + 1e-12);
    }
}

namespace Cantilena;

/// <summary>
/// The loudness a score asks of its notes, applied to the audio an engine sang. Measured on NEUTRINO
/// (Lily# session 1183): a dynamic in its MusicXML reaches the labels and leaves the loudness where
/// it was, pp and ff alike, so a marking has to be heard by scaling what was sung. Each note holds
/// its level, or goes from <see cref="SungNote.Level"/> to <see cref="SungNote.LevelEnd"/> across
/// itself; a rest keeps the level before it; a change between notes is eased over a few milliseconds.
/// </summary>
public static class SungLevels
{
    /// <summary>The window a change of level is eased over.</summary>
    private const double EaseSeconds = 0.04;

    /// <summary>The loudest a scaled sample may be: louder, and the whole voice comes down to it.</summary>
    private const float Ceiling = 0.98f;

    /// <summary><paramref name="audio"/> with the score's levels on it; the same audio when the score writes none.</summary>
    public static SungAudio Apply(SungAudio audio, SungScore score)
    {
        if (audio.Samples.Length == 0 || score.Notes.All(n => n.Level is null && n.LevelEnd is null))
        {
            return audio;
        }
        var gains = Eased(GainsOf(audio.Samples.Length, audio.SampleRate, score.Notes), (int)(EaseSeconds * audio.SampleRate));
        var samples = new float[audio.Samples.Length];
        float peak = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = audio.Samples[i] * gains[i];
            peak = Math.Max(peak, Math.Abs(samples[i]));
        }
        if (peak > Ceiling)
        {
            float scale = Ceiling / peak;
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] *= scale;
            }
        }
        return audio with { Samples = samples };
    }

    private static float[] GainsOf(int count, int rate, IReadOnlyList<SungNote> notes)
    {
        var gains = new float[count];
        int next = 0;
        double held = notes[0].Level ?? 1;      // what the audio before the first note, a consonant among it, has
        for (int i = 0; i < count; i++)
        {
            double t = (double)i / rate;
            while (next < notes.Count && notes[next].End <= t)
            {
                held = notes[next].LevelEnd ?? notes[next].Level ?? 1;
                next++;
            }
            if (next < notes.Count && notes[next].Start <= t)
            {
                var n = notes[next];
                double from = n.Level ?? 1, to = n.LevelEnd ?? from;
                gains[i] = (float)(from + ((to - from) * (t - n.Start) / n.Duration));
            }
            else
            {
                gains[i] = (float)held;
            }
        }
        return gains;
    }

    /// <summary>A moving average over <paramref name="window"/> samples, centred.</summary>
    private static float[] Eased(float[] gains, int window)
    {
        if (window < 2)
        {
            return gains;
        }
        var eased = new float[gains.Length];
        double sum = 0;
        int inWindow = 0;
        int half = window / 2;
        for (int i = 0, head = 0, tail = 0; i < gains.Length; i++)
        {
            for (; head < gains.Length && head <= i + half; head++, inWindow++)
            {
                sum += gains[head];
            }
            for (; tail < i - half; tail++, inWindow--)
            {
                sum -= gains[tail];
            }
            eased[i] = (float)(sum / inWindow);
        }
        return eased;
    }
}

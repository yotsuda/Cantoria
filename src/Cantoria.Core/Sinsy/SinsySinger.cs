// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Cantoria.Neutrino;

namespace Cantoria.Sinsy;

/// <summary>
/// Sings with a Sinsy voice (HMM, hts_engine) in this process: no GPU, no model to wait for, a
/// small library and a voice of a few megabytes, so it sings on any machine. Japanese only, read
/// from kana by Sinsy's own dictionary.
/// <para>The score goes in note by note at a constant 60 to the quarter with Sinsy's 960 ticks a
/// quarter: one tick is 1/960 s and nothing is lost, as with NEUTRINO's MusicXML. Each note starts
/// and ends on the tick nearest its time, so no rounding adds up. A second of rest leads in (Sinsy
/// lets a first consonant start before its note) and is cut from what it sings. A melisma note gets
/// <c>ー</c> (the dictionary's MACRON: the vowel goes on); a breath or an inhale is a breath mark.
/// The pitch is the nearest semitone. Level, vibrato, bend and the voice settings are not Sinsy's.</para>
/// </summary>
public sealed class SinsySinger : ISinger, IDisposable
{
    /// <summary>Sinsy's ticks a quarter note (its BASE_DIVISIONS).</summary>
    public const int TicksPerQuarter = 960;

    /// <summary>Quarter notes a minute: one quarter is one second.</summary>
    public const double Tempo = 60;

    /// <summary>The rest before the first note, cut from the audio.</summary>
    public const double LeadInSeconds = 1;

    private readonly SinsyOptions _options;
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private IntPtr _handle;
    private bool _disposed;

    public SinsySinger(SinsyOptions options)
    {
        _options = options;
    }

    public string Name => $"sinsy/{_options.Name}";

    /// <summary>Ticks for a time in seconds.</summary>
    public static int Ticks(double seconds) => (int)Math.Round(seconds * TicksPerQuarter * Tempo / 60);

    public async Task<SungAudio> SingAsync(SungScore score, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var problems = score.Validate();
        if (problems.Count > 0)
        {
            throw new SingerException("the score is not sound: " + string.Join("; ", problems));
        }
        if (NeutrinoMusicXml.NotKanaProblem(score, "Sinsy") is { } notKana)
        {
            throw new SingerException(notKana);
        }
        await _oneAtATime.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => Sing(score), ct).ConfigureAwait(false);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    /// <summary>
    /// The semitones the notes are moved to come inside the voice's range (<see cref="SinsyOptions.Low"/>
    /// to <see cref="SinsyOptions.High"/>); the voice sings them there and its pitch is moved back by as
    /// much, so what is heard is what is written. An HMM voice sings only the range it learnt: under it,
    /// every note comes out at its floor (nitech_jp_song070_f001 sang C3 and F3 alike at G#3,
    /// 2026-11-28). A score wider than the range is centred on it. 0 for a score inside it.
    /// </summary>
    public static int Shift(SungScore score, int low, int high)
    {
        var pitches = score.Notes.Select(n => (int)Math.Round(n.Pitch)).ToList();
        if (pitches.Count == 0)
        {
            return 0;
        }
        int lo = pitches.Min(), hi = pitches.Max();
        if (lo >= low && hi <= high)
        {
            return 0;
        }
        if (hi - lo > high - low)
        {
            return (int)Math.Round((low + high) / 2.0 - (lo + hi) / 2.0);
        }
        return lo < low ? low - lo : high - hi;
    }

    /// <summary>What is kept after the score's end: the last vowel's release.</summary>
    public const double TailSeconds = 0.5;

    private SungAudio Sing(SungScore score)
    {
        var h = Handle();
        int shift = Shift(score, _options.Low, _options.High);
        Check(SinsyNative.Clear(h), h, "clear the score");
        Check(SinsyNative.Tempo(h, Tempo), h, "take the tempo");
        int lead = Ticks(LeadInSeconds);
        int at = 0;
        Check(SinsyNative.Rest(h, lead), h, "take the lead-in");
        for (int i = 0; i < score.Notes.Count; i++)
        {
            var n = score.Notes[i];
            int start = Ticks(n.Start), end = Math.Max(start + 1, Ticks(n.End));
            if (start > at)
            {
                Check(SinsyNative.Rest(h, start - at), h, $"take the rest before note {i}");
            }
            else
            {
                start = at;   // a note rounded onto the one before it starts where that one ends
                end = Math.Max(end, start + 1);
            }
            string lyric = n.Melisma ? NeutrinoMusicXml.MelismaLyric : n.Lyric ?? "";
            int pitch = (int)Math.Round(n.Pitch) + shift - 12;   // Sinsy's C4 is 48
            Check(SinsyNative.Note(h, end - start, lyric, pitch, 0, 0, 0, n.Breath || n.Inhale ? 1 : 0), h, $"take note {i} at {n.Start:0.###} s");
            at = end;
        }
        int count = SinsyNative.Synthesize(h, -shift);
        if (count < 0)
        {
            throw new SingerException($"Sinsy did not sing: {Words(SinsyNative.Messages(h))}");
        }
        var samples = new float[count];
        SinsyNative.Samples(h, samples, count);
        int rate = SinsyNative.SampleRate(h);
        // The lead-in is cut, and the rest Sinsy fills the last bar with (its bars are 4 s here).
        int from = Math.Min(count, (int)Math.Round(LeadInSeconds * rate));
        int to = Math.Clamp((int)Math.Round((LeadInSeconds + score.End + TailSeconds) * rate), from, count);
        return new SungAudio(samples[from..to], rate);
    }

    /// <summary>The library's Sinsy with the voice, opened on first use and kept.</summary>
    private IntPtr Handle()
    {
        if (_handle != IntPtr.Zero)
        {
            return _handle;
        }
        var problems = _options.Validate();
        if (problems.Count > 0)
        {
            throw new SingerException(string.Join("; ", problems));
        }
        try
        {
            _handle = SinsyNative.Open(_options.DictionaryFolder, _options.VoiceFile);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            throw new SingerException($"the Sinsy engine could not be loaded ({SinsyOptions.LibraryFile}): {e.Message}", e);
        }
        if (_handle == IntPtr.Zero)
        {
            throw new SingerException($"Sinsy could not read the voice {_options.VoiceFile}: {Words(SinsyNative.LastOpenError())}");
        }
        return _handle;
    }

    private static void Check(int ok, IntPtr h, string what)
    {
        if (ok == 0)
        {
            throw new SingerException($"Sinsy could not {what}: {Words(SinsyNative.Messages(h))}");
        }
    }

    /// <summary>Sinsy's messages on one line, or a word that it said nothing.</summary>
    private static string Words(string messages)
    {
        var t = string.Join(" ", messages.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return t.Length == 0 ? "(it gave no reason)" : t;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        if (_handle != IntPtr.Zero)
        {
            SinsyNative.Close(_handle);
            _handle = IntPtr.Zero;
        }
        _oneAtATime.Dispose();
    }
}

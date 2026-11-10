// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cantoria.Audio;
using Cantoria.DiffSinger;

namespace Cantoria.Voicevox;

/// <summary>A singing style of the running VOICEVOX: <c>ずんだもん-ノーマル</c> and the engine's id for it.</summary>
public sealed record VoicevoxVoice(string Name, string Speaker, int StyleId);

/// <summary>What the running VOICEVOX offers: the voices that sing, and the style that writes a score's pitch.</summary>
public sealed record VoicevoxSingers(IReadOnlyList<VoicevoxVoice> Voices, int? Teacher)
{
    /// <summary>The answer of <c>GET /singers</c>.</summary>
    public static VoicevoxSingers Parse(string json)
    {
        var voices = new List<VoicevoxVoice>();
        int? teacher = null;
        foreach (var speaker in JsonNode.Parse(json)!.AsArray())
        {
            string name = (string)speaker!["name"]!;
            foreach (var style in speaker["styles"]!.AsArray())
            {
                string type = (string?)style!["type"] ?? "";
                int id = (int)style["id"]!;
                if (type == "sing")
                {
                    teacher ??= id;
                }
                if (type is "sing" or "frame_decode")
                {
                    voices.Add(new VoicevoxVoice($"{name}-{(string)style["name"]!}", name, id));
                }
            }
        }
        return new VoicevoxSingers(voices, teacher);
    }
}

/// <summary>A note as the engine's score has it; a rest has no key and an empty lyric.</summary>
public sealed record VoicevoxNote(int? Key, int FrameLength, string Lyric);

/// <summary>A <see cref="SungScore"/> as VOICEVOX's score: one mora a note, in frames of 256 samples at 24 kHz.</summary>
public static class VoicevoxScore
{
    public const double FrameRate = 93.75;

    /// <summary>The silence before the score and after it: the engine places a first consonant in
    /// the rest before its note, so a score must begin with one.</summary>
    public const double PadSeconds = 0.5;

    private const string A = "あかさたなはまやらわがざだばぱぁゃ", I = "いきしちにひみりぎじぢびぴぃ", U = "うくすつぬふむゆるぐずづぶぷぅゅゔ",
        E = "えけせてねへめれげぜでべぺぇ", O = "おこそとのほもよろをごぞどぼぽぉょ";

    private static int Frame(double seconds) => (int)Math.Round((seconds + PadSeconds) * FrameRate);

    /// <summary>The frames of silence before the score's time 0.</summary>
    public static int LeadFrames => Frame(0);

    /// <summary>The notes the engine is given, rests between them, starting and ending with one.</summary>
    /// <exception cref="SingerException">A syllable is not written in kana.</exception>
    public static List<VoicevoxNote> Notes(IReadOnlyList<SungNote> notes)
    {
        var result = new List<VoicevoxNote>();
        int cursor = 0;
        string vowel = "あ";
        foreach (var n in notes)
        {
            int start = Frame(n.Start);
            // One frame is rounding, not a rest, and the engine answers 500 to it before a consonant.
            if (start > cursor + 1)
            {
                result.Add(new VoicevoxNote(null, start - cursor, ""));
                cursor = start;
            }
            var morae = new List<string>();
            IReadOnlyList<string> written = n.Melisma ? ["ー"] : KanaPhonemizer.Morae(n.Lyric!);
            foreach (var mora in written)
            {
                if (mora == "ー")
                {
                    morae.Add(vowel);
                }
                else if (mora.All(c => c is >= 'ぁ' and <= 'ゖ'))
                {
                    morae.Add(mora);
                    vowel = VowelOf(mora) ?? vowel;
                }
                else
                {
                    throw new SingerException($"VOICEVOX sings kana, and \"{n.Lyric}\" at {n.Start:0.###} s is not");
                }
            }
            int end = Math.Max(Frame(n.End), cursor + morae.Count);
            int key = (int)Math.Round(n.Pitch);
            int from = cursor;
            for (int i = 0; i < morae.Count; i++)
            {
                int to = from + (end - from) * (i + 1) / morae.Count;
                result.Add(new VoicevoxNote(key, to - cursor, Katakana(morae[i])));
                cursor = to;
            }
            cursor = end;
        }
        result.Add(new VoicevoxNote(null, LeadFrames, ""));
        return result;
    }

    /// <summary>The lowest key the singing style that writes the pitch answers in tune (VOICEVOX 0.26.1,
    /// measured 2026-11-10): below E3 its pitch came 13 to 31 semitones too high, every voice with it.</summary>
    public const int LowestKey = 52;

    /// <summary>How many octaves up the notes must be asked for, so that none is below
    /// <see cref="LowestKey"/>; the answer's pitch is brought down as many. The whole phrase moves
    /// together, so its melody keeps its shape.</summary>
    public static int OctavesUp(IEnumerable<VoicevoxNote> notes)
    {
        int? lowest = notes.Min(n => n.Key);
        return lowest is { } key && key < LowestKey ? (LowestKey - key + 11) / 12 : 0;
    }

    private static string? VowelOf(string mora)
    {
        char last = mora[^1];
        return A.Contains(last) ? "あ" : I.Contains(last) ? "い" : U.Contains(last) ? "う" : E.Contains(last) ? "え" : O.Contains(last) ? "お" : last == 'ん' ? "ん" : null;
    }

    private static string Katakana(string kana) => string.Concat(kana.Select(c => c is >= 'ぁ' and <= 'ゖ' ? (char)(c + 0x60) : c));
}

/// <summary>
/// Sings through a VOICEVOX the user is running, over its local HTTP API: the singing style turns
/// the score into a query of pitches and phonemes, and the chosen voice sings the query. Japanese
/// only. Nothing of VOICEVOX is bundled, and each character has its own terms.
/// </summary>
public sealed class VoicevoxSinger : ISinger, IDisposable
{
    private readonly HttpClient _http;
    private readonly VoicevoxVoice _voice;
    private readonly int _teacher;

    public VoicevoxSinger(HttpClient http, VoicevoxVoice voice, int teacher)
    {
        _http = http;
        _voice = voice;
        _teacher = teacher;
    }

    public string Name => $"voicevox/{_voice.Name}";

    public async Task<SungAudio> SingAsync(SungScore score, CancellationToken ct = default)
    {
        var problems = score.Validate();
        if (problems.Count > 0)
        {
            throw new SingerException("the score is not sound: " + string.Join("; ", problems));
        }
        if (score.Language.Split('-')[0] != "ja")
        {
            throw new SingerException($"{Name} sings Japanese, and these words are {score.Language}");
        }
        var sung = VoicevoxScore.Notes(score.Notes);
        int octaves = VoicevoxScore.OctavesUp(sung);
        var notes = new JsonArray();
        foreach (var n in sung)
        {
            notes.Add(new JsonObject { ["key"] = n.Key + 12 * octaves, ["frame_length"] = n.FrameLength, ["lyric"] = n.Lyric });
        }
        var query = JsonNode.Parse(await Post($"sing_frame_audio_query?speaker={_teacher}", new JsonObject { ["notes"] = notes }.ToJsonString(), ct).ConfigureAwait(false))!;
        if (octaves > 0)
        {
            var f0 = query["f0"]!.AsArray();
            for (int i = 0; i < f0.Count; i++)
            {
                f0[i] = (double)f0[i]! / (1 << octaves);
            }
        }
        query["outputSamplingRate"] = score.SampleRate;
        var audio = Wav.Read(await Post($"frame_synthesis?speaker={_voice.StyleId}", query.ToJsonString(), ct).ConfigureAwait(false));
        int lead = Math.Min(audio.Samples.Length, (int)Math.Round(VoicevoxScore.LeadFrames / VoicevoxScore.FrameRate * audio.SampleRate));
        return new SungAudio(audio.Samples[lead..], audio.SampleRate);
    }

    private async Task<byte[]> Post(string path, string json, CancellationToken ct)
    {
        using var content = new StringContent(json, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        try
        {
            using var answer = await _http.PostAsync(path, content, ct).ConfigureAwait(false);
            if (!answer.IsSuccessStatusCode)
            {
                string body = await answer.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new SingerException($"VOICEVOX refused the score: {Detail(body)}");
            }
            return await answer.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw new SingerException($"VOICEVOX did not answer at {_http.BaseAddress}: is it running? ({e.Message})", e);
        }
    }

    private static string Detail(string body)
    {
        try
        {
            return JsonNode.Parse(body)?["detail"]?.ToString() ?? body;
        }
        catch (JsonException)
        {
            return body;
        }
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>Reaches a running VOICEVOX.</summary>
public static class VoicevoxEngine
{
    public const string DefaultUrl = "http://127.0.0.1:50021";

    /// <summary>A client for the engine at <paramref name="url"/>; synthesis of a long score takes minutes on a CPU.</summary>
    public static HttpClient Client(string url, HttpMessageHandler? handler = null)
    {
        var client = handler is null ? new HttpClient() : new HttpClient(handler);
        client.BaseAddress = new Uri(url.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromMinutes(10);
        return client;
    }

    /// <summary>What the engine at <paramref name="url"/> offers, or null when it does not answer.</summary>
    public static VoicevoxSingers? Singers(string url)
    {
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(url.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(2) };
            return VoicevoxSingers.Parse(client.GetStringAsync("singers").GetAwaiter().GetResult());
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or UriFormatException or InvalidOperationException or NullReferenceException)
        {
            return null;
        }
    }
}

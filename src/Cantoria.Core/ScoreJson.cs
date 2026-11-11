// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cantoria;

/// <summary>
/// The score as JSON — the CLI's input and the contract a caller that does not link to
/// Cantoria writes. Property names are camelCase; absent optional fields take the record
/// defaults; unknown fields are ignored so a newer caller can add hints an older Cantoria
/// does not read.
/// </summary>
public static class ScoreJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Reads a score, a caller's rounding taken out (<see cref="SungScore.Tidy"/>); throws
    /// <see cref="JsonException"/> on malformed JSON.</summary>
    public static SungScore Read(string json)
    {
        var dto = JsonSerializer.Deserialize<ScoreDto>(json, Options)
            ?? throw new JsonException("the score is null");
        var notes = (dto.Notes ?? []).Select(n => new SungNote(
            n.Start, n.Duration, n.Pitch, n.Lyric, n.Melisma ?? false, n.Breath ?? false, n.Level, n.LevelEnd, n.Inhale ?? false, n.Hyphen ?? false,
            n.Vibrato, n.VibratoRate)).ToList();
        return new SungScore(notes, dto.Language ?? "ja", dto.SampleRate ?? 44100, SingOf(dto.Sing)).Tidy();
    }

    /// <summary>The settings of a "sing" object: a number or a word a name; anything else is not one.</summary>
    private static Dictionary<string, SingValue>? SingOf(Dictionary<string, JsonElement>? sing)
    {
        if (sing is null || sing.Count == 0)
        {
            return null;
        }
        var values = new Dictionary<string, SingValue>(StringComparer.Ordinal);
        foreach (var (name, value) in sing)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Number:
                    values[name] = SingValue.Of(value.GetDouble());
                    break;
                case JsonValueKind.String:
                    values[name] = SingValue.Of(value.GetString() ?? "");
                    break;
                default:
                    throw new JsonException($"sing.{name}: a setting is a number or a word");
            }
        }
        return values;
    }

    /// <summary>Writes a score as indented JSON, lyrics unescaped.</summary>
    public static string Write(SungScore score)
    {
        var dto = new ScoreDto
        {
            SampleRate = score.SampleRate,
            Language = score.Language,
            Sing = score.Sing?.ToDictionary(p => p.Key, p => p.Value.Number is { } n
                ? JsonSerializer.SerializeToElement(n) : JsonSerializer.SerializeToElement(p.Value.Word)),
            Notes = score.Notes.Select(n => new NoteDto
            {
                Start = n.Start, Duration = n.Duration, Pitch = n.Pitch, Lyric = n.Lyric,
                Melisma = n.Melisma ? true : null, Breath = n.Breath ? true : null,
                Level = n.Level, LevelEnd = n.LevelEnd, Inhale = n.Inhale ? true : null, Hyphen = n.Hyphen ? true : null,
                Vibrato = n.Vibrato, VibratoRate = n.VibratoRate,
            }).ToList(),
        };
        return JsonSerializer.Serialize(dto, Options);
    }

    private sealed class ScoreDto
    {
        public int? SampleRate { get; set; }
        public string? Language { get; set; }
        public Dictionary<string, JsonElement>? Sing { get; set; }
        public List<NoteDto>? Notes { get; set; }
    }

    private sealed class NoteDto
    {
        public double Start { get; set; }
        public double Duration { get; set; }
        public double Pitch { get; set; }
        public string? Lyric { get; set; }
        public bool? Melisma { get; set; }
        public bool? Breath { get; set; }
        public double? Level { get; set; }
        public double? LevelEnd { get; set; }
        public bool? Inhale { get; set; }
        public bool? Hyphen { get; set; }
        public double? Vibrato { get; set; }
        public double? VibratoRate { get; set; }
    }
}

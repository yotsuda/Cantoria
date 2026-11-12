// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text.RegularExpressions;
using Cantoria.DiffSinger;
using Cantoria.Voicevox;

namespace Cantoria;

/// <summary>
/// A voice as <c>voices</c> lists it, by the names the extension's API gives it: its
/// <c>engine/name</c>, what it calls itself, and what it says of itself where it says.
/// </summary>
/// <param name="Id"><c>engine/name</c>.</param>
/// <param name="Engine"><c>neutrino</c>, <c>diffsinger</c> or <c>voicevox</c>.</param>
/// <param name="Name">The engine's own name for the voice: a folder, a singing style.</param>
/// <param name="Label">What the voice calls itself, else <paramref name="Name"/>.</param>
public sealed record VoiceEntry(string Id, string Engine, string Name, string Label)
{
    /// <summary>The language it sings, as a BCP-47 tag where known; the first of <see cref="Languages"/> when it has several.</summary>
    public string? Language { get; init; }
    /// <summary>Every language it sings, where it says (a DiffSinger voice's dictionaries the engine can read).</summary>
    public IReadOnlyList<string>? Languages { get; init; }
    /// <summary><c>male</c> or <c>female</c>, where the voice says or <see cref="VoiceGenders"/> does.</summary>
    public string? Gender { get; init; }
    /// <summary>The terms it is used under, where the voice or its engine states them.</summary>
    public string? Licence { get; init; }

    /// <summary>Whether the voice says it sings <paramref name="language"/>: its list of languages, else its one.</summary>
    public bool Sings(string language) => Languages?.Contains(language) ?? Language == language;

    /// <summary>The languages it says it sings; none for a voice that does not say.</summary>
    public IReadOnlyList<string> Sung => Languages ?? (Language is null ? [] : [Language]);
}

/// <summary>
/// The voices installed, read where each engine keeps them — the same reading, in the same order,
/// as the extension's <c>voiceEntriesIn</c>, <c>diffSingerEntriesIn</c> and <c>voicevoxEntriesFrom</c>
/// (<c>editors/vscode/src/core.ts</c>): the order is part of the answer, the first voice of a
/// gender being that gender's default.
/// </summary>
public static partial class VoiceEntries
{
    /// <summary>The languages the DiffSinger backend reads a voice's dictionary of.</summary>
    public static readonly string[] DiffSingerLanguages = ["ja", "en", "zh", "ko"];

    /// <summary>NEUTRINO's voices: the folders of <c>model\</c> holding an <c>info.toml</c>, each read for its speaker.</summary>
    public static IReadOnlyList<VoiceEntry> Neutrino(string folder)
    {
        var model = Path.Combine(folder, "model");
        if (!Directory.Exists(model))
        {
            return [];
        }
        return Folders(model, "info.toml").Select(name =>
        {
            var (label, language, gender) = SpeakerOf(ReadOr(Path.Combine(model, name, "info.toml")) ?? "");
            return new VoiceEntry("neutrino/" + name, "neutrino", name, label ?? name)
            {
                Language = LanguageTag(language),
                Gender = gender?.ToLowerInvariant(),
            };
        }).ToList();
    }

    /// <summary>The DiffSinger voices under a folder: its subfolders holding a <c>dsconfig.yaml</c>, and
    /// after each voice its other speakers (<c>TIGER/tiger_disco</c>), the plain name being the first.</summary>
    public static IReadOnlyList<VoiceEntry> DiffSinger(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }
        var entries = new List<VoiceEntry>();
        foreach (var name in Folders(folder, "dsconfig.yaml"))
        {
            var character = ReadOr(Path.Combine(folder, name, "character.yaml"));
            var label = CharacterNameOf(character ?? "") ?? name;
            // The voices sing what their dictionaries spell, in the languages the engine can read through them.
            var languages = DiffSingerLanguages.Where(l => File.Exists(Path.Combine(folder, name, "dsdur", $"dsdict-{l}.yaml"))).ToList();
            var entry = new VoiceEntry("diffsinger/" + name, "diffsinger", name, label)
            {
                Language = languages.Count > 0 ? languages[0] : null,
                Languages = languages.Count > 0 ? languages : null,
                Gender = VoiceGenders.DiffSinger.GetValueOrDefault(name),
            };
            entries.Add(entry);
            var colors = SpeakerColors(character ?? "");
            foreach (var speaker in SpeakersOf(Path.Combine(folder, name, "dsconfig.yaml")).Skip(1))
            {
                entries.Add(entry with { Id = $"diffsinger/{name}/{speaker}", Name = $"{name}/{speaker}", Label = $"{label} ({colors.GetValueOrDefault(speaker, speaker)})" });
            }
        }
        return entries;
    }

    /// <summary>The singing styles of a running VOICEVOX. The credit its terms ask of a published work is the voice's licence.</summary>
    public static IReadOnlyList<VoiceEntry> Voicevox(VoicevoxSingers singers)
        => singers.Voices.Select(v => new VoiceEntry("voicevox/" + v.Name, "voicevox", v.Name, $"{v.Speaker} ({v.Name[Math.Min(v.Name.Length, v.Speaker.Length + 1)..]})")
        {
            Language = "ja",
            Gender = VoiceGenders.Voicevox.GetValueOrDefault(v.Speaker),
            Licence = $"Credit \"VOICEVOX:{v.Speaker}\" and follow the character's terms",
        }).ToList();

    /// <summary>What a NEUTRINO voice's <c>info.toml</c> says of its speaker: <c>[speaker] name = "Merrow"</c>,
    /// <c>language = "Japanese"</c>, <c>gender = "female"</c>; any null when the file does not say.</summary>
    public static (string? Name, string? Language, string? Gender) SpeakerOf(string toml)
    {
        var section = new List<string>();
        bool inside = false;
        foreach (var line in toml.Split('\n'))
        {
            if (line.TrimStart().StartsWith('['))
            {
                if (inside)
                {
                    break;
                }
                inside = line.Trim() == "[speaker]";
            }
            else if (inside)
            {
                section.Add(line);
            }
        }
        string? Value(string key) => section.Select(l => Regex.Match(l, $"^\\s*{key}\\s*=\\s*\"([^\"]*)\"")).Where(m => m.Success)
            .Select(m => m.Groups[1].Value.Trim()).FirstOrDefault() is { Length: > 0 } v ? v : null;
        return (Value("name"), Value("language"), Value("gender"));
    }

    /// <summary>A language as a BCP-47 tag: a name the voices write (<c>Japanese</c>), or a tag already (<c>ja</c>, <c>en-US</c>); null otherwise.</summary>
    public static string? LanguageTag(string? name)
    {
        var n = (name ?? "").Trim();
        return n.ToLowerInvariant() switch
        {
            "" => null,
            "japanese" => "ja",
            "english" => "en",
            "chinese" or "mandarin" => "zh",
            "korean" => "ko",
            _ => TagShape().IsMatch(n) ? n : null,
        };
    }

    /// <summary>What a DiffSinger voice's <c>character.yaml</c> calls it: its <c>name:</c> line, unquoted; null when there is none.</summary>
    public static string? CharacterNameOf(string yaml)
    {
        var m = NameLine().Match(yaml.Replace("\r", ""));
        if (!m.Success)
        {
            return null;
        }
        var v = m.Groups[1].Value.Trim();
        if (v.Length >= 2 && v[0] is '"' or '\'' && v[^1] == v[0])
        {
            v = v[1..^1].Trim();
        }
        return v.Length > 0 ? v : null;
    }

    /// <summary>What a voice's <c>character.yaml</c> calls each speaker: <c>subbanks</c> of <c>color</c> and <c>suffix</c>, by the suffix's last segment.</summary>
    private static Dictionary<string, string> SpeakerColors(string character)
    {
        var colors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in SubBank().Matches(character))
        {
            colors[m.Groups[2].Value.Split('/', '\\')[^1]] = LeadingNumber().Replace(m.Groups[1].Value, "").Trim();
        }
        return colors;
    }

    /// <summary>The speakers a voice's root <c>dsconfig.yaml</c> lists, by the last segment of each; none for a file that cannot be read.</summary>
    private static IReadOnlyList<string> SpeakersOf(string dsconfig)
    {
        try
        {
            return (DsConfig.Load(dsconfig).Speakers ?? []).Select(s => s.Split('/', '\\')[^1]).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or FormatException or ArgumentException or InvalidOperationException)
        {
            return [];
        }
    }

    /// <summary>The subfolders holding <paramref name="file"/>, in the order a JavaScript sort gives their names (by UTF-16 code unit).</summary>
    private static IEnumerable<string> Folders(string folder, string file)
        => Directory.EnumerateDirectories(folder).Where(d => File.Exists(Path.Combine(d, file))).Select(d => Path.GetFileName(d)!).Order(StringComparer.Ordinal);

    private static string? ReadOr(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"^[a-z]{2,3}(-[A-Za-z0-9]{2,8})*$")]
    private static partial Regex TagShape();

    [GeneratedRegex(@"^[ \t]*name[ \t]*:[ \t]*(.+?)[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex NameLine();

    [GeneratedRegex(@"color:\s*['""]?([^'"",}\n]+?)['""]?\s*,\s*suffix:\s*['""]?([^'""}\s]+)")]
    private static partial Regex SubBank();

    [GeneratedRegex(@"^\d+\s+")]
    private static partial Regex LeadingNumber();
}

// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text;

namespace Cantoria.Utau;

/// <summary>
/// One line of <c>oto.ini</c>: where in a recording an alias is, in milliseconds. The sound starts at
/// <see cref="Offset"/>; up to <see cref="Offset"/> + <see cref="Consonant"/> it is never stretched;
/// <see cref="Cutoff"/> ends it (positive: from the file's end; negative: its length from the offset).
/// <see cref="Preutter"/> before the offset's end of the start lies the note's start (the consonant
/// sounds before the beat), and the first <see cref="Overlap"/> of it crossfades with the sound before.
/// </summary>
public sealed record Oto(string File, string Alias, double Offset, double Consonant, double Cutoff, double Preutter, double Overlap);

/// <summary>
/// An UTAU voicebank read: its aliases from every <c>oto.ini</c> in it (the folder's own first, then
/// its subfolders' by name; the first of an alias wins), its <c>prefix.map</c> (a prefix and suffix an
/// alias takes for each pitch, which is how a bank recorded at several pitches picks one), and the
/// name its <c>character.txt</c> gives. Files are UTF-8 where they decode as UTF-8, else Shift_JIS
/// (what UTAU writes).
/// </summary>
public sealed class UtauVoicebank
{
    private readonly Dictionary<string, Oto> _aliases;
    private readonly Dictionary<int, (string Prefix, string Suffix)> _prefixMap;

    private UtauVoicebank(string folder, string? label, Dictionary<string, Oto> aliases, Dictionary<int, (string, string)> prefixMap)
    {
        Folder = folder;
        Label = label;
        _aliases = aliases;
        _prefixMap = prefixMap;
    }

    /// <summary>The voicebank's folder.</summary>
    public string Folder { get; }

    /// <summary>The name in <c>character.txt</c> (<c>name=</c>), or null.</summary>
    public string? Label { get; }

    /// <summary>The aliases it has.</summary>
    public IReadOnlyCollection<string> Aliases => _aliases.Keys;

    /// <summary>The languages it sings, as <see cref="LanguagesOf"/> reads its aliases.</summary>
    public IReadOnlyList<string> Languages => _languages ??= LanguagesOf(_aliases.Keys);

    private IReadOnlyList<string>? _languages;

    /// <summary>The languages a voicebank of these aliases sings: <c>ja</c> where an alias is kana, <c>en</c> where it
    /// has one of an English CVVC bank's (<see cref="UtauEnglishPhonemizer.Markers"/>); <c>ja</c> where neither (a
    /// bank of romaji aliases).</summary>
    public static IReadOnlyList<string> LanguagesOf(IEnumerable<string> aliases)
    {
        bool kana = false, english = false;
        foreach (var alias in aliases)
        {
            kana |= alias.Any(c => c is >= 'ぁ' and <= 'ヿ');
            english |= UtauEnglishPhonemizer.Markers.Contains(alias);
        }
        return kana && english ? ["ja", "en"] : english ? ["en"] : ["ja"];
    }

    /// <summary>The alias's entry as written, or null.</summary>
    public Oto? Get(string alias) => _aliases.GetValueOrDefault(alias);

    /// <summary>The alias's entry for a note at <paramref name="midi"/>: with the prefix and suffix
    /// <c>prefix.map</c> gives that pitch, else as written; null when neither is there.</summary>
    public Oto? Find(string alias, int midi)
    {
        if (_prefixMap.TryGetValue(midi, out var fix) && (fix.Prefix.Length > 0 || fix.Suffix.Length > 0)
            && _aliases.TryGetValue(fix.Prefix + alias + fix.Suffix, out var found))
        {
            return found;
        }
        return _aliases.GetValueOrDefault(alias);
    }

    /// <summary>Reads the voicebank in <paramref name="folder"/>.</summary>
    /// <exception cref="SingerException">It has no <c>oto.ini</c>.</exception>
    public static UtauVoicebank Load(string folder)
    {
        var otos = Directory.EnumerateFiles(folder, "oto.ini", UtauOptions.NoCase)
            .Concat(Directory.EnumerateDirectories(folder).Order(StringComparer.Ordinal)
                .SelectMany(d => Directory.EnumerateFiles(d, "oto.ini", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive, RecurseSubdirectories = true })
                    .Order(StringComparer.Ordinal)))
            .ToList();
        if (otos.Count == 0)
        {
            throw new SingerException($"{folder} is not an UTAU voicebank: it has no oto.ini");
        }
        var aliases = new Dictionary<string, Oto>(StringComparer.Ordinal);
        foreach (var oto in otos)
        {
            foreach (var entry in ParseOto(Text(oto), Path.GetDirectoryName(oto)!))
            {
                aliases.TryAdd(entry.Alias, entry);
            }
        }
        var map = Directory.EnumerateFiles(folder, "prefix.map", UtauOptions.NoCase).FirstOrDefault();
        var character = Directory.EnumerateFiles(folder, "character.txt", UtauOptions.NoCase).FirstOrDefault();
        return new UtauVoicebank(folder, character is null ? null : NameIn(Text(character)), aliases, map is null ? [] : ParsePrefixMap(Text(map)));
    }

    /// <summary>The entries of an <c>oto.ini</c>'s text: <c>file.wav=alias,offset,consonant,cutoff,preutter,overlap</c>,
    /// the file relative to <paramref name="folder"/>. An empty alias is the file's name without its extension;
    /// a number missing or unreadable is 0.</summary>
    public static IEnumerable<Oto> ParseOto(string text, string folder)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            int eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }
            var file = line[..eq].Trim();
            var fields = line[(eq + 1)..].Split(',');
            var alias = fields[0].Trim();
            if (alias.Length == 0)
            {
                alias = Path.GetFileNameWithoutExtension(file);
            }
            double Field(int i) => i < fields.Length && double.TryParse(fields[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
            yield return new Oto(Path.Combine(folder, file.Replace('\\', Path.DirectorySeparatorChar)), alias, Field(1), Field(2), Field(3), Field(4), Field(5));
        }
    }

    /// <summary>A <c>prefix.map</c>'s text: lines of a note name (C1 to B7), a prefix and a suffix, parted by tabs.</summary>
    public static Dictionary<int, (string Prefix, string Suffix)> ParsePrefixMap(string text)
    {
        var map = new Dictionary<int, (string, string)>();
        foreach (var raw in text.Split('\n'))
        {
            var parts = raw.TrimEnd('\r').Split('\t');
            if (parts.Length >= 2 && Midi(parts[0].Trim()) is { } midi)
            {
                map[midi] = (parts[1], parts.Length >= 3 ? parts[2] : "");
            }
        }
        return map;
    }

    private static readonly string[] NoteNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    /// <summary>The MIDI note of a name as UTAU writes it (<c>C4</c> = 60, <c>A#3</c> = 58), or null.</summary>
    public static int? Midi(string name)
    {
        for (int k = NoteNames.Length - 1; k >= 0; k--)
        {
            if (name.StartsWith(NoteNames[k], StringComparison.Ordinal) && int.TryParse(name[NoteNames[k].Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int octave))
            {
                return (octave + 1) * 12 + k;
            }
        }
        return null;
    }

    private static string? NameIn(string text)
        => text.Split('\n').Select(l => l.TrimEnd('\r')).FirstOrDefault(l => l.StartsWith("name=", StringComparison.OrdinalIgnoreCase))?[5..].Trim() is { Length: > 0 } name ? name : null;

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    /// <summary>A file's text: UTF-8 where it decodes as UTF-8, else Shift_JIS.</summary>
    public static string Text(string file) => Decode(File.ReadAllBytes(file));

    /// <summary>Bytes as text: UTF-8 where they decode as UTF-8 (a BOM dropped), else Shift_JIS.</summary>
    public static string Decode(byte[] bytes)
    {
        try
        {
            var s = StrictUtf8.GetString(bytes);
            return s.Length > 0 && s[0] == '﻿' ? s[1..] : s;
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(932).GetString(bytes);
        }
    }
}

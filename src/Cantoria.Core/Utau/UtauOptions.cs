// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Cantoria.Utau;

/// <summary>
/// An UTAU voicebank: a folder of recordings with <c>oto.ini</c> (in it, or in its subfolders for a
/// bank recorded at several pitches), sung by Cantoria's own resampler over the <c>cantoria_world</c>
/// library beside the program.
/// </summary>
/// <param name="Folder">The voicebank's folder.</param>
public sealed record UtauOptions(string Folder)
{
    /// <summary>The voice's name: its folder's.</summary>
    public string Name => Path.GetFileName(Path.TrimEndingDirectorySeparator(Folder));

    /// <summary>The library's file name on this system.</summary>
    public static string LibraryFile =>
        OperatingSystem.IsWindows() ? "cantoria_world.dll" : OperatingSystem.IsMacOS() ? "libcantoria_world.dylib" : "libcantoria_world.so";

    /// <summary>Whether this build carries the engine: the library beside the program.</summary>
    public static bool Built => File.Exists(Path.Combine(AppContext.BaseDirectory, LibraryFile));

    /// <summary>The problems with these options, as messages; empty when the voice looks usable.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (!Built)
        {
            problems.Add($"this build of Cantoria has no UTAU engine ({LibraryFile} is not beside the program; tools/build-native.ps1 builds it)");
        }
        if (!Directory.Exists(Folder))
        {
            problems.Add($"the UTAU voicebank {Folder} does not exist");
        }
        else if (!IsVoicebank(Folder))
        {
            problems.Add($"{Folder} is not an UTAU voicebank: it has no oto.ini, in it or a folder down");
        }
        return problems;
    }

    /// <summary>A folder with <c>oto.ini</c> in it or in one of its subfolders.</summary>
    public static bool IsVoicebank(string folder)
        => Directory.Exists(folder)
           && (Directory.EnumerateFiles(folder, "oto.ini", NoCase).Any()
               || Directory.EnumerateDirectories(folder).Any(d => Directory.EnumerateFiles(d, "oto.ini", NoCase).Any()));

    /// <summary>File names matched as Windows matches them, whatever the system: UTAU's are written so.</summary>
    internal static readonly EnumerationOptions NoCase = new() { MatchCasing = MatchCasing.CaseInsensitive };

    /// <summary>The voicebanks in a folder, by name: each subfolder that is one (<see cref="IsVoicebank"/>),
    /// in the order of their names (by UTF-16 code unit, as the extension sorts).</summary>
    public static IReadOnlyList<string> Voices(string folder)
        => Directory.Exists(folder)
            ? Directory.EnumerateDirectories(folder).Where(IsVoicebank).Select(d => Path.GetFileName(d)!).Order(StringComparer.Ordinal).ToList()
            : [];

    /// <summary>The voicebank of a name in the folder, or null.</summary>
    public static string? Find(string folder, string name)
    {
        if (name.Length == 0 || name.Contains('/') || name.Contains('\\') || name is "." or "..")
        {
            return null;
        }
        var path = Path.Combine(folder, name);
        return IsVoicebank(path) ? path : null;
    }
}

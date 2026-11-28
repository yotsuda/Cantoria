// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Cantoria.Sinsy;

/// <summary>
/// A Sinsy voice: one <c>.htsvoice</c> file (an HMM voice for hts_engine), sung by the
/// <c>cantoria_sinsy</c> library beside the program with Sinsy's Japanese dictionary.
/// </summary>
/// <param name="VoiceFile">The <c>.htsvoice</c> file.</param>
public sealed record SinsyOptions(string VoiceFile)
{
    /// <summary>The voice files' extension.</summary>
    public const string Extension = ".htsvoice";

    /// <summary>Sinsy's Japanese dictionary (<c>japanese.utf_8.table</c> and <c>.conf</c>): the one
    /// built with the library (<c>sinsy-dic</c> beside the program).</summary>
    public string DictionaryFolder { get; init; } = DefaultDictionaryFolder;

    /// <summary><c>sinsy-dic</c> beside the program.</summary>
    public static string DefaultDictionaryFolder => Path.Combine(AppContext.BaseDirectory, "sinsy-dic");

    /// <summary>The lowest MIDI note the voice sings as written; a score under it is sung higher and moved
    /// back down (<see cref="SinsySinger.Shift"/>). Measured on nitech_jp_song070_f001 (2026-11-28): under
    /// G#3 every note came out at G#3, A#3 a little flat; C4 is in tune.</summary>
    public int Low { get; init; } = 60;

    /// <summary>The highest MIDI note the voice sings as written (nitech_jp_song070_f001: E5).</summary>
    public int High { get; init; } = 76;

    /// <summary>The voice's name: its file's, without the extension.</summary>
    public string Name => Path.GetFileNameWithoutExtension(VoiceFile);

    /// <summary>The library's file name on this system.</summary>
    public static string LibraryFile =>
        OperatingSystem.IsWindows() ? "cantoria_sinsy.dll" : OperatingSystem.IsMacOS() ? "libcantoria_sinsy.dylib" : "libcantoria_sinsy.so";

    /// <summary>Whether this build carries the engine: the library and its dictionary beside the program.</summary>
    public static bool Built =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, LibraryFile)) && File.Exists(Path.Combine(DefaultDictionaryFolder, "japanese.utf_8.table"));

    /// <summary>The problems with these options, as messages; empty when the voice looks usable.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (!Built)
        {
            problems.Add($"this build of Cantoria has no Sinsy engine ({LibraryFile} and sinsy-dic are not beside the program; tools/build-native.ps1 builds them)");
        }
        else if (!File.Exists(Path.Combine(DictionaryFolder, "japanese.utf_8.table")))
        {
            problems.Add($"Sinsy's dictionary is not in {DictionaryFolder}");
        }
        if (!File.Exists(VoiceFile))
        {
            problems.Add($"the Sinsy voice {VoiceFile} does not exist");
        }
        return problems;
    }

    /// <summary>
    /// The voices in a folder, by name: each <c>.htsvoice</c> in it or one folder down (a voice's
    /// release unpacked as it comes: <c>hts_voice_nitech_jp_song070_f001-0.90/nitech_jp_song070_f001.htsvoice</c>,
    /// beside its COPYING), in the order of their names (by UTF-16 code unit, as the extension sorts).
    /// Of two files of one name, the one in the folder itself, else the first folder's, is the voice.
    /// </summary>
    public static IReadOnlyList<string> Voices(string folder) => Files(folder).Keys.Order(StringComparer.Ordinal).ToList();

    /// <summary>The voice file of a name in the folder, or null.</summary>
    public static string? Find(string folder, string name) => Files(folder).GetValueOrDefault(name);

    private static Dictionary<string, string> Files(string folder)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(folder))
        {
            return found;
        }
        var files = Directory.EnumerateFiles(folder, "*" + Extension).Order(StringComparer.Ordinal)
            .Concat(Directory.EnumerateDirectories(folder).Order(StringComparer.Ordinal)
                .SelectMany(d => Directory.EnumerateFiles(d, "*" + Extension).Order(StringComparer.Ordinal)));
        foreach (var file in files)
        {
            found.TryAdd(Path.GetFileNameWithoutExtension(file), file);
        }
        return found;
    }
}

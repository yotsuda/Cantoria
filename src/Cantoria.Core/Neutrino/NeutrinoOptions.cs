// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Cantoria.Neutrino;

/// <summary>
/// Where the user's NEUTRINO is and how to run it. <paramref name="Folder"/> is the folder that
/// holds <c>bin\</c>, <c>model\</c> and <c>settings\</c> (NEUTRINO Tau v3.2.x, Windows);
/// <paramref name="Voice"/> is a folder name under <c>model\</c>, e.g. <c>MERROW</c>.
/// </summary>
public sealed record NeutrinoOptions(string Folder, string Voice = "MERROW")
{
    /// <summary>CPU threads for the parts NEUTRINO runs on the CPU (<c>-n</c>).</summary>
    public int Threads { get; init; } = 4;

    /// <summary>Ask for the GPU (<c>-m</c>); NEUTRINO falls back to the CPU without one.</summary>
    public bool UseGpu { get; init; } = true;

    /// <summary>The first render loads the models (about 45 s on a GPU, longer on a CPU).</summary>
    public TimeSpan FirstRenderTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Renders after the first (about 0.1× real time on a GPU; slower on a CPU).</summary>
    public TimeSpan RenderTimeout { get; init; } = TimeSpan.FromMinutes(3);

    /// <summary>Leave the MusicXML, labels and WAV in the work folder, for a look.</summary>
    public bool KeepFiles { get; init; }

    /// <summary>The executables' folder.</summary>
    public string BinDir => Path.Combine(Folder, "bin");

    /// <summary>The voice's folder, as NEUTRINO wants it: relative to <see cref="Folder"/>, trailing separator.</summary>
    public string VoiceDirArgument => $"model\\{Voice}\\";

    /// <summary>The problems with these options, as messages; empty when NEUTRINO looks usable.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (!Directory.Exists(Folder))
        {
            problems.Add($"NEUTRINO folder not found: {Folder}");
            return problems;
        }
        foreach (var exe in new[] { "neutrino_server.exe", "neutrino_client.exe", "musicXMLtoLabel.exe" })
        {
            if (!File.Exists(Path.Combine(BinDir, exe)))
            {
                problems.Add($"{exe} not found under {BinDir} (NEUTRINO Tau v3.2.x's bin folder is expected)");
            }
        }
        if (!Directory.Exists(Path.Combine(Folder, "settings", "dic")))
        {
            problems.Add($"settings\\dic not found under {Folder}");
        }
        if (string.IsNullOrWhiteSpace(Voice) || Voice.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            problems.Add($"voice must be a folder name under model\\, not \"{Voice}\"");
        }
        else if (!File.Exists(Path.Combine(Folder, "model", Voice, "info.toml")))
        {
            var voices = Voices(Folder);
            problems.Add($"voice {Voice} not found under {Path.Combine(Folder, "model")}"
                + (voices.Count > 0 ? $" (installed: {string.Join(", ", voices)})" : ""));
        }
        if (Threads < 1)
        {
            problems.Add("threads must be at least 1");
        }
        return problems;
    }

    /// <summary>The voices installed under a NEUTRINO folder: each <c>model\*\info.toml</c>.</summary>
    public static IReadOnlyList<string> Voices(string folder)
    {
        var model = Path.Combine(folder, "model");
        if (!Directory.Exists(model))
        {
            return [];
        }
        return Directory.EnumerateDirectories(model)
            .Where(d => File.Exists(Path.Combine(d, "info.toml")))
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .Select(n => n!)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

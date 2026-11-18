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

    private const string Standalone = "neutrino.exe";

    /// <summary>
    /// Whether this NEUTRINO has the server and its client (the package that comes with the
    /// tuning tool, "調声支援ツール同梱版"). The plain package has <c>neutrino.exe</c> alone, which
    /// takes the client's arguments and loads the models on every run.
    /// </summary>
    /// <remarks>
    /// MEASURED 2026-11-18 (RTX 5070 Ti, MERROW, 43.2 s of song; Lily# Lab sessions/p1304/neutrino):
    /// the plain v3.2.2 sings it in 6.8 s every time and one 4.8 s phrase in 3.6 s; the server of
    /// v3.2.6 in 6.6 s the first time and 2.2 s after.
    /// </remarks>
    public bool HasServer => File.Exists(Path.Combine(BinDir, "neutrino_server.exe"))
        && File.Exists(Path.Combine(BinDir, "neutrino_client.exe"));

    /// <summary>The program a render is handed to: the server's client, or NEUTRINO itself without a server.</summary>
    public string RenderProgram => HasServer ? "neutrino_client.exe" : Standalone;

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
        if (!File.Exists(Path.Combine(BinDir, "musicXMLtoLabel.exe")))
        {
            problems.Add($"musicXMLtoLabel.exe not found under {BinDir} (NEUTRINO Tau v3.2.x's bin folder is expected)");
        }
        // Half a server is the package with the tuning tool with a program gone, not the plain
        // package: that package's neutrino.exe is a 5 KB stand-in that prints its version and
        // refuses to sing ("this dummy executable only displays version information", v3.2.6),
        // so it is not fallen back to.
        bool server = File.Exists(Path.Combine(BinDir, "neutrino_server.exe"));
        bool client = File.Exists(Path.Combine(BinDir, "neutrino_client.exe"));
        if (server != client)
        {
            problems.Add($"{(server ? "neutrino_client.exe" : "neutrino_server.exe")} not found under {BinDir}, beside {(server ? "neutrino_server.exe" : "neutrino_client.exe")}"
                + $" (the NEUTRINO package with the tuning tool sings through the two; its {Standalone} does not sing)");
        }
        else if (!HasServer && !File.Exists(Path.Combine(BinDir, Standalone)))
        {
            problems.Add($"neither neutrino_server.exe with neutrino_client.exe nor {Standalone} found under {BinDir} (NEUTRINO Tau v3.2.x's bin folder is expected)");
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

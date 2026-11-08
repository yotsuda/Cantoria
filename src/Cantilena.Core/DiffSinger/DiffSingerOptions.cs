// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Cantilena.DiffSinger;

/// <summary>
/// Where a DiffSinger voice is and how to sing with it. <paramref name="Folder"/> holds the voice
/// in the OpenUtau format: <c>dsconfig.yaml</c> (the acoustic model), <c>dsdur/</c> (timing),
/// <c>dspitch/</c> and <c>dsvariance/</c> where the voice has them, and <c>dsvocoder/</c> or a
/// <see cref="VocoderFolder"/> holding <c>vocoder.yaml</c>.
/// </summary>
public sealed record DiffSingerOptions(string Folder)
{
    /// <summary>A vocoder folder, for a voice that bundles none.</summary>
    public string? VocoderFolder { get; init; }

    /// <summary>The speaker of a multi-speaker voice (an entry of <c>speakers</c>, or its last path segment); null for the first.</summary>
    public string? Speaker { get; init; }

    /// <summary>Diffusion sampling steps of the acoustic model: more is slower and a little cleaner.</summary>
    public int Steps { get; init; } = 20;

    /// <summary>Leave the dictionary's language prefix handling to this code (<c>ja</c>).</summary>
    public string Language { get; init; } = "ja";

    /// <summary>Where the models run: DirectML when it is there, else the CPU (<see cref="OnnxProvider.Auto"/>),
    /// the CPU always, or DirectML or an error.</summary>
    public OnnxProvider Provider { get; init; } = OnnxProvider.Auto;

    /// <summary>The name of the voice: its folder's name.</summary>
    public string Name => Path.GetFileName(Path.TrimEndingDirectorySeparator(Folder));

    /// <summary>The problems with these options, as messages; empty when the voice looks usable.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (!Directory.Exists(Folder))
        {
            problems.Add($"voice folder not found: {Folder}");
            return problems;
        }
        if (!File.Exists(Path.Combine(Folder, "dsconfig.yaml")))
        {
            problems.Add($"not a DiffSinger voice (no dsconfig.yaml): {Folder}");
            return problems;
        }
        if (!File.Exists(Path.Combine(Folder, "dsdur", "dsconfig.yaml")))
        {
            problems.Add($"the voice has no duration model (dsdur/dsconfig.yaml): {Folder}");
        }
        if (!File.Exists(Path.Combine(Folder, "dsvocoder", "vocoder.yaml"))
            && (VocoderFolder is null || !File.Exists(Path.Combine(VocoderFolder, "vocoder.yaml"))))
        {
            problems.Add(VocoderFolder is null
                ? $"the voice bundles no vocoder (dsvocoder/vocoder.yaml); name one"
                : $"no vocoder.yaml under {VocoderFolder}");
        }
        if (Steps < 1)
        {
            problems.Add("steps must be at least 1");
        }
        return problems;
    }

    /// <summary>The voices under a folder: each subfolder holding a <c>dsconfig.yaml</c>, by name.</summary>
    public static IReadOnlyList<string> Voices(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }
        return Directory.EnumerateDirectories(folder)
            .Where(d => File.Exists(Path.Combine(d, "dsconfig.yaml")))
            .Select(d => Path.GetFileName(d)!)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

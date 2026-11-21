// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Cantoria.DiffSinger;

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

    /// <summary>Where sung phrases are kept on disk (<see cref="PhraseDisk.FolderIn"/>); null keeps them in memory only.</summary>
    public string? PhraseFolder { get; init; }

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
        if (Vocoder() is null)
        {
            problems.Add(VocoderFolder is null
                ? $"the voice bundles no vocoder (dsvocoder/vocoder.yaml), and its own ({NamedVocoder() ?? "none named"}) is not beside it; name one"
                : $"no vocoder.yaml under {VocoderFolder}");
        }
        if (Steps < 1)
        {
            problems.Add("steps must be at least 1");
        }
        return problems;
    }

    /// <summary>
    /// The vocoder folder the voice is sung with: the one it bundles (<c>dsvocoder/</c>); else the
    /// one its <c>dsconfig.yaml</c> names (<c>vocoder:</c>, the <c>name</c> in a <c>vocoder.yaml</c>), looked
    /// for as <see cref="VocoderFolder"/>, in it, beside it, and beside the voice (an OpenUtau
    /// dependency unpacked into the voices folder); else <see cref="VocoderFolder"/>. Null when there
    /// is none. Voices name different vocoders (TIGER nsf_hifigan 2024.02, Qixuan pc_nsf_hifigan
    /// 2025.02), and one folder for all sang a voice with another's.
    /// </summary>
    public string? Vocoder()
    {
        string own = Path.Combine(Folder, "dsvocoder");
        if (File.Exists(Path.Combine(own, "vocoder.yaml")))
        {
            return own;
        }
        if (NamedVocoder() is { } name)
        {
            var places = new List<string>();
            if (VocoderFolder is not null && Directory.Exists(VocoderFolder))
            {
                places.Add(VocoderFolder);
                places.AddRange(Directory.EnumerateDirectories(VocoderFolder));
                if (Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(VocoderFolder)) is { } besideVocoder && Directory.Exists(besideVocoder))
                {
                    places.AddRange(Directory.EnumerateDirectories(besideVocoder));
                }
            }
            if (Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Folder)) is { } voices && Directory.Exists(voices))
            {
                places.AddRange(Directory.EnumerateDirectories(voices));
            }
            foreach (var place in places.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string yaml = Path.Combine(place, "vocoder.yaml");
                if (File.Exists(yaml) && string.Equals(VocoderName(yaml), name, StringComparison.OrdinalIgnoreCase))
                {
                    return place;
                }
            }
        }
        return VocoderFolder is not null && File.Exists(Path.Combine(VocoderFolder, "vocoder.yaml")) ? VocoderFolder : null;
    }

    /// <summary>The vocoder the voice's <c>dsconfig.yaml</c> names, or null (none, or <c>dsvocoder</c>: the bundled one).</summary>
    public string? NamedVocoder()
    {
        string config = Path.Combine(Folder, "dsconfig.yaml");
        if (!File.Exists(config))
        {
            return null;
        }
        try
        {
            return DsConfig.Load(config).Vocoder is { Length: > 0 } name && name != "dsvocoder" ? name : null;
        }
        catch (Exception e) when (e is IOException or YamlDotNet.Core.YamlException)
        {
            return null;
        }
    }

    private static string? VocoderName(string yaml)
    {
        try
        {
            return DsVocoderConfig.Load(yaml).Name;
        }
        catch (Exception e) when (e is IOException or YamlDotNet.Core.YamlException)
        {
            return null;
        }
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

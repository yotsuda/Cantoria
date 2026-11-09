// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text.Json;
using Cantilena.DiffSinger;
using Cantilena.Neutrino;
using Cantilena.Voicevox;

namespace Cantilena.Cli;

/// <summary>
/// The engines the command line makes singers of, and where their voices are. A voice is named
/// <c>engine/name</c>: <c>neutrino/MERROW</c> (a folder under NEUTRINO's <c>model\</c>),
/// <c>diffsinger/TIGER</c> (a folder under the voices folder, or a path holding <c>dsconfig.yaml</c>),
/// <c>voicevox/ずんだもん-ノーマル</c> (a singing style of the running VOICEVOX).
/// A bare name is NEUTRINO's, unless it is such a path.
/// </summary>
sealed class Engines
{
    public const string DefaultNeutrinoVoice = "MERROW";
    public static readonly string[] Names = ["neutrino", "diffsinger", "voicevox"];

    /// <summary>NEUTRINO's folder (<c>bin\</c>, <c>model\</c>, <c>settings\</c>).</summary>
    public string? NeutrinoFolder { get; init; }
    /// <summary>The folder whose subfolders are DiffSinger voices.</summary>
    public string? VoicesFolder { get; init; }
    /// <summary>A vocoder folder for DiffSinger voices that bundle none.</summary>
    public string? VocoderFolder { get; init; }
    /// <summary>Where the running VOICEVOX answers.</summary>
    public string VoicevoxUrl { get; init; } = VoicevoxEngine.DefaultUrl;
    /// <summary>NEUTRINO on its GPU (CUDA); false with <c>--cpu</c> / <c>--provider cpu</c>.</summary>
    public bool UseGpu => Provider != OnnxProvider.Cpu;
    /// <summary>Where a DiffSinger voice's models run (<c>--provider</c>; <c>--cpu</c> is <c>cpu</c>).</summary>
    public OnnxProvider Provider { get; init; } = OnnxProvider.Auto;
    public int Threads { get; init; } = 4;
    public int Steps { get; init; } = 20;
    public bool KeepFiles { get; init; }

    /// <summary>
    /// The folders from the options, else the environment (<c>CANTILENA_NEUTRINO_DIR</c>,
    /// <c>CANTILENA_VOICES_DIR</c>, <c>CANTILENA_VOCODER_DIR</c>), else <c>cantilena.json</c> in the
    /// user's config folder (<c>neutrino.folder</c>, <c>diffsinger.voices</c>, <c>diffsinger.vocoder</c>;
    /// <c>%APPDATA%\cantilena</c> on Windows, <c>~/.config/cantilena</c> elsewhere). NEUTRINO runs on
    /// Windows only: elsewhere its folder is read as not set, whatever says it.
    /// </summary>
    public static Engines Configure(string? neutrinoDir, string? voicesDir, string? vocoderDir, OnnxProvider provider, int threads, int steps, bool keep, string? voicevoxUrl = null)
    {
        var settings = Settings();
        return new Engines
        {
            NeutrinoFolder = OperatingSystem.IsWindows()
                ? First(neutrinoDir, Environment.GetEnvironmentVariable("CANTILENA_NEUTRINO_DIR"), settings("neutrino", "folder"))
                : null,
            VoicesFolder = First(voicesDir, Environment.GetEnvironmentVariable("CANTILENA_VOICES_DIR"), settings("diffsinger", "voices")),
            VocoderFolder = First(vocoderDir, Environment.GetEnvironmentVariable("CANTILENA_VOCODER_DIR"), settings("diffsinger", "vocoder")),
            VoicevoxUrl = First(voicevoxUrl, Environment.GetEnvironmentVariable("CANTILENA_VOICEVOX_URL"), settings("voicevox", "url")) ?? VoicevoxEngine.DefaultUrl,
            Provider = provider,
            Threads = threads,
            Steps = steps,
            KeepFiles = keep,
        };
    }

    private static string? First(params string?[] candidates) => candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

    /// <summary>A reader of <c>%APPDATA%\cantilena\cantilena.json</c>'s string values, section.key; null when absent.</summary>
    private static Func<string, string, string?> Settings()
    {
        var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "cantilena", "cantilena.json");
        if (!File.Exists(file))
        {
            return (_, _) => null;
        }
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException e)
        {
            Console.Error.WriteLine($"cantilena: {file} is not valid JSON: {e.Message}");
            return (_, _) => null;
        }
        return (section, key) =>
            doc.RootElement.TryGetProperty(section, out var s) && s.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    /// <summary>The engine and the name a voice means (see the class remarks); empty is NEUTRINO's default voice.</summary>
    public static (string Engine, string Name) Split(string? voice)
    {
        var t = (voice ?? "").Trim();
        if (t.Length == 0)
        {
            return ("neutrino", DefaultNeutrinoVoice);
        }
        if (File.Exists(Path.Combine(t, "dsconfig.yaml")))
        {
            return ("diffsinger", t);
        }
        int slash = t.IndexOf('/');
        if (slash < 0 || t.Contains(':') || t.Contains('\\'))
        {
            return ("neutrino", t);
        }
        var engine = t[..slash].ToLowerInvariant();
        return Names.Contains(engine) ? (engine, t[(slash + 1)..]) : (engine, t[(slash + 1)..]);
    }

    /// <summary>Why a voice cannot be sung with: an engine this build has not, a folder not set, a voice not installed.</summary>
    public IReadOnlyList<string> Problems(string? voice)
    {
        var (engine, name) = Split(voice);
        switch (engine)
        {
            case "neutrino":
                if (!OperatingSystem.IsWindows())
                {
                    return ["NEUTRINO runs on Windows only; on this system sing through a DiffSinger voice (--voices-dir)"];
                }
                if (NeutrinoFolder is null)
                {
                    return ["where is NEUTRINO? give --neutrino-dir, set CANTILENA_NEUTRINO_DIR, or write neutrino.folder in cantilena.json"];
                }
                return new NeutrinoOptions(NeutrinoFolder, name) { UseGpu = UseGpu, Threads = Threads, KeepFiles = KeepFiles }.Validate();
            case "diffsinger":
                var folder = DiffSingerFolder(name);
                if (folder is null)
                {
                    return [$"the DiffSinger voice {name} is not a folder with dsconfig.yaml" + (VoicesFolder is null
                        ? " (give --voices-dir, set CANTILENA_VOICES_DIR, or write diffsinger.voices in cantilena.json, or name the voice's folder)"
                        : $" and not under {VoicesFolder} (installed: {string.Join(", ", DiffSingerOptions.Voices(VoicesFolder).DefaultIfEmpty("none"))})")];
                }
                return DiffSinger(folder).Validate();
            case "voicevox":
                var singers = VoicevoxEngine.Singers(VoicevoxUrl);
                if (singers is null)
                {
                    return [$"VOICEVOX is not answering at {VoicevoxUrl}: start VOICEVOX, or give --voicevox-url"];
                }
                if (singers.Teacher is null)
                {
                    return [$"the VOICEVOX at {VoicevoxUrl} has no singing style: it needs a version that sings"];
                }
                return singers.Voices.Any(v => v.Name == name) ? [] : [$"VOICEVOX has no voice {name} (there is: {string.Join(", ", singers.Voices.Select(v => v.Name).DefaultIfEmpty("none"))})"];
            default:
                return [$"no engine named {engine} in {voice} (there is: {string.Join(", ", Names)})"];
        }
    }

    /// <summary>A DiffSinger voice's folder: the name as a path holding dsconfig.yaml, else under the voices folder.</summary>
    private string? DiffSingerFolder(string name)
    {
        if (File.Exists(Path.Combine(name, "dsconfig.yaml")))
        {
            return name;
        }
        if (VoicesFolder is not null && File.Exists(Path.Combine(VoicesFolder, name, "dsconfig.yaml")))
        {
            return Path.Combine(VoicesFolder, name);
        }
        return null;
    }

    /// <summary>A singer for the voice; the problems are thrown as one <see cref="SingerException"/>.</summary>
    public ISinger Create(string? voice)
    {
        var problems = Problems(voice);
        if (problems.Count > 0)
        {
            throw new SingerException(string.Join("; ", problems));
        }
        var (engine, name) = Split(voice);
        return engine switch
        {
            "neutrino" => new NeutrinoSinger(new NeutrinoOptions(NeutrinoFolder!, name) { UseGpu = UseGpu, Threads = Threads, KeepFiles = KeepFiles }),
            "diffsinger" => new DiffSingerSinger(DiffSinger(DiffSingerFolder(name)!)),
            "voicevox" => Voicevox(name),
            _ => throw new SingerException($"no engine named {engine}"),
        };
    }

    private VoicevoxSinger Voicevox(string name)
    {
        var singers = VoicevoxEngine.Singers(VoicevoxUrl) ?? throw new SingerException($"VOICEVOX is not answering at {VoicevoxUrl}");
        return new VoicevoxSinger(VoicevoxEngine.Client(VoicevoxUrl), singers.Voices.First(v => v.Name == name), singers.Teacher!.Value);
    }

    /// <summary>A DiffSinger voice's options, on <see cref="Provider"/> (<c>--provider</c>; the default
    /// <see cref="OnnxProvider.Auto"/> is DirectML when this build and this machine have it, else the CPU).</summary>
    private DiffSingerOptions DiffSinger(string folder) => new(folder)
    {
        VocoderFolder = VocoderFolder,
        Steps = Steps,
        Provider = Provider,
    };

    /// <summary>Every voice installed, as <c>engine/name</c>: NEUTRINO's, then the DiffSinger voices folder's.</summary>
    public IReadOnlyList<string> List()
    {
        var voices = new List<string>();
        if (NeutrinoFolder is not null)
        {
            voices.AddRange(NeutrinoOptions.Voices(NeutrinoFolder).Select(v => "neutrino/" + v));
        }
        if (VoicesFolder is not null)
        {
            voices.AddRange(DiffSingerOptions.Voices(VoicesFolder).Select(v => "diffsinger/" + v));
        }
        if (VoicevoxEngine.Singers(VoicevoxUrl) is { } singers)
        {
            voices.AddRange(singers.Voices.Select(v => "voicevox/" + v.Name));
        }
        return voices;
    }

    /// <summary>What is configured, for the <c>ready</c> line and messages.</summary>
    public string Describe() =>
        $"neutrino {(OperatingSystem.IsWindows() ? NeutrinoFolder ?? "(not set)" : "(Windows only)")}; diffsinger voices {VoicesFolder ?? "(not set)"}"
        + (VocoderFolder is null ? "" : $", vocoder {VocoderFolder}") + $", provider {Provider.ToString().ToLowerInvariant()}";
}

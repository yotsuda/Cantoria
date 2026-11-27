// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text.Json;
using Cantoria.DiffSinger;
using Cantoria.Neutrino;
using Cantoria.Sinsy;
using Cantoria.Voicevox;

namespace Cantoria.Cli;

/// <summary>
/// The engines the command line makes singers of, and where their voices are. A voice is named
/// <c>engine/name</c>: <c>neutrino/MERROW</c> (a folder under NEUTRINO's <c>model\</c>),
/// <c>diffsinger/TIGER</c> (a folder under the voices folder, or a path holding <c>dsconfig.yaml</c>; <c>diffsinger/TIGER/tiger_disco</c> one of its speakers),
/// <c>voicevox/ずんだもん-ノーマル</c> (a singing style of the running VOICEVOX),
/// <c>sinsy/nitech_jp_song070_f001</c> (a <c>.htsvoice</c> in the Sinsy voices folder or one folder down).
/// A bare name is NEUTRINO's, unless it is such a path.
/// </summary>
sealed class Engines
{
    public const string DefaultNeutrinoVoice = "MERROW";
    public static readonly string[] Names = ["neutrino", "diffsinger", "voicevox", "sinsy"];

    /// <summary>Where the Sinsy voices are when nothing says: <c>~/Cantoria/Sinsy</c>.</summary>
    public static string DefaultSinsyFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Cantoria", "Sinsy");

    /// <summary>The folder whose <c>.htsvoice</c> files (or its subfolders') are Sinsy voices.</summary>
    public string SinsyFolder { get; init; } = DefaultSinsyFolder;

    /// <summary>NEUTRINO's folder (<c>bin\</c>, <c>model\</c>, <c>settings\</c>).</summary>
    public string? NeutrinoFolder { get; init; }
    /// <summary>The folder whose subfolders are DiffSinger voices.</summary>
    public string? VoicesFolder { get; init; }
    /// <summary>A vocoder folder for DiffSinger voices that bundle none.</summary>
    public string? VocoderFolder { get; init; }
    /// <summary>Where the running VOICEVOX answers.</summary>
    public string VoicevoxUrl { get; set; } = VoicevoxEngine.DefaultUrl;
    /// <summary>NEUTRINO on its GPU (CUDA); false with <c>--cpu</c> / <c>--provider cpu</c>.</summary>
    public bool UseGpu => Provider != OnnxProvider.Cpu;
    /// <summary>Where a DiffSinger voice's models run (<c>--provider</c>; <c>--cpu</c> is <c>cpu</c>).</summary>
    public OnnxProvider Provider { get; init; } = OnnxProvider.Auto;
    public int Threads { get; init; } = 4;
    public int Steps { get; init; } = 20;
    public bool KeepFiles { get; init; }
    /// <summary>Where sung phrases are kept on disk; null keeps them in memory only.</summary>
    public string? PhraseFolder { get; set; }

    /// <summary>
    /// The folders from the options, else the environment (<c>CANTORIA_NEUTRINO_DIR</c>,
    /// <c>CANTORIA_VOICES_DIR</c>, <c>CANTORIA_VOCODER_DIR</c>, <c>CANTORIA_SINSY_DIR</c>), else <c>cantoria.json</c> in the
    /// user's config folder (<c>neutrino.folder</c>, <c>diffsinger.voices</c>, <c>diffsinger.vocoder</c>, <c>sinsy.voices</c>;
    /// <c>%APPDATA%\cantoria</c> on Windows, <c>~/.config/cantoria</c> elsewhere). NEUTRINO runs on
    /// Windows only: elsewhere its folder is read as not set, whatever says it. The Sinsy folder is
    /// <see cref="DefaultSinsyFolder"/> when nothing says.
    /// </summary>
    public static Engines Configure(string? neutrinoDir, string? voicesDir, string? vocoderDir, OnnxProvider provider, int threads, int steps, bool keep, string? voicevoxUrl = null,
        string? defaultFemale = null, string? defaultMale = null, string? sinsyDir = null)
    {
        var settings = Settings();
        return new Engines
        {
            SinsyFolder = First(sinsyDir, Environment.GetEnvironmentVariable("CANTORIA_SINSY_DIR"), settings("sinsy", "voices")) ?? DefaultSinsyFolder,
            NeutrinoFolder = OperatingSystem.IsWindows()
                ? First(neutrinoDir, Environment.GetEnvironmentVariable("CANTORIA_NEUTRINO_DIR"), settings("neutrino", "folder"))
                : null,
            VoicesFolder = First(voicesDir, Environment.GetEnvironmentVariable("CANTORIA_VOICES_DIR"), settings("diffsinger", "voices")),
            VocoderFolder = First(vocoderDir, Environment.GetEnvironmentVariable("CANTORIA_VOCODER_DIR"), settings("diffsinger", "vocoder")),
            VoicevoxUrl = First(voicevoxUrl, Environment.GetEnvironmentVariable("CANTORIA_VOICEVOX_URL"), settings("voicevox", "url")) ?? VoicevoxEngine.DefaultUrl,
            Provider = provider,
            Threads = threads,
            Steps = steps,
            KeepFiles = keep,
            Defaults = new DefaultVoices(
                First(defaultFemale, Environment.GetEnvironmentVariable("CANTORIA_DEFAULT_FEMALE_VOICE"), settings("default", "female")) ?? "",
                First(defaultMale, Environment.GetEnvironmentVariable("CANTORIA_DEFAULT_MALE_VOICE"), settings("default", "male")) ?? ""),
        };
    }

    private static string? First(params string?[] candidates) => candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

    /// <summary>A reader of <c>%APPDATA%\cantoria\cantoria.json</c>'s string values, section.key; null when absent.</summary>
    private static Func<string, string, string?> Settings()
    {
        var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "cantoria", "cantoria.json");
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
            Console.Error.WriteLine($"cantoria: {file} is not valid JSON: {e.Message}");
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
                    return ["where is NEUTRINO? give --neutrino-dir, set CANTORIA_NEUTRINO_DIR, or write neutrino.folder in cantoria.json"];
                }
                return new NeutrinoOptions(NeutrinoFolder, name) { UseGpu = UseGpu, Threads = Threads, KeepFiles = KeepFiles }.Validate();
            case "diffsinger":
                var (voiceName, speaker) = SpeakerOf(name);
                var folder = DiffSingerFolder(voiceName);
                if (folder is null)
                {
                    return [$"the DiffSinger voice {name} is not a folder with dsconfig.yaml" + (VoicesFolder is null
                        ? " (give --voices-dir, set CANTORIA_VOICES_DIR, or write diffsinger.voices in cantoria.json, or name the voice's folder)"
                        : $" and not under {VoicesFolder} (installed: {string.Join(", ", DiffSingerOptions.Voices(VoicesFolder).DefaultIfEmpty("none"))})")];
                }
                var invalid = DiffSinger(folder).Validate();
                if (invalid.Count > 0 || speaker is null)
                {
                    return invalid;
                }
                var speakers = Speakers(folder);
                return speakers.Contains(speaker) ? [] : [$"{voiceName} has no speaker {speaker} (there is: {string.Join(", ", speakers.DefaultIfEmpty("none"))})"];
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
            case "sinsy":
                if (SinsyOptions.Find(SinsyFolder, name) is not { } file)
                {
                    return [$"the Sinsy voice {name} is not in {SinsyFolder} (installed: {string.Join(", ", SinsyOptions.Voices(SinsyFolder).DefaultIfEmpty("none"))};"
                        + " give --sinsy-dir, set CANTORIA_SINSY_DIR, or write sinsy.voices in cantoria.json)"];
                }
                return new SinsyOptions(file).Validate();
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
            "neutrino" => Phrased(new NeutrinoSinger(new NeutrinoOptions(NeutrinoFolder!, name) { UseGpu = UseGpu, Threads = Threads, KeepFiles = KeepFiles }), RefuseWhatIsNotKana),
            "diffsinger" => DiffSingerOf(name),
            // The revision: phrases sung before low notes were brought down are not to be found again.
            "voicevox" => Phrased(Voicevox(name), score => VoicevoxScore.Notes(score.Notes), "@2"),
            "sinsy" => Phrased(SinsyOf(name), RefuseWhatSinsyCannotRead),
            _ => throw new SingerException($"no engine named {engine}"),
        };
    }

    /// <summary>One singer a Sinsy voice: its library handle holds the voice.</summary>
    private readonly Dictionary<string, SinsySinger> _sinsySingers = new(StringComparer.OrdinalIgnoreCase);

    private SinsySinger SinsyOf(string name)
    {
        var file = SinsyOptions.Find(SinsyFolder, name)!;
        if (!_sinsySingers.TryGetValue(file, out var singer))
        {
            _sinsySingers[file] = singer = new SinsySinger(new SinsyOptions(file));
        }
        return singer;
    }

    private static void RefuseWhatSinsyCannotRead(SungScore score)
    {
        if (NeutrinoMusicXml.NotKanaProblem(score, "Sinsy") is { } problem)
        {
            throw new SingerException(problem);
        }
    }

    private PhrasedSinger Phrased(ISinger inner, Action<SungScore> check, string revision = "")
        => new(inner, check, PhraseFolder is { } folder ? new PhraseDisk(folder, inner.Name + revision) : null, Left);

    /// <summary>The phrase let go of mid-song, one for all the voices these engines sing: the next
    /// score in any voice settles it (LeftPhrase).</summary>
    public LeftPhrase Left { get; } = new();

    private static void RefuseWhatIsNotKana(SungScore score)
    {
        if (NeutrinoMusicXml.NotKanaProblem(score) is { } problem)
        {
            throw new SingerException(problem);
        }
    }

    /// <summary>A voice's speakers share its models, which are large: one singer a folder.</summary>
    private readonly Dictionary<string, DiffSingerSinger> _diffSingers = new(StringComparer.OrdinalIgnoreCase);

    private ISinger DiffSingerOf(string name)
    {
        var (voice, speaker) = SpeakerOf(name);
        var folder = DiffSingerFolder(voice)!;
        if (!_diffSingers.TryGetValue(folder, out var shared))
        {
            _diffSingers[folder] = shared = new DiffSingerSinger(DiffSinger(folder));
        }
        return speaker is null ? shared : new DiffSingerSpeaker(shared, speaker);
    }

    /// <summary>A voice's folder name and its speaker: <c>TIGER/tiger_disco</c>. A path to a voice is a voice.</summary>
    private static (string Voice, string? Speaker) SpeakerOf(string name)
    {
        int slash = name.LastIndexOf('/');
        return slash < 0 || File.Exists(Path.Combine(name, "dsconfig.yaml")) ? (name, null) : (name[..slash], name[(slash + 1)..]);
    }

    /// <summary>The speakers a voice lists, by the last segment of each.</summary>
    private static IReadOnlyList<string> Speakers(string folder)
        => (DsConfig.Load(Path.Combine(folder, "dsconfig.yaml")).Speakers ?? []).Select(s => s.Split('/', '\\')[^1]).ToList();

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
        PhraseFolder = PhraseFolder,
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
            foreach (var v in DiffSingerOptions.Voices(VoicesFolder))
            {
                voices.Add("diffsinger/" + v);
                voices.AddRange(Speakers(Path.Combine(VoicesFolder, v)).Skip(1).Select(s => $"diffsinger/{v}/{s}"));
            }
        }
        if (VoicevoxEngine.Singers(VoicevoxUrl) is { } singers)
        {
            voices.AddRange(singers.Voices.Select(v => "voicevox/" + v.Name));
        }
        voices.AddRange(SinsyOptions.Voices(SinsyFolder).Select(v => "sinsy/" + v));
        return voices;
    }

    /// <summary>
    /// Every voice installed with what it says of itself — its languages, its gender — in the order
    /// the extension lists them (<see cref="VoiceEntries"/>): what <c>plan</c> chooses among.
    /// <paramref name="only"/>: the engines to look in, for a caller that needs no other (asking
    /// VOICEVOX costs a wait when it is not running).
    /// </summary>
    public IReadOnlyList<VoiceEntry> Entries(IReadOnlyCollection<string>? only = null)
    {
        var voices = new List<VoiceEntry>();
        if (NeutrinoFolder is not null && only?.Contains("neutrino") != false)
        {
            voices.AddRange(VoiceEntries.Neutrino(NeutrinoFolder));
        }
        if (VoicesFolder is not null && only?.Contains("diffsinger") != false)
        {
            voices.AddRange(VoiceEntries.DiffSinger(VoicesFolder));
        }
        if (only?.Contains("voicevox") != false && VoicevoxEngine.Singers(VoicevoxUrl) is { } singers)
        {
            voices.AddRange(VoiceEntries.Voicevox(singers));
        }
        if (only?.Contains("sinsy") != false)
        {
            voices.AddRange(VoiceEntries.Sinsy(SinsyFolder));
        }
        return voices;
    }

    /// <summary>For each engine with no voice among <paramref name="voices"/>, why, in a score writer's words.</summary>
    public IReadOnlyDictionary<string, string> Missing(IReadOnlyList<VoiceEntry> voices)
    {
        var why = new Dictionary<string, string>();
        if (voices.All(v => v.Engine != "neutrino"))
        {
            why["neutrino"] = !OperatingSystem.IsWindows() ? "NEUTRINO runs on Windows only"
                : NeutrinoFolder is null ? "NEUTRINO's folder is not set"
                : $"NEUTRINO's folder holds no voice ({Path.Combine(NeutrinoFolder, "model")})";
        }
        if (voices.All(v => v.Engine != "diffsinger"))
        {
            why["diffsinger"] = VoicesFolder is null ? "the DiffSinger voices folder is not set" : $"the DiffSinger voices folder holds no voice ({VoicesFolder})";
        }
        if (voices.All(v => v.Engine != "voicevox"))
        {
            why["voicevox"] = $"VOICEVOX is not running or not installed: nothing answers at {VoicevoxUrl}";
        }
        if (voices.All(v => v.Engine != "sinsy"))
        {
            why["sinsy"] = SinsyOptions.Built ? $"the Sinsy voices folder holds no voice ({SinsyFolder})" : "this build of Cantoria has no Sinsy engine";
        }
        return why;
    }

    /// <summary>Who would sing what is asked (<see cref="SingerPlan"/>): the installed voices are read and
    /// their files looked for; no model is loaded and nothing is started.</summary>
    /// <param name="asked">The voices asked for.</param>
    /// <param name="defaults">Each gender's default voice; null for <see cref="Defaults"/>.</param>
    /// <param name="voicevox">False from a caller that has just asked VOICEVOX and heard nothing: it is
    /// not asked again (two seconds, when nothing is there).</param>
    public SingerPlan Plan(IEnumerable<PlanAsk> asked, DefaultVoices? defaults = null, bool voicevox = true)
    {
        var voices = Entries(voicevox ? null : ["neutrino", "diffsinger", "sinsy"]);
        return SingerPlan.For(asked, voices, defaults ?? Defaults, Problems, Missing(voices));
    }

    /// <summary>Why the voice cannot sing words in <paramref name="language"/> — the reason <c>plan</c> gives —
    /// or null: a voice that says what it sings and does not sing this. One named by its folder says nothing.</summary>
    public string? LanguageProblem(string? voice, string language)
    {
        var (engine, name) = Split(voice);
        string id = engine + "/" + name;
        if (SingerResolver.LanguageProblem(Entries([engine]).FirstOrDefault(v => v.Id == id), language) is null)
        {
            return null;
        }
        // What every engine's voices sing is part of the sentence, as it is of plan's.
        var all = Entries();
        return SingerResolver.LanguageReason(all.FirstOrDefault(v => v.Id == id), language, all);
    }

    /// <summary>The default voice of each gender, for a <c>plan</c> that is told none: the options, else the
    /// environment (<c>CANTORIA_DEFAULT_FEMALE_VOICE</c>, <c>CANTORIA_DEFAULT_MALE_VOICE</c>), else
    /// <c>cantoria.json</c> (<c>default.female</c>, <c>default.male</c>); empty is not set.</summary>
    public DefaultVoices Defaults { get; init; } = new();

    /// <summary>What is configured, for the <c>ready</c> line and messages.</summary>
    public string Describe() =>
        $"neutrino {(OperatingSystem.IsWindows() ? NeutrinoFolder ?? "(not set)" : "(Windows only)")}; diffsinger voices {VoicesFolder ?? "(not set)"}"
        + (VocoderFolder is null ? "" : $", vocoder {VocoderFolder}") + $", provider {Provider.ToString().ToLowerInvariant()}"
        + $"; sinsy voices {(SinsyOptions.Built ? SinsyFolder : "(not in this build)")}";
}

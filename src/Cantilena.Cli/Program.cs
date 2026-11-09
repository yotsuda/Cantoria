// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Cantilena;
using Cantilena.Audio;
using Cantilena.Cli;
using Cantilena.DiffSinger;

// cantilena — the command line over the library (docs/design.md §4).
// Exit codes: 0 done, 1 the backend failed, 2 bad arguments, score or settings.

return args switch
{
    ["--version"] or ["-V"] => Version(),
    [] or ["--help"] or ["-h"] => Help(0),
    ["check", var path] => Check(path),
    ["voices", ..] => Voices(args[1..]),
    ["providers"] => Providers(),
    ["sing", ..] => await Sing(args[1..]),
    ["serve", ..] => await Serve(args[1..]),
    _ => Help(2),
};

static int Version()
{
    var v = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion ?? "?";
    Console.WriteLine($"cantilena {v.Split('+')[0]}");
    return 0;
}

static int Help(int code)
{
    (code == 0 ? Console.Out : Console.Error).Write("""
        cantilena — a singing engine for notation software

        usage:
          cantilena check <score.json>                     validate a score, print its summary
          cantilena sing <score.json> -o <out.wav> [options]
          cantilena voices [options]                       the voices installed, as engine/name
          cantilena providers                              where DiffSinger models can run on this machine
          cantilena serve [options]                        sing scores sent on stdin, one JSON line each
          cantilena --version

        a voice is named engine/name:
          neutrino/MERROW             a folder under NEUTRINO's model\ (a bare name means this)
          diffsinger/TIGER            a folder under the DiffSinger voices folder, or a path holding dsconfig.yaml
          voicevox/ずんだもん-ノーマル  a singing style of the running VOICEVOX

        sing options:
          --voice <engine/name>       the voice (default neutrino/MERROW); voices joined by + sing together
          --neutrino-dir <folder>     NEUTRINO's folder (holds bin\, model\, settings\)
          --voices-dir <folder>       the folder whose subfolders are DiffSinger voices
          --vocoder-dir <folder>      a vocoder folder for DiffSinger voices that bundle none
          --voicevox-url <url>        where the running VOICEVOX answers (default http://127.0.0.1:50021)
          --steps <n>                 DiffSinger sampling steps (default 20; fewer is faster)
          --provider <name>           DiffSinger: auto (default: DirectML on Windows where there is a
                                      DirectX 12 device, else the CPU), cpu, directml, coreml (macOS; untested)
          --cpu                       the CPU for every engine (NEUTRINO without its GPU; --provider cpu)
          --threads <n>               NEUTRINO: CPU threads (default 4)
          --keep                      NEUTRINO: leave the MusicXML, labels and WAV under output\cantilena\
          --backend <name>            (older form) the engine of a bare --voice name

        serve: the same options but -o and --keep, plus --out-dir <folder> for the WAVs (default
        %TEMP%\cantilena). The engines stay warm between requests. One request per line on stdin:
          {"id":"1","score":{…},"voice":"neutrino/MERROW","output":"C:\\x.wav"}   score, voice and output optional
          {"id":"1","scoreFile":"C:\\score.json"}
          {"command":"voices"}                                                    → {"voices":["neutrino/MERROW", …]}
          {"command":"shutdown"}
        One answer per line on stdout, in order: first {"ready":true,"voices":[…],"outDir":"…"},
        then {"id":"1","wav":"…","duration":8.0,"sampleRate":48000,"seconds":0.5} or {"id":"1","error":"…"}.
        End of stdin ends the process.

        The folders are taken from the options, else the environment (CANTILENA_NEUTRINO_DIR,
        CANTILENA_VOICES_DIR, CANTILENA_VOCODER_DIR), else cantilena.json in the user's config folder
        (%APPDATA%\cantilena on Windows, ~/.config/cantilena elsewhere: "neutrino": {"folder": …},
        "diffsinger": {"voices": …, "vocoder": …}). NEUTRINO runs on Windows only. The score format
        is documented in README.md.

        """);
    return code;
}

static SungScore? Load(string path, out int code)
{
    code = 0;
    string json;
    try
    {
        json = File.ReadAllText(path);
    }
    catch (IOException e)
    {
        Console.Error.WriteLine($"cantilena: cannot read {path}: {e.Message}");
        code = 2;
        return null;
    }
    SungScore score;
    try
    {
        score = ScoreJson.Read(json);
    }
    catch (JsonException e)
    {
        Console.Error.WriteLine($"cantilena: {path} is not a score: {e.Message}");
        code = 2;
        return null;
    }
    var problems = score.Validate();
    if (problems.Count > 0)
    {
        foreach (var p in problems)
        {
            Console.Error.WriteLine($"cantilena: {path}: {p}");
        }
        code = 2;
        return null;
    }
    return score;
}

static int Check(string path)
{
    var score = Load(path, out int code);
    if (score is null)
    {
        return code;
    }
    int sung = score.Notes.Count(n => !n.Melisma);
    int breaths = score.Notes.Count(n => n.Breath);
    Console.WriteLine($"{path}: {score.Notes.Count} notes ({sung} syllables, {score.Notes.Count - sung} melisma), "
        + $"{breaths} breaths, {score.End:0.###} s, {score.Language}, {score.SampleRate} Hz");
    return 0;
}

static int Voices(string[] rest)
{
    var a = EngineArgs.Parse(rest);
    if (a.Unknown.Count > 0 || a.Path is not null)
    {
        Console.Error.WriteLine($"cantilena: unknown option {string.Join(" ", a.Unknown.Append(a.Path ?? ""))}");
        return Help(2);
    }
    var engines = a.Engines();
    var voices = engines.List();
    if (voices.Count == 0)
    {
        Console.Error.WriteLine($"cantilena: no voice installed ({engines.Describe()})");
        return 1;
    }
    foreach (var v in voices)
    {
        Console.WriteLine(v);
    }
    return 0;
}

/// <summary>What this build on this machine can run DiffSinger models on — the CPU, DirectML, CoreML —
/// and what `auto` would pick; a line each. Exit 0 always: it is a report.</summary>
static int Providers()
{
    foreach (var line in Cantilena.DiffSinger.OnnxProviders.Describe())
    {
        Console.WriteLine(line);
    }
    return 0;
}

static async Task<int> Sing(string[] rest)
{
    var a = EngineArgs.Parse(rest);
    if (a.Unknown.Count > 0)
    {
        Console.Error.WriteLine($"cantilena: unknown option {string.Join(" ", a.Unknown)}");
        return Help(2);
    }
    if (a.Path is null || a.Output is null)
    {
        return Help(2);
    }
    var score = Load(a.Path, out int code);
    if (score is null)
    {
        return code;
    }
    var engines = a.Engines();
    var problems = engines.Problems(a.Voice);
    if (problems.Count > 0)
    {
        foreach (var p in problems)
        {
            Console.Error.WriteLine($"cantilena: {p}");
        }
        return 2;
    }
    var singer = engines.Create(a.Voice);
    try
    {
        var watch = Stopwatch.StartNew();
        var audio = SungLevels.Apply(await singer.SingAsync(score), score);
        var dirName = Path.GetDirectoryName(Path.GetFullPath(a.Output));
        if (!string.IsNullOrEmpty(dirName))
        {
            Directory.CreateDirectory(dirName);
        }
        await File.WriteAllBytesAsync(a.Output, Wav.Write(audio));
        Console.Error.WriteLine($"cantilena: {singer.Name} sang {score.Notes.Count} notes, {audio.Duration:0.##} s at {audio.SampleRate} Hz, "
            + $"in {watch.Elapsed.TotalSeconds:0.#} s{ProviderOf(singer)} -> {a.Output}");
        return 0;
    }
    catch (SingerException e)
    {
        Console.Error.WriteLine($"cantilena: {e.Message}");
        return 1;
    }
    finally
    {
        (singer as IDisposable)?.Dispose();
    }
}

/// <summary>
/// A resident singer for an editor: scores arrive on stdin as JSON lines and the WAVs' paths
/// go back on stdout, so an engine's warm-up (NEUTRINO's server and its models, DiffSinger's
/// ONNX sessions) is paid once per editor session and not once per Play. Requests are served in
/// order, one at a time. One singer per voice asked for.
/// </summary>
static async Task<int> Serve(string[] rest)
{
    var a = EngineArgs.Parse(rest);
    if (a.Unknown.Count > 0 || a.Path is not null || a.Output is not null)
    {
        Console.Error.WriteLine($"cantilena: unknown option {string.Join(" ", a.Unknown.Append(a.Path ?? "").Append(a.Output ?? ""))}");
        return Help(2);
    }
    var engines = a.Engines();
    var defaultVoice = a.Voice ?? (engines.NeutrinoFolder is not null ? "neutrino/" + Engines.DefaultNeutrinoVoice : null);
    if (defaultVoice is not null)
    {
        var problems = engines.Problems(defaultVoice);
        if (problems.Count > 0)
        {
            foreach (var p in problems)
            {
                Console.Error.WriteLine($"cantilena: {p}");
            }
            return 2;
        }
    }
    else if (engines.NeutrinoFolder is null && engines.VoicesFolder is null && engines.List().Count == 0)
    {
        // Not fatal: a request can say where a VOICEVOX started after this process answers.
        Console.Error.WriteLine("cantilena: no engine configured yet: give --neutrino-dir or --voices-dir (or the environment, or cantilena.json), or start VOICEVOX");
    }
    var outDir = a.OutDir ?? Path.Combine(Path.GetTempPath(), "cantilena");
    Directory.CreateDirectory(outDir);

    // The wire is UTF-8 both ways whatever the console's code page: paths and lyrics are not ASCII.
    var utf8 = new System.Text.UTF8Encoding(false);
    using var stdin = new StreamReader(Console.OpenStandardInput(), utf8);
    using var stdout = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true, NewLine = "\n" };
    var json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
    var singers = new Dictionary<string, ISinger>(StringComparer.OrdinalIgnoreCase);
    ISinger SingerOf(string? name)
    {
        var id = string.Join("+", Engines.Pieces(name ?? defaultVoice).Select(piece =>
        {
            var (engine, voice) = Engines.Split(piece);
            return engine + "/" + voice;
        }));
        var key = id.Contains("voicevox/", StringComparison.Ordinal) ? id + "@" + engines.VoicevoxUrl : id;
        if (!singers.TryGetValue(key, out var singer))
        {
            singers[key] = singer = engines.Create(id);
        }
        return singer;
    }

    Console.Error.WriteLine($"cantilena: serving ({engines.Describe()}), WAVs under {outDir}");
    var backend = defaultVoice is not null ? Engines.Split(defaultVoice).Engine : engines.NeutrinoFolder is not null ? "neutrino" : "diffsinger";
    stdout.WriteLine(JsonSerializer.Serialize(new { ready = true, backend, voices = engines.List(), outDir }, json));
    try
    {
        int count = 0;
        string? line;
        while ((line = await stdin.ReadLineAsync()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            string id = (++count).ToString();
            try
            {
                var request = JsonSerializer.Deserialize<ServeRequest>(line, json) ?? throw new JsonException("an empty request");
                id = request.Id ?? id;
                if (request.Command == "shutdown")
                {
                    break;
                }
                if (request.Command == "voices")
                {
                    stdout.WriteLine(JsonSerializer.Serialize(new { id, voices = engines.List() }, json));
                    continue;
                }
                if (request.Command is not null)
                {
                    throw new SingerException($"no command named {request.Command} (there is: voices, shutdown)");
                }
                var score = request.Score is { } element ? ScoreJson.Read(element.GetRawText())
                    : request.ScoreFile is { } file ? ScoreJson.Read(await File.ReadAllTextAsync(file))
                    : throw new SingerException("a request needs \"score\" or \"scoreFile\"");
                var bad = score.Validate();
                if (bad.Count > 0)
                {
                    throw new SingerException(string.Join("; ", bad));
                }
                if (request.VoicevoxUrl is { Length: > 0 } voicevoxUrl)
                {
                    engines.VoicevoxUrl = voicevoxUrl;
                }
                var singer = SingerOf(request.Voice);
                var watch = Stopwatch.StartNew();
                var audio = SungLevels.Apply(await singer.SingAsync(score), score);
                var output = request.Output ?? Path.Combine(outDir, FileNameOf(id) + ".wav");
                var dirName = Path.GetDirectoryName(Path.GetFullPath(output));
                if (!string.IsNullOrEmpty(dirName))
                {
                    Directory.CreateDirectory(dirName);
                }
                await File.WriteAllBytesAsync(output, Wav.Write(audio));
                stdout.WriteLine(JsonSerializer.Serialize(new
                {
                    id,
                    wav = Path.GetFullPath(output),
                    duration = Math.Round(audio.Duration, 4),
                    sampleRate = audio.SampleRate,
                    seconds = Math.Round(watch.Elapsed.TotalSeconds, 2),
                    voice = singer.Name,
                    provider = (singer as Cantilena.DiffSinger.DiffSingerSinger)?.Provider,
                }, json));
            }
            catch (Exception e) when (e is SingerException or JsonException or IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
            {
                stdout.WriteLine(JsonSerializer.Serialize(new { id, error = e.Message }, json));
            }
        }
    }
    finally
    {
        foreach (var singer in singers.Values)
        {
            (singer as IDisposable)?.Dispose();
        }
    }
    return 0;
}

/// <summary>" on DirectML" / " on the CPU" for a DiffSinger singer (and why, when the CPU stood in); nothing for NEUTRINO.</summary>
static string ProviderOf(ISinger singer)
{
    if (singer is not Cantilena.DiffSinger.DiffSingerSinger ds)
    {
        return "";
    }
    var note = ds.Voice.ProviderNote;
    return (ds.Provider == "CPU" ? " on the CPU" : $" on {ds.Provider}") + (note is null ? "" : $" ({note})");
}

/// <summary>A request's id as a file name: letters, digits, '-', '_' and '.', the rest '_'.</summary>
static string FileNameOf(string id)
{
    var chars = id.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray();
    return chars.Length == 0 ? "score" : new string(chars);
}

/// <summary>The options the engines share, read off the arguments; what is not one is kept for the command.</summary>
sealed class EngineArgs
{
    public string? Voice, Backend, NeutrinoDir, VoicesDir, VocoderDir, VoicevoxUrl, Output, OutDir, Path;
    public bool Keep;
    public OnnxProvider Provider = OnnxProvider.Auto;
    public int Threads = 4, Steps = 20;
    public List<string> Unknown = [];

    public static EngineArgs Parse(string[] rest)
    {
        var a = new EngineArgs();
        for (int i = 0; i < rest.Length; i++)
        {
            switch (rest[i])
            {
                case "-o" or "--output" when i + 1 < rest.Length: a.Output = rest[++i]; break;
                case "--out-dir" when i + 1 < rest.Length: a.OutDir = rest[++i]; break;
                case "--backend" when i + 1 < rest.Length: a.Backend = rest[++i]; break;
                case "--voice" when i + 1 < rest.Length: a.Voice = rest[++i]; break;
                case "--neutrino-dir" when i + 1 < rest.Length: a.NeutrinoDir = rest[++i]; break;
                case "--voices-dir" when i + 1 < rest.Length: a.VoicesDir = rest[++i]; break;
                case "--vocoder-dir" when i + 1 < rest.Length: a.VocoderDir = rest[++i]; break;
                case "--voicevox-url" when i + 1 < rest.Length: a.VoicevoxUrl = rest[++i]; break;
                case "--threads" when i + 1 < rest.Length && int.TryParse(rest[i + 1], out a.Threads): i++; break;
                case "--steps" when i + 1 < rest.Length && int.TryParse(rest[i + 1], out a.Steps): i++; break;
                case "--cpu": a.Provider = OnnxProvider.Cpu; break;
                case "--provider" when i + 1 < rest.Length:
                    i++;
                    if (Enum.TryParse<OnnxProvider>(rest[i], ignoreCase: true, out var p))
                    {
                        a.Provider = p;
                    }
                    else
                    {
                        a.Unknown.Add($"--provider {rest[i]} (there is: auto, cpu, directml, coreml)");
                    }
                    break;
                case "--keep": a.Keep = true; break;
                case var s when s.StartsWith('-'): a.Unknown.Add(s); break;
                default:
                    if (a.Path is not null)
                    {
                        a.Unknown.Add(rest[i]);
                    }
                    a.Path = rest[i];
                    break;
            }
        }
        // --backend X with a bare --voice: the older spelling of X/voice.
        if (a.Backend is not null && a.Voice is not null && !a.Voice.Contains('/'))
        {
            a.Voice = a.Backend + "/" + a.Voice;
        }
        else if (a.Backend is not null && a.Voice is null && a.Backend != "neutrino")
        {
            a.Unknown.Add($"--backend {a.Backend} needs --voice");
        }
        return a;
    }

    public Engines Engines() => Cantilena.Cli.Engines.Configure(NeutrinoDir, VoicesDir, VocoderDir, Provider, Threads, Steps, Keep, VoicevoxUrl);
}

/// <summary>One line of <c>cantilena serve</c>'s stdin.</summary>
sealed class ServeRequest
{
    public string? Id { get; set; }
    /// <summary><c>shutdown</c> ends the process, <c>voices</c> lists the voices; anything else is a score to sing.</summary>
    public string? Command { get; set; }
    /// <summary>The score itself (README.md's shape).</summary>
    public System.Text.Json.JsonElement? Score { get; set; }
    /// <summary>A file holding the score, instead of <see cref="Score"/>.</summary>
    public string? ScoreFile { get; set; }
    /// <summary>The voice as <c>engine/name</c>; default the <c>--voice</c> option, else NEUTRINO's MERROW.</summary>
    public string? Voice { get; set; }
    /// <summary>Where VOICEVOX answers now: the editor may have started it after this process began.</summary>
    public string? VoicevoxUrl { get; set; }
    /// <summary>Where to write the WAV; default a file named after the id under the out dir.</summary>
    public string? Output { get; set; }
}

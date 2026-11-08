// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Cantilena;
using Cantilena.Audio;
using Cantilena.Neutrino;

// cantilena — the command line over the library (docs/design.md §4).
// Exit codes: 0 done, 1 the backend failed, 2 bad arguments, score or settings.

return args switch
{
    ["--version"] or ["-V"] => Version(),
    [] or ["--help"] or ["-h"] => Help(0),
    ["check", var path] => Check(path),
    ["voices", ..] => Voices(args[1..]),
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
          cantilena voices [--neutrino-dir <folder>]       the voices installed
          cantilena serve [options]                        sing scores sent on stdin, one JSON line each
          cantilena --version

        sing options:
          --backend neutrino          the only backend so far (default)
          --voice <name>              a folder under NEUTRINO's model\ (default MERROW)
          --neutrino-dir <folder>     NEUTRINO's folder (holds bin\, model\, settings\)
          --cpu                       do not ask for the GPU
          --threads <n>               CPU threads (default 4)
          --keep                      leave the MusicXML, labels and WAV under output\cantilena\

        serve: the same options but -o and --keep, plus --out-dir <folder> for the WAVs (default
        %TEMP%\cantilena). The engine stays warm between requests. One request per line on stdin:
          {"id":"1","score":{…},"voice":"MERROW","output":"C:\\x.wav"}   score, voice and output optional
          {"id":"1","scoreFile":"C:\\score.json"}
          {"command":"shutdown"}
        One answer per line on stdout, in order: first {"ready":true,"backend":"neutrino","voices":[…]},
        then {"id":"1","wav":"…","duration":8.0,"sampleRate":48000,"seconds":0.5} or {"id":"1","error":"…"}.
        End of stdin ends the process.

        NEUTRINO's folder is taken from --neutrino-dir, else the CANTILENA_NEUTRINO_DIR environment
        variable, else "neutrino.folder" in %APPDATA%\cantilena\cantilena.json. The score format is
        documented in README.md.

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

/// <summary>NEUTRINO's folder: the option, else the environment, else the settings file.</summary>
static string? NeutrinoFolder(string? option)
{
    if (!string.IsNullOrWhiteSpace(option))
    {
        return option;
    }
    var env = Environment.GetEnvironmentVariable("CANTILENA_NEUTRINO_DIR");
    if (!string.IsNullOrWhiteSpace(env))
    {
        return env;
    }
    var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "cantilena", "cantilena.json");
    if (File.Exists(file))
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (doc.RootElement.TryGetProperty("neutrino", out var n) && n.TryGetProperty("folder", out var f) && f.ValueKind == JsonValueKind.String)
            {
                return f.GetString();
            }
        }
        catch (JsonException e)
        {
            Console.Error.WriteLine($"cantilena: {file} is not valid JSON: {e.Message}");
        }
    }
    return null;
}

static int Voices(string[] rest)
{
    string? dir = null;
    for (int i = 0; i < rest.Length; i++)
    {
        if (rest[i] == "--neutrino-dir" && i + 1 < rest.Length)
        {
            dir = rest[++i];
        }
        else
        {
            return Help(2);
        }
    }
    var folder = NeutrinoFolder(dir);
    if (folder is null)
    {
        Console.Error.WriteLine("cantilena: where is NEUTRINO? give --neutrino-dir, set CANTILENA_NEUTRINO_DIR, or write cantilena.json");
        return 2;
    }
    var voices = NeutrinoOptions.Voices(folder);
    if (voices.Count == 0)
    {
        Console.Error.WriteLine($"cantilena: no voice under {Path.Combine(folder, "model")}");
        return 1;
    }
    foreach (var v in voices)
    {
        Console.WriteLine($"neutrino/{v}");
    }
    return 0;
}

static async Task<int> Sing(string[] rest)
{
    string? path = null, output = null, backend = null, voice = null, dir = null;
    bool cpu = false, keep = false;
    int threads = 4;
    for (int i = 0; i < rest.Length; i++)
    {
        switch (rest[i])
        {
            case "-o" or "--output" when i + 1 < rest.Length: output = rest[++i]; break;
            case "--backend" when i + 1 < rest.Length: backend = rest[++i]; break;
            case "--voice" when i + 1 < rest.Length: voice = rest[++i]; break;
            case "--neutrino-dir" when i + 1 < rest.Length: dir = rest[++i]; break;
            case "--threads" when i + 1 < rest.Length && int.TryParse(rest[i + 1], out threads): i++; break;
            case "--cpu": cpu = true; break;
            case "--keep": keep = true; break;
            case var a when a.StartsWith('-'):
                Console.Error.WriteLine($"cantilena: unknown option {a}");
                return Help(2);
            default:
                if (path is not null)
                {
                    return Help(2);
                }
                path = rest[i];
                break;
        }
    }
    if (path is null || output is null)
    {
        return Help(2);
    }
    if (backend is not (null or "neutrino"))
    {
        Console.Error.WriteLine($"cantilena: no backend named {backend} (there is: neutrino)");
        return 2;
    }
    var score = Load(path, out int code);
    if (score is null)
    {
        return code;
    }
    var folder = NeutrinoFolder(dir);
    if (folder is null)
    {
        Console.Error.WriteLine("cantilena: where is NEUTRINO? give --neutrino-dir, set CANTILENA_NEUTRINO_DIR, or write cantilena.json");
        return 2;
    }
    var options = new NeutrinoOptions(folder, voice ?? "MERROW") { UseGpu = !cpu, Threads = threads, KeepFiles = keep };
    var problems = options.Validate();
    if (problems.Count > 0)
    {
        foreach (var p in problems)
        {
            Console.Error.WriteLine($"cantilena: {p}");
        }
        return 2;
    }
    using var singer = new NeutrinoSinger(options);
    var watch = Stopwatch.StartNew();
    try
    {
        var audio = await singer.SingAsync(score);
        var dirName = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrEmpty(dirName))
        {
            Directory.CreateDirectory(dirName);
        }
        await File.WriteAllBytesAsync(output, Wav.Write(audio));
        Console.Error.WriteLine($"cantilena: {singer.Name} sang {score.Notes.Count} notes, {audio.Duration:0.##} s at {audio.SampleRate} Hz, "
            + $"in {watch.Elapsed.TotalSeconds:0.#} s -> {output}");
        return 0;
    }
    catch (SingerException e)
    {
        Console.Error.WriteLine($"cantilena: {e.Message}");
        return 1;
    }
}

/// <summary>
/// A resident singer for an editor: scores arrive on stdin as JSON lines and the WAVs' paths
/// go back on stdout, so the engine's warm-up (NEUTRINO's server, its models, the GPU's
/// kernels) is paid once per editor session and not once per Play. Requests are served in
/// order, one at a time. One singer per voice asked for.
/// </summary>
static async Task<int> Serve(string[] rest)
{
    string? backend = null, voice = null, dir = null, outDir = null;
    bool cpu = false;
    int threads = 4;
    for (int i = 0; i < rest.Length; i++)
    {
        switch (rest[i])
        {
            case "--backend" when i + 1 < rest.Length: backend = rest[++i]; break;
            case "--voice" when i + 1 < rest.Length: voice = rest[++i]; break;
            case "--neutrino-dir" when i + 1 < rest.Length: dir = rest[++i]; break;
            case "--out-dir" when i + 1 < rest.Length: outDir = rest[++i]; break;
            case "--threads" when i + 1 < rest.Length && int.TryParse(rest[i + 1], out threads): i++; break;
            case "--cpu": cpu = true; break;
            default:
                Console.Error.WriteLine($"cantilena: unknown option {rest[i]}");
                return Help(2);
        }
    }
    if (backend is not (null or "neutrino"))
    {
        Console.Error.WriteLine($"cantilena: no backend named {backend} (there is: neutrino)");
        return 2;
    }
    var folder = NeutrinoFolder(dir);
    if (folder is null)
    {
        Console.Error.WriteLine("cantilena: where is NEUTRINO? give --neutrino-dir, set CANTILENA_NEUTRINO_DIR, or write cantilena.json");
        return 2;
    }
    var probe = new NeutrinoOptions(folder, voice ?? "MERROW") { UseGpu = !cpu, Threads = threads };
    var problems = probe.Validate();
    if (problems.Count > 0)
    {
        foreach (var p in problems)
        {
            Console.Error.WriteLine($"cantilena: {p}");
        }
        return 2;
    }
    outDir ??= Path.Combine(Path.GetTempPath(), "cantilena");
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
    var singers = new Dictionary<string, NeutrinoSinger>(StringComparer.OrdinalIgnoreCase);
    NeutrinoSinger SingerOf(string name)
    {
        if (!singers.TryGetValue(name, out var singer))
        {
            var options = probe with { Voice = name };
            var wrong = options.Validate();
            if (wrong.Count > 0)
            {
                throw new SingerException(string.Join("; ", wrong));
            }
            singers[name] = singer = new NeutrinoSinger(options);
        }
        return singer;
    }

    Console.Error.WriteLine($"cantilena: serving neutrino from {folder}, WAVs under {outDir}");
    stdout.WriteLine(JsonSerializer.Serialize(new { ready = true, backend = "neutrino", voices = NeutrinoOptions.Voices(folder), outDir }, json));
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
                var score = request.Score is { } element ? ScoreJson.Read(element.GetRawText())
                    : request.ScoreFile is { } file ? ScoreJson.Read(await File.ReadAllTextAsync(file))
                    : throw new SingerException("a request needs \"score\" or \"scoreFile\"");
                var bad = score.Validate();
                if (bad.Count > 0)
                {
                    throw new SingerException(string.Join("; ", bad));
                }
                var singer = SingerOf(request.Voice ?? voice ?? "MERROW");
                var watch = Stopwatch.StartNew();
                var audio = await singer.SingAsync(score);
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
                }, json));
            }
            catch (Exception e) when (e is SingerException or JsonException or IOException or UnauthorizedAccessException)
            {
                stdout.WriteLine(JsonSerializer.Serialize(new { id, error = e.Message }, json));
            }
        }
    }
    finally
    {
        foreach (var singer in singers.Values)
        {
            singer.Dispose();
        }
    }
    return 0;
}

/// <summary>A request's id as a file name: letters, digits, '-', '_' and '.', the rest '_'.</summary>
static string FileNameOf(string id)
{
    var chars = id.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray();
    return chars.Length == 0 ? "score" : new string(chars);
}

/// <summary>One line of <c>cantilena serve</c>'s stdin.</summary>
sealed class ServeRequest
{
    public string? Id { get; set; }
    /// <summary><c>shutdown</c> ends the process; anything else is a score to sing.</summary>
    public string? Command { get; set; }
    /// <summary>The score itself (README.md's shape).</summary>
    public System.Text.Json.JsonElement? Score { get; set; }
    /// <summary>A file holding the score, instead of <see cref="Score"/>.</summary>
    public string? ScoreFile { get; set; }
    public string? Voice { get; set; }
    /// <summary>Where to write the WAV; default a file named after the id under the out dir.</summary>
    public string? Output { get; set; }
}

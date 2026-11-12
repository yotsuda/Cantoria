// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Cantoria;
using Cantoria.Audio;
using Cantoria.Cli;
using Cantoria.DiffSinger;

// cantoria — the command line over the library (docs/design.md §4).
// Exit codes: 0 done, 1 the backend failed, 2 bad arguments, score or settings,
// 3 (plan only) something asked for has no singer.

return args switch
{
    ["--version"] or ["-V"] => Version(),
    [] or ["--help"] or ["-h"] => Help(0),
    ["check", var path] => Check(path),
    ["voices", ..] => Voices(args[1..]),
    ["plan", ..] => Plan(args[1..]),
    ["providers"] => Providers(),
    ["sing", ..] => await Sing(args[1..]),
    ["serve", ..] => await Serve(args[1..]),
    _ => Help(2),
};

static int Version()
{
    var v = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion ?? "?";
    Console.WriteLine($"cantoria {v.Split('+')[0]}");
    return 0;
}

static int Help(int code)
{
    (code == 0 ? Console.Out : Console.Error).Write("""
        cantoria — a singing engine for notation software

        usage:
          cantoria check <score.json>                     validate a score, print its summary
          cantoria sing <score.json> -o <out.wav> [options]
          cantoria voices [--language <tag>] [--json] [options]
                                                          the voices installed, as engine/name, with the gender
                                                          and the languages each says
          cantoria plan [<score.json>] [plan options] [options]
                                                          who would sing it here, without singing
          cantoria providers                             where DiffSinger models can run on this machine
          cantoria serve [options]                        sing scores sent on stdin, one JSON line each
          cantoria --version

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
          --keep                      NEUTRINO: leave the MusicXML, labels and WAV under output\cantoria\
          --backend <name>            (older form) the engine of a bare --voice name

        plan: for each voice asked for, the installed voice that would sing it, or why none can. The file
        is a score (it asks for one voice in its language) or {"asked":[{"part":…,"key":…,"voice":…,
        "gender":…,"language":…}, …]}, each field optional; with no file the options are what is asked.
        A voice asked for by name sings when it is installed; else, and for a gender alone, that gender's
        default voice: the one set, else the first installed that says it is that gender and sings the
        language (else the other gender's, and the line says so). Hand `sing --voice` the voice it answers.
          --voice <engine/name>       the voice asked for (or female, male, as a singer line writes it)
          --gender <female|male>      the gender asked for when no voice is named (default female)
          --language <tag>            the words' language (ja, en, zh, ko, la, …) where the file says none
          --default-female-voice <engine/name>, --default-male-voice <engine/name>
                                      each gender's default voice (else CANTORIA_DEFAULT_FEMALE_VOICE,
                                      CANTORIA_DEFAULT_MALE_VOICE, else "default": {"female": …, "male": …}
                                      in cantoria.json)
          --json                      {"ready":true,"singers":[{"part":…,"asked":{…},"voice":"engine/name"
                                      or null,"note":…,"reason":… or null,"languages":[…]}]}
        Exit code 0 when everything asked for has a singer, 3 when something has none.

        serve: the same options but -o and --keep, plus --out-dir <folder> for the WAVs (default
        %TEMP%\cantoria) and --cache-mb <n>, which also keeps sung phrases on disk under that folder
        and holds its .wav files and the phrases to n megabytes together, the longest unused going
        first. The engines stay warm between requests. One request per line on stdin:
          {"id":"1","score":{…},"voice":"neutrino/MERROW","output":"C:\\x.wav"}   score, voice and output optional
          {"id":"1","scoreFile":"C:\\score.json"}
          {"command":"voices"}                                                    → {"voices":["neutrino/MERROW", …]}
          {"command":"plan","asked":[{"gender":"female","language":"la"}],"defaults":{"female":"","male":""}}
                                            → {"id":"3","plan":{"ready":false,"singers":[…]}}   (as plan --json;
                                            or "score"/"scoreFile" with "voice", "gender"; "defaults" optional)
          {"command":"cancel","id":"1"}     lets go of request 1 at its next phrase → {"id":"1","cancelled":true}
          {"command":"shutdown"}
        One answer per line on stdout, in order: first {"ready":true,"voices":[…],"outDir":"…"},
        then {"id":"1","wav":"…","duration":8.0,"sampleRate":48000,"seconds":0.5} or {"id":"1","error":"…"}.
        End of stdin ends the process.

        The folders are taken from the options, else the environment (CANTORIA_NEUTRINO_DIR,
        CANTORIA_VOICES_DIR, CANTORIA_VOCODER_DIR), else cantoria.json in the user's config folder
        (%APPDATA%\cantoria on Windows, ~/.config/cantoria elsewhere: "neutrino": {"folder": …},
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
        Console.Error.WriteLine($"cantoria: cannot read {path}: {e.Message}");
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
        Console.Error.WriteLine($"cantoria: {path} is not a score: {e.Message}");
        code = 2;
        return null;
    }
    var problems = score.Validate();
    if (problems.Count > 0)
    {
        foreach (var p in problems)
        {
            Console.Error.WriteLine($"cantoria: {path}: {p}");
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
        Console.Error.WriteLine($"cantoria: unknown option {string.Join(" ", a.Unknown.Append(a.Path ?? ""))}");
        return Help(2);
    }
    var engines = a.Engines();
    IReadOnlyList<VoiceEntry> voices = engines.Entries();
    if (voices.Count == 0)
    {
        Console.Error.WriteLine($"cantoria: no voice installed ({engines.Describe()})");
        return 1;
    }
    if (a.Language is { } language)
    {
        var all = voices;
        voices = all.Where(v => v.Sings(language)).ToList();
        if (voices.Count == 0)
        {
            Console.Error.WriteLine($"cantoria: no installed voice sings {language}; installed voices sing {string.Join(", ", SingerResolver.SungBy(all).DefaultIfEmpty("nothing they say"))}");
            return 1;
        }
    }
    if (a.Json)
    {
        Console.WriteLine(JsonSerializer.Serialize(voices.Select(v => new { id = v.Id, engine = v.Engine, name = v.Name, label = v.Label, language = v.Language, languages = v.Languages, gender = v.Gender, licence = v.Licence }), Wire()));
        return 0;
    }
    // The id first, as it always was; then what the voice says of itself, where it says.
    int width = voices.Max(v => v.Id.Length);
    foreach (var v in voices)
    {
        var says = new[] { v.Gender, v.Sung.Count > 0 ? "sings " + string.Join(", ", v.Sung) : null }.Where(s => s is not null).ToList();
        Console.WriteLine(says.Count == 0 ? v.Id : $"{v.Id.PadRight(width)}  {string.Join(", ", says)}");
    }
    return 0;
}

/// <summary>
/// Who would sing a score here, without singing it (the owner's decision 2026-11-13: a part asked
/// for a language no installed voice sings played in silence, and nothing said why). A line an
/// asked voice, or <c>--json</c>; exit 0 when each has a singer, 3 when one has none.
/// </summary>
static int Plan(string[] rest)
{
    var a = EngineArgs.Parse(rest);
    if (a.Unknown.Count > 0 || a.Output is not null)
    {
        Console.Error.WriteLine($"cantoria: unknown option {string.Join(" ", a.Unknown.Append(a.Output is null ? "" : "-o"))}");
        return Help(2);
    }
    IReadOnlyList<PlanAsk> asked;
    try
    {
        using JsonDocument? document = a.Path is null ? null : JsonDocument.Parse(File.ReadAllText(a.Path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        asked = SingerPlan.AskedOf(document?.RootElement, a.Voice, a.Gender, a.Language);
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"cantoria: cannot read {a.Path}: {e.Message}");
        return 2;
    }
    catch (JsonException e)
    {
        Console.Error.WriteLine($"cantoria: {a.Path}: {e.Message}");
        return 2;
    }
    var plan = a.Engines().Plan(asked);
    if (a.Json)
    {
        Console.WriteLine(plan.ToJson().ToJsonString(Wire()));
    }
    else
    {
        foreach (var line in plan.Lines())
        {
            Console.WriteLine(line);
        }
    }
    return plan.Ready ? 0 : 3;
}

/// <summary>JSON as the program writes it: camelCase, what is absent left out, no escaping of kana.</summary>
static JsonSerializerOptions Wire() => new(JsonSerializerDefaults.Web)
{
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
};

/// <summary>What this build on this machine can run DiffSinger models on — the CPU, DirectML, CoreML —
/// and what `auto` would pick; a line each. Exit 0 always: it is a report.</summary>
static int Providers()
{
    foreach (var line in Cantoria.DiffSinger.OnnxProviders.Describe())
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
        Console.Error.WriteLine($"cantoria: unknown option {string.Join(" ", a.Unknown)}");
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
            Console.Error.WriteLine($"cantoria: {p}");
        }
        return 2;
    }
    // A voice that says what it sings is not asked for another language's words: the reason plan gives.
    if (engines.LanguageProblem(a.Voice, score.Language) is { } cannot)
    {
        Console.Error.WriteLine($"cantoria: {cannot}");
        return 1;
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
        Console.Error.WriteLine($"cantoria: {singer.Name} sang {score.Notes.Count} notes, {audio.Duration:0.##} s at {audio.SampleRate} Hz, "
            + $"in {watch.Elapsed.TotalSeconds:0.#} s{ProviderOf(singer)} -> {a.Output}");
        return 0;
    }
    catch (SingerException e)
    {
        Console.Error.WriteLine($"cantoria: {e.Message}");
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
        Console.Error.WriteLine($"cantoria: unknown option {string.Join(" ", a.Unknown.Append(a.Path ?? "").Append(a.Output ?? ""))}");
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
                Console.Error.WriteLine($"cantoria: {p}");
            }
            return 2;
        }
    }
    else if (engines.NeutrinoFolder is null && engines.VoicesFolder is null && engines.List().Count == 0)
    {
        // Not fatal: a request can say where a VOICEVOX started after this process answers.
        Console.Error.WriteLine("cantoria: no engine configured yet: give --neutrino-dir or --voices-dir (or the environment, or cantoria.json), or start VOICEVOX");
    }
    var outDir = a.OutDir ?? Path.Combine(Path.GetTempPath(), "cantoria");
    Directory.CreateDirectory(outDir);
    if (a.CacheMb > 0)
    {
        engines.PhraseFolder = Cantoria.DiffSinger.PhraseDisk.FolderIn(outDir);
    }

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

    Console.Error.WriteLine($"cantoria: serving ({engines.Describe()}), WAVs under {outDir}");
    var backend = defaultVoice is not null ? Engines.Split(defaultVoice).Engine : engines.NeutrinoFolder is not null ? "neutrino" : "diffsinger";
    stdout.WriteLine(JsonSerializer.Serialize(new { ready = true, backend, voices = engines.List(), outDir }, json));
    try
    {
        // Lines are read while a score is being sung, so that a cancel can reach it; everything else
        // is done in order, one at a time.
        var work = System.Threading.Channels.Channel.CreateUnbounded<(string Line, CancellationTokenSource Cancel)>();
        var cancels = new Dictionary<string, CancellationTokenSource>();
        _ = Task.Run(async () =>
        {
            string? read;
            while ((read = await stdin.ReadLineAsync()) is not null)
            {
                if (string.IsNullOrWhiteSpace(read))
                {
                    continue;
                }
                var cancel = new CancellationTokenSource();
                try
                {
                    var asked = JsonSerializer.Deserialize<ServeRequest>(read, json);
                    if (asked?.Command == "cancel")
                    {
                        lock (cancels)
                        {
                            if (asked.Id is not null && cancels.TryGetValue(asked.Id, out var running))
                            {
                                running.Cancel();
                            }
                        }
                        continue;
                    }
                    if (asked?.Id is not null)
                    {
                        lock (cancels)
                        {
                            cancels[asked.Id] = cancel;
                        }
                    }
                }
                catch (JsonException)
                {
                    // Told when its turn comes.
                }
                work.Writer.TryWrite((read, cancel));
            }
            work.Writer.Complete();
        });

        int count = 0;
        await foreach (var (line, cancelled) in work.Reader.ReadAllAsync())
        {
            string id = (++count).ToString();
            var ct = cancelled.Token;
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
                if (request.Command == "parameters")
                {
                    // The settings a voice takes ("sing" in a score), for an editor's list: each
                    // with where it can change, what it does and its range.
                    var taken = (SingerOf(request.Voice) as ISingParameters)?.Parameters ?? [];
                    stdout.WriteLine(JsonSerializer.Serialize(new
                    {
                        id,
                        parameters = taken.Select(p => new { name = p.Name, scope = p.Scope.ToString().ToLowerInvariant(), what = p.What, min = p.Min, max = p.Max, @default = p.Default, unit = p.Unit, word = p.Word ? true : (bool?)null, values = p.Values }),
                    }, json));
                    continue;
                }
                if (request.Command == "plan")
                {
                    // Who would sing what is asked, without singing: the asked voices themselves, or a
                    // score with its voice and gender; the defaults the editor holds come with the request.
                    if (request.VoicevoxUrl is { Length: > 0 } answering)
                    {
                        engines.VoicevoxUrl = answering;
                    }
                    using JsonDocument? planFile = request.Asked is null && request.Score is null && request.ScoreFile is { } planPath
                        ? JsonDocument.Parse(await File.ReadAllTextAsync(planPath), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) : null;
                    var asking = SingerPlan.AskedOf(request.Asked is { } lines ? JsonSerializer.SerializeToElement(new { asked = lines }) : request.Score ?? planFile?.RootElement,
                        request.Voice, request.Gender, request.Language);
                    // "voicevoxUrl":"" — the editor asked VOICEVOX and none answers: it is not asked again.
                    var plan = engines.Plan(asking, request.Defaults is { } d ? new DefaultVoices(d.Female ?? "", d.Male ?? "") : null, voicevox: request.VoicevoxUrl != "");
                    stdout.WriteLine(new System.Text.Json.Nodes.JsonObject { ["id"] = id, ["plan"] = plan.ToJson() }.ToJsonString(json));
                    continue;
                }
                if (request.Command is not null)
                {
                    throw new SingerException($"no command named {request.Command} (there is: voices, plan, parameters, cancel, shutdown)");
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
                ct.ThrowIfCancellationRequested();
                if (engines.LanguageProblem(request.Voice ?? defaultVoice, score.Language) is { } cannot)
                {
                    throw new SingerException(cannot);
                }
                var singer = SingerOf(request.Voice);
                Console.Error.WriteLine($"cantoria: {DateTime.Now:HH:mm:ss} singing {FileNameOf(id)} in {singer.Name}, {score.Notes.Count} notes");
                var watch = Stopwatch.StartNew();
                // How far the score is, a line a phrase: the seconds of music to sing anew (what
                // is kept from before is not in them) and how many are done. The request's id
                // is INSIDE "progress", so a reader that knows only the answers - an id with a
                // wav or an error - takes the line for none of its own.
                string asked = id;
                SungAudio audio;
                // What the singer has to say of the score (a setting the voice does not take):
                // sentences for whoever wrote it, sent back with the answer.
                var said = new List<string>();
                using (SingReport.ListenNotes(text => { lock (said) { said.Add(text); } }))
                using (SingReport.Listen(p => stdout.WriteLine(JsonSerializer.Serialize(
                    new { progress = new { id = asked, toSing = Math.Round(p.ToSing, 3), sung = Math.Round(p.Sung, 3) } }, json))))
                {
                    audio = SungLevels.Apply(await singer.SingAsync(score, ct), score);
                }
                foreach (string text in said)
                {
                    Console.Error.WriteLine($"cantoria: {text}");
                }
                var output = request.Output ?? Path.Combine(outDir, FileNameOf(id) + ".wav");
                var dirName = Path.GetDirectoryName(Path.GetFullPath(output));
                if (!string.IsNullOrEmpty(dirName))
                {
                    Directory.CreateDirectory(dirName);
                }
                await File.WriteAllBytesAsync(output, Wav.Write(audio));
                if (a.CacheMb > 0)
                {
                    Cantoria.DiffSinger.PhraseDisk.Trim(outDir, a.CacheMb * 1024L * 1024);
                }
                stdout.WriteLine(JsonSerializer.Serialize(new
                {
                    id,
                    wav = Path.GetFullPath(output),
                    duration = Math.Round(audio.Duration, 4),
                    sampleRate = audio.SampleRate,
                    seconds = Math.Round(watch.Elapsed.TotalSeconds, 2),
                    voice = singer.Name,
                    provider = DiffSingerOf(singer)?.Provider,
                    notes = said.Count > 0 ? said : null,
                }, json));
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine($"cantoria: {DateTime.Now:HH:mm:ss} let go of {FileNameOf(id)}");
                stdout.WriteLine(JsonSerializer.Serialize(new { id, cancelled = true }, json));
            }
            catch (Exception e) when (e is SingerException or JsonException or IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
            {
                stdout.WriteLine(JsonSerializer.Serialize(new { id, error = e.Message }, json));
            }
            finally
            {
                lock (cancels)
                {
                    if (cancels.TryGetValue(id, out var mine) && ReferenceEquals(mine, cancelled))
                    {
                        cancels.Remove(id);
                    }
                }
                cancelled.Dispose();
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

static Cantoria.DiffSinger.DiffSingerSinger? DiffSingerOf(ISinger singer)
    => singer as Cantoria.DiffSinger.DiffSingerSinger ?? (singer as Cantoria.DiffSinger.DiffSingerSpeaker)?.Shared;

/// <summary>" on DirectML" / " on the CPU" for a DiffSinger singer (and why, when the CPU stood in); nothing for NEUTRINO.</summary>
static string ProviderOf(ISinger singer)
{
    if (DiffSingerOf(singer) is not { } ds)
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
    /// <summary><c>plan</c>'s and <c>voices</c>' own: what is asked for, each gender's default voice, the answer as JSON.</summary>
    public string? Gender, Language, DefaultFemale, DefaultMale;
    public bool Keep, Json;
    public OnnxProvider Provider = OnnxProvider.Auto;
    public int Threads = 4, Steps = 20, CacheMb;
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
                case "--cache-mb" when i + 1 < rest.Length && int.TryParse(rest[i + 1], out a.CacheMb): i++; break;
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
                case "--gender" when i + 1 < rest.Length: a.Gender = rest[++i]; break;
                case "--language" when i + 1 < rest.Length: a.Language = rest[++i]; break;
                case "--default-female-voice" when i + 1 < rest.Length: a.DefaultFemale = rest[++i]; break;
                case "--default-male-voice" when i + 1 < rest.Length: a.DefaultMale = rest[++i]; break;
                case "--json": a.Json = true; break;
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

    public Engines Engines() => Cantoria.Cli.Engines.Configure(NeutrinoDir, VoicesDir, VocoderDir, Provider, Threads, Steps, Keep, VoicevoxUrl, DefaultFemale, DefaultMale);
}

/// <summary>One line of <c>cantoria serve</c>'s stdin.</summary>
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
    /// <summary><c>plan</c>: the voices asked for, each <c>{ part?, key?, voice?, gender?, language? }</c>; instead of a score.</summary>
    public List<System.Text.Json.JsonElement>? Asked { get; set; }
    /// <summary><c>plan</c>, with a score: the gender asked for when <see cref="Voice"/> names no voice.</summary>
    public string? Gender { get; set; }
    /// <summary><c>plan</c>: the words' language where a line or the score says none.</summary>
    public string? Language { get; set; }
    /// <summary><c>plan</c>: each gender's default voice as the editor holds it; absent, the program's own.</summary>
    public ServeDefaults? Defaults { get; set; }
}

/// <summary>The default voices of a <c>plan</c> request: <c>{"female":"neutrino/MERROW","male":""}</c>.</summary>
sealed class ServeDefaults
{
    public string? Female { get; set; }
    public string? Male { get; set; }
}

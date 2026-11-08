// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Reflection;
using System.Text.Json;
using Cantilena;

// cantilena — the command line over the library (docs/design.md §4).
// Exit codes: 0 done, 1 the backend failed, 2 bad arguments or score.

return args switch
{
    ["--version"] or ["-V"] => Version(),
    [] or ["--help"] or ["-h"] => Help(0),
    ["check", var path] => Check(path),
    ["sing", ..] => Sing(args[1..]),
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
          cantilena check <score.json>                 validate a score, print its summary
          cantilena sing <score.json> -o <out.wav> [--backend <name>] [--voice <name|path>]
          cantilena --version

        The score format is documented in README.md. No backend sings yet (skeleton).

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

static int Sing(string[] rest)
{
    string? path = null, output = null, backend = null, voice = null;
    for (int i = 0; i < rest.Length; i++)
    {
        switch (rest[i])
        {
            case "-o" or "--output" when i + 1 < rest.Length: output = rest[++i]; break;
            case "--backend" when i + 1 < rest.Length: backend = rest[++i]; break;
            case "--voice" when i + 1 < rest.Length: voice = rest[++i]; break;
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
    var score = Load(path, out int code);
    if (score is null)
    {
        return code;
    }
    Console.Error.WriteLine($"cantilena: no backend is implemented yet (asked for {backend ?? "default"}"
        + $"{(voice is null ? "" : $", voice {voice}")}); {score.Notes.Count} notes, {score.End:0.###} s, "
        + $"would be written to {output}");
    return 1;
}

// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cantoria;

/// <summary>The default voice of each gender (the extension's <c>cantoria.defaultFemaleVoice</c> and
/// <c>cantoria.defaultMaleVoice</c>), as <c>engine/name</c>; empty is not set.</summary>
public sealed record DefaultVoices(string Female = "", string Male = "")
{
    public string Of(string gender) => (gender == "male" ? Male : Female) ?? "";
}

/// <summary>The voice a score's <c>singer</c> comes to, and a sentence when it is not the one asked for.</summary>
public sealed record ResolvedSinger(string Voice, string? Note = null);

/// <summary>
/// Which installed voice sings what a score asks for: a voice by name, a gender alone, nothing at
/// all. The one home of the rule (the owner's decision 2026-11-13): <c>cantoria plan</c>, <c>serve</c>'s
/// <c>plan</c> and the extension's <c>plan()</c> answer from here. The extension's
/// <c>resolveSinger</c> (<c>editors/vscode/src/core.ts</c>) is the same rule written in TypeScript,
/// kept while its callers move over; each function here names the one it is.
/// </summary>
public static class SingerResolver
{
    /// <summary>The gender a word of a score's <c>singer</c> says, or null when it says none (<c>genderOfWord</c>).</summary>
    public static string? GenderOfWord(string word) => word switch
    {
        "female" or "soprano" or "mezzosoprano" or "alto" or "contralto" => "female",
        "male" or "tenor" or "baritone" or "bass" => "male",
        _ => null,
    };

    /// <summary>The voices a <c>+</c> joins, each to sing the score together (<c>voicePieces</c>).</summary>
    public static IReadOnlyList<string> Pieces(string text)
        => text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>A voice as written, as its <c>engine/name</c>: a bare name is NEUTRINO's, empty its MERROW (<c>splitVoiceId</c>).</summary>
    public static string NormalVoice(string? text)
    {
        var t = (text ?? "").Trim();
        int slash = t.IndexOf('/');
        if (slash < 0)
        {
            return "neutrino/" + (t.Length > 0 ? t : "MERROW");
        }
        var engine = t[..slash].Trim().ToLowerInvariant();
        var name = t[(slash + 1)..].Trim();
        return (engine.Length > 0 ? engine : "neutrino") + "/" + (name.Length > 0 ? name : "MERROW");
    }

    /// <summary>The first voice listed that says it is <paramref name="gender"/> and sings <paramref name="language"/>
    /// (the same one every time). A voice that does not say is not guessed at (<c>firstOfGender</c>).</summary>
    public static string? FirstOfGender(IReadOnlyList<VoiceEntry> voices, string gender, string? language)
        => voices.FirstOrDefault(v => v.Gender == gender && (string.IsNullOrEmpty(language) || v.Sings(language)))?.Id;

    /// <summary>
    /// The default voice of a gender on this machine: the setting's, else the first installed voice
    /// that says it is that gender and sings <paramref name="language"/>. With none, the other
    /// gender's sings, and a sentence says so (<c>defaultVoiceFor</c>).
    /// </summary>
    public static ResolvedSinger DefaultVoiceFor(IReadOnlyList<VoiceEntry> voices, DefaultVoices defaults, string gender, string? language = null)
    {
        string? Installed(string g)
        {
            var written = defaults.Of(g).Trim();
            var voice = written.Length > 0 ? NormalVoice(written) : null;
            return voice is not null && voices.Any(v => v.Id == voice) ? voice : null;
        }
        var own = Installed(gender) ?? FirstOfGender(voices, gender, language);
        var set = defaults.Of(gender).Trim();
        if (own is not null)
        {
            return set.Length > 0 && own != NormalVoice(set) ? new(own, $"{NormalVoice(set)} is not installed; singing with {own}") : new(own);
        }
        var other = gender == "male" ? "female" : "male";
        var spare = Installed(other) ?? FirstOfGender(voices, other, language)
            ?? voices.FirstOrDefault(v => string.IsNullOrEmpty(language) || string.IsNullOrEmpty(v.Language) || v.Sings(language))?.Id
            ?? (voices.Count > 0 ? voices[0].Id : NormalVoice(""));
        return new(spare, $"no installed voice for these words says it is {gender}; singing with {spare}");
    }

    /// <summary>
    /// The voice a score's <c>singer</c> string comes to on this machine: the voice it names when that
    /// is installed; else the default voice of the gender it says, or of <paramref name="gender"/>
    /// (what the part sounds like), with a sentence when it was not the one asked for (<c>resolveSinger</c>).
    /// </summary>
    public static ResolvedSinger Resolve(string? written, IReadOnlyList<VoiceEntry> voices, DefaultVoices defaults, string? language = null, string? gender = null)
    {
        var pieces = Pieces(written ?? "");
        if (pieces.Count > 1)
        {
            var each = pieces.Select(p => Resolve(p, voices, defaults, language, gender)).ToList();
            var notes = each.Select(r => r.Note).Where(n => !string.IsNullOrEmpty(n)).ToList();
            return new(string.Join("+", each.Select(r => r.Voice).Distinct()), notes.Count > 0 ? string.Join("; ", notes) : null);
        }
        var asked = (written ?? "").Trim();
        var said = GenderOfWord(asked);
        var fallback = DefaultVoiceFor(voices, defaults, said ?? (gender == "male" ? "male" : "female"), language);
        if (asked.Contains('/'))
        {
            var voice = NormalVoice(asked);
            return voices.Any(v => v.Id == voice) ? new(voice) : new(fallback.Voice, $"{voice} is not installed; singing with {fallback.Voice}");
        }
        if (asked.Length == 0 || said is not null)
        {
            return fallback;
        }
        return new(fallback.Voice, $"the singer \"{asked}\" names no voice; singing with {fallback.Voice}");
    }

    /// <summary>Why <paramref name="voice"/> cannot sing words in <paramref name="language"/>, or null — a voice
    /// that does not say what it sings is let try (<c>languageProblem</c>).</summary>
    public static string? LanguageProblem(VoiceEntry? voice, string? language)
        => voice?.Language is { Length: > 0 } && !string.IsNullOrEmpty(language) && !voice.Sings(language)
            ? $"{voice.Id} sings {string.Join(", ", voice.Sung)}, and these words are {language}" : null;

    /// <summary>A language's name for a sentence; a tag with no name here is shown as the tag.</summary>
    public static string LanguageName(string tag) => tag switch
    {
        "ja" => "Japanese",
        "en" => "English",
        "zh" => "Chinese",
        "ko" => "Korean",
        "la" => "Latin",
        _ => tag,
    };

    /// <summary>
    /// Why the words cannot be sung by <paramref name="voice"/>, in words a score's writer can act on,
    /// or null: that no installed voice sings the language, and what they do sing; else what this
    /// voice sings, and which voices sing the words. What <c>sing</c> refuses with and <c>plan</c> answers.
    /// </summary>
    public static string? LanguageReason(VoiceEntry? voice, string? language, IReadOnlyList<VoiceEntry> voices)
    {
        if (LanguageProblem(voice, language) is not { } problem)
        {
            return null;
        }
        // A voice's other speakers sing what it sings: the voice stands for them.
        var singing = voices.Where(v => v.Sings(language!)).Select(v => v.Id).ToList();
        singing = singing.Where(id => !singing.Any(other => id.StartsWith(other + "/", StringComparison.Ordinal))).ToList();
        if (singing.Count == 0)
        {
            var sung = SungBy(voices);
            return (LanguageName(language!) == language ? $"no installed voice sings {language}" : $"no installed voice sings {LanguageName(language!)} ({language})")
                + (sung.Count > 0 ? $"; installed voices sing {string.Join(", ", sung)}" : "");
        }
        return $"{problem} ({string.Join(", ", singing.Take(4))}{(singing.Count > 4 ? ", …" : "")} {(singing.Count == 1 ? "sings" : "sing")} {language})";
    }

    /// <summary>The languages the voices sing between them, in the order first met.</summary>
    public static IReadOnlyList<string> SungBy(IReadOnlyList<VoiceEntry> voices) => voices.SelectMany(v => v.Sung).Distinct().ToList();
}

/// <summary>What a score asks a singer for: a voice by name (<c>neutrino/MERROW</c>; several joined by
/// <c>+</c>; or as a <c>singer</c> line writes it, a gender word), a gender alone, the words' language —
/// each optional — and the part and the key the caller knows the line by, given back as they came.</summary>
public sealed record PlanAsk(string? Part = null, string? Key = null, string? Voice = null, string? Gender = null, string? Language = null);

/// <summary>Who sings what was asked: the installed voice (<c>engine/name</c>), a sentence when it is not
/// the one asked for; or no voice, and the reason. <see cref="Languages"/>: what that voice sings where it
/// says, and with no voice what the installed voices sing between them.</summary>
public sealed record PlannedSinger(PlanAsk Asked, string? Voice, string? Note, string? Reason, IReadOnlyList<string>? Languages);

/// <summary>
/// The answer of <c>cantoria plan</c>: for each voice a score asks for, who would sing it on this
/// machine — without singing. <see cref="Ready"/> when every one has a singer.
/// </summary>
public sealed record SingerPlan(IReadOnlyList<PlannedSinger> Singers)
{
    public bool Ready => Singers.All(s => s.Voice is not null);

    /// <summary>
    /// The plan for <paramref name="asked"/> over the installed <paramref name="voices"/>:
    /// <see cref="SingerResolver.Resolve"/> names the voice, and what <c>sing</c> would then refuse is the reason.
    /// <paramref name="problems"/>: why a voice (<c>engine/name</c>) cannot be sung with as the engines
    /// stand — a folder that is not one, a missing vocoder; null or empty when it can.
    /// <paramref name="setup"/>: for an engine with no voice here, by its name, why — its folder is
    /// not set, it is not running.
    /// </summary>
    public static SingerPlan For(IEnumerable<PlanAsk> asked, IReadOnlyList<VoiceEntry> voices, DefaultVoices defaults,
        Func<string, IReadOnlyList<string>?>? problems = null, IReadOnlyDictionary<string, string>? setup = null)
    {
        var checkedVoices = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        IReadOnlyList<string> ProblemsOf(string voice)
        {
            if (!checkedVoices.TryGetValue(voice, out var found))
            {
                checkedVoices[voice] = found = problems?.Invoke(voice) ?? [];
            }
            return found;
        }
        string? Missing(string engine) => setup is not null && setup.TryGetValue(engine, out var why) ? why : null;

        var singers = new List<PlannedSinger>();
        foreach (var ask in asked)
        {
            var language = string.IsNullOrWhiteSpace(ask.Language) ? null : ask.Language.Trim();
            var resolved = SingerResolver.Resolve(ask.Voice, voices, defaults, language, ask.Gender);
            var named = SingerResolver.Pieces(ask.Voice ?? "").Where(p => p.Contains('/')).Select(SingerResolver.NormalVoice).ToList();
            string? reason = null;
            if (voices.Count == 0)
            {
                reason = "no voice is installed" + (setup is { Count: > 0 } ? ": " + string.Join("; ", setup.Values) : "");
            }
            var entries = new List<VoiceEntry>();
            foreach (var piece in SingerResolver.Pieces(resolved.Voice))
            {
                if (reason is not null)
                {
                    break;
                }
                var entry = voices.FirstOrDefault(v => v.Id == piece);
                if (entry is null)
                {
                    reason = $"{piece} is not installed" + (Missing(piece[..piece.IndexOf('/')]) is { } why ? $": {why}" : "");
                }
                else
                {
                    entries.Add(entry);
                    reason = SingerResolver.LanguageReason(entry, language, voices)
                        ?? (ProblemsOf(piece) is { Count: > 0 } wrong ? string.Join("; ", wrong) : null);
                    // A default voice that was set sings whatever the words are: the setting is what to change.
                    if (reason is not null && reason.StartsWith(piece + " sings ", StringComparison.Ordinal) && !named.Contains(piece)
                        && new[] { "female", "male" }.FirstOrDefault(g => defaults.Of(g).Trim().Length > 0 && SingerResolver.NormalVoice(defaults.Of(g)) == piece) is { } whose)
                    {
                        reason = $"the default {whose} voice {reason}";
                    }
                }
            }
            // A voice asked for by name whose engine has nothing here: the sentence says why beside what sings instead.
            var note = resolved.Note;
            if (note is not null)
            {
                var engines = named.Where(v => voices.All(x => x.Id != v)).Select(v => Missing(v[..v.IndexOf('/')])).Where(w => w is not null).Distinct().ToList();
                if (engines.Count > 0)
                {
                    note += "; " + string.Join("; ", engines);
                }
            }
            IReadOnlyList<string> languages = reason is null ? entries.SelectMany(e => e.Sung).Distinct().ToList() : SingerResolver.SungBy(voices);
            singers.Add(new PlannedSinger(ask, reason is null ? resolved.Voice : null, reason is null ? note : null, reason, languages.Count > 0 ? languages : null));
        }
        return new SingerPlan(singers);
    }

    /// <summary>
    /// What a plan is asked: from <c>{"asked":[{ "part", "key", "voice", "gender", "language" }, …]}</c>,
    /// each field optional; or from a score (README.md's, the one <c>sing</c> takes), which asks for
    /// one voice in its <c>language</c> (absent: <c>ja</c>, as it is sung). <paramref name="voice"/>,
    /// <paramref name="gender"/> and <paramref name="language"/> — a command's options — stand where a
    /// line says none; with no document they are the one voice asked for.
    /// </summary>
    /// <exception cref="JsonException">The document is neither.</exception>
    public static IReadOnlyList<PlanAsk> AskedOf(JsonElement? document, string? voice = null, string? gender = null, string? language = null)
    {
        if (document is not { } root)
        {
            return [new PlanAsk(Voice: voice, Gender: gender, Language: language)];
        }
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("neither a score nor {\"asked\":[…]}: not an object");
        }
        static string? Text(JsonElement o, string name) => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        if (root.TryGetProperty("asked", out var list))
        {
            if (list.ValueKind != JsonValueKind.Array || list.EnumerateArray().Any(e => e.ValueKind != JsonValueKind.Object))
            {
                throw new JsonException("\"asked\" is not an array of objects");
            }
            return list.EnumerateArray().Select(e => new PlanAsk(Text(e, "part"), Text(e, "key"), Text(e, "voice") ?? voice, Text(e, "gender") ?? gender, Text(e, "language") ?? language)).ToList();
        }
        if (!root.TryGetProperty("notes", out _))
        {
            throw new JsonException("neither a score (\"notes\") nor a list of what is asked ({\"asked\":[…]})");
        }
        return [new PlanAsk(Voice: voice, Gender: gender, Language: language ?? Text(root, "language") ?? "ja")];
    }

    /// <summary>One line an asked voice: <c>alto: female, la -> no installed voice sings Latin (la); installed voices sing ja, en</c>.</summary>
    public IReadOnlyList<string> Lines() => Singers.Select(s =>
    {
        var who = s.Asked.Part ?? s.Asked.Key;
        var asked = new[] { s.Asked.Voice?.Trim(), s.Asked.Gender, s.Asked.Language }.Where(t => !string.IsNullOrEmpty(t)).ToList();
        return (string.IsNullOrEmpty(who) ? "" : who + ": ") + (asked.Count > 0 ? string.Join(", ", asked) : "the default voice")
            + " -> " + (s.Voice is null ? s.Reason : s.Voice + (s.Note is null ? "" : $" ({s.Note})"));
    }).ToList();

    /// <summary>
    /// <c>{ "ready": bool, "singers": [{ "part"?, "key"?, "asked": { "voice"?, "gender"?, "language"? },
    /// "voice": "engine/name" | null, "note"?, "reason": string | null, "languages"?: […] }] }</c> —
    /// the same from <c>plan --json</c>, <c>serve</c> and the extension's <c>plan()</c>.
    /// </summary>
    public JsonObject ToJson()
    {
        static JsonObject Some(params (string Name, string? Value)[] fields)
        {
            var o = new JsonObject();
            foreach (var (name, value) in fields)
            {
                if (value is not null)
                {
                    o[name] = value;
                }
            }
            return o;
        }
        var singers = new JsonArray();
        foreach (var s in Singers)
        {
            var o = Some(("part", s.Asked.Part), ("key", s.Asked.Key));
            o["asked"] = Some(("voice", s.Asked.Voice), ("gender", s.Asked.Gender), ("language", s.Asked.Language));
            o["voice"] = s.Voice;
            if (s.Note is not null)
            {
                o["note"] = s.Note;
            }
            o["reason"] = s.Reason;
            if (s.Languages is not null)
            {
                o["languages"] = new JsonArray(s.Languages.Select(l => (JsonNode?)l).ToArray());
            }
            singers.Add(o);
        }
        return new JsonObject { ["ready"] = Ready, ["singers"] = singers };
    }
}

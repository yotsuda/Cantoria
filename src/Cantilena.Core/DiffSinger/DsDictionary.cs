// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Cantilena.DiffSinger;

/// <summary>
/// A voice's grapheme-to-phoneme dictionary: which symbols exist and which are vowels or glides
/// (the timing rules need both), and what each grapheme spells. <c>SP</c> and <c>AP</c> (silence,
/// breath) count as vowels, as OpenUtau has it.
/// </summary>
public sealed class DsDictionary
{
    private readonly Dictionary<string, bool> _vowel = new();
    private readonly HashSet<string> _glide = new();
    private readonly Dictionary<string, string[]> _entries = new();

    private DsDictionary() { }

    /// <summary>Reads a dictionary file's text.</summary>
    public static DsDictionary Parse(string yaml) => Of(DsYaml.Parse<G2pDictionaryData>(yaml));

    public static DsDictionary Load(string path) => Of(DsYaml.Read<G2pDictionaryData>(path));

    private static DsDictionary Of(G2pDictionaryData data)
    {
        var d = new DsDictionary();
        foreach (var s in data.Symbols ?? [])
        {
            var symbol = s.Symbol.Trim();
            d._vowel[symbol] = s.Type == "vowel";
            if (s.Type is "semivowel" or "liquid")
            {
                d._glide.Add(symbol);
            }
        }
        d._vowel["SP"] = true;
        d._vowel["AP"] = true;
        foreach (var e in data.Entries ?? [])
        {
            d._entries[e.Grapheme] = e.Phonemes;
        }
        return d;
    }

    public int Count => _entries.Count;
    public bool IsSymbol(string symbol) => _vowel.ContainsKey(symbol);
    public bool IsVowel(string symbol) => _vowel.TryGetValue(symbol, out var v) && v;
    public bool IsGlide(string symbol) => _glide.Contains(symbol);

    /// <summary>The phonemes a grapheme spells, as written in the file; null when it has no entry.</summary>
    public string[]? Query(string grapheme) =>
        _entries.TryGetValue(grapheme, out var p) ? p
        : _entries.TryGetValue(grapheme.ToLowerInvariant(), out p) ? p
        : null;
}

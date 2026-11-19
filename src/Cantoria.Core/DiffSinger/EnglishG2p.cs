// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.
//
// Follows OpenUtau's ArpabetG2p and G2pPack (OpenUtau.Core/G2p/ArpabetG2p.cs, Api/G2pPack.cs; MIT,
// Copyright (c) 2014 StAkira): the pack's dictionary first, then its model, read a letter at a
// time. The pack itself (g2p-arpabet.zip: CMUdict 0.7 and the model trained on it) is OpenUtau's,
// carried as it is. See THIRD-PARTY-NOTICES.md.

using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Cantoria.DiffSinger;

/// <summary>
/// How an English word is said, for a word the voice's own dictionary does not list (owner's
/// report 2026-11-19, Lily#'s Paper-Boats.lys under diffsinger/TIGER, a dictionary of ten
/// thousand words: "folded" was read from its letters as f aa l d eh d, "heavy" as hh iy v iy):
/// the CMU Pronouncing Dictionary (<see cref="Listed"/>, 134,000 words), and for a word it does
/// not have either, a model trained on it (<see cref="Predicted"/>) — what OpenUtau's English
/// phonemizers read a word by. The phonemes are ARPABET's, lower case, without their stress.
/// </summary>
public static class EnglishG2p
{
    // The model's alphabets, in its own order (OpenUtau's ArpabetG2p): the first four are its marks.
    private static readonly string[] Graphemes =
    [
        "", "", "", "", "'", "-", "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m", "n", "o", "p",
        "q", "r", "s", "t", "u", "v", "w", "x", "y", "z",
    ];

    private static readonly string[] Phonemes =
    [
        "", "", "", "", "aa", "ae", "ah", "ao", "aw", "ay", "b", "ch", "d", "dh", "eh", "er", "ey", "f", "g", "hh", "ih", "iy", "jh",
        "k", "l", "m", "n", "ng", "ow", "oy", "p", "r", "s", "sh", "t", "th", "uh", "uw", "v", "w", "y", "z", "zh",
    ];

    /// <summary>The unstressed AH (CMUdict's AH0), the schwa: a voice that has <c>ax</c> sings it so
    /// (TIGER's and Nishiren's dictionaries write "the" dh ax), another its <c>ah</c>.</summary>
    public const string Schwa = "ax";

    private static readonly Lazy<(Dictionary<string, string[]> Words, InferenceSession? Model)> Pack = new(Load);
    private static readonly ConcurrentDictionary<string, string[]?> Said = new(StringComparer.Ordinal);
    private static readonly object Running = new();

    /// <summary>The word's phonemes as the dictionary lists them (its first reading), an unstressed
    /// AH as <see cref="Schwa"/>; null for a word it does not have.</summary>
    public static string[]? Listed(string word) =>
        Pack.Value.Words.TryGetValue(word.ToLowerInvariant(), out var phonemes) ? (string[])phonemes.Clone() : null;

    /// <summary>The word's phonemes as the model reads its letters; null where there is no model
    /// to ask, or it makes nothing of the word (no letter of its alphabet).</summary>
    public static string[]? Predicted(string word)
    {
        word = word.ToLowerInvariant();
        return Said.GetOrAdd(word, Predict) is { Length: > 0 } phonemes ? (string[])phonemes.Clone() : null;
    }

    private static string[]? Predict(string word)
    {
        if (Pack.Value.Model is not { } model)
        {
            return null;
        }
        var letters = new List<int>();
        foreach (char c in word)
        {
            // A word with anything but the model's letters is not one it reads (OpenUtau drops
            // the others and reads the rest: "starほげ" would be sung as "star").
            int index = Array.IndexOf(Graphemes, c.ToString(), 4);
            if (index < 0)
            {
                return null;
            }
            letters.Add(index);
        }
        if (letters.Count == 0)
        {
            return null;
        }
        var src = new DenseTensor<int>(letters.ToArray(), [1, letters.Count]);
        var said = new List<int> { 2 };     // (the model's start mark)
        var t = new DenseTensor<int>(1);
        lock (Running)
        {
            // A letter at a time: the model says the next phoneme of this letter, or (2) that the
            // letter is done. No word of 48 phonemes.
            while (t[0] < letters.Count && said.Count < 48)
            {
                var tgt = new DenseTensor<int>(said.ToArray(), [1, said.Count]);
                using var outputs = model.Run(
                [
                    NamedOnnxValue.CreateFromTensor("src", src),
                    NamedOnnxValue.CreateFromTensor("tgt", tgt),
                    NamedOnnxValue.CreateFromTensor("t", t),
                ]);
                int next = outputs.First().AsTensor<int>()[0];
                if (next == 2)
                {
                    t[0]++;
                }
                else
                {
                    said.Add(next);
                }
            }
        }
        return said.Skip(1).Where(i => i >= 4 && i < Phonemes.Length).Select(i => Phonemes[i]).ToArray();
    }

    private static (Dictionary<string, string[]>, InferenceSession?) Load()
    {
        using var stream = typeof(EnglishG2p).Assembly.GetManifestResourceStream("Cantoria.DiffSinger.g2p-arpabet.zip")
            ?? throw new InvalidOperationException("the English pronouncing dictionary is not in the assembly");
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var words = new Dictionary<string, string[]>(140000, StringComparer.Ordinal);
        using (var reader = new StreamReader(zip.GetEntry("dict.txt")!.Open(), Encoding.UTF8))
        {
            while (reader.ReadLine() is { } line)
            {
                // "WORD  PH1 PH2 …"; ";;;" a comment; "WORD(1)" another reading of WORD, not taken.
                int gap = line.IndexOf("  ", StringComparison.Ordinal);
                if (gap <= 0 || line.StartsWith(";;;", StringComparison.Ordinal) || line[gap - 1] == ')')
                {
                    continue;
                }
                var phonemes = line[(gap + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < phonemes.Length; i++)
                {
                    phonemes[i] = phonemes[i] == "AH0" ? Schwa : phonemes[i].TrimEnd('0', '1', '2').ToLowerInvariant();
                }
                words.TryAdd(line[..gap].ToLowerInvariant(), phonemes);
            }
        }
        InferenceSession? model = null;
        try
        {
            using var onnx = zip.GetEntry("g2p.onnx")!.Open();
            using var bytes = new MemoryStream();
            onnx.CopyTo(bytes);
            model = new InferenceSession(bytes.ToArray());
        }
        catch (Exception e) when (e is OnnxRuntimeException or DllNotFoundException or InvalidDataException)
        {
            // No model here: the dictionary alone, and the letters after it (EnglishLetters).
        }
        return (words, model);
    }
}

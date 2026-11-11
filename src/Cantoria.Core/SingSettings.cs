// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Cantoria;

/// <summary>
/// One setting of how a voice sings, as a score writes it: a number, or a word (a name such as a
/// support voice's, or <see cref="DefaultWord"/> for "as it was before I set it").
/// </summary>
public readonly record struct SingValue(double? Number, string? Word)
{
    /// <summary>The word that gives a setting back to the voice.</summary>
    public const string DefaultWord = "default";

    public static SingValue Of(double number) => new(number, null);
    public static SingValue Of(string word) => new(null, word);

    /// <summary>True for <see cref="DefaultWord"/>: the setting is the voice's own again.</summary>
    public bool IsDefault => Word == DefaultWord;

    public override string ToString() => Number is { } n ? n.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) : Word ?? "";
}

/// <summary>Where a setting can change (Lily#'s grammar, 2026-11-12: one way to write, three kinds
/// of setting; the voice says which each is, and the editor's list shows it).</summary>
public enum SingScope
{
    /// <summary>A value a frame: it can change at any note and glide between two.</summary>
    Curve,
    /// <summary>One value a phrase sung: it changes where a phrase ends, and does not glide.</summary>
    Phrase,
    /// <summary>One value for the voice in a score.</summary>
    Voice,
}

/// <summary>
/// A setting a voice takes, for whoever writes scores for it: its name as a score writes it,
/// where it can change, what it does in a sentence, and — for a number — the range that means
/// something, the value that changes nothing and the unit.
/// </summary>
public sealed record SingParameter(string Name, SingScope Scope, string What,
    double? Min = null, double? Max = null, double? Default = null, string? Unit = null);

/// <summary>A singer that takes settings (<see cref="SungScore.Sing"/>): which.</summary>
public interface ISingParameters
{
    /// <summary>The settings this voice takes; a name not among them is not sung, and said so
    /// (<see cref="SingReport.Note"/>).</summary>
    IReadOnlyList<SingParameter> Parameters { get; }
}

/// <summary>What a score's settings come to for a voice.</summary>
public static class SingSettings
{
    /// <summary>
    /// The numbers <paramref name="sing"/> sets among <paramref name="known"/>, clamped to each
    /// one's range; a name the voice does not take, a word where it takes a number, and a value
    /// out of range are told once each (<see cref="SingReport.Note"/>) and the rest is sung.
    /// </summary>
    public static Dictionary<string, double> Numbers(IReadOnlyDictionary<string, SingValue>? sing,
        IReadOnlyList<SingParameter> known, string voice)
    {
        var numbers = new Dictionary<string, double>(StringComparer.Ordinal);
        if (sing is null)
        {
            return numbers;
        }
        foreach (var (name, value) in sing)
        {
            if (value.IsDefault)
            {
                continue;
            }
            var parameter = known.FirstOrDefault(p => p.Name == name);
            if (parameter is null)
            {
                string there = known.Count == 0 ? "it takes none" : "it takes " + string.Join(", ", known.Select(p => p.Name));
                SingReport.Note($"{voice} does not take the setting \"{name}\" ({there}); it sings without it");
                continue;
            }
            if (value.Number is not { } number)
            {
                SingReport.Note($"{voice}: \"{name}\" takes a number, not \"{value.Word}\"; it sings without it");
                continue;
            }
            double clamped = Math.Clamp(number, parameter.Min ?? double.MinValue, parameter.Max ?? double.MaxValue);
            if (clamped != number)
            {
                SingReport.Note($"{voice}: \"{name} {value}\" is outside {parameter.Min:0.##} to {parameter.Max:0.##}; sung as {clamped:0.##}");
            }
            numbers[name] = clamped;
        }
        return numbers;
    }

    /// <summary>What tells a phrase sung with these settings from the same notes without (a phrase
    /// cache's key); empty with none, so the phrases kept before are still found.</summary>
    public static string KeyOf(IReadOnlyDictionary<string, double> numbers)
        => numbers.Count == 0 ? "" : "|sing " + string.Join(' ', numbers.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{p.Key}={p.Value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)}"));
}
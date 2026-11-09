using System.Text.RegularExpressions;

namespace Cantoria.Neutrino;

/// <summary>
/// A correction to NEUTRINO's timing prediction. Where a note starts on the very phoneme the last
/// one ended on (ん after ん, a held vowel), nothing is articulated at the join, and the model puts
/// the boundary where it likes: 0.4 s after the beat in a choir's held ん (the owner's report on
/// M24 of Harukaze-no-Mukou-e, 2026-11-09), so the pitch moves late. The score knows where that
/// note starts, so the boundary goes there. A boundary with a consonant keeps the model's place:
/// a singer starts a consonant before the beat.
/// </summary>
public static partial class NeutrinoTiming
{
    /// <summary>True when the score has a join <see cref="AlignRepeats"/> would move, so the
    /// engine must stop to have its timing corrected.</summary>
    public static bool HasRepeats(string fullLab)
    {
        var phones = Lines(fullLab).Select(Phoneme).ToList();
        return Enumerable.Range(1, Math.Max(0, phones.Count - 1)).Any(i => IsRepeat(phones[i - 1], phones[i]));
    }

    /// <summary><paramref name="timingLab"/> with every join between two like phonemes moved to
    /// where <paramref name="fullLab"/>'s score has it. Unchanged when the two do not line up, and
    /// a join that would leave a phoneme no length stays.</summary>
    public static string AlignRepeats(string fullLab, string timingLab)
    {
        var full = Lines(fullLab);
        var timing = Lines(timingLab).Select(l => l.Split(' ', 3)).ToList();
        if (full.Count != timing.Count || timing.Any(t => t.Length < 3)
            || !full.Select(Phoneme).SequenceEqual(timing.Select(t => t[2])))
        {
            return timingLab;
        }
        for (int i = 1; i < timing.Count; i++)
        {
            if (IsRepeat(timing[i - 1][2], timing[i][2])
                && long.TryParse(full[i].Split(' ', 2)[0], out long at)
                && long.TryParse(timing[i - 1][0], out long start)
                && long.TryParse(timing[i][1], out long end)
                && start < at && at < end)
            {
                timing[i - 1][1] = timing[i][0] = at.ToString();
            }
        }
        return string.Join("\n", timing.Select(t => string.Join(' ', t))) + "\n";
    }

    private static bool IsRepeat(string before, string after) => before == after && after is not ("pau" or "sil" or "br");

    private static List<string> Lines(string lab)
        => [.. lab.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0)];

    private static string Phoneme(string fullLine)
    {
        var m = PhonemeOfContext().Match(fullLine);
        return m.Success ? m.Groups[1].Value : "";
    }

    [GeneratedRegex(@"-([^+\s]+)\+")]
    private static partial Regex PhonemeOfContext();
}

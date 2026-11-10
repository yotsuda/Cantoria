// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Cantoria.DiffSinger;

/// <summary>
/// Where a sung line is cut into the phrases the models run on. A cut in a rest cannot be heard, and
/// depends only on that rest, so an edit moves no cut outside the phrase it is in.
/// </summary>
public static class PhraseCut
{
    /// <summary>
    /// The phrases, as the index of the first word and how many: a new one after each rest of at
    /// least <paramref name="restSeconds"/>. A phrase that would still pass
    /// <paramref name="longestSeconds"/> is cut in its widest gap in the later half of that length,
    /// the later one if they tie.
    /// </summary>
    /// <param name="words">Each word's start and end in seconds, in time order.</param>
    /// <param name="restSeconds">The shortest rest that ends a phrase.</param>
    /// <param name="longestSeconds">The longest a phrase may be.</param>
    public static IReadOnlyList<(int First, int Count)> Split(IReadOnlyList<(double Start, double End)> words, double restSeconds, double longestSeconds)
    {
        var cuts = new List<(int First, int Count)>();
        int from = 0;
        for (int i = 1; i <= words.Count; i++)
        {
            if (i == words.Count || words[i].Start - words[i - 1].End >= restSeconds)
            {
                Shorten(words, from, i, longestSeconds, cuts);
                from = i;
            }
        }
        return cuts;
    }

    private static void Shorten(IReadOnlyList<(double Start, double End)> words, int from, int to, double longest, List<(int First, int Count)> cuts)
    {
        while (words[to - 1].End - words[from].Start > longest)
        {
            int best = from + 1;
            double widest = -1;
            for (int k = from + 1; k < to && words[k - 1].End - words[from].Start <= longest; k++)
            {
                double sung = words[k - 1].End - words[from].Start;
                double gap = words[k].Start - words[k - 1].End;
                if (sung < longest / 2)
                {
                    best = k;       // the latest so far, should the later half have no word end at all
                }
                else if (gap >= widest)
                {
                    best = k;
                    widest = gap;
                }
            }
            cuts.Add((from, best - from));
            from = best;
            if (from == to)
            {
                return;
            }
        }
        cuts.Add((from, to - from));
    }
}

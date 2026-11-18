// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Cantoria.DiffSinger;

namespace Cantoria;

/// <summary>
/// The phrase a score was let go of in the middle of (<see cref="PhrasedSinger"/>): still being
/// sung, until the next score says whether it wants it. A score that sings it lets it finish and
/// takes it; any other stops it there, so that an engine which takes minutes over a phrase (NEUTRINO
/// on a CPU) is not kept on a text nobody asks for any more (owner's report 2026-11-18: the edits
/// of one minute piled up behind a phrase being sung for five).
/// </summary>
/// <remarks>
/// One is shared by the singers of one engine process: the engine sings one score at a time, so a
/// score in another voice decides too — it would otherwise be sung beside the phrase, on the same
/// processor.
/// </remarks>
public sealed class LeftPhrase
{
    private readonly object _gate = new();
    private Held? _held;

    private sealed record Held(object Owner, PhraseKey Key, Task Done, CancellationTokenSource Stop);

    /// <summary>Holds the phrase <paramref name="owner"/> was let go of; <paramref name="stop"/> stops it, and is this object's to dispose from now.</summary>
    internal void Hold(object owner, PhraseKey key, Task done, CancellationTokenSource stop)
    {
        Held? before;
        lock (_gate)
        {
            before = _held;
            _held = new Held(owner, key, done, stop);
        }
        // Only one is ever let go of before the next score settles it; a second stops the first.
        if (before is not null)
        {
            before.Stop.Cancel();
        }
    }

    /// <summary>
    /// Settles the phrase held, if any: it is let finish when it is <paramref name="owner"/>'s and
    /// <paramref name="wanted"/>, and stopped otherwise; returns when it has ended. With
    /// <paramref name="onlyMine"/>, another owner's phrase is left as it is.
    /// </summary>
    public async Task Settle(object? owner, Func<PhraseKey, bool> wanted, bool onlyMine = false)
    {
        Held? held;
        lock (_gate)
        {
            held = _held;
            if (held is null || (onlyMine && !ReferenceEquals(held.Owner, owner)))
            {
                return;
            }
            _held = null;
        }
        if (!ReferenceEquals(held.Owner, owner) || !wanted(held.Key))
        {
            held.Stop.Cancel();
        }
        try
        {
            await held.Done.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Its failure, or its stop, is its own: a phrase that was not kept is sung again by the
            // score that wants it.
        }
        held.Stop.Dispose();
    }
}

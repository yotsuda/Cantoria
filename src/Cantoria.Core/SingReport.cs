// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Cantoria;

/// <summary>
/// How far a score is while it is sung: the seconds of music that have to be sung ANEW —
/// phrases kept from an earlier sing are laid in place and cost nothing, so they are not
/// counted — and how many of those seconds are done. A caller's bar that runs from 0 to
/// <see cref="ToSing"/> starts empty and is full when the score is.
/// </summary>
public readonly record struct SingProgress(double ToSing, double Sung);

/// <summary>
/// Where a singer says how far it is, for whoever asked the score of it to hear. The listener
/// follows the call through its awaits (an <see cref="AsyncLocal{T}"/>), so no singer's signature
/// carries it and a singer that wraps others (<see cref="ChorusSinger"/>) passes it on unasked.
/// A singer that sings in phrases tells once before the first — what there is to sing — and
/// once after each phrase sung; one that does not tell says nothing, and the caller keeps its
/// own estimate.
/// </summary>
public static class SingReport
{
    private static readonly AsyncLocal<Action<SingProgress>?> Listener = new();

    /// <summary>Hears what is told from here until the answer is disposed.</summary>
    public static IDisposable Listen(Action<SingProgress> listener)
    {
        var before = Listener.Value;
        Listener.Value = listener;
        return new Scope(before);
    }

    /// <summary>From here until the answer is disposed, what several singers tell one after
    /// another reaches the listener there is as one count (<see cref="Together"/>): for a singer
    /// that asks others in turn. With no listener nothing changes.</summary>
    public static IDisposable ListenTogether()
        => Listener.Value is { } outer ? Listen(Together(outer)) : new Scope(null);

    /// <summary>Tells the listener, if there is one.</summary>
    public static void Tell(double toSing, double sung) => Listener.Value?.Invoke(new SingProgress(toSing, sung));

    /// <summary>
    /// One count for several singers heard one after another (a chorus): each begins its own
    /// count at 0, so what the ones before had to sing is added to both numbers. The total
    /// grows as each next singer says what it has — it is not known sooner.
    /// </summary>
    public static Action<SingProgress> Together(Action<SingProgress> listener)
    {
        double before = 0, last = 0;
        bool any = false;
        return p =>
        {
            if (p.Sung <= 0)
            {
                // A count beginning: the one before is done, whatever it last said.
                if (any)
                {
                    before += last;
                }
                any = true;
            }
            last = p.ToSing;
            listener(new SingProgress(before + p.ToSing, before + p.Sung));
        };
    }

    private sealed class Scope(Action<SingProgress>? before) : IDisposable
    {
        public void Dispose() => Listener.Value = before;
    }
}
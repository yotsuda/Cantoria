// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Cantoria.DiffSinger;

/// <summary>One of a voice's speakers as a singer of its own: <c>diffsinger/TIGER/tiger_disco</c>.
/// The models are the shared singer's.</summary>
public sealed class DiffSingerSpeaker(DiffSingerSinger shared, string speaker) : ISinger, ISingParameters, IDisposable
{
    /// <summary>The voice's own: a speaker is the same models told who sings.</summary>
    public IReadOnlyList<SingParameter> Parameters => shared.Parameters;

    /// <summary>The singer of the voice, whose models this speaker sings with.</summary>
    public DiffSingerSinger Shared => shared;

    public string Name => $"{shared.Name}/{speaker}";

    public Task<SungAudio> SingAsync(SungScore score, CancellationToken ct = default) => shared.SingAsync(score, speaker, ct);

    public void Dispose() => shared.Dispose();
}

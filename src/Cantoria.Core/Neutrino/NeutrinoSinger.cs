// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Text;
using Cantoria.Audio;

namespace Cantoria.Neutrino;

/// <summary>
/// Sings through the user's NEUTRINO (STUDIO NEUTRINO, Tau v3.2.x, Windows), run as separate
/// processes: <c>musicXMLtoLabel.exe</c> turns the score into labels, <c>neutrino_server.exe</c>
/// is started once and kept for this object's life (the models load on the first render, about
/// 45 s on a GPU; after that a phrase renders in well under a second), and
/// <c>neutrino_client.exe</c> hands it one render at a time.
/// <para>
/// NEUTRINO's client finds its server through the <em>parent process</em>: the server is bound
/// to the process that started it and the client must be a direct child of that same process.
/// Both are therefore started by this process, with the NEUTRINO folder as working directory and
/// paths relative to it — the form NEUTRINO's own scripts use. The work files go under
/// <c>output\cantoria\</c> inside that folder and are removed after each render unless
/// <see cref="NeutrinoOptions.KeepFiles"/>.
/// </para>
/// Nothing of NEUTRINO is copied or redistributed; see THIRD-PARTY-NOTICES.md.
/// </summary>
public sealed class NeutrinoSinger : ISinger, ISingParameters, IDisposable
{
    private readonly NeutrinoOptions _options;
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private Process? _server;
    private bool _rendered;
    private bool _disposed;

    /// <exception cref="ArgumentException">The options name no usable NEUTRINO (the message lists the problems).</exception>
    public NeutrinoSinger(NeutrinoOptions options)
    {
        var problems = options.Validate();
        if (problems.Count > 0)
        {
            throw new ArgumentException(string.Join("; ", problems), nameof(options));
        }
        _options = options;
    }

    public string Name => $"neutrino/{_options.Voice}";

    /// <summary>
    /// The settings NEUTRINO's own program takes, and nothing else (its v3 has no knob for breath
    /// or tension; what else it can be made to do is in its timing, pitch and spectrum files):
    /// <c>-f</c>, sung as if the key were so many semitones away, and <c>-S</c>, another of its
    /// voices as a support model. One value a render: a phrase here, since a score is sung a
    /// phrase at a time, so neither changes inside the music.
    /// </summary>
    public IReadOnlyList<SingParameter> Parameters =>
    [
        new("transpose", SingScope.Phrase, "Sung as if the key were this many semitones away (NEUTRINO's -f): the colour of the voice moves, the notes stay", -12, 12, 0, "semitones"),
        new("support", SingScope.Voice, "Another NEUTRINO voice as a support model (NEUTRINO's -S)", Word: true,
            Values: NeutrinoOptions.Voices(_options.Folder).Where(v => !string.Equals(v, _options.Voice, StringComparison.OrdinalIgnoreCase)).ToList()),
    ];

    /// <summary>Lines NEUTRINO's programs printed during the last render, for a log.</summary>
    public string LastLog { get; private set; } = "";

    public async Task<SungAudio> SingAsync(SungScore score, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var problems = score.Validate();
        if (problems.Count > 0)
        {
            throw new SingerException("the score is not sound: " + string.Join("; ", problems));
        }
        if (NeutrinoMusicXml.NotKanaProblem(score) is { } notKana)
        {
            throw new SingerException(notKana);
        }
        await _oneAtATime.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureServer();
            string workRel = Path.Combine("output", "cantoria");
            string work = Path.Combine(_options.Folder, workRel);
            Directory.CreateDirectory(work);
            string id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Environment.ProcessId;
            string Rel(string ext) => Path.Combine(workRel, id + ext);
            string Abs(string ext) => Path.Combine(_options.Folder, Rel(ext));
            var log = new StringBuilder();
            try
            {
                File.WriteAllText(Abs(".musicxml"), NeutrinoMusicXml.Write(score), new UTF8Encoding(false));

                var label = await Run("musicXMLtoLabel.exe",
                    [Rel(".musicxml"), Rel(".full.lab"), Rel(".mono.lab"), "-x", Path.Combine("settings", "dic")],
                    TimeSpan.FromMinutes(1), ct).ConfigureAwait(false);
                log.Append(label.Output);
                if (label.ExitCode != 0 || !File.Exists(Abs(".full.lab")))
                {
                    throw new SingerException("NEUTRINO could not read the score (musicXMLtoLabel exit "
                        + $"{label.ExitCode}):\n{label.Output.Trim()}");
                }

                var args = new List<string>
                {
                    Rel(".full.lab"), Rel(".timing.lab"), Rel(".f0"), Rel(".melspec"), Rel(".wav"),
                    _options.VoiceDirArgument, "-n", _options.Threads.ToString(),
                };
                if (_options.UseGpu)
                {
                    args.Add("-m");
                }
                args.Add("-t");
                // How the score asks to be sung, of what NEUTRINO takes (Parameters).
                var taken = SingSettings.Taken(score.Sing, Parameters, Name);
                if (taken.TryGetValue("transpose", out var transpose) && transpose.Number is { } key && key != 0)
                {
                    args.Add("-f");
                    args.Add(key.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                }
                if (taken.TryGetValue("support", out var support) && support.Word is { Length: > 0 } supportVoice)
                {
                    // A model's FOLDER, as the voice's own is given (measured on v3.2.6: a bare
                    // name is looked for as a path and not found).
                    args.Add("-S");
                    args.Add($"model\\{supportVoice}\\");
                }
                var timeout = _rendered ? _options.RenderTimeout : _options.FirstRenderTimeout;

                // A join of like phonemes (ん after ん): the timing alone first, corrected, and
                // the rest from it (NeutrinoTiming).
                string full = await File.ReadAllTextAsync(Abs(".full.lab"), ct).ConfigureAwait(false);
                if (NeutrinoTiming.HasRepeats(full))
                {
                    var timing = await Run("neutrino_client.exe", [.. args, "--skip-f0", "--skip-melspec", "--skip-wav"], timeout, ct).ConfigureAwait(false);
                    log.Append(timing.Output);
                    if (timing.ExitCode != 0 || !File.Exists(Abs(".timing.lab")))
                    {
                        throw new SingerException($"NEUTRINO did not time the score (neutrino_client exit {timing.ExitCode}):\n{timing.Output.Trim()}");
                    }
                    string predicted = await File.ReadAllTextAsync(Abs(".timing.lab"), ct).ConfigureAwait(false);
                    await File.WriteAllTextAsync(Abs(".timing.lab"), NeutrinoTiming.AlignRepeats(full, predicted), new UTF8Encoding(false), ct).ConfigureAwait(false);
                    args.Add("--skip-timing");
                }

                var render = await Run("neutrino_client.exe", args, timeout, ct).ConfigureAwait(false);
                log.Append(render.Output);
                if (render.ExitCode != 0 || !File.Exists(Abs(".wav")))
                {
                    throw new SingerException($"NEUTRINO did not sing (neutrino_client exit {render.ExitCode}):\n{render.Output.Trim()}");
                }
                _rendered = true;

                var audio = Wav.Read(await File.ReadAllBytesAsync(Abs(".wav"), ct).ConfigureAwait(false));
                int leadIn = (int)Math.Round(NeutrinoMusicXml.LeadInSeconds * audio.SampleRate);
                var trimmed = audio.Samples.Length > leadIn ? audio.Samples[leadIn..] : [];
                return new SungAudio(trimmed, audio.SampleRate);
            }
            finally
            {
                LastLog = log.ToString();
                if (!_options.KeepFiles)
                {
                    foreach (var ext in new[] { ".musicxml", ".full.lab", ".mono.lab", ".timing.lab", ".f0", ".melspec", ".wav" })
                    {
                        try { File.Delete(Abs(ext)); } catch (IOException) { /* a reader still has it; next time */ }
                    }
                }
            }
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    /// <summary>Starts the server if it is not running, and waits until it answers "READY".</summary>
    private void EnsureServer()
    {
        if (_server is { HasExited: false })
        {
            return;
        }
        _server?.Dispose();
        var psi = new ProcessStartInfo(Path.Combine(_options.BinDir, "neutrino_server.exe"))
        {
            WorkingDirectory = _options.Folder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        _server = Process.Start(psi) ?? throw new SingerException("neutrino_server.exe did not start");
        // Drain, or the server blocks on a full pipe.
        _server.OutputDataReceived += (_, _) => { };
        _server.ErrorDataReceived += (_, _) => { };
        _server.BeginOutputReadLine();
        _server.BeginErrorReadLine();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            if (_server.HasExited)
            {
                throw new SingerException($"neutrino_server.exe exited with {_server.ExitCode} before it was ready");
            }
            var status = Run("neutrino_client.exe", ["--owner-pid", Environment.ProcessId.ToString(), "status"],
                TimeSpan.FromSeconds(10), CancellationToken.None).GetAwaiter().GetResult();
            if (status.ExitCode == 0 && status.Output.Contains("READY", StringComparison.Ordinal))
            {
                return;
            }
            Thread.Sleep(250);
        }
        throw new SingerException("neutrino_server.exe did not become ready within 60 s");
    }

    private readonly record struct Result(int ExitCode, string Output);

    /// <summary>Runs one of NEUTRINO's programs from the NEUTRINO folder and collects what it printed.</summary>
    private async Task<Result> Run(string exe, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(Path.Combine(_options.BinDir, exe))
        {
            WorkingDirectory = _options.Folder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }
        using var p = Process.Start(psi) ?? throw new SingerException($"{exe} did not start");
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timer.CancelAfter(timeout);
        try
        {
            await p.WaitForExitAsync(timer.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(); } catch (InvalidOperationException) { /* already gone */ }
            if (ct.IsCancellationRequested)
            {
                throw;
            }
            throw new SingerException($"{exe} did not finish within {timeout.TotalSeconds:0} s");
        }
        var output = (await stdout.ConfigureAwait(false)) + (await stderr.ConfigureAwait(false));
        return new Result(p.ExitCode, output);
    }

    /// <summary>Asks the server to shut down; kills it if it does not within a few seconds.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        if (_server is { HasExited: false })
        {
            try
            {
                Run("neutrino_client.exe", ["--owner-pid", Environment.ProcessId.ToString(), "shutdown"],
                    TimeSpan.FromSeconds(10), CancellationToken.None).GetAwaiter().GetResult();
                if (!_server.WaitForExit(5000))
                {
                    _server.Kill();
                }
            }
            catch (Exception e) when (e is SingerException or InvalidOperationException)
            {
                try { _server.Kill(); } catch (InvalidOperationException) { /* gone */ }
            }
        }
        _server?.Dispose();
        _oneAtATime.Dispose();
    }
}

// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Microsoft.ML.OnnxRuntime;

namespace Cantoria.DiffSinger;

/// <summary>Where a DiffSinger voice's models run.</summary>
public enum OnnxProvider
{
    /// <summary>The platform's GPU provider where this build and this machine have it, else the CPU:
    /// DirectML on Windows (a DirectX 12 device); the CPU on Linux; the CPU on macOS too for now
    /// (CoreML is there to ask for by name, untested here).</summary>
    Auto,
    /// <summary>The CPU, always.</summary>
    Cpu,
    /// <summary>DirectML (Windows, a DirectX 12 GPU, the DirectML build of the program), or an error.</summary>
    DirectML,
    /// <summary>CoreML (macOS; in the standard ONNX Runtime package), or an error. Not measured yet.</summary>
    CoreML,
}

/// <summary>
/// The ONNX Runtime session options for a provider (roadmap B3). The Windows program is built on
/// the DirectML package of ONNX Runtime, whose native library runs the CPU provider too, so one
/// build serves both: <see cref="OnnxProvider.Auto"/> tries DirectML and falls back to the CPU
/// when the build has no DirectML (the CPU package: the entry point is missing) or the machine no
/// DirectX 12 device (the provider cannot be appended, or the first session cannot be created).
/// Measured on the spike (2026-11-08, RTX 5070 Ti): the acoustic model 0.29 s on DirectML against
/// 1.3–14 s on the CPU for a 7 s phrase. On macOS the standard package carries CoreML; it is
/// appended on request only (<see cref="OnnxProvider.CoreML"/>) until it has been heard.
/// </summary>
public static class OnnxProviders
{
    public const string DirectMLName = "DirectML";
    public const string CoreMLName = "CoreML";
    public const string CpuName = "CPU";

    /// <summary>Why <see cref="OnnxProvider.Auto"/> took the CPU on Windows with the DirectML build.</summary>
    public const string NoGpuNote = "this computer has no GPU, only Windows' software adapter; the CPU sings";

    /// <summary>
    /// Session options for <paramref name="wanted"/>: the provider's name, and — when something
    /// other than the one asked for came out — why, in a sentence. A GPU provider asked for by name
    /// that cannot be had is a <see cref="SingerException"/>.
    /// </summary>
    public static (SessionOptions Options, string Name, string? Note) Create(OnnxProvider wanted)
    {
        string? gpu = wanted switch
        {
            OnnxProvider.Cpu => null,
            OnnxProvider.DirectML => DirectMLName,
            OnnxProvider.CoreML => CoreMLName,
            _ => OperatingSystem.IsWindows() ? DirectMLName : null,
        };
        if (gpu is null)
        {
            return (new SessionOptions(), CpuName, null);
        }
        if (wanted == OnnxProvider.Auto && Gpus.Hardware().Count == 0)
        {
            // DirectML would run on Microsoft's software adapter: the CPU, slower.
            return (new SessionOptions(), CpuName, NoGpuNote);
        }
        var options = new SessionOptions();
        try
        {
            if (gpu == DirectMLName)
            {
                // What the DirectML provider asks of a session (ONNX Runtime's DirectML notes):
                // sequential execution and no memory pattern.
                options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
                options.EnableMemoryPattern = false;
                options.AppendExecutionProvider_DML(0);
            }
            else
            {
                options.AppendExecutionProvider(CoreMLName, CoreMLOptions(Environment.GetEnvironmentVariable(CoreMLVariable)));
            }
            return (options, gpu, null);
        }
        catch (Exception e) when (IsUnavailable(e))
        {
            options.Dispose();
            string why = Why(e, gpu);
            if (wanted != OnnxProvider.Auto)
            {
                throw new SingerException($"{gpu} is not available ({why}); run on the CPU", e);
            }
            return (new SessionOptions(), CpuName, $"{gpu} is not available ({why}); the CPU sings");
        }
    }

    /// <summary>The environment variable whose <c>key=value;…</c> pairs replace or add to
    /// <see cref="CoreMLDefaults"/>: how the macOS runs are measured (tools/bench-providers.ps1).</summary>
    public const string CoreMLVariable = "CANTORIA_COREML";

    /// <summary>
    /// The CoreML provider's options when nothing else is said: an ML Program (the newer format,
    /// which takes more of ONNX's operators than NeuralNetwork) on every compute unit (CPU, GPU,
    /// Neural Engine; CoreML chooses). Not yet measured against the others; the macOS workflow
    /// (.github/workflows/mac.yml) is where they are compared.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> CoreMLDefaults = new Dictionary<string, string>
    {
        ["ModelFormat"] = "MLProgram",
        ["MLComputeUnits"] = "ALL",
    };

    /// <summary><see cref="CoreMLDefaults"/> with <paramref name="spec"/>'s <c>key=value</c> pairs
    /// (separated by <c>;</c>) put over them; an empty value removes the key.</summary>
    public static Dictionary<string, string> CoreMLOptions(string? spec)
    {
        var options = new Dictionary<string, string>(CoreMLDefaults);
        foreach (var pair in (spec ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0)
            {
                throw new SingerException($"{CoreMLVariable}: \"{pair}\" is not key=value");
            }
            string key = pair[..eq].Trim(), value = pair[(eq + 1)..].Trim();
            if (value.Length == 0)
            {
                options.Remove(key);
            }
            else
            {
                options[key] = value;
            }
        }
        return options;
    }

    /// <summary>True for the ways a GPU provider fails to be there, as against a bug.</summary>
    public static bool IsUnavailable(Exception e) =>
        e is OnnxRuntimeException or EntryPointNotFoundException or DllNotFoundException
            or TypeInitializationException or PlatformNotSupportedException or NotSupportedException
            or InvalidOperationException;

    /// <summary>One line per provider, for <c>cantoria providers</c>: what this build on this machine can do.</summary>
    public static IEnumerable<string> Describe()
    {
        if (OperatingSystem.IsWindows())
        {
            var gpus = Gpus.Hardware();
            yield return $"GPU: {(gpus.Count == 0 ? "none (only Windows' software adapter)" : string.Join(", ", gpus))}";
        }
        yield return $"CPU: available (ONNX Runtime {OrtEnv.Instance().GetVersionString()})";
        foreach (var (provider, name) in new[] { (OnnxProvider.DirectML, DirectMLName), (OnnxProvider.CoreML, CoreMLName) })
        {
            string line;
            try
            {
                var (options, _, _) = Create(provider);
                options.Dispose();
                line = $"{name}: available";
            }
            catch (SingerException e)
            {
                line = $"{name}: {e.Message}";
            }
            yield return line;
        }
        var (auto, autoName, note) = Create(OnnxProvider.Auto);
        auto.Dispose();
        yield return $"auto: {autoName}" + (note is null ? "" : $" ({note})");
    }

    /// <summary>Where <see cref="OnnxProvider.Auto"/> runs DiffSinger's models on this machine with this
    /// build — <c>DirectML</c> or <c>CPU</c> — and why the CPU, when a GPU was looked for.</summary>
    public static (string Name, string? Note) Auto()
    {
        var (options, name, note) = Create(OnnxProvider.Auto);
        options.Dispose();
        return (name, note);
    }

    private static string Why(Exception e, string gpu) => e switch
    {
        EntryPointNotFoundException or NotSupportedException => $"this build of the program has no {gpu}",
        DllNotFoundException => $"{gpu}'s library is not beside the program",
        _ => e.Message.Split('\n')[0].Trim(),
    };
}

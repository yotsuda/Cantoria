// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Microsoft.ML.OnnxRuntime;

namespace Cantilena.DiffSinger;

/// <summary>Where a DiffSinger voice's models run.</summary>
public enum OnnxProvider
{
    /// <summary>DirectML when this build carries it and the machine has a DirectX 12 device, else the CPU.</summary>
    Auto,
    /// <summary>The CPU, always.</summary>
    Cpu,
    /// <summary>DirectML (Windows, a DirectX 12 GPU, the DirectML build of the program), or an error.</summary>
    DirectML,
}

/// <summary>
/// The ONNX Runtime session options for a provider (roadmap B3). The Windows program is built on
/// the DirectML package of ONNX Runtime, whose native library runs the CPU provider too, so one
/// build serves both: <see cref="OnnxProvider.Auto"/> tries DirectML and falls back to the CPU
/// when the build has no DirectML (the CPU package: the entry point is missing) or the machine no
/// DirectX 12 device (the provider cannot be appended, or the first session cannot be created).
/// Measured on the spike (2026-11-08, RTX 5070 Ti): the acoustic model 0.29 s on DirectML against
/// 1.3–14 s on the CPU for a 7 s phrase.
/// </summary>
internal static class OnnxProviders
{
    public const string DirectMLName = "DirectML";
    public const string CpuName = "CPU";

    /// <summary>
    /// Session options for <paramref name="wanted"/>: the provider's name, and — when something
    /// other than the one asked for came out — why, in a sentence. <see cref="OnnxProvider.DirectML"/>
    /// that cannot be had is a <see cref="SingerException"/>.
    /// </summary>
    public static (SessionOptions Options, string Name, string? Note) Create(OnnxProvider wanted)
    {
        if (wanted == OnnxProvider.Cpu)
        {
            return (new SessionOptions(), CpuName, null);
        }
        var options = new SessionOptions();
        try
        {
            // What the DirectML provider asks of a session (ONNX Runtime's DirectML notes):
            // sequential execution and no memory pattern.
            options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
            options.EnableMemoryPattern = false;
            options.AppendExecutionProvider_DML(0);
            return (options, DirectMLName, null);
        }
        catch (Exception e) when (IsUnavailable(e))
        {
            options.Dispose();
            string why = Why(e);
            if (wanted == OnnxProvider.DirectML)
            {
                throw new SingerException($"DirectML is not available ({why}); run on the CPU, or install a DirectX 12 driver", e);
            }
            return (new SessionOptions(), CpuName, $"DirectML is not available ({why}); the CPU sings");
        }
    }

    /// <summary>True for the ways DirectML fails to be there, as against a bug.</summary>
    public static bool IsUnavailable(Exception e) =>
        e is OnnxRuntimeException or EntryPointNotFoundException or DllNotFoundException
            or TypeInitializationException or PlatformNotSupportedException or InvalidOperationException;

    private static string Why(Exception e) => e switch
    {
        EntryPointNotFoundException => "this build of the program has no DirectML",
        DllNotFoundException => "DirectML.dll is not beside the program",
        _ => e.Message.Split('\n')[0].Trim(),
    };
}

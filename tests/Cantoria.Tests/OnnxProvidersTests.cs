// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Cantoria.DiffSinger;
using Xunit;

namespace Cantoria.Tests;

/// <summary>
/// The providers (roadmap B3, B5): the CPU is always there; a GPU provider asked for by name is
/// had or refused with a sentence; `Auto` never fails. The tests run on the CPU package of ONNX
/// Runtime (the DirectML one is the Windows program's), on every platform CI has.
/// </summary>
public class OnnxProvidersTests
{
    [Fact]
    public void TheCpu_IsAlwaysThere_WithNothingToExplain()
    {
        var (options, name, note) = OnnxProviders.Create(OnnxProvider.Cpu);
        using (options)
        {
            Assert.Equal("CPU", name);
            Assert.Null(note);
        }
    }

    [Fact]
    public void Auto_NeverFails_AndSaysWhyWhenTheCpuStandsIn()
    {
        var (options, name, note) = OnnxProviders.Create(OnnxProvider.Auto);
        using (options)
        {
            Assert.Contains(name, new[] { "CPU", "DirectML" });
            if (OperatingSystem.IsWindows() && name == "CPU")
            {
                Assert.Contains("DirectML is not available", note);     // the CPU package, or no device
            }
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal("CPU", name);                              // Linux; macOS until CoreML is heard
                Assert.Null(note);
            }
        }
    }

    [Fact]
    public void AGpuProvider_AskedForByName_IsHadOrRefusedWithASentence()
    {
        foreach (var provider in new[] { OnnxProvider.DirectML, OnnxProvider.CoreML })
        {
            try
            {
                var (options, name, note) = OnnxProviders.Create(provider);
                using (options)
                {
                    Assert.Equal(provider.ToString(), name);
                    Assert.Null(note);
                }
            }
            catch (SingerException e)
            {
                Assert.Contains($"{provider} is not available", e.Message);
                Assert.Contains("run on the CPU", e.Message);
            }
        }
    }

    [Fact]
    public void Describe_NamesEveryProvider_AndWhatAutoPicks()
    {
        var lines = OnnxProviders.Describe().ToList();
        Assert.Equal(4, lines.Count);
        Assert.StartsWith("CPU: available (ONNX Runtime ", lines[0]);
        Assert.StartsWith("DirectML: ", lines[1]);
        Assert.StartsWith("CoreML: ", lines[2]);
        Assert.StartsWith("auto: ", lines[3]);
    }
}

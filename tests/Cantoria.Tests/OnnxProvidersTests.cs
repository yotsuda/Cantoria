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
            Assert.Contains(name, new[] { "CPU", "DirectML", "CoreML" });
            if (OperatingSystem.IsWindows() && name == "CPU")
            {
                // the CPU package, or no device; or no GPU but Windows' software adapter
                Assert.True(note!.Contains("DirectML is not available") || note == OnnxProviders.NoGpuNote, note);
            }
            if (OperatingSystem.IsMacOS())
            {
                Assert.Equal("CoreML", name);                           // the standard package carries it
                Assert.Null(note);
            }
            if (OperatingSystem.IsLinux())
            {
                Assert.Equal("CPU", name);
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
    public void TheCoreMLOptions_AreTheDefaults_WithTheVariablesPairsPutOver()
    {
        Assert.Equal(OnnxProviders.CoreMLDefaults, OnnxProviders.CoreMLOptions(null));
        var o = OnnxProviders.CoreMLOptions(" MLComputeUnits=CPUAndGPU ; ModelFormat= ;RequireStaticInputShapes=1;");
        Assert.Equal("CPUAndGPU", o["MLComputeUnits"]);
        Assert.False(o.ContainsKey("ModelFormat"));             // an empty value takes the default away
        Assert.Equal("1", o["RequireStaticInputShapes"]);
        var e = Assert.Throws<SingerException>(() => OnnxProviders.CoreMLOptions("MLProgram"));
        Assert.Contains("CANTORIA_COREML", e.Message);
    }

    [Fact]
    public void TheCoreMLModels_AreAllUnlessNamed()
    {
        Assert.True(OnnxProviders.CoreMLModels(null)!.SetEquals(["vocoder"]));   // the default
        Assert.True(OnnxProviders.CoreMLModels("ModelFormat=NeuralNetwork")!.SetEquals(["vocoder"]));
        Assert.Null(OnnxProviders.CoreMLModels("Models="));                       // every model
        var models = OnnxProviders.CoreMLModels("Models=acoustic, Vocoder;ModelFormat=NeuralNetwork")!;
        Assert.True(models.SetEquals(["acoustic", "vocoder"]));
        Assert.Contains("VOCODER", models);                    // as written or not
    }

    [Fact]
    public void Describe_NamesEveryProvider_AndWhatAutoPicks()
    {
        var lines = OnnxProviders.Describe().ToList();
        if (OperatingSystem.IsWindows())
        {
            Assert.StartsWith("GPU: ", lines[0]);
            lines.RemoveAt(0);
        }
        Assert.Equal(4, lines.Count);
        Assert.StartsWith("CPU: available (ONNX Runtime ", lines[0]);
        Assert.StartsWith("DirectML: ", lines[1]);
        Assert.StartsWith("CoreML: ", lines[2]);
        Assert.StartsWith("auto: ", lines[3]);
    }

    [Fact]
    public void TheGpus_AreHardwareOnly_AndNoneOffWindows()
    {
        var gpus = Gpus.Hardware();
        if (!OperatingSystem.IsWindows())
        {
            Assert.Empty(gpus);
            return;
        }
        // The software adapter every Windows has is never one (a GitHub runner has only it).
        Assert.DoesNotContain(gpus, g => g.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase));
        Assert.All(gpus, g => Assert.False(string.IsNullOrWhiteSpace(g)));
        if (gpus.Count == 0)
        {
            Assert.Equal("CPU", OnnxProviders.Auto().Name);   // not DirectML on the software adapter
        }
    }
}

// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Cantoria.DiffSinger;
using Xunit;

namespace Cantoria.Tests;

/// <summary>
/// Each voice is sung with the vocoder it names (TIGER nsf_hifigan 2024.02, Qixuan pc_nsf_hifigan
/// 2025.02), found beside the one set or beside the voice; one folder for all sang a voice with another's.
/// </summary>
public sealed class VocoderLookupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cantoria-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private string Voice(string name, string? vocoder, bool bundles = false)
    {
        string dir = Path.Combine(_root, "voices", name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "dsconfig.yaml"), vocoder is null ? "acoustic: a.onnx\n" : $"acoustic: a.onnx\nvocoder: {vocoder}\n");
        if (bundles)
        {
            Vocoder(Path.Combine(dir, "dsvocoder"), "its_own");
        }
        return dir;
    }

    private static string Vocoder(string dir, string name)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "vocoder.yaml"), $"name: \"{name}\"\nmodel: \"{name}.onnx\"\n");
        return dir;
    }

    [Fact]
    public void TheVocoderAVoiceNames_IsFoundBesideTheVoice_WithNoneSet()
    {
        string voice = Voice("Qixuan", "pc_nsf_hifigan_2025.02");
        string pc = Vocoder(Path.Combine(_root, "voices", "pc_nsf_hifigan"), "pc_nsf_hifigan_2025.02");
        Vocoder(Path.Combine(_root, "voices", "nsf_hifigan"), "nsf_hifigan_2024.02");
        Assert.Equal(pc, new DiffSingerOptions(voice).Vocoder());
        Assert.DoesNotContain(new DiffSingerOptions(voice).Validate(), p => p.Contains("vocoder"));
    }

    [Fact]
    public void TheVocoderAVoiceNames_WinsOverTheOneSet_WhenItIsBesideIt()
    {
        string voice = Voice("Qixuan", "pc_nsf_hifigan_2025.02");
        string deps = Path.Combine(_root, "OpenUtau", "Dependencies");
        string set = Vocoder(Path.Combine(deps, "nsf"), "nsf_hifigan_2024.02");
        string pc = Vocoder(Path.Combine(deps, "pc"), "PC_NSF_HIFIGAN_2025.02");
        Assert.Equal(pc, new DiffSingerOptions(voice) { VocoderFolder = set }.Vocoder());
        // Set as the folder of dependencies itself, the one named is found in it.
        Assert.Equal(pc, new DiffSingerOptions(voice) { VocoderFolder = deps }.Vocoder());
    }

    [Fact]
    public void AVoiceThatBundlesItsVocoder_UsesIt_AndOneNamedNowhere_TheOneSet()
    {
        string set = Vocoder(Path.Combine(_root, "elsewhere", "nsf"), "nsf_hifigan_2024.02");
        string nishiren = Voice("Nishiren", "gda_pc-hifigan", bundles: true);
        Assert.Equal(Path.Combine(nishiren, "dsvocoder"), new DiffSingerOptions(nishiren) { VocoderFolder = set }.Vocoder());
        string tiger = Voice("TIGER", "dsvocoder");
        Assert.Equal(set, new DiffSingerOptions(tiger) { VocoderFolder = set }.Vocoder());
        string unnamed = Voice("Plain", null);
        Assert.Equal(set, new DiffSingerOptions(unnamed) { VocoderFolder = set }.Vocoder());
    }

    [Fact]
    public void NoVocoderAnywhere_IsAProblem_NamingTheOneTheVoiceWants()
    {
        string voice = Voice("Qixuan", "pc_nsf_hifigan_2025.02");
        Assert.Null(new DiffSingerOptions(voice).Vocoder());
        Assert.Contains(new DiffSingerOptions(voice).Validate(), p => p.Contains("its own (pc_nsf_hifigan_2025.02) is not beside it"));
    }
}

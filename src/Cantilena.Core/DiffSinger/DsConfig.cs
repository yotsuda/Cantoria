// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.
//
// The keys and their meanings follow OpenUtau's OpenUtau.Core/DiffSinger/DiffSingerConfig.cs and
// DiffSingerVocoder.cs (MIT License, Copyright (c) 2019 Stakira); see THIRD-PARTY-NOTICES.md.

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cantilena.DiffSinger;

/// <summary>
/// A voice's <c>dsconfig.yaml</c> in the OpenUtau DiffSinger format — the root one (the acoustic
/// model) and each module's (<c>dsdur</c>, <c>dspitch</c>, <c>dsvariance</c>). Keys are snake_case
/// in the file; unknown keys are ignored, missing ones take OpenUtau's defaults.
/// </summary>
public sealed class DsConfig
{
    /// <summary>The phoneme list, one a line (or a JSON map), relative to the config's folder.</summary>
    public string Phonemes { get; set; } = "phonemes.txt";
    /// <summary>The language ids (JSON map) of a multi-language model, with <see cref="UseLangId"/>.</summary>
    public string? Languages { get; set; }
    public string? Acoustic { get; set; }
    /// <summary>The vocoder's dependency name; a voice may instead bundle <c>dsvocoder/vocoder.yaml</c>.</summary>
    public string? Vocoder { get; set; }
    /// <summary>Speaker embeddings (<c>.emb</c> files without the extension), relative to the folder.</summary>
    public List<string>? Speakers { get; set; }
    public int HiddenSize { get; set; } = 256;
    public bool UseKeyShiftEmbed { get; set; }
    public bool UseSpeedEmbed { get; set; }
    public bool UseEnergyEmbed { get; set; }
    public bool UseBreathinessEmbed { get; set; }
    public bool UseVoicingEmbed { get; set; }
    public bool UseTensionEmbed { get; set; }
    public bool UseContinuousAcceleration { get; set; }
    public bool UseLangId { get; set; }
    public AugmentationArgs? AugmentationArgs { get; set; }
    /// <summary>The older name of <c>use_variable_depth</c>.</summary>
    public bool? UseShallowDiffusion { get; set; }
    [YamlMember(Alias = "use_variable_depth")]
    public bool? VariableDepth { get; set; }
    /// <summary>Shallow diffusion: the render depth can be lowered, down to <see cref="MaxDepth"/>.</summary>
    [YamlIgnore]
    public bool UseVariableDepth => VariableDepth ?? UseShallowDiffusion ?? false;
    /// <summary>As written: steps out of 1000, or 0..1 with <see cref="UseContinuousAcceleration"/>.</summary>
    [YamlMember(Alias = "max_depth")]
    public double MaxDepthRaw { get; set; }
    /// <summary>The largest depth, 0..1.</summary>
    [YamlIgnore]
    public double MaxDepth => UseContinuousAcceleration ? MaxDepthRaw : MaxDepthRaw / 1000.0;
    public string? Dur { get; set; }
    /// <summary><c>abs</c> or <c>rel</c>; unspecified, the duration model is probed for word inputs.</summary>
    public string? DurType { get; set; }
    public string? Linguistic { get; set; }
    public string? Pitch { get; set; }
    public string? Variance { get; set; }
    /// <summary>The linguistic encoder takes words (word_div, word_dur) instead of phoneme durations.</summary>
    public bool PredictDur { get; set; } = true;
    public bool PredictEnergy { get; set; } = true;
    public bool PredictBreathiness { get; set; } = true;
    public bool PredictVoicing { get; set; }
    public bool PredictTension { get; set; }
    public bool UseExpr { get; set; }
    public bool UseNoteRest { get; set; }
    public int SampleRate { get; set; } = 44100;
    public int HopSize { get; set; } = 512;
    public int WinSize { get; set; } = 2048;
    public int FftSize { get; set; } = 2048;
    public int NumMelBins { get; set; } = 128;
    public double MelFmin { get; set; } = 40;
    public double MelFmax { get; set; } = 16000;
    public string MelBase { get; set; } = "10";
    public string MelScale { get; set; } = "slaney";

    /// <summary>Milliseconds per frame: hop / rate.</summary>
    [YamlIgnore]
    public double FrameMs => 1000.0 * HopSize / SampleRate;

    public static DsConfig Load(string path) => DsYaml.Read<DsConfig>(path);
}

/// <summary>Data augmentation the acoustic model was trained with; the key shift range scales the <c>gender</c> input.</summary>
public sealed class AugmentationArgs
{
    public RandomPitchShifting? RandomPitchShifting { get; set; }
}

public sealed class RandomPitchShifting
{
    /// <summary>Semitones, [lowest, highest].</summary>
    public float[]? Range { get; set; }
}

/// <summary>A vocoder folder's <c>vocoder.yaml</c>.</summary>
public sealed class DsVocoderConfig
{
    public string Name { get; set; } = "vocoder";
    public string Model { get; set; } = "model.onnx";
    public int SampleRate { get; set; } = 44100;
    public int HopSize { get; set; } = 512;
    public int WinSize { get; set; } = 2048;
    public int FftSize { get; set; } = 2048;
    public int NumMelBins { get; set; } = 128;
    public double MelFmin { get; set; } = 40;
    public double MelFmax { get; set; } = 16000;
    public string MelBase { get; set; } = "10";
    public string MelScale { get; set; } = "slaney";
    public bool PitchControllable { get; set; }

    public static DsVocoderConfig Load(string path) => DsYaml.Read<DsVocoderConfig>(path);
}

/// <summary>A dictionary file (<c>dsdict.yaml</c>, <c>dsdict-ja.yaml</c>): the symbols with their types, the entries grapheme → phonemes.</summary>
public sealed class G2pDictionaryData
{
    public sealed class SymbolData
    {
        public string Symbol { get; set; } = "";
        /// <summary><c>vowel</c>, <c>stop</c>, <c>fricative</c>, <c>semivowel</c>, <c>liquid</c>, …</summary>
        public string Type { get; set; } = "";
    }

    public sealed class Entry
    {
        public string Grapheme { get; set; } = "";
        public string[] Phonemes { get; set; } = [];
    }

    public SymbolData[]? Symbols { get; set; }
    public Entry[]? Entries { get; set; }
}

/// <summary>The YAML reader the voices' files need: snake_case keys, unknown keys ignored.</summary>
public static class DsYaml
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static T Parse<T>(string yaml) => Deserializer.Deserialize<T>(yaml) ?? throw new InvalidDataException("the YAML is empty");

    public static T Read<T>(string path)
    {
        try
        {
            return Parse<T>(File.ReadAllText(path, System.Text.Encoding.UTF8));
        }
        catch (YamlDotNet.Core.YamlException e)
        {
            throw new InvalidDataException($"{path}: {e.Message}", e);
        }
    }
}

// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.
//
// The folder layout and the loading follow OpenUtau's OpenUtau.Core/DiffSinger/DiffSingerSinger.cs,
// DiffSingerBasePhonemizer.cs, DiffSingerPitch.cs, DiffSingerVariance.cs and
// DiffSingerSpeakerEmbedManager.cs (MIT License, Copyright (c) 2019 Stakira); see THIRD-PARTY-NOTICES.md.

using System.Text;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Cantilena.DiffSinger;

/// <summary>
/// A DiffSinger voice in the OpenUtau format, loaded from its folder: the acoustic model's
/// config, phonemes and speakers; the modules (<c>dsdur</c>, <c>dspitch</c>, <c>dsvariance</c>),
/// each with its own config, phoneme list, dictionaries and speaker embeddings; the vocoder.
/// The ONNX sessions are created on first use and kept until disposed (loading is seconds).
/// </summary>
public sealed class DiffSingerVoice : IDisposable
{
    private InferenceSession? _acoustic;
    private InferenceSession? _vocoder;
    private SessionOptions? _sessionOptions;
    private bool _disposed;
    private string _language;

    public DiffSingerVoice(DiffSingerOptions options)
    {
        var problems = options.Validate();
        if (problems.Count > 0)
        {
            throw new ArgumentException(string.Join("; ", problems), nameof(options));
        }
        Options = options;
        _language = options.Language;
        Folder = options.Folder;
        Provider = options.Provider switch
        {
            OnnxProvider.Cpu => OnnxProviders.CpuName,
            OnnxProvider.CoreML => OnnxProviders.CoreMLName + "?",
            OnnxProvider.DirectML => OnnxProviders.DirectMLName + "?",
            _ => OperatingSystem.IsWindows() ? OnnxProviders.DirectMLName + "?" : OnnxProviders.CpuName,
        };
        Acoustic = DsConfig.Load(Path.Combine(Folder, "dsconfig.yaml"));
        if (Acoustic.Acoustic is null)
        {
            throw new InvalidDataException($"{Folder}: dsconfig.yaml names no acoustic model");
        }
        AcousticPhonemes = LoadPhonemes(Folder, Acoustic);
        AcousticLanguages = LoadLanguages(Folder, Acoustic);
        AcousticSpeaker = LoadSpeaker(Folder, Acoustic, options.Speaker);
        Dur = new Module(Path.Combine(Folder, "dsdur"), options.Speaker, options.Language, Open);
        var pitchDir = Path.Combine(Folder, "dspitch");
        Pitch = File.Exists(Path.Combine(pitchDir, "dsconfig.yaml")) ? new Module(pitchDir, options.Speaker, options.Language, Open) : null;
        var varianceDir = Path.Combine(Folder, "dsvariance");
        Variance = File.Exists(Path.Combine(varianceDir, "dsconfig.yaml")) ? new Module(varianceDir, options.Speaker, options.Language, Open) : null;
        var own = Path.Combine(Folder, "dsvocoder");
        VocoderFolder = File.Exists(Path.Combine(own, "vocoder.yaml")) ? own : options.VocoderFolder!;
        Vocoder = DsVocoderConfig.Load(Path.Combine(VocoderFolder, "vocoder.yaml"));
        if (Vocoder.SampleRate != Acoustic.SampleRate || Vocoder.HopSize != Acoustic.HopSize || Vocoder.NumMelBins != Acoustic.NumMelBins)
        {
            throw new InvalidDataException($"the vocoder ({Vocoder.SampleRate} Hz, hop {Vocoder.HopSize}, {Vocoder.NumMelBins} mel bins) does not match the acoustic model ({Acoustic.SampleRate} Hz, hop {Acoustic.HopSize}, {Acoustic.NumMelBins} mel bins)");
        }
    }

    public DiffSingerOptions Options { get; }
    public string Folder { get; }
    public DsConfig Acoustic { get; }
    public IReadOnlyDictionary<string, int> AcousticPhonemes { get; }
    public IReadOnlyDictionary<string, int> AcousticLanguages { get; }
    /// <summary>The speaker embedding the acoustic model takes, or null for a single-speaker voice.</summary>
    public float[]? AcousticSpeaker { get; private set; }
    public Module Dur { get; }
    public Module? Pitch { get; }
    public Module? Variance { get; }
    public string VocoderFolder { get; }
    public DsVocoderConfig Vocoder { get; }
    /// <summary>Milliseconds per frame of the acoustic model and the vocoder.</summary>
    public double FrameMs => Acoustic.FrameMs;

    /// <summary>The dictionary the words are read with: the duration module's.</summary>
    public DsDictionary Dictionary => Dur.Dictionary;

    /// <summary>Where the models run, as settled when the first one was opened: <c>DirectML</c> or
    /// <c>CPU</c>; <c>DirectML?</c> until then, when that is what will be tried
    /// (<see cref="OnnxProviders"/>).</summary>
    public string Provider { get; private set; }

    /// <summary>Why the provider is not the one asked for (DirectML wanted, the CPU sings), or null.</summary>
    public string? ProviderNote { get; private set; }

    public InferenceSession AcousticSession => _acoustic ??= Open(Path.Combine(Folder, Acoustic.Acoustic!));
    public InferenceSession VocoderSession => _vocoder ??= Open(Path.Combine(VocoderFolder, Vocoder.Model));

    /// <summary>A phoneme as the acoustic model spells it (itself, else with the language prefix), or null.</summary>
    public string? AcousticSymbol(string symbol) => Known(symbol, AcousticPhonemes, _language);

    /// <summary>Reads the next score's words in <paramref name="language"/> (its dictionaries, the
    /// prefix of its phonemes). A voice's models sing every language it has a dictionary for.</summary>
    internal void UseLanguage(string language)
    {
        _language = language;
        Dur.UseLanguage(language);
        Pitch?.UseLanguage(language);
        Variance?.UseLanguage(language);
    }

    /// <summary>Sings the next score as <paramref name="speaker"/> (null for the first): the models are
    /// shared, a speaker being only an embedding each model is given.</summary>
    internal void UseSpeaker(string? speaker)
    {
        AcousticSpeaker = LoadSpeaker(Folder, Acoustic, speaker);
        Dur.UseSpeaker(speaker);
        Pitch?.UseSpeaker(speaker);
        Variance?.UseSpeaker(speaker);
    }

    /// <summary>
    /// A model's session on this voice's provider. The provider is settled on the first call
    /// (<see cref="OnnxProviders.Create"/>); a DirectML session that cannot be created on a
    /// machine the provider accepted falls back to the CPU for this and every later model, unless
    /// DirectML was asked for by name.
    /// </summary>
    internal InferenceSession Open(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"model not found: {path}", path);
        }
        if (_sessionOptions is null)
        {
            var (options, name, note) = OnnxProviders.Create(Options.Provider);
            _sessionOptions = options;
            Provider = name;
            ProviderNote = note;
        }
        try
        {
            return new InferenceSession(path, _sessionOptions);
        }
        catch (Exception e) when (Provider != OnnxProviders.CpuName && Options.Provider == OnnxProvider.Auto && OnnxProviders.IsUnavailable(e))
        {
            _sessionOptions.Dispose();
            _sessionOptions = new SessionOptions();
            ProviderNote = $"{Provider} could not open {Path.GetFileName(path)} ({e.Message.Split('\n')[0].Trim()}); the CPU sings";
            Provider = OnnxProviders.CpuName;
            return new InferenceSession(path, _sessionOptions);
        }
    }

    internal static string? Known(string symbol, IReadOnlyDictionary<string, int> phonemes, string language)
    {
        if (phonemes.ContainsKey(symbol))
        {
            return symbol;
        }
        var prefixed = language + "/" + symbol;
        return phonemes.ContainsKey(prefixed) ? prefixed : null;
    }

    internal static Dictionary<string, int> LoadPhonemes(string dir, DsConfig config)
    {
        var path = Path.Combine(dir, config.Phonemes);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"phoneme list not found: {path}", path);
        }
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path)) ?? new();
        }
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        var d = new Dictionary<string, int>();
        for (int i = 0; i < lines.Length; i++)
        {
            d[lines[i]] = i;
        }
        return d;
    }

    internal static Dictionary<string, int> LoadLanguages(string dir, DsConfig config)
    {
        if (!config.UseLangId || config.Languages is null)
        {
            return new();
        }
        return JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(Path.Combine(dir, config.Languages))) ?? new();
    }

    /// <summary>The one speaker's embedding: the named one, else the first; null for a voice without speakers.</summary>
    internal static float[]? LoadSpeaker(string dir, DsConfig config, string? wanted)
    {
        if (config.Speakers is null || config.Speakers.Count == 0)
        {
            return null;
        }
        int index = 0;
        if (wanted is not null)
        {
            index = config.Speakers.FindIndex(s => s == wanted || s.EndsWith("/" + wanted, StringComparison.Ordinal) || s.EndsWith("\\" + wanted, StringComparison.Ordinal));
            if (index < 0)
            {
                throw new ArgumentException($"no speaker named {wanted} (the voice has: {string.Join(", ", config.Speakers.Select(s => s.Split('/', '\\')[^1]))})");
            }
        }
        var path = Path.Combine(dir, config.Speakers[index] + ".emb");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"speaker embedding not found: {path}", path);
        }
        using var reader = new BinaryReader(File.OpenRead(path));
        var embed = new float[config.HiddenSize];
        for (int i = 0; i < embed.Length; i++)
        {
            embed[i] = reader.ReadSingle();
        }
        return embed;
    }

    /// <summary>The <c>spk_embed</c> input: the one speaker's embedding, one row per phoneme or frame.</summary>
    internal static NamedOnnxValue SpeakerRows(float[] embed, int rows)
    {
        var data = new float[rows * embed.Length];
        for (int i = 0; i < rows; i++)
        {
            Array.Copy(embed, 0, data, i * embed.Length, embed.Length);
        }
        return NamedOnnxValue.CreateFromTensor("spk_embed", new DenseTensor<float>(data, [1, rows, embed.Length]));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _acoustic?.Dispose();
        _vocoder?.Dispose();
        Dur.Dispose();
        Pitch?.Dispose();
        Variance?.Dispose();
        _sessionOptions?.Dispose();
    }

    /// <summary>
    /// A module of the voice: <c>dsdur</c>, <c>dspitch</c> or <c>dsvariance</c>, with its own config,
    /// phoneme list, language ids, speaker embedding and dictionaries (the language's, else <c>dsdict.yaml</c>).
    /// </summary>
    public sealed class Module : IDisposable
    {
        private InferenceSession? _linguistic;
        private InferenceSession? _main;
        private readonly Func<string, InferenceSession> _open;
        private readonly Dictionary<string, (DsDictionary Dictionary, string File)> _dictionaries = new();

        internal Module(string dir, string? speaker, string language, Func<string, InferenceSession> open)
        {
            _open = open;
            Dir = dir;
            Config = DsConfig.Load(Path.Combine(dir, "dsconfig.yaml"));
            Phonemes = LoadPhonemes(dir, Config);
            Languages = LoadLanguages(dir, Config);
            Speaker = LoadSpeaker(dir, Config, speaker);
            Language = language;
            UseLanguage(language);
        }

        internal void UseSpeaker(string? speaker) => Speaker = LoadSpeaker(Dir, Config, speaker);

        /// <summary>This language's dictionary, read once.</summary>
        internal void UseLanguage(string language)
        {
            Language = language;
            if (!_dictionaries.TryGetValue(language, out var loaded))
            {
                var dictPath = new[] { $"dsdict-{language}.yaml", "dsdict.yaml" }.Select(n => Path.Combine(Dir, n)).FirstOrDefault(File.Exists)
                    ?? throw new FileNotFoundException($"no dictionary (dsdict-{language}.yaml or dsdict.yaml) under {Dir}");
                loaded = (DsDictionary.Load(dictPath), Path.GetFileName(dictPath));
                _dictionaries[language] = loaded;
            }
            (Dictionary, DictionaryFile) = loaded;
        }

        public string Dir { get; }
        public DsConfig Config { get; }
        public IReadOnlyDictionary<string, int> Phonemes { get; }
        public IReadOnlyDictionary<string, int> Languages { get; }
        public float[]? Speaker { get; private set; }
        public string Language { get; private set; }
        public DsDictionary Dictionary { get; private set; } = null!;
        public string DictionaryFile { get; private set; } = "";
        public double FrameMs => Config.FrameMs;

        /// <summary>The linguistic encoder (<c>linguistic.onnx</c>).</summary>
        public InferenceSession Linguistic => _linguistic ??= _open(Path.Combine(Dir, Config.Linguistic ?? throw new InvalidDataException($"{Dir}: dsconfig.yaml names no linguistic model")));

        /// <summary>The module's own model: the duration, pitch or variance predictor.</summary>
        public InferenceSession Main => _main ??= _open(Path.Combine(Dir, Config.Dur ?? Config.Pitch ?? Config.Variance ?? throw new InvalidDataException($"{Dir}: dsconfig.yaml names no model")));

        public string? Symbol(string symbol) => Known(symbol, Phonemes, Language);

        public long Token(string symbol) =>
            Phonemes.TryGetValue(symbol, out var t) ? t : throw new SingerException($"the phoneme \"{symbol}\" is not in {Path.Combine(Dir, Config.Phonemes)}");

        public long LanguageId(string symbol) => Languages.GetValueOrDefault(symbol.Contains('/') ? symbol[..symbol.IndexOf('/')] : "", 0);

        public void Dispose()
        {
            _linguistic?.Dispose();
            _main?.Dispose();
        }
    }
}

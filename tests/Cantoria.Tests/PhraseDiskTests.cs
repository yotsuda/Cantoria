// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Cantoria.DiffSinger;
using Xunit;

namespace Cantoria.Tests;

/// <summary>Phrases on disk: found by an engine started later, and held to a size.</summary>
public sealed class PhraseDiskTests : IDisposable
{
    private readonly string _out = Directory.CreateTempSubdirectory("cantoria-phrases-").FullName;
    private string Folder => PhraseDisk.FolderIn(_out);

    public void Dispose() => Directory.Delete(_out, recursive: true);

    private static PhraseKey Key(double start = 0, double pitch = 60, double? room = null, string? speaker = null)
        => new(speaker, "ja", ["k a|1"], [new PhraseNote(start, 1, pitch, false)], room);

    private static SungPhrase Sung(params float[] samples) => new(samples, -100, 44100);

    private PhraseCache Cache(string voice = "v") => new(1000, new PhraseDisk(Folder, voice));

    [Fact]
    public void APhrase_IsFoundByACacheMadeLater_WithItsPlaceAndRate()
    {
        Cache().Add(Key(), Sung(0.5f, -0.25f, 1f));
        Assert.True(Cache().TryGet(Key(), out var found));
        Assert.Equal(-100, found.StartMs);
        Assert.Equal(44100, found.SampleRate);
        Assert.Equal(3, found.Samples.Length);
        Assert.Equal(0.5f, found.Samples[0], 0.0001f);
        Assert.Equal(-0.25f, found.Samples[1], 0.0001f);
        Assert.Equal(1f, found.Samples[2], 0.0001f);
    }

    [Fact]
    public void ASampleBeyondFull_IsHeldAtFull()
    {
        Cache().Add(Key(), Sung(1.5f, -1.5f));
        Assert.True(Cache().TryGet(Key(), out var found));
        Assert.InRange(found.Samples[0], 0.999f, 1.001f);
        Assert.InRange(found.Samples[1], -1.001f, -0.999f);
    }

    [Fact]
    public void TheCallersRounding_DoesNotMakeAnotherPhrase()
    {
        Cache().Add(Key(start: 0), Sung(0.5f));
        Assert.True(Cache().TryGet(Key(start: 0.0004), out _));
    }

    [Fact]
    public void TwoTimings_OfTheSameWordsAndPitches_AreKeptApart()
    {
        Cache().Add(Key(start: 0), Sung(0.5f));
        Cache().Add(Key(start: 0.5), Sung(0.25f));
        Assert.True(Cache().TryGet(Key(start: 0.5), out var found));
        Assert.Equal(0.25f, found.Samples[0], 0.0001f);
        Assert.False(Cache().TryGet(Key(start: 0.25), out _));
    }

    [Fact]
    public void AnotherVoice_SpeakerPitchOrBreath_IsAnotherPhrase()
    {
        Cache("a").Add(Key(room: 300), Sung(0.5f));
        Assert.False(Cache("b").TryGet(Key(room: 300), out _));
        Assert.False(Cache("a").TryGet(Key(room: 300, speaker: "s"), out _));
        Assert.False(Cache("a").TryGet(Key(room: 300, pitch: 61), out _));
        Assert.False(Cache("a").TryGet(Key(room: 200), out _));
        Assert.False(Cache("a").TryGet(Key(), out _));
        Assert.True(Cache("a").TryGet(Key(room: 300), out _));
    }

    [Fact]
    public void AFileCutShort_IsAPhraseSungAgain_AndGoes()
    {
        Cache().Add(Key(), Sung(0.5f, 0.5f, 0.5f));
        var file = Assert.Single(Directory.GetFiles(Folder));
        var bytes = File.ReadAllBytes(file);
        File.WriteAllBytes(file, bytes[..^2]);
        Assert.False(Cache().TryGet(Key(), out _));
        Assert.Empty(Directory.GetFiles(Folder));
    }

    [Fact]
    public void AFolderThatCannotBeWritten_IsOnlyNoDisk()
    {
        string blocked = Path.Combine(_out, "a-file");
        File.WriteAllText(blocked, "");
        var cache = new PhraseCache(1000, new PhraseDisk(blocked, "v"));
        cache.Add(Key(), Sung(0.5f));
        Assert.True(cache.TryGet(Key(), out _));
        Assert.False(cache.TryGet(Key(pitch: 62), out _));
    }

    private string Wav(string name, int bytes, int minutesAgo)
    {
        string file = Path.Combine(_out, name);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, new byte[bytes]);
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(-minutesAgo));
        return file;
    }

    [Fact]
    public void Trim_HoldsWavsAndPhrasesTogether_LongestUnusedFirst()
    {
        var oldWav = Wav("a.wav", 100, 30);
        var oldPhrase = Wav(Path.Combine("phrases", "a.phrase"), 100, 20);
        var newWav = Wav("b.wav", 100, 10);
        var newPhrase = Wav(Path.Combine("phrases", "b.phrase"), 100, 5);
        PhraseDisk.Trim(_out, 250);
        Assert.False(File.Exists(oldWav));
        Assert.False(File.Exists(oldPhrase));
        Assert.True(File.Exists(newWav));
        Assert.True(File.Exists(newPhrase));
    }

    [Fact]
    public void Trim_LeavesTheFileUsedLast_ThoughItPassesTheLimit()
    {
        var only = Wav("a.wav", 1000, 1);
        PhraseDisk.Trim(_out, 10);
        Assert.True(File.Exists(only));
    }

    [Fact]
    public void Trim_TouchesNothingElseInTheFolder()
    {
        var other = Wav("notes.txt", 1000, 100);
        Wav("a.wav", 100, 1);
        Wav("b.wav", 100, 2);
        PhraseDisk.Trim(_out, 50);
        Assert.True(File.Exists(other));
    }

    [Fact]
    public void APhraseFoundInMemoryOrOnDisk_CountsAsUsed()
    {
        var cache = Cache();
        cache.Add(Key(), Sung(0.5f));
        var file = Assert.Single(Directory.GetFiles(Folder));
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddHours(-1));
        Assert.True(cache.TryGet(Key(), out _));
        Assert.True(File.GetLastWriteTimeUtc(file) > DateTime.UtcNow.AddMinutes(-1), "from memory");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddHours(-1));
        Assert.True(Cache().TryGet(Key(), out _));
        Assert.True(File.GetLastWriteTimeUtc(file) > DateTime.UtcNow.AddMinutes(-1), "from disk");
    }

    private sealed class Fake : ISinger
    {
        public int Sung { get; private set; }
        public string Name => "fake/voice";

        public Task<SungAudio> SingAsync(SungScore score, CancellationToken ct = default)
        {
            Sung++;
            return Task.FromResult(new SungAudio(new float[(int)(score.End * 1000)], 1000));
        }
    }

    [Fact]
    public async Task AnEngineStartedAgain_SingsOnlyWhatChanged()
    {
        SungNote[] Phrase(double at, string a) => [new(at, 0.5, 60, a), new(at + 0.5, 0.5, 62, "き")];
        var first = new Fake();
        using (var singer = new PhrasedSinger(first, null, new PhraseDisk(Folder, first.Name)))
        {
            await singer.SingAsync(new SungScore([.. Phrase(0, "か"), .. Phrase(2, "さ")]));
        }
        Assert.Equal(2, first.Sung);
        var second = new Fake();
        using (var singer = new PhrasedSinger(second, null, new PhraseDisk(Folder, second.Name)))
        {
            await singer.SingAsync(new SungScore([.. Phrase(0, "か"), .. Phrase(2, "な")]));
        }
        Assert.Equal(1, second.Sung);
    }
}

// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using Xunit;

namespace Cantoria.Tests;

/// <summary>Fetching an offered voice (roadmap F): checked, unpacked whole or not at all, kept when there.</summary>
public sealed class VoiceOffersTests : IDisposable
{
    private readonly string _voices = Path.Combine(Path.GetTempPath(), "cantoria-tests", Guid.NewGuid().ToString("N"), "voices");

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_voices)!, true); } catch (IOException) { }
    }

    private sealed class Serve(Dictionary<string, byte[]> files) : HttpMessageHandler
    {
        public int Asked { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Asked++;
            return Task.FromResult(files.TryGetValue(request.RequestUri!.ToString(), out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static byte[] Zip(params (string Name, string Text)[] entries)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            foreach (var (name, text) in entries)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open());
                w.Write(text);
            }
        }
        return memory.ToArray();
    }

    private static OfferedFile File(string url, byte[] body, string folder, string? vocoder = null)
        => new(url, body.Length, Convert.ToHexStringLower(SHA256.HashData(body)), folder, vocoder);

    private static VoiceOffer Offer(params OfferedFile[] files) => new("v", "diffsinger/V", "V", ["ja"], ["free"], "https://t", files);

    [Fact]
    public async Task AVoiceAndItsVocoder_AreUnpacked_TheVoiceFromInsideItsOneFolder()
    {
        var voice = Zip(("V_1.0/dsconfig.yaml", "acoustic: a.onnx\n"), ("V_1.0/dsdur/dsconfig.yaml", "x: 1\n"));
        var vocoder = Zip(("vocoder.yaml", "name: \"voc\"\n"), ("NOTICE.txt", "cc"));
        var serve = new Serve(new() { ["https://a/v.zip"] = voice, ["https://a/voc.oudep"] = vocoder });
        var offer = Offer(File("https://a/v.zip", voice, "V"), File("https://a/voc.oudep", vocoder, "voc", "voc"));
        var made = await VoiceOffers.FetchAsync(offer, _voices, new HttpClient(serve));
        Assert.Equal([Path.Combine(_voices, "V"), Path.Combine(_voices, "voc")], made);
        Assert.True(System.IO.File.Exists(Path.Combine(_voices, "V", "dsdur", "dsconfig.yaml")));
        Assert.True(System.IO.File.Exists(Path.Combine(_voices, "voc", "NOTICE.txt")));
        Assert.Equal([Path.Combine(_voices, "V"), Path.Combine(_voices, "voc")], Directory.GetDirectories(_voices).Order());   // nothing else left

        // Again: all there, nothing fetched.
        int asked = serve.Asked;
        Assert.Empty(await VoiceOffers.FetchAsync(offer, _voices, new HttpClient(serve)));
        Assert.Equal(asked, serve.Asked);
    }

    [Fact]
    public async Task AVocoderOfThatName_UnderAnotherFolder_IsNotFetchedAgain()
    {
        Directory.CreateDirectory(Path.Combine(_voices, "mine"));
        System.IO.File.WriteAllText(Path.Combine(_voices, "mine", "vocoder.yaml"), "name: \"VOC\"\n");
        var vocoder = Zip(("vocoder.yaml", "name: \"voc\"\n"));
        var serve = new Serve(new() { ["https://a/voc.oudep"] = vocoder });
        Assert.Empty(await VoiceOffers.FetchAsync(Offer(File("https://a/voc.oudep", vocoder, "voc", "voc")), _voices, new HttpClient(serve)));
        Assert.Equal(0, serve.Asked);
    }

    [Fact]
    public async Task AFileThatIsNotThePublishedOne_IsRefused_AndNothingIsLeft()
    {
        var published = Zip(("dsconfig.yaml", "a"));
        var served = Zip(("dsconfig.yaml", "b"));
        var serve = new Serve(new() { ["https://a/v.zip"] = served });
        var e = await Assert.ThrowsAsync<IOException>(() => VoiceOffers.FetchAsync(Offer(File("https://a/v.zip", published, "V")), _voices, new HttpClient(serve)));
        Assert.Contains("is not the file it was published as", e.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_voices));
    }

    [Fact]
    public async Task AnEntryThatWouldLandOutside_IsRefused_AndNothingIsLeft()
    {
        var evil = Zip(("dsconfig.yaml", "a"), ("../evil.txt", "x"));
        var serve = new Serve(new() { ["https://a/v.zip"] = evil });
        var e = await Assert.ThrowsAsync<IOException>(() => VoiceOffers.FetchAsync(Offer(File("https://a/v.zip", evil, "V")), _voices, new HttpClient(serve)));
        Assert.Contains("outside its folder", e.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_voices));
        Assert.False(System.IO.File.Exists(Path.Combine(Path.GetDirectoryName(_voices)!, "evil.txt")));
    }

    [Fact]
    public void TheOffers_AreWhatTheResearchFound()
    {
        var qixuan = Assert.Single(VoiceOffers.All);
        Assert.Same(qixuan, VoiceOffers.Find("QIXUAN"));
        Assert.Equal("diffsinger/Qixuan", qixuan.Voice);
        Assert.Contains(qixuan.Terms, t => t.Contains("synthesised with AI"));
        Assert.All(qixuan.Files, f => Assert.StartsWith("https://github.com/", f.Url));
        Assert.All(qixuan.Files, f => Assert.Matches("^[0-9a-f]{64}$", f.Sha256));
    }
}

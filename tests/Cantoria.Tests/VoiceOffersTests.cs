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

    private static byte[] TarGz(params (string Name, string Text)[] entries)
    {
        using var memory = new MemoryStream();
        using (var gz = new GZipStream(memory, CompressionLevel.Fastest, true))
        using (var tar = new System.Formats.Tar.TarWriter(gz))
        {
            foreach (var (name, text) in entries)
            {
                tar.WriteEntry(new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, name)
                {
                    DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)),
                });
            }
        }
        return memory.ToArray();
    }

    // A Sinsy voice comes as a .tar.gz from an address that ends in /download: told by its first bytes,
    // unpacked from inside its one folder, installed when its .htsvoice is there.
    [Fact]
    public async Task AGzippedTar_IsUnpacked_FromInsideItsOneFolder_AndItsMarkerSaysItIsThere()
    {
        var voice = TarGz(("v-0.90/v_f001.htsvoice", "[GLOBAL]"), ("v-0.90/COPYING", "CC BY 3.0"));
        var serve = new Serve(new() { ["https://a/v.tar.gz/download"] = voice });
        var file = File("https://a/v.tar.gz/download", voice, "v-0.90") with { Marker = "v_f001.htsvoice" };
        var offer = new VoiceOffer("v", "sinsy/v_f001", "V", ["ja"], ["free"], "https://t", [file]);
        Assert.Equal("sinsy", offer.Engine);
        Assert.Equal([Path.Combine(_voices, "v-0.90")], await VoiceOffers.FetchAsync(offer, _voices, new HttpClient(serve)));
        Assert.Equal("CC BY 3.0", System.IO.File.ReadAllText(Path.Combine(_voices, "v-0.90", "COPYING")));
        Assert.True(VoiceOffers.IsThere(file, _voices));
        Assert.Empty(await VoiceOffers.FetchAsync(offer, _voices, new HttpClient(serve)));
        Assert.Equal(1, serve.Asked);
    }

    [Fact]
    public async Task AnEntryOfATarThatWouldLandOutside_IsRefused_AndNothingIsLeft()
    {
        var evil = TarGz(("a.htsvoice", "a"), ("../evil.txt", "x"));
        var serve = new Serve(new() { ["https://a/v.tgz"] = evil });
        var offer = new VoiceOffer("v", "sinsy/a", "V", ["ja"], ["free"], "https://t", [File("https://a/v.tgz", evil, "V") with { Marker = "a.htsvoice" }]);
        var e = await Assert.ThrowsAsync<IOException>(() => VoiceOffers.FetchAsync(offer, _voices, new HttpClient(serve)));
        Assert.Contains("outside its folder", e.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_voices));
    }

    [Fact]
    public void TheOffers_AreWhatTheResearchFound()
    {
        Assert.Equal(["qixuan", "nitech-song070"], VoiceOffers.All.Select(o => o.Id));
        var qixuan = VoiceOffers.All[0];
        Assert.Same(qixuan, VoiceOffers.Find("QIXUAN"));
        Assert.Equal("diffsinger/Qixuan", qixuan.Voice);
        Assert.Contains(qixuan.Terms, t => t.Contains("synthesised with AI"));
        Assert.All(qixuan.Files, f => Assert.StartsWith("https://github.com/", f.Url));
        var sinsy = VoiceOffers.All[1];
        Assert.Equal("sinsy/nitech_jp_song070_f001", sinsy.Voice);
        Assert.Contains(sinsy.Terms, t => t.Contains("Nagoya Institute of Technology"));
        Assert.Equal("nitech_jp_song070_f001.htsvoice", Assert.Single(sinsy.Files).Marker);
        Assert.All(VoiceOffers.All.SelectMany(o => o.Files), f => Assert.Matches("^[0-9a-f]{64}$", f.Sha256));
    }
}

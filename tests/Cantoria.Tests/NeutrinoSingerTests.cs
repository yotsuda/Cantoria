// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Cantoria.Neutrino;
using Xunit;

namespace Cantoria.Tests;

/// <summary>
/// Runs only where NEUTRINO is installed: set CANTORIA_NEUTRINO_DIR to its folder. Elsewhere the
/// facts pass without doing anything (xunit 2 cannot skip at run time), so CI stays green.
/// </summary>
public class NeutrinoSingerTests
{
    private static string? Folder => Environment.GetEnvironmentVariable("CANTORIA_NEUTRINO_DIR");

    [Fact]
    public void Options_NameTheProblems()
    {
        var problems = new NeutrinoOptions(@"C:\no\such\folder").Validate();
        Assert.Single(problems);
        Assert.Contains("not found", problems[0]);
    }

    /// <summary>A folder shaped like a NEUTRINO: the named programs under bin (empty files), the
    /// dictionary's folder, and the voice MERROW.</summary>
    private static string Shaped(params string[] programs)
    {
        string root = Path.Combine(Path.GetTempPath(), "cantoria-neutrino-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        Directory.CreateDirectory(Path.Combine(root, "settings", "dic"));
        Directory.CreateDirectory(Path.Combine(root, "model", "MERROW"));
        File.WriteAllText(Path.Combine(root, "model", "MERROW", "info.toml"), "");
        foreach (var program in programs)
        {
            File.WriteAllText(Path.Combine(root, "bin", program), "");
        }
        return root;
    }

    /// <summary>The package with the tuning tool has the server and its client (and neutrino.exe
    /// beside them); the plain package has neutrino.exe alone. Both are a NEUTRINO, and the server
    /// is used where it is.</summary>
    [Theory]
    [InlineData(true, "neutrino_client.exe", "neutrino_server.exe", "neutrino_client.exe", "neutrino.exe", "musicXMLtoLabel.exe")]
    [InlineData(true, "neutrino_client.exe", "neutrino_server.exe", "neutrino_client.exe", "musicXMLtoLabel.exe")]
    [InlineData(false, "neutrino.exe", "neutrino.exe", "musicXMLtoLabel.exe")]
    // Half a server is none.
    [InlineData(false, "neutrino.exe", "neutrino_server.exe", "neutrino.exe", "musicXMLtoLabel.exe")]
    [InlineData(false, "neutrino.exe", "neutrino_client.exe", "neutrino.exe", "musicXMLtoLabel.exe")]
    public void Options_TakeEitherPackage_AndPreferTheServer(bool server, string program, params string[] programs)
    {
        string root = Shaped(programs);
        try
        {
            var options = new NeutrinoOptions(root);
            Assert.Empty(options.Validate());
            Assert.Equal(server, options.HasServer);
            Assert.Equal(program, options.RenderProgram);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("neither neutrino_server.exe with neutrino_client.exe nor neutrino.exe", "musicXMLtoLabel.exe")]
    [InlineData("neither neutrino_server.exe with neutrino_client.exe nor neutrino.exe", "neutrino_server.exe", "musicXMLtoLabel.exe")]
    [InlineData("musicXMLtoLabel.exe not found", "neutrino.exe")]
    [InlineData("musicXMLtoLabel.exe not found", "neutrino_server.exe", "neutrino_client.exe")]
    public void Options_NameTheProgramThatIsMissing(string said, params string[] programs)
    {
        string root = Shaped(programs);
        try
        {
            var problem = Assert.Single(new NeutrinoOptions(root).Validate());
            Assert.Contains(said, problem);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Sings_TheExample_WithTheLeadInTrimmed()
    {
        if (Folder is null)
        {
            return;
        }
        using var singer = new NeutrinoSinger(new NeutrinoOptions(Folder));
        var score = ScoreJson.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "yukiyakonko.json")));
        var audio = await singer.SingAsync(score);
        // The score ends at 6.819 s; the file is padded to the 8 s bar, lead-in trimmed.
        Assert.InRange(audio.Duration, score.End, 8.5);
        Assert.Contains(audio.Samples, s => Math.Abs(s) > 0.05f);
        // Silence before the first note: the lead-in is gone, so sound comes within the first 0.3 s
        // (the consonant may start a little before the beat).
        int firstLoud = Array.FindIndex(audio.Samples, s => Math.Abs(s) > 0.02f);
        Assert.InRange(firstLoud / (double)audio.SampleRate, 0, 0.3);
    }
}

// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.IO.Compression;
using System.Security.Cryptography;

namespace Cantoria;

/// <summary>One file of an offered voice: where its author publishes it, what it must be, and
/// the folder it is unpacked into under the voices folder.</summary>
/// <param name="Url">The author's own address for it.</param>
/// <param name="Size">Its length in bytes.</param>
/// <param name="Sha256">Its SHA-256, lower-case hex: a file that is not this is refused.</param>
/// <param name="Folder">The folder it becomes, under the voices folder.</param>
/// <param name="Vocoder">For a vocoder, the name in its <c>vocoder.yaml</c>: one already beside the
/// voices under any folder name is not fetched again. Null for a voice.</param>
public sealed record OfferedFile(string Url, long Size, string Sha256, string Folder, string? Vocoder = null);

/// <summary>
/// A voice whose terms allow a program to fetch it for the user from its author, with what the user
/// is to be told before (roadmap F; docs/voices-2026-11-21.md). Cantoria ships no voice: it offers,
/// shows the terms, and fetches the files its author publishes, checked against their SHA-256.
/// </summary>
/// <param name="Id">The short name it is asked for by (<c>qixuan</c>).</param>
/// <param name="Voice">The voice it installs, as <c>engine/name</c>.</param>
/// <param name="Title">What it is called to the user.</param>
/// <param name="Languages">What it sings.</param>
/// <param name="Terms">What the user is to know before: the points of its terms, in sentences.</param>
/// <param name="TermsUrl">Where its whole terms are.</param>
/// <param name="Files">What is fetched.</param>
public sealed record VoiceOffer(string Id, string Voice, string Title, IReadOnlyList<string> Languages,
    IReadOnlyList<string> Terms, string TermsUrl, IReadOnlyList<OfferedFile> Files)
{
    /// <summary>Bytes fetched when nothing of it is there yet.</summary>
    public long Size => Files.Sum(f => f.Size);
}

/// <summary>How far a fetch is: the file (its folder), bytes received of its size.</summary>
public readonly record struct FetchProgress(string Folder, long Received, long Size);

/// <summary>The voices Cantoria can offer, and their fetching.</summary>
public static class VoiceOffers
{
    /// <summary>The offers, the first the one to suggest.</summary>
    public static IReadOnlyList<VoiceOffer> All { get; } =
    [
        new("qixuan", "diffsinger/Qixuan", "Qixuan 绮萱 2.7.0 (DiffSinger, by the DiffSinger community)", ["zh", "ja", "en"],
            [
                "Qixuan is free; it may not be sold, nor offered as a paid service.",
                "Songs made with it may be published and sold. Where you publish one, say in a visible place that it is synthesised with AI, and credit the voice \"绮萱 (Qixuan)\" and \"DiffSinger\".",
                "What it sings may not be used to train voice synthesis or voice conversion, and its model files may not be changed, converted or fine-tuned.",
                "Its character picture may not be used commercially or in AI image work.",
                "It sings through OpenVPI's vocoder pc_nsf_hifigan, fetched with it: CC BY-NC-SA 4.0 (its NOTICE.txt stays beside it); what it synthesises is not restricted.",
                "The terms are the voice author's, in Chinese (Terms_of_Use.zh-CN.pdf comes with the voice); read them before you use it.",
            ],
            "https://github.com/yqzhishen/qixuan-diffsinger/blob/main/terms_of_use/Terms_of_Use.zh-CN.md",
            [
                new("https://github.com/yqzhishen/qixuan-diffsinger/releases/download/v2.7.0/Qixuan_v2.7.0_DiffSinger_OpenUtau.zip",
                    420707627, "fe8ee7c95883d327a2b7facbbe832a7a53e1344918dc791ca83fc23b08034110", "Qixuan"),
                new("https://github.com/openvpi/vocoders/releases/download/pc-nsf-hifigan-44.1k-hop512-128bin-2025.02/pc_nsf_hifigan_44.1k_hop512_128bin_2025.02.oudep",
                    52760610, "ba7d43142d41f6900c8264b5662ca7125a50feb8760bb8b9615c61a8f5e6902e", "pc_nsf_hifigan_44.1k_hop512_128bin_2025.02",
                    Vocoder: "pc_nsf_hifigan_44.1k_hop512_128bin_2025.02"),
            ]),
    ];

    /// <summary>The offer asked for by its id (any case), or null.</summary>
    public static VoiceOffer? Find(string id) => All.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether a file of an offer is already under <paramref name="voicesFolder"/>: its folder
    /// holding what it holds, or, for a vocoder, one of that name in any folder beside the voices.</summary>
    public static bool IsThere(OfferedFile file, string voicesFolder)
    {
        if (file.Vocoder is null)
        {
            return File.Exists(Path.Combine(voicesFolder, file.Folder, "dsconfig.yaml"));
        }
        if (!Directory.Exists(voicesFolder))
        {
            return false;
        }
        return Directory.EnumerateDirectories(voicesFolder).Any(d =>
        {
            string yaml = Path.Combine(d, "vocoder.yaml");
            try
            {
                return File.Exists(yaml) && string.Equals(DiffSinger.DsVocoderConfig.Load(yaml).Name, file.Vocoder, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception e) when (e is IOException or YamlDotNet.Core.YamlException)
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Fetches what of <paramref name="offer"/> is not under <paramref name="voicesFolder"/> yet, checks
    /// each file's length and SHA-256, and unpacks it into its folder (a zip holding one folder is
    /// unpacked from inside it). A file is fetched beside its folder and unpacked there before it
    /// takes the folder's name, so a fetch stopped or failed leaves nothing half there. Answers the
    /// folders made.
    /// </summary>
    public static async Task<IReadOnlyList<string>> FetchAsync(VoiceOffer offer, string voicesFolder, HttpClient http,
        IProgress<FetchProgress>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(voicesFolder);
        var made = new List<string>();
        foreach (var file in offer.Files)
        {
            if (IsThere(file, voicesFolder))
            {
                continue;
            }
            string target = Path.Combine(voicesFolder, file.Folder);
            if (Directory.Exists(target))
            {
                throw new IOException($"{target} is there and is not {file.Folder} as {offer.Title} has it: move it away, or fetch into another voices folder");
            }
            string part = Path.Combine(voicesFolder, $".{file.Folder}.download");
            string unpacking = Path.Combine(voicesFolder, $".{file.Folder}.unpacking");
            TryDelete(part);         // what a fetch that was killed left
            TryDelete(unpacking);
            try
            {
                await DownloadAsync(file, part, http, progress, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                Unpack(part, unpacking);
                Directory.Move(unpacking, target);
                made.Add(target);
            }
            finally
            {
                TryDelete(part);
                TryDelete(unpacking);
            }
        }
        return made;
    }

    private static async Task DownloadAsync(OfferedFile file, string part, HttpClient http, IProgress<FetchProgress>? progress, CancellationToken ct)
    {
        using var answer = await http.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!answer.IsSuccessStatusCode)
        {
            throw new IOException($"{file.Url} answered {(int)answer.StatusCode} {answer.ReasonPhrase}");
        }
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long received = 0;
        await using (var from = await answer.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var to = File.Create(part))
        {
            var buffer = new byte[1 << 16];
            long told = -1;
            int n;
            while ((n = await from.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                received += n;
                if (received > file.Size)
                {
                    throw new IOException($"{file.Url} is longer than the {file.Size} bytes it was published with: not fetched");
                }
                sha.AppendData(buffer, 0, n);
                await to.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                if (received - told >= 1 << 20 || received == file.Size)
                {
                    progress?.Report(new FetchProgress(file.Folder, received, file.Size));
                    told = received;
                }
            }
        }
        string hash = Convert.ToHexStringLower(sha.GetHashAndReset());
        if (received != file.Size || hash != file.Sha256)
        {
            throw new IOException($"{file.Url} is not the file it was published as ({received} bytes, SHA-256 {hash}; expected {file.Size}, {file.Sha256}): not installed");
        }
    }

    /// <summary>Unpacks a zip into <paramref name="folder"/>; a zip whose every entry is in one folder
    /// is unpacked from inside it. An entry that would land outside is refused.</summary>
    internal static void Unpack(string zip, string folder)
    {
        using var archive = ZipFile.OpenRead(zip);
        var names = archive.Entries.Select(e => e.FullName.Replace('\\', '/')).Where(n => n.Length > 0).ToList();
        string? top = names.Count > 0 && names[0].IndexOf('/') is > 0 and var slash ? names[0][..(slash + 1)] : null;
        string strip = top is not null && names.All(n => n.StartsWith(top, StringComparison.Ordinal)) ? top : "";
        string root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(folder);
        foreach (var entry in archive.Entries)
        {
            string name = entry.FullName.Replace('\\', '/');
            if (name.Length <= strip.Length)
            {
                continue;
            }
            string to = Path.GetFullPath(Path.Combine(folder, name[strip.Length..]));
            if (!to.StartsWith(root, StringComparison.Ordinal))
            {
                throw new IOException($"{Path.GetFileName(zip)}: \"{entry.FullName}\" would be unpacked outside its folder; not installed");
            }
            if (name.EndsWith('/'))
            {
                Directory.CreateDirectory(to);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            entry.ExtractToFile(to, overwrite: false);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            else if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

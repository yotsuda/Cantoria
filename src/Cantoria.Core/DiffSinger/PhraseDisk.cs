// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Cantoria.DiffSinger;

/// <summary>
/// Phrases on disk, so that an engine that starts again still sings only what changed. Everything
/// here is a cache: a file that cannot be read or written is a phrase sung again.
/// </summary>
/// <param name="folder">The folder of phrase files (<see cref="FolderIn"/>).</param>
/// <param name="voice">Whose phrases: files of another voice are not looked at.</param>
public sealed class PhraseDisk(string folder, string voice)
{
    private const int Magic = 0x31485043;   // "CPH1"
    private const string Extension = ".phrase";

    /// <summary>The folder of phrase files under a folder of rendered WAVs.</summary>
    public static string FolderIn(string outDir) => Path.Combine(outDir, "phrases");

    /// <summary>The name's first part: what must match exactly. Times may differ by a caller's
    /// rounding, so they are in the file and compared there (<see cref="PhraseKey.SameAs"/>).</summary>
    private string Stem(PhraseKey key)
    {
        var text = new StringBuilder().Append(voice).Append('\n').Append(key.Speaker).Append('\n').Append(key.Language).Append('\n')
            .AppendJoin('\n', key.Words).Append('\n')
            .AppendJoin(' ', key.Notes.Select(n => n.Pitch.ToString("R", CultureInfo.InvariantCulture) + (n.Breath ? "b" : "")));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..40];
    }

    /// <summary>Finds the phrase and marks it as used.</summary>
    public bool TryLoad(PhraseKey key, out SungPhrase phrase, out string file)
    {
        string[] candidates;
        try
        {
            candidates = Directory.GetFiles(folder, Stem(key) + "-*" + Extension);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            candidates = [];
        }
        foreach (var candidate in candidates)
        {
            if (Read(candidate, key) is { } found)
            {
                Touch(candidate);
                phrase = found;
                file = candidate;
                return true;
            }
        }
        phrase = null!;
        file = "";
        return false;
    }

    private static SungPhrase? Read(string file, PhraseKey key)
    {
        try
        {
            using var reader = new BinaryReader(File.OpenRead(file));
            if (reader.ReadInt32() != Magic)
            {
                throw new InvalidDataException("not a phrase");
            }
            int count = reader.ReadInt32();
            if (count != key.Notes.Count)
            {
                return null;
            }
            var notes = new PhraseNote[count];
            for (int i = 0; i < count; i++)
            {
                notes[i] = key.Notes[i] with { Start = reader.ReadDouble(), Duration = reader.ReadDouble() };
            }
            double? room = reader.ReadBoolean() ? reader.ReadDouble() : null;
            if (!key.SameAs(key with { Notes = notes, EndBreathRoom = room }))
            {
                return null;
            }
            double startMs = reader.ReadDouble();
            int rate = reader.ReadInt32();
            int length = reader.ReadInt32();
            if (rate <= 0 || length < 0)
            {
                throw new InvalidDataException("not a phrase");
            }
            var bytes = reader.ReadBytes(length * sizeof(short));
            if (bytes.Length != length * sizeof(short))
            {
                throw new InvalidDataException("a phrase cut short");
            }
            var shorts = new short[length];
            Buffer.BlockCopy(bytes, 0, shorts, 0, bytes.Length);
            return new SungPhrase(shorts.Select(s => s / 32767f).ToArray(), startMs, rate);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            try { File.Delete(file); } catch (Exception gone) when (gone is IOException or UnauthorizedAccessException) { /* next time */ }
            return null;
        }
    }

    /// <summary>Writes the phrase at the depth of the WAV it ends up in; the file, or null if it could not be.</summary>
    public string? Save(PhraseKey key, SungPhrase phrase)
    {
        string file = Path.Combine(folder, $"{Stem(key)}-{Guid.NewGuid():N}{Extension}");
        string temporary = file + ".tmp";
        try
        {
            Directory.CreateDirectory(folder);
            using (var writer = new BinaryWriter(File.Create(temporary)))
            {
                writer.Write(Magic);
                writer.Write(key.Notes.Count);
                foreach (var n in key.Notes)
                {
                    writer.Write(n.Start);
                    writer.Write(n.Duration);
                }
                writer.Write(key.EndBreathRoom.HasValue);
                if (key.EndBreathRoom is { } room)
                {
                    writer.Write(room);
                }
                writer.Write(phrase.StartMs);
                writer.Write(phrase.SampleRate);
                writer.Write(phrase.Samples.Length);
                var shorts = phrase.Samples.Select(s => (short)Math.Clamp(Math.Round(s * 32767.0), short.MinValue, short.MaxValue)).ToArray();
                var bytes = new byte[shorts.Length * sizeof(short)];
                Buffer.BlockCopy(shorts, 0, bytes, 0, bytes.Length);
                writer.Write(bytes);
            }
            File.Move(temporary, file);
            return file;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temporary); } catch (Exception gone) when (gone is IOException or UnauthorizedAccessException) { /* next time */ }
            return null;
        }
    }

    /// <summary>Marks a file as used now: <see cref="Trim"/> lets the longest unused go first.</summary>
    public static void Touch(string file)
    {
        try
        {
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Trimmed meanwhile, or held: it is only a cache.
        }
    }

    /// <summary>
    /// Keeps the rendered WAVs directly under <paramref name="outDir"/> and the phrase files together
    /// within <paramref name="maxBytes"/>, removing the longest unused first. The file used last
    /// always stays. A WAV removed is laid together again from its phrases, a phrase is sung again.
    /// </summary>
    public static void Trim(string outDir, long maxBytes)
    {
        try
        {
            var root = new DirectoryInfo(outDir);
            var phrases = new DirectoryInfo(FolderIn(outDir));
            var files = (root.Exists ? root.GetFiles("*.wav") : []).Concat(phrases.Exists ? phrases.GetFiles() : [])
                .OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            long kept = 0;
            for (int i = 0; i < files.Count; i++)
            {
                kept += files[i].Length;
                if (i > 0 && kept > maxBytes)
                {
                    try { files[i].Delete(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* in use: next time */ }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Only a cache.
        }
    }
}

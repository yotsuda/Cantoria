// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers.Binary;
using System.Text;

namespace Cantilena.Audio;

/// <summary>
/// RIFF WAVE in and out: reads PCM 8/16/24/32-bit and IEEE float, any channel count (mixed down
/// to mono by averaging); writes 16-bit PCM mono. Enough for the backends' files and the CLI.
/// </summary>
public static class Wav
{
    /// <summary>Reads a WAVE file's samples as mono floats in −1..1 and its sample rate.</summary>
    /// <exception cref="InvalidDataException">Not a WAVE file, or a format not handled.</exception>
    public static SungAudio Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12 || !bytes[..4].SequenceEqual("RIFF"u8) || !bytes[8..12].SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException("not a RIFF WAVE file");
        }
        int format = 0, channels = 0, rate = 0, bits = 0;
        ReadOnlySpan<byte> data = default;
        int pos = 12;
        while (pos + 8 <= bytes.Length)
        {
            var id = bytes.Slice(pos, 4);
            int size = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(pos + 4, 4));
            int body = pos + 8;
            if (size < 0 || body + size > bytes.Length)
            {
                size = bytes.Length - body;   // a truncated last chunk: take what is there
            }
            if (id.SequenceEqual("fmt "u8))
            {
                var f = bytes.Slice(body, size);
                format = BinaryPrimitives.ReadUInt16LittleEndian(f);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(f[2..]);
                rate = BinaryPrimitives.ReadInt32LittleEndian(f[4..]);
                bits = BinaryPrimitives.ReadUInt16LittleEndian(f[14..]);
                if (format == 0xFFFE && size >= 26)   // WAVE_FORMAT_EXTENSIBLE: the real format leads the GUID
                {
                    format = BinaryPrimitives.ReadUInt16LittleEndian(f[24..]);
                }
            }
            else if (id.SequenceEqual("data"u8))
            {
                data = bytes.Slice(body, size);
            }
            pos = body + size + (size & 1);
        }
        if (channels == 0 || rate == 0)
        {
            throw new InvalidDataException("the WAVE file has no fmt chunk");
        }
        int bytesPer = bits / 8;
        if (bytesPer == 0)
        {
            throw new InvalidDataException($"unhandled bit depth {bits}");
        }
        int frames = data.Length / (bytesPer * channels);
        var mono = new float[frames];
        for (int i = 0; i < frames; i++)
        {
            float sum = 0;
            for (int c = 0; c < channels; c++)
            {
                var s = data.Slice((i * channels + c) * bytesPer, bytesPer);
                sum += (format, bits) switch
                {
                    (1, 8) => (s[0] - 128) / 128f,
                    (1, 16) => BinaryPrimitives.ReadInt16LittleEndian(s) / 32768f,
                    (1, 24) => ((s[0] << 8 | s[1] << 16 | s[2] << 24) >> 8) / 8388608f,
                    (1, 32) => BinaryPrimitives.ReadInt32LittleEndian(s) / 2147483648f,
                    (3, 32) => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(s)),
                    (3, 64) => (float)BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(s)),
                    _ => throw new InvalidDataException($"unhandled WAVE format {format} at {bits} bits"),
                };
            }
            mono[i] = sum / channels;
        }
        return new SungAudio(mono, rate);
    }

    /// <summary>Writes mono 16-bit PCM; samples outside −1..1 are clipped.</summary>
    public static byte[] Write(SungAudio audio)
    {
        int dataSize = audio.Samples.Length * 2;
        var bytes = new byte[44 + dataSize];
        var span = bytes.AsSpan();
        Encoding.ASCII.GetBytes("RIFF", span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + dataSize);
        Encoding.ASCII.GetBytes("WAVEfmt ", span[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteUInt16LittleEndian(span[20..], 1);                       // PCM
        BinaryPrimitives.WriteUInt16LittleEndian(span[22..], 1);                       // mono
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], audio.SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], audio.SampleRate * 2);     // byte rate
        BinaryPrimitives.WriteUInt16LittleEndian(span[32..], 2);                       // block align
        BinaryPrimitives.WriteUInt16LittleEndian(span[34..], 16);
        Encoding.ASCII.GetBytes("data", span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], dataSize);
        var data = span[44..];
        for (int i = 0; i < audio.Samples.Length; i++)
        {
            float v = Math.Clamp(audio.Samples[i], -1f, 1f);
            BinaryPrimitives.WriteInt16LittleEndian(data[(i * 2)..], (short)MathF.Round(v * 32767f));
        }
        return bytes;
    }
}

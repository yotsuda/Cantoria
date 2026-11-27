// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Runtime.InteropServices;

namespace Cantoria.Sinsy;

/// <summary>
/// The C entry point of <c>cantoria_sinsy</c> (<c>native/sinsy/cantoria_sinsy.cpp</c>): Sinsy and
/// hts_engine_API in one library beside the program, built by <c>tools/build-sinsy.ps1</c>.
/// </summary>
internal static partial class SinsyNative
{
    private const string Library = "cantoria_sinsy";

    [LibraryImport(Library, EntryPoint = "cantoria_sinsy_open", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr Open(string dictionaryFolder, string voiceFile);

    [LibraryImport(Library, EntryPoint = "cantoria_sinsy_last_open_error")]
    private static partial IntPtr LastOpenErrorPtr();

    [LibraryImport(Library, EntryPoint = "cantoria_sinsy_close")]
    public static partial void Close(IntPtr handle);

    [LibraryImport(Library, EntryPoint = "cantoria_sinsy_sample_rate")]
    public static partial int SampleRate(IntPtr handle);

    [LibraryImport(Library, EntryPoint = "cantoria_sinsy_messages")]
    private static partial IntPtr MessagesPtr(IntPtr handle);

    [LibraryImport(Library, EntryPoint = "cantoria_sinsy_clear")]
    public static partial int Clear(IntPtr handle);

    [LibraryImport(Library, EntryPoint = "cantoria_sinsy_tempo")]
    public static partial int Tempo(IntPtr handle, double bpm);

    [LibraryImport(Library, EntryPoint = "cantoria_sinsy_note", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int Note(IntPtr handle, int duration, string lyric, int pitch, int tie, int slur, int syllabic, int breath);

    [LibraryImport(Library, EntryPoint = "cantoria_sinsy_rest")]
    public static partial int Rest(IntPtr handle, int duration);

    [LibraryImport(Library, EntryPoint = "cantoria_sinsy_synthesize")]
    public static partial int Synthesize(IntPtr handle, double halfTones);

    [LibraryImport(Library, EntryPoint = "cantoria_sinsy_samples")]
    public static partial int Samples(IntPtr handle, [Out] float[] buffer, int count);

    /// <summary>What Sinsy wrote while the last open failed.</summary>
    public static string LastOpenError() => Marshal.PtrToStringUTF8(LastOpenErrorPtr()) ?? "";

    /// <summary>What Sinsy wrote during the last call on the handle.</summary>
    public static string Messages(IntPtr handle) => Marshal.PtrToStringUTF8(MessagesPtr(handle)) ?? "";
}

// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Runtime.InteropServices;

namespace Cantoria.Utau;

/// <summary>
/// The C entry point of <c>cantoria_world</c> (<c>native/world/cantoria_world.cpp</c>): WORLD's
/// analysis and synthesis over flat arrays, in one library beside the program, built by
/// <c>tools/build-native.ps1</c>. A spectrogram or aperiodicity is <c>frames</c> rows of
/// <c>fftSize / 2 + 1</c> values, one row after another.
/// </summary>
internal static partial class WorldNative
{
    private const string Library = "cantoria_world";

    [LibraryImport(Library, EntryPoint = "cantoria_world_frames")]
    public static partial int Frames(int fs, int length, double framePeriod);

    [LibraryImport(Library, EntryPoint = "cantoria_world_fft_size")]
    public static partial int FftSize(int fs);

    [LibraryImport(Library, EntryPoint = "cantoria_world_analyze")]
    public static partial int Analyze(double[] x, int length, int fs, double framePeriod, double f0Floor, double f0Ceil, int frames,
        [Out] double[] f0, [Out] double[] sp, [Out] double[] ap);

    [LibraryImport(Library, EntryPoint = "cantoria_world_synthesize")]
    public static partial int Synthesize(double[] f0, int frames, double[] sp, double[] ap, int fftSize, double framePeriod, int fs, int length,
        [Out] double[] y);
}

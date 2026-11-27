// Copyright 2026 Yoshifumi Tsuda
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Runtime.InteropServices;

namespace Cantoria.DiffSinger;

/// <summary>
/// The GPUs DirectML could run DiffSinger's models on: Windows' display adapters (DXGI) that are
/// hardware. Without one, DirectX still offers Microsoft's software adapter (the Basic Render
/// Driver), and DirectML on it is the CPU at a GPU's pace, so <see cref="OnnxProvider.Auto"/> takes
/// the CPU instead; and Download a Voice says so (a voice that sings quickly with no GPU, 2026-11-28).
/// </summary>
public static unsafe class Gpus
{
    /// <summary>The names of the hardware display adapters, as their drivers give them; empty where
    /// there is none, and off Windows (the program runs DiffSinger on the CPU there). Never throws: an
    /// adapter list that cannot be read is no GPU.</summary>
    public static IReadOnlyList<string> Hardware()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }
        try
        {
            return Adapters();
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or SEHException or COMException)
        {
            return [];
        }
    }

    private const uint SoftwareFlag = 2;   // DXGI_ADAPTER_FLAG_SOFTWARE
    /// <summary>Microsoft's vendor id: its adapters are software or stand-ins (the Basic Render Driver, the
    /// Basic Display Adapter of a GPU with no driver, the Remote Display Adapter), none of them a GPU.</summary>
    private const uint Microsoft = 0x1414;
    private const int NotFound = unchecked((int)0x887A0002);   // DXGI_ERROR_NOT_FOUND: past the last adapter

    private static readonly Guid Factory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");   // IDXGIFactory1

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(in Guid riid, out IntPtr factory);

    /// <summary>DXGI_ADAPTER_DESC1.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDesc
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
        public uint Flags;
    }

    private static List<string> Adapters()
    {
        var names = new List<string>();
        if (CreateDXGIFactory1(Factory1, out var factory) < 0 || factory == IntPtr.Zero)
        {
            return names;
        }
        try
        {
            // IDXGIFactory1's table: IUnknown (3), IDXGIObject (4), IDXGIFactory (5), then EnumAdapters1.
            var enumAdapters1 = (delegate* unmanaged[Stdcall]<IntPtr, uint, out IntPtr, int>)Slot(factory, 12);
            for (uint i = 0; ; i++)
            {
                int hr = enumAdapters1(factory, i, out var adapter);
                if (hr == NotFound || hr < 0 || adapter == IntPtr.Zero)
                {
                    break;
                }
                try
                {
                    // IDXGIAdapter1's table: IUnknown (3), IDXGIObject (4), IDXGIAdapter (3), then GetDesc1.
                    var getDesc1 = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Slot(adapter, 10);
                    var buffer = Marshal.AllocHGlobal(Marshal.SizeOf<AdapterDesc>());
                    try
                    {
                        if (getDesc1(adapter, buffer) >= 0)
                        {
                            var desc = Marshal.PtrToStructure<AdapterDesc>(buffer);
                            if ((desc.Flags & SoftwareFlag) == 0 && desc.VendorId != Microsoft)
                            {
                                names.Add(desc.Description.Trim());
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
                finally
                {
                    Marshal.Release(adapter);
                }
            }
        }
        finally
        {
            Marshal.Release(factory);
        }
        return names;
    }

    private static IntPtr Slot(IntPtr comObject, int index) => ((IntPtr*)*(IntPtr*)comObject)[index];
}

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Lukit.Display;

/// <summary>
/// Per-monitor advanced-color state queried through the Win32 <c>DisplayConfig</c> API.
/// </summary>
public readonly record struct AdvancedColorInfo(
    bool HdrEnabled,
    float SdrWhiteNits,
    string DeviceName)
{
    /// <summary>A safe fallback when the display can't be queried.</summary>
    public static AdvancedColorInfo Fallback(string device = "")
        => new(false, 200f, device);
}

/// <summary>
/// Resolves whether HDR (advanced color) is enabled for a given monitor and what SDR
/// white level Windows is currently using for it. The SDR white level is the crucial
/// input for tone mapping: it is the luminance that SDR content's "white" is shown at,
/// and dividing by it is what restores correct contrast to an HDR screenshot.
///
/// The DisplayConfigGetDeviceInfo queries are issued against a manually allocated,
/// generously sized unmanaged buffer rather than typed structs. This avoids fragile
/// struct-marshaling for the (partly deprecated / OS-version-dependent) advanced-color
/// packets and any risk of a native buffer overrun.
/// </summary>
public static class DisplayInfo
{
    private const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;

    private const uint DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
    private const uint DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9;
    private const uint DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL = 11;

    // Documented native sizes of each request packet (header = 20 bytes).
    private const int SIZE_SOURCE_DEVICE_NAME = 84;   // header + WCHAR[32]
    private const int SIZE_ADVANCED_COLOR_INFO = 32;  // header + value + encoding + bpc
    private const int SIZE_SDR_WHITE_LEVEL = 24;      // header + ULONG
    private const int PACKET_BUFFER = 256;            // slack buffer for any packet

    public static AdvancedColorInfo GetForMonitor(IntPtr hmon)
    {
        string device = GetMonitorDeviceName(hmon);
        foreach (var p in EnumerateActivePaths())
        {
            string gdi = GetSourceName(p.sourceInfo.adapterId, p.sourceInfo.id);
            if (!string.Equals(gdi, device, StringComparison.OrdinalIgnoreCase))
                continue;

            bool hdr = QueryHdrEnabled(p.targetInfo.adapterId, p.targetInfo.id);
            float nits = QuerySdrWhiteNits(p.targetInfo.adapterId, p.targetInfo.id);
            return new AdvancedColorInfo(hdr, nits, device);
        }
        return AdvancedColorInfo.Fallback(device);
    }

    public static IReadOnlyList<AdvancedColorInfo> GetAll()
    {
        var list = new List<AdvancedColorInfo>();
        foreach (var p in EnumerateActivePaths())
        {
            string gdi = GetSourceName(p.sourceInfo.adapterId, p.sourceInfo.id);
            bool hdr = QueryHdrEnabled(p.targetInfo.adapterId, p.targetInfo.id);
            float nits = QuerySdrWhiteNits(p.targetInfo.adapterId, p.targetInfo.id);
            list.Add(new AdvancedColorInfo(hdr, nits, gdi));
        }
        return list;
    }

    private static IEnumerable<DISPLAYCONFIG_PATH_INFO> EnumerateActivePaths()
    {
        if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint numPath, out uint numMode) != 0 || numPath == 0)
            yield break;

        // Use raw unmanaged buffers with generous slack rather than [Out] managed struct
        // arrays: QueryDisplayConfig writes at the native element stride, and any mismatch
        // with the managed marshaled size overruns the buffer (a native-heap corruption).
        // The slack absorbs such a mismatch, and we read back at the marshaled stride.
        int pathStride = Marshal.SizeOf<DISPLAYCONFIG_PATH_INFO>();
        int modeStride = Marshal.SizeOf<DISPLAYCONFIG_MODE_INFO>();
        IntPtr pPaths = Marshal.AllocHGlobal((int)numPath * pathStride + 1024);
        IntPtr pModes = Marshal.AllocHGlobal((int)numMode * modeStride + 1024);
        try
        {
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref numPath, pPaths, ref numMode, pModes, IntPtr.Zero) != 0)
                yield break;

            for (int i = 0; i < numPath; i++)
                yield return Marshal.PtrToStructure<DISPLAYCONFIG_PATH_INFO>(pPaths + i * pathStride);
        }
        finally
        {
            Marshal.FreeHGlobal(pPaths);
            Marshal.FreeHGlobal(pModes);
        }
    }

    private static string GetSourceName(LUID adapter, uint id)
    {
        IntPtr buf = Marshal.AllocHGlobal(PACKET_BUFFER);
        try
        {
            if (GetDeviceInfo(DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME, SIZE_SOURCE_DEVICE_NAME, adapter, id, buf) != 0)
                return string.Empty;
            // viewGdiDeviceName: WCHAR[32] at offset 20 (right after the 20-byte header).
            return Marshal.PtrToStringUni(buf + 20) ?? string.Empty;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static bool QueryHdrEnabled(LUID adapter, uint id)
    {
        IntPtr buf = Marshal.AllocHGlobal(PACKET_BUFFER);
        try
        {
            if (GetDeviceInfo(DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO, SIZE_ADVANCED_COLOR_INFO, adapter, id, buf) != 0)
                return false;
            uint value = unchecked((uint)Marshal.ReadInt32(buf, 20));
            // bit 0: advancedColorSupported, bit 1: advancedColorEnabled.
            return (value & 0x2) != 0;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static float QuerySdrWhiteNits(LUID adapter, uint id)
    {
        IntPtr buf = Marshal.AllocHGlobal(PACKET_BUFFER);
        try
        {
            if (GetDeviceInfo(DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL, SIZE_SDR_WHITE_LEVEL, adapter, id, buf) != 0)
                return 200f;
            uint level = unchecked((uint)Marshal.ReadInt32(buf, 20));
            if (level == 0)
                return 200f;
            // Encoding: nits = SDRWhiteLevel / 1000 * 80.
            return level / 1000f * 80f;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    /// <summary>
    /// Fills the request-packet header at the start of <paramref name="buffer"/> and
    /// issues the query. The buffer is <see cref="PACKET_BUFFER"/> bytes (far larger
    /// than any packet) and the OS only writes <paramref name="declaredSize"/> bytes.
    /// </summary>
    private static int GetDeviceInfo(uint type, int declaredSize, LUID adapter, uint id, IntPtr buffer)
    {
        // Zero the buffer, then write the DISPLAYCONFIG_DEVICE_INFO_HEADER.
        Marshal.Copy(new byte[PACKET_BUFFER], 0, buffer, PACKET_BUFFER);
        Marshal.WriteInt32(buffer, 0, (int)type);
        Marshal.WriteInt32(buffer, 4, declaredSize);
        Marshal.WriteInt32(buffer, 8, (int)adapter.LowPart);
        Marshal.WriteInt32(buffer, 12, adapter.HighPart);
        Marshal.WriteInt32(buffer, 16, (int)id);
        return DisplayConfigGetDeviceInfo(buffer);
    }

    private static string GetMonitorDeviceName(IntPtr hmon)
    {
        var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        return GetMonitorInfo(hmon, ref mi) ? mi.szDevice : string.Empty;
    }

    #region P/Invoke

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags; // present since Windows 10; makes this 20 bytes (path stride 72)
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public uint scanLineOrdering;
        public int targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    // Never read; size must match the native DISPLAYCONFIG_MODE_INFO (64 bytes).
    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_MODE_INFO
    {
        public uint infoType;
        public uint id;
        public LUID adapterId;
        public ulong u0, u1, u2, u3, u4, u5;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left, top, right, bottom;
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        IntPtr pathInfoArray,
        ref uint numModeInfoArrayElements,
        IntPtr modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(IntPtr requestPacket);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    #endregion
}

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Lukit.Display;

/// <summary>An enumerated display: its capture handle, bounds, and identity.</summary>
public sealed record MonitorTarget(int Index, IntPtr Handle, Monitors.RECT Bounds, bool IsPrimary, string Device);

/// <summary>Monitor and window lookups needed for the various capture modes.</summary>
public static class Monitors
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private const uint MONITOR_DEFAULTTOPRIMARY = 1;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    /// <summary>Physical-pixel virtual-desktop bounds of the given monitor.</summary>
    public static RECT GetMonitorBounds(IntPtr hmon)
    {
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(hmon, ref mi);
        return mi.rcMonitor;
    }

    public static IntPtr GetPrimaryMonitor()
        => MonitorFromPoint(new POINT { X = 0, Y = 0 }, MONITOR_DEFAULTTOPRIMARY);

    public static IntPtr GetMonitorUnderCursor()
    {
        if (GetCursorPos(out POINT pt))
            return MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
        return GetPrimaryMonitor();
    }

    public static IntPtr GetMonitorForWindow(IntPtr hwnd)
        => MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

    public static IntPtr GetForeground()
        => GetForegroundWindow();

    // --- Enumeration of all monitors ---

    private const uint MONITORINFOF_PRIMARY = 0x1;

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

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    /// <summary>All active monitors, ordered left-to-right then top-to-bottom.</summary>
    public static IReadOnlyList<MonitorTarget> GetAllMonitors()
    {
        var handles = new List<IntPtr>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr h, IntPtr _, ref RECT _, IntPtr _) => { handles.Add(h); return true; },
            IntPtr.Zero);

        var list = new List<MonitorTarget>();
        foreach (IntPtr h in handles)
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (!GetMonitorInfo(h, ref mi))
                continue;
            bool primary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0;
            list.Add(new MonitorTarget(0, h, mi.rcMonitor, primary, mi.szDevice));
        }

        list.Sort((a, b) => a.Bounds.Left != b.Bounds.Left
            ? a.Bounds.Left.CompareTo(b.Bounds.Left)
            : a.Bounds.Top.CompareTo(b.Bounds.Top));

        var result = new List<MonitorTarget>(list.Count);
        for (int i = 0; i < list.Count; i++)
            result.Add(list[i] with { Index = i });
        return result;
    }
}

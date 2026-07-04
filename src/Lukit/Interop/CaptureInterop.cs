using System;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using WinRT;

namespace Lukit.Interop;

/// <summary>
/// Bridges Win32 handles (HMONITOR / HWND) to a WinRT <see cref="GraphicsCaptureItem"/>.
///
/// Ported from Microsoft's WPF ScreenCapture sample (CaptureHelper.cs, MIT), but
/// adapted from the .NET Framework WinRT interop (WindowsRuntimeMarshal /
/// Marshal.GetObjectForIUnknown) to the modern CsWinRT projection used on .NET 5+.
/// </summary>
internal static class CaptureInterop
{
    // IID of Windows.Graphics.Capture.IGraphicsCaptureItem (the item's default interface).
    private static readonly Guid GraphicsCaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
        IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
    }

    private static IGraphicsCaptureItemInterop GetInterop()
    {
        // The activation factory for GraphicsCaptureItem also implements the interop interface.
        IObjectReference factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        return factory.AsInterface<IGraphicsCaptureItemInterop>();
    }

    public static GraphicsCaptureItem CreateForMonitor(IntPtr hmon)
    {
        var interop = GetInterop();
        Guid iid = GraphicsCaptureItemIid;
        IntPtr ptr = interop.CreateForMonitor(hmon, ref iid);
        try { return MarshalInspectable<GraphicsCaptureItem>.FromAbi(ptr); }
        finally { Marshal.Release(ptr); }
    }

    public static GraphicsCaptureItem CreateForWindow(IntPtr hwnd)
    {
        var interop = GetInterop();
        Guid iid = GraphicsCaptureItemIid;
        IntPtr ptr = interop.CreateForWindow(hwnd, ref iid);
        try { return MarshalInspectable<GraphicsCaptureItem>.FromAbi(ptr); }
        finally { Marshal.Release(ptr); }
    }
}

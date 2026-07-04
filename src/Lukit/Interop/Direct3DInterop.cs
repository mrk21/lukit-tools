using System;
using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;
using Vortice.Direct3D11;
using Vortice.DXGI;
using WinRT;

namespace Lukit.Interop;

/// <summary>
/// Interop between Vortice (managed Direct3D 11) objects and the WinRT
/// <c>Windows.Graphics.DirectX.Direct3D11</c> types that the Graphics Capture API
/// consumes and produces.
/// </summary>
internal static class Direct3DInterop
{
    // IID of ID3D11Texture2D.
    private static readonly Guid ID3D11Texture2DIid = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface([In] ref Guid iid);
    }

    [DllImport("d3d11.dll", ExactSpelling = true, PreserveSig = false)]
    private static extern void CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    /// <summary>
    /// Wraps a Vortice/DXGI device as the WinRT IDirect3DDevice the capture API needs.
    /// </summary>
    public static IDirect3DDevice CreateWinRtDevice(ID3D11Device d3dDevice)
    {
        using IDXGIDevice dxgi = d3dDevice.QueryInterface<IDXGIDevice>();
        CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out IntPtr pUnknown);
        try
        {
            return MarshalInspectable<IDirect3DDevice>.FromAbi(pUnknown);
        }
        finally
        {
            Marshal.Release(pUnknown);
        }
    }

    /// <summary>
    /// Retrieves the underlying ID3D11Texture2D from a WinRT capture surface.
    /// The returned texture owns a reference and must be disposed by the caller.
    /// </summary>
    public static ID3D11Texture2D GetTexture(IDirect3DSurface surface)
    {
        var access = surface.As<IDirect3DDxgiInterfaceAccess>();
        Guid iid = ID3D11Texture2DIid;
        IntPtr texPtr = access.GetInterface(ref iid);
        // The Vortice wrapper takes ownership of the reference returned by GetInterface.
        return new ID3D11Texture2D(texPtr);
    }
}

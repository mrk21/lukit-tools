using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Lukit.Interop;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using static Vortice.Direct3D11.D3D11;

namespace Lukit.Capture;

/// <summary>
/// Owns a Direct3D 11 device and turns a <see cref="GraphicsCaptureItem"/> (monitor
/// or window) into a single <see cref="HdrFrame"/> captured in 16-bit float scRGB.
///
/// Requesting the <see cref="DirectXPixelFormat.R16G16B16A16Float"/> frame format is
/// what makes HDR capture correct: it preserves the full scRGB range (values &gt; 1.0
/// for content brighter than 80 nits, and out-of-sRGB-gamut colors) instead of the
/// clipped 8-bit representation that leaves HDR screenshots washed out.
/// </summary>
public sealed class CaptureEngine : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDirect3DDevice _winrtDevice;

    public CaptureEngine()
    {
        FeatureLevel[] levels = { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };

        Result result = D3D11CreateDevice(
            adapter: null,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            levels,
            out _device!);

        if (result.Failure || _device is null)
        {
            // Fall back to the WARP software renderer if no suitable GPU device is available.
            D3D11CreateDevice(
                adapter: null,
                DriverType.Warp,
                DeviceCreationFlags.BgraSupport,
                levels,
                out _device!).CheckError();
        }

        _context = _device.ImmediateContext;
        _winrtDevice = Direct3DInterop.CreateWinRtDevice(_device);
    }

    /// <summary>
    /// Captures a single frame from the given item. Runs to completion off the caller's
    /// thread; the caller may block on the returned task.
    /// </summary>
    public Task<HdrFrame> CaptureAsync(GraphicsCaptureItem item, bool includeCursor, CancellationToken ct = default)
    {
        var size = item.Size;

        Direct3D11CaptureFramePool framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _winrtDevice,
            DirectXPixelFormat.R16G16B16A16Float,
            numberOfBuffers: 1,
            size);

        GraphicsCaptureSession session = framePool.CreateCaptureSession(item);

        // Not every OS build exposes these knobs; ignore if unavailable.
        TrySet(() => session.IsCursorCaptureEnabled = includeCursor);
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            TrySet(() => session.IsBorderRequired = false);

        var tcs = new TaskCompletionSource<HdrFrame>(TaskCreationOptions.RunContinuationsAsynchronously);

        TypedEventHandler<Direct3D11CaptureFramePool, object> handler = (pool, _) =>
        {
            try
            {
                using Direct3D11CaptureFrame? frame = pool.TryGetNextFrame();
                if (frame is null) return;
                tcs.TrySetResult(ReadFrame(frame));
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        };

        framePool.FrameArrived += handler;
        session.StartCapture();

        return AwaitAndCleanup(tcs, framePool, session, handler, ct);
    }

    private static async Task<HdrFrame> AwaitAndCleanup(
        TaskCompletionSource<HdrFrame> tcs,
        Direct3D11CaptureFramePool framePool,
        GraphicsCaptureSession session,
        TypedEventHandler<Direct3D11CaptureFramePool, object> handler,
        CancellationToken ct)
    {
        try
        {
            return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
        }
        finally
        {
            framePool.FrameArrived -= handler;
            session.Dispose();
            framePool.Dispose();
        }
    }

    private HdrFrame ReadFrame(Direct3D11CaptureFrame frame)
    {
        var content = frame.ContentSize;
        using ID3D11Texture2D srcTex = Direct3DInterop.GetTexture(frame.Surface);
        Texture2DDescription desc = srcTex.Description;

        int w = Math.Clamp(content.Width, 1, (int)desc.Width);
        int h = Math.Clamp(content.Height, 1, (int)desc.Height);

        var stagingDesc = new Texture2DDescription
        {
            Width = desc.Width,
            Height = desc.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Vortice.DXGI.Format.R16G16B16A16_Float,
            SampleDescription = new Vortice.DXGI.SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None,
        };

        using ID3D11Texture2D staging = _device.CreateTexture2D(stagingDesc);
        _context.CopyResource(staging, srcTex);

        MappedSubresource map = _context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var rgb = new float[(long)w * h * 3];
            unsafe
            {
                byte* basePtr = (byte*)map.DataPointer;
                int rowPitch = (int)map.RowPitch;
                for (int y = 0; y < h; y++)
                {
                    Half* row = (Half*)(basePtr + (long)y * rowPitch);
                    int di = y * w * 3;
                    for (int x = 0; x < w; x++)
                    {
                        Half* px = row + (long)x * 4;
                        rgb[di++] = (float)px[0];
                        rgb[di++] = (float)px[1];
                        rgb[di++] = (float)px[2];
                    }
                }
            }
            return new HdrFrame(w, h, rgb);
        }
        finally
        {
            _context.Unmap(staging, 0);
        }
    }

    private static void TrySet(Action action)
    {
        try { action(); } catch { /* property not supported on this OS build */ }
    }

    public void Dispose()
    {
        _winrtDevice?.Dispose();
        _context?.Dispose();
        _device?.Dispose();
    }
}

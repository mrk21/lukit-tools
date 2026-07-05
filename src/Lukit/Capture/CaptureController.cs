using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Lukit.Display;
using Lukit.Imaging;
using Lukit.Interop;
using Lukit.Settings;

namespace Lukit.Capture;

/// <summary>
/// Orchestrates a capture end-to-end for every mode: pick target, detect the display's
/// SDR white level, capture in HDR, tone-map with the user's settings, then save and/or
/// copy to the clipboard. Shared by the tray UI, global hotkeys, and the CLI.
/// </summary>
public sealed class CaptureController : IDisposable
{
    private readonly AppSettings _settings;
    private CaptureEngine? _engine;

    /// <summary>Raised with a short status message after a capture (for tray notifications).</summary>
    public event Action<string, bool>? Notify; // (message, isError)

    public CaptureController(AppSettings settings) => _settings = settings;

    private CaptureEngine Engine => _engine ??= new CaptureEngine();

    public Task CaptureFullscreenAsync()
        => CaptureMonitorAsync(Monitors.GetMonitorUnderCursor(), crop: null);

    /// <summary>
    /// Region capture: grab the monitor under the cursor in HDR, tone-map it, show the
    /// frozen result as a selection overlay, then crop the user's rectangle.
    /// </summary>
    public async Task CaptureRegionAsync()
    {
        try
        {
            IntPtr hmon = Monitors.GetMonitorUnderCursor();
            Monitors.RECT bounds = Monitors.GetMonitorBounds(hmon);
            AdvancedColorInfo color = DisplayInfo.GetForMonitor(hmon);
            var item = CaptureInterop.CreateForMonitor(hmon);
            HdrFrame frame = await Engine.CaptureAsync(item, _settings.IncludeCursor).ConfigureAwait(false);

            ToneMapSettings ts = _settings.ToToneMapSettings(color.SdrWhiteNits);
            BitmapSource full = await Task.Run(() =>
            {
                byte[] bgra = ToneMapper.ToBgra32(frame, ts, out int stride);
                return ImageOutput.CreateBitmap(bgra, frame.Width, frame.Height, stride);
            }).ConfigureAwait(false);

            Int32Rect? sel = await ShowSelectionAsync(full, bounds).ConfigureAwait(false);
            if (sel is not { } rect || rect.Width < 1 || rect.Height < 1)
                return; // cancelled

            var cropped = new CroppedBitmap(full, rect);
            cropped.Freeze();
            await OutputAsync(cropped, "region").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Notify?.Invoke("Capture failed: " + ex.Message, true);
        }
    }

    private static Task<Int32Rect?> ShowSelectionAsync(BitmapSource image, Monitors.RECT bounds)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
            return Task.FromResult<Int32Rect?>(null);

        return dispatcher.InvokeAsync(() =>
        {
            var overlay = new UI.SelectionOverlay(image, bounds);
            bool ok = overlay.ShowDialog() == true;
            return ok ? overlay.SelectedPixelRect : (Int32Rect?)null;
        }).Task;
    }

    public async Task CaptureMonitorAsync(IntPtr hmon, (int x, int y, int w, int h)? crop)
    {
        try
        {
            AdvancedColorInfo color = DisplayInfo.GetForMonitor(hmon);
            var item = CaptureInterop.CreateForMonitor(hmon);
            HdrFrame frame = await Engine.CaptureAsync(item, _settings.IncludeCursor).ConfigureAwait(false);
            if (crop is { } c)
                frame = frame.Crop(c.x, c.y, c.w, c.h);
            await FinishAsync(frame, color.SdrWhiteNits, "screen").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Notify?.Invoke("Capture failed: " + ex.Message, true);
        }
    }

    /// <summary>
    /// Captures every monitor and composites them into a single image at their
    /// virtual-desktop positions. Each monitor is tone-mapped with its own detected
    /// SDR white level (they can differ), so mixed HDR/SDR setups come out correct.
    /// </summary>
    public async Task CaptureAllMonitorsAsync()
    {
        try
        {
            var monitors = Monitors.GetAllMonitors();
            if (monitors.Count == 0)
            {
                Notify?.Invoke("No monitors found", true);
                return;
            }
            if (monitors.Count == 1)
            {
                await CaptureMonitorAsync(monitors[0].Handle, crop: null).ConfigureAwait(false);
                return;
            }

            // Capture + tone-map each monitor (environment-dependent), then hand the tiles
            // to the pure compositor for the virtual-desktop layout (see DesktopComposite).
            var tiles = new List<MonitorTile>(monitors.Count);
            foreach (var m in monitors)
            {
                AdvancedColorInfo color = DisplayInfo.GetForMonitor(m.Handle);
                var item = CaptureInterop.CreateForMonitor(m.Handle);
                HdrFrame frame = await Engine.CaptureAsync(item, _settings.IncludeCursor).ConfigureAwait(false);
                ToneMapSettings ts = _settings.ToToneMapSettings(color.SdrWhiteNits);
                byte[] bgra = await Task.Run(() => ToneMapper.ToBgra32(frame, ts, out _)).ConfigureAwait(false);
                tiles.Add(new MonitorTile(m.Bounds, bgra, frame.Width, frame.Height));
            }

            ComposedImage composed = DesktopComposite.Compose(tiles);
            BitmapSource bmp = ImageOutput.CreateBitmap(composed.Bgra, composed.Width, composed.Height, composed.Stride);
            await OutputAsync(bmp, "all").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Notify?.Invoke("Capture failed: " + ex.Message, true);
        }
    }

    public async Task CaptureWindowAsync(IntPtr hwnd)
    {
        try
        {
            if (hwnd == IntPtr.Zero)
            {
                Notify?.Invoke("No window to capture", true);
                return;
            }
            IntPtr hmon = Monitors.GetMonitorForWindow(hwnd);
            AdvancedColorInfo color = DisplayInfo.GetForMonitor(hmon);
            var item = CaptureInterop.CreateForWindow(hwnd);
            HdrFrame frame = await Engine.CaptureAsync(item, _settings.IncludeCursor).ConfigureAwait(false);
            await FinishAsync(frame, color.SdrWhiteNits, "window").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Notify?.Invoke("Capture failed: " + ex.Message, true);
        }
    }

    private async Task FinishAsync(HdrFrame frame, float detectedSdrWhite, string kind)
    {
        ToneMapSettings ts = _settings.ToToneMapSettings(detectedSdrWhite);

        // Tone-mapping is CPU-heavy; keep it off the UI thread.
        BitmapSource bmp = await Task.Run(() =>
        {
            byte[] bgra = ToneMapper.ToBgra32(frame, ts, out int stride);
            return ImageOutput.CreateBitmap(bgra, frame.Width, frame.Height, stride); // frozen, cross-thread safe
        }).ConfigureAwait(false);

        await OutputAsync(bmp, kind).ConfigureAwait(false);
    }

    private async Task OutputAsync(BitmapSource bmp, string kind)
    {
        string? savedPath = null;
        if (_settings.SaveToFile)
        {
            savedPath = BuildPath(kind);
            ImageOutput.SavePng(bmp, savedPath);
        }

        if (_settings.CopyToClipboard)
            await OnUiAsync(() => ImageOutput.CopyToClipboard(bmp)).ConfigureAwait(false);

        string msg = savedPath is not null
            ? $"Saved {Path.GetFileName(savedPath)}" + (_settings.CopyToClipboard ? " • copied" : "")
            : "Copied to clipboard";
        Notify?.Invoke(msg, false);
    }

    private string BuildPath(string kind)
    {
        string dir = _settings.ResolvedSaveFolder;
        Directory.CreateDirectory(dir);
        string name = $"Lukit_{DateTime.Now:yyyyMMdd_HHmmss}_{kind}.png";
        return Path.Combine(dir, name);
    }

    private static Task OnUiAsync(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }
        return dispatcher.InvokeAsync(action).Task;
    }

    public void Dispose() => _engine?.Dispose();
}

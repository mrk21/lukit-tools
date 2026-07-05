using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Lukit.Display;
using Lukit.Imaging;
using Lukit.Settings;

namespace Lukit.UI;

/// <summary>
/// Renders WPF UI surfaces (the settings window, the selection overlay, …) to PNG
/// files off-screen — without showing anything to the user and without touching the
/// tray or the single-instance mutex. This is the "screenshot the app's own UI" half
/// of the visual-check loop; the CLI --shot-* flags cover the captured-image half.
///
/// To add a surface, extend <see cref="Build"/> only — the render/save machinery is
/// surface-agnostic, so the loop keeps working as the UI grows.
/// </summary>
public static class UiShot
{
    /// <summary>Renders the named surface to <paramref name="outPath"/> as PNG. Returns process exit code.</summary>
    public static int Capture(string surface, string outPath)
    {
        // A hidden Application makes default control templates and system brushes resolve
        // exactly as they do in the real tray app. Never call Run(); we drive the
        // dispatcher by hand and tear the window down synchronously.
        if (Application.Current is null)
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        Window window = Build(surface);

        // Show far off any real monitor so layout + Loaded handlers run and the visual
        // tree is attached to a presentation source (required for RenderTargetBitmap),
        // while never flashing onto the user's screen or stealing focus.
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = OffScreen;
        window.Top = OffScreen;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Show();

        // Pump twice: once to run Loaded / SizeToContent, once to settle the final layout.
        window.UpdateLayout();
        Pump();
        window.UpdateLayout();
        Pump();

        BitmapSource shot = RenderContent(window);
        string full = Path.GetFullPath(outPath);
        ImageOutput.SavePng(shot, full);
        window.Close();

        Console.WriteLine($"OK ui[{surface}] {shot.PixelWidth}x{shot.PixelHeight} -> {full}");
        return 0;
    }

    /// <summary>Well off the left edge of any plausible monitor arrangement.</summary>
    private const int OffScreen = -32000;

    private static Window Build(string surface) => surface.ToLowerInvariant() switch
    {
        "settings" => BuildSettings(),
        "overlay" => BuildOverlay(),
        _ => throw new ArgumentException(
            $"Unknown UI surface '{surface}'. Known surfaces: settings, overlay."),
    };

    private static Window BuildSettings()
    {
        var settings = AppSettings.Load();
        Localization.Strings.Apply(settings.Language); // render in the saved UI language
        return new SettingsWindow(settings);
    }

    private static Window BuildOverlay()
    {
        // Stand in for the frozen, already-tone-mapped screenshot the overlay dims. The
        // bounds keep the window off-screen at a representative size so SelectionOverlay's
        // own SetWindowPos can't drag it onto a real monitor.
        BitmapSource frozen = PlaceholderShot(320, 200);
        var bounds = new Monitors.RECT
        {
            Left = OffScreen,
            Top = OffScreen,
            Right = OffScreen + 1280,
            Bottom = OffScreen + 800,
        };
        return new SelectionOverlay(frozen, bounds);
    }

    private static BitmapSource RenderContent(Window window)
    {
        var root = (FrameworkElement)window.Content;

        // Include the content root's own margin: rendering a visual standalone drops its
        // parent-applied offset, which otherwise shaves the padding and clips the last
        // row (e.g. the Save/Cancel buttons on a SizeToContent window).
        Thickness margin = root.Margin;
        double contentW = root.ActualWidth;
        double contentH = root.ActualHeight;
        int w = Math.Max(1, (int)Math.Ceiling(contentW + margin.Left + margin.Right));
        int h = Math.Max(1, (int)Math.Ceiling(contentH + margin.Top + margin.Bottom));

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            // Content roots are usually transparent, so paint the window background first.
            dc.DrawRectangle(window.Background ?? SystemColors.WindowBrush, null, new Rect(0, 0, w, h));
            // Then the live content, translated back to its intended margin position.
            var brush = new VisualBrush(root) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };
            dc.DrawRectangle(brush, null, new Rect(margin.Left, margin.Top, contentW, contentH));
        }
        rtb.Render(visual);

        rtb.Freeze();
        return rtb;
    }

    /// <summary>Processes queued dispatcher work (layout, Loaded handlers) synchronously.</summary>
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    /// <summary>
    /// A deterministic stand-in for a captured screenshot: a diagonal gradient with a
    /// block of blown-out highlights, so the overlay's dimming and preview are visible.
    /// </summary>
    private static BitmapSource PlaceholderShot(int w, int h)
    {
        int stride = w * 4;
        var px = new byte[h * stride];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * stride + x * 4;
                byte r = (byte)(255 * x / (w - 1));
                byte g = (byte)(255 * y / (h - 1));
                byte b = 128;
                // A checkerboard of highlights in one quadrant to mimic bright regions.
                if (x > w / 2 && y < h / 2 && ((x / 40 + y / 40) & 1) == 0)
                    r = g = b = 255;
                px[i + 0] = b;
                px[i + 1] = g;
                px[i + 2] = r;
                px[i + 3] = 255;
            }
        }
        BitmapSource bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, palette: null, px, stride);
        bmp.Freeze();
        return bmp;
    }
}

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Lukit.Capture;
using Lukit.Display;
using Lukit.Imaging;
using Lukit.Interop;

namespace Lukit;

internal static class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);
    private const int ATTACH_PARENT_PROCESS = -1;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int nStdHandle);
    private const int STD_OUTPUT_HANDLE = -11;

    // Session-local name (no "Global\") → one tray instance per user session.
    // A fixed GUID keeps it stable and collision-free with unrelated apps.
    private const string SingleInstanceMutexName = "Lukit_SingleInstance_9C6B4D2E7A834F1B9E5A1D2C3B4A5F60";

    /// <summary>
    /// Attach to the launching console so Console.Write is visible — but only when
    /// stdout hasn't already been redirected (e.g. to a file/pipe), otherwise we'd
    /// steal output away from the redirection.
    /// </summary>
    private static void EnsureConsole()
    {
        IntPtr h = GetStdHandle(STD_OUTPUT_HANDLE);
        if (h == IntPtr.Zero)
            AttachConsole(ATTACH_PARENT_PROCESS);
    }

    [STAThread]
    private static int Main(string[] args)
    {
        // Optional CLI utilities; with no arguments the tray-resident GUI launches.
        if (args.Length >= 1)
        {
            switch (args[0])
            {
                case "--shot-fullscreen": EnsureConsole(); return RunCliFullscreen(args);
                case "--shot-window": EnsureConsole(); return RunCliWindow(args);
                case "--shot-monitor": EnsureConsole(); return RunCliMonitor(args);
                case "--shot-all": EnsureConsole(); return RunCliAll(args);
                case "--shot-ui": EnsureConsole(); return RunShotUi(args);
                case "--display-info": EnsureConsole(); return RunDisplayInfo();
                case "--frame-stats": EnsureConsole(); return RunFrameStats();
                case "--help" or "-h" or "/?": EnsureConsole(); PrintUsage(); return 0;
            }
        }

        // Default: launch the tray-resident GUI.
        return RunGui();
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            "Lukit Tools — HDR-correct screenshots\n\n" +
            "  (no args)            Launch the tray application\n" +
            "  --shot-fullscreen <out.png> [--sdr-white <nits>] [--op clip|reinhard|aces]\n" +
            "  --shot-monitor <index> <out.png>   Capture a specific display (see --display-info order)\n" +
            "  --shot-all <out.png>               Capture all displays combined\n" +
            "  --shot-window <out.png> [--hwnd <handle>]\n" +
            "  --shot-ui <settings|overlay> <out.png>   Render an app UI surface to PNG (off-screen)\n" +
            "  --display-info       Show HDR state and SDR white level per monitor\n" +
            "  --frame-stats        Capture the primary monitor and print scRGB statistics");
    }

    private static int RunCliAll(string[] args)
    {
        string outPath = args.Length >= 2 && !args[1].StartsWith("--") ? Path.GetFullPath(args[1]) : Path.GetFullPath("all-displays.png");
        var settings = new Settings.AppSettings
        {
            SaveFolder = Path.GetDirectoryName(outPath)!,
            CopyToClipboard = false,
            SaveToFile = true,
            ShowNotifications = false,
        };
        // Route through the controller but capture the composited bitmap to our exact path.
        using var controller = new Capture.CaptureController(settings);
        string? note = null;
        bool err = false;
        controller.Notify += (m, e) => { note = m; err = e; };
        controller.CaptureAllMonitorsAsync().GetAwaiter().GetResult();

        string dir = settings.ResolvedSaveFolder;
        string[] files = Directory.Exists(dir) ? Directory.GetFiles(dir, "Lukit_*_all.png") : Array.Empty<string>();
        Console.WriteLine($"note=\"{note}\" isError={err} composited files={files.Length}");
        foreach (var f in files) Console.WriteLine("  " + f);
        return (!err && files.Length > 0) ? 0 : 1;
    }

    private static int RunCliMonitor(string[] args)
    {
        int index = 0;
        string outPath = "monitor.png";
        if (args.Length >= 2) int.TryParse(args[1], out index);
        if (args.Length >= 3) outPath = args[2];

        try
        {
            var monitors = Monitors.GetAllMonitors();
            Console.WriteLine($"Monitors: {monitors.Count}");
            foreach (var m in monitors)
                Console.WriteLine($"  [{m.Index}] {m.Device} {m.Bounds.Width}x{m.Bounds.Height} @({m.Bounds.Left},{m.Bounds.Top}){(m.IsPrimary ? " primary" : "")}");
            if (index < 0 || index >= monitors.Count) { Console.Error.WriteLine("bad index"); return 1; }

            MonitorTarget target = monitors[index];
            AdvancedColorInfo color = DisplayInfo.GetForMonitor(target.Handle);
            var item = CaptureInterop.CreateForMonitor(target.Handle);
            using var engine = new CaptureEngine();
            HdrFrame frame = engine.CaptureAsync(item, includeCursor: false).GetAwaiter().GetResult();

            var settings = new ToneMapSettings { SdrWhiteNits = color.SdrWhiteNits, Operator = ToneMapOperator.Reinhard };
            byte[] bgra = ToneMapper.ToBgra32(frame, settings, out int stride);
            var bmp = ImageOutput.CreateBitmap(bgra, frame.Width, frame.Height, stride);
            string full = Path.GetFullPath(outPath);
            ImageOutput.SavePng(bmp, full);

            Console.WriteLine($"OK monitor[{index}] {frame.Width}x{frame.Height} HDR={color.HdrEnabled} sdrWhite={color.SdrWhiteNits:0.#} -> {full}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("monitor capture failed: " + ex.Message);
            return 1;
        }
    }

    private static int RunCliWindow(string[] args)
    {
        string outPath = args.Length >= 2 && !args[1].StartsWith("--") ? args[1] : "window.png";
        IntPtr hwnd = Monitors.GetForeground();
        for (int i = 1; i < args.Length - 1; i++)
            if (args[i] == "--hwnd" && long.TryParse(args[i + 1], out long h)) hwnd = new IntPtr(h);

        try
        {
            if (hwnd == IntPtr.Zero) { Console.Error.WriteLine("No target window."); return 1; }

            string full = Path.GetFullPath(outPath);
            IntPtr hmon = Monitors.GetMonitorForWindow(hwnd);
            AdvancedColorInfo color = DisplayInfo.GetForMonitor(hmon);

            var item = CaptureInterop.CreateForWindow(hwnd);
            using var engine = new CaptureEngine();
            HdrFrame frame = engine.CaptureAsync(item, includeCursor: false).GetAwaiter().GetResult();

            var settings = new ToneMapSettings { SdrWhiteNits = color.SdrWhiteNits, Operator = ToneMapOperator.Reinhard };
            byte[] bgra = ToneMapper.ToBgra32(frame, settings, out int stride);
            var bmp = ImageOutput.CreateBitmap(bgra, frame.Width, frame.Height, stride);
            ImageOutput.SavePng(bmp, full);

            Console.WriteLine($"OK window {frame.Width}x{frame.Height} HDR={color.HdrEnabled} sdrWhite={color.SdrWhiteNits:0.#} -> {full}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("window capture failed: " + ex.Message);
            return 1;
        }
    }

    private static int RunShotUi(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: --shot-ui <settings|overlay> <out.png>");
            return 1;
        }

        try
        {
            return UI.UiShot.Capture(args[1], args[2]);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ui shot failed: " + ex.Message);
            return 1;
        }
    }

    private static int RunGui()
    {
        // Single-instance guard for the tray-resident GUI. The CLI utilities above
        // return before reaching here, so diagnostics (--display-info etc.) stay
        // runnable even while an instance is resident. Hold the mutex for the whole
        // process lifetime; the OS releases the handle on exit.
        using var singleInstance = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            // No main window to focus (tray-only) — just tell the user where it is.
            System.Windows.MessageBox.Show(
                Localization.Strings.AlreadyRunning,
                Localization.Strings.AppShortName, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return 0;
        }

        var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        using var tray = new UI.TrayApp(app);
        app.Run();
        return 0;
    }

    private static int RunFrameStats()
    {
        try
        {
            IntPtr hmon = Monitors.GetPrimaryMonitor();
            AdvancedColorInfo color = DisplayInfo.GetForMonitor(hmon);
            var item = CaptureInterop.CreateForMonitor(hmon);
            using var engine = new CaptureEngine();
            HdrFrame f = engine.CaptureAsync(item, includeCursor: false).GetAwaiter().GetResult();

            int n = f.Width * f.Height;
            var lum = new float[n];
            float maxCh = 0f;
            for (int i = 0, p = 0; i < n; i++, p += 3)
            {
                float r = f.Rgb[p], g = f.Rgb[p + 1], b = f.Rgb[p + 2];
                if (r > maxCh) maxCh = r; if (g > maxCh) maxCh = g; if (b > maxCh) maxCh = b;
                lum[i] = 0.2126f * r + 0.7152f * g + 0.0722f * b;
            }
            Array.Sort(lum);
            float Pct(float q) => lum[Math.Clamp((int)(q * (n - 1)), 0, n - 1)];
            int over1 = 0;
            foreach (var l in lum) if (l > 1f) over1++;

            Console.WriteLine($"HDR={color.HdrEnabled} detectedSDRwhite={color.SdrWhiteNits:0.#} nits (scRGB {color.SdrWhiteNits / 80f:0.##})");
            Console.WriteLine($"scRGB luminance  p50={Pct(0.50f):0.###}  p90={Pct(0.90f):0.###}  p99={Pct(0.99f):0.###}  p99.9={Pct(0.999f):0.###}  max={lum[n - 1]:0.###}");
            Console.WriteLine($"max channel={maxCh:0.###}  pixels>1.0(80nit): {100.0 * over1 / n:0.##}%   => in nits: p99={Pct(0.99f) * 80f:0.#}  max={lum[n - 1] * 80f:0.#}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("frame-stats failed: " + ex);
            return 1;
        }
    }

    private static int RunDisplayInfo()
    {
        try
        {
            var all = DisplayInfo.GetAll();
            Console.WriteLine($"Active displays: {all.Count}");
            foreach (var d in all)
                Console.WriteLine($"  {d.DeviceName}  HDR={(d.HdrEnabled ? "ON " : "off")}  SDRwhite={d.SdrWhiteNits:0.#} nits");

            var primary = DisplayInfo.GetForMonitor(Monitors.GetPrimaryMonitor());
            Console.WriteLine($"Primary: HDR={(primary.HdrEnabled ? "ON" : "off")}  SDRwhite={primary.SdrWhiteNits:0.#} nits");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("display-info failed: " + ex);
            return 1;
        }
    }

    private static int RunCliFullscreen(string[] args)
    {
        string outPath = args.Length >= 2 && !args[1].StartsWith("--") ? args[1] : "capture.png";
        float? sdrWhiteOverride = null;
        var op = ToneMapOperator.Reinhard;

        for (int i = 1; i < args.Length - 1; i++)
        {
            if (args[i] == "--sdr-white" && float.TryParse(args[i + 1], out float v)) sdrWhiteOverride = v;
            if (args[i] == "--op") op = ParseOperator(args[i + 1]);
        }

        try
        {
            string full = Path.GetFullPath(outPath);

            IntPtr hmon = Monitors.GetPrimaryMonitor();
            AdvancedColorInfo color = DisplayInfo.GetForMonitor(hmon);
            float sdrWhite = sdrWhiteOverride ?? color.SdrWhiteNits;

            var item = CaptureInterop.CreateForMonitor(hmon);

            using var engine = new CaptureEngine();
            HdrFrame frame = engine.CaptureAsync(item, includeCursor: false).GetAwaiter().GetResult();

            var settings = new ToneMapSettings { SdrWhiteNits = sdrWhite, Operator = op };
            byte[] bgra = ToneMapper.ToBgra32(frame, settings, out int stride);
            var bmp = ImageOutput.CreateBitmap(bgra, frame.Width, frame.Height, stride);
            ImageOutput.SavePng(bmp, full);

            Console.WriteLine($"OK {frame.Width}x{frame.Height} HDR={(color.HdrEnabled ? "ON" : "off")} " +
                $"sdrWhite={sdrWhite:0.#}{(sdrWhiteOverride is null ? "(auto)" : "(manual)")} op={op} -> {full}");
            return 0;
        }
        catch (Exception ex)
        {
            string log = Path.Combine(Path.GetTempPath(), "Lukit-error.txt");
            File.WriteAllText(log, ex.ToString());
            Console.Error.WriteLine("Capture failed: " + ex.Message);
            Console.Error.WriteLine("Details: " + log);
            return 1;
        }
    }

    private static ToneMapOperator ParseOperator(string s) => s.ToLowerInvariant() switch
    {
        "clip" => ToneMapOperator.Clip,
        "aces" => ToneMapOperator.AcesFilmic,
        _ => ToneMapOperator.Reinhard,
    };
}

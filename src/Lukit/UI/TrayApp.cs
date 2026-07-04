using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Lukit.Capture;
using Lukit.Display;
using Lukit.Interop;
using Lukit.Settings;
using WF = System.Windows.Forms;

namespace Lukit.UI;

/// <summary>
/// The tray-resident application: a notify icon with a context menu, global hotkeys,
/// and capture orchestration. Created and owned on the WPF UI (STA) thread.
/// </summary>
public sealed class TrayApp : IDisposable
{
    private readonly Application _app;
    private readonly AppSettings _settings;
    private readonly CaptureController _controller;
    private readonly WF.NotifyIcon _tray;
    private HotkeyManager? _hotkeys;

    public TrayApp(Application app)
    {
        _app = app;
        _settings = AppSettings.Load();
        _controller = new CaptureController(_settings);
        _controller.Notify += OnNotify;

        _tray = new WF.NotifyIcon
        {
            Icon = TrayIconFactory.Create(),
            Text = "Lukit Tools — HDR-correct screenshots",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        _tray.DoubleClick += (_, _) => Fire(_controller.CaptureFullscreenAsync);

        RegisterHotkeys();
    }

    private WF.ContextMenuStrip BuildMenu()
    {
        var menu = new WF.ContextMenuStrip();
        menu.Items.Add($"Capture full screen   ({_settings.HotkeyFullscreen})", null, (_, _) => Fire(_controller.CaptureFullscreenAsync));
        menu.Items.Add($"Capture region   ({_settings.HotkeyRegion})", null, (_, _) => Fire(CaptureRegionAsync));
        menu.Items.Add($"Capture window   ({_settings.HotkeyWindow})", null, (_, _) => Fire(CaptureWindowAsync));

        var displayMenu = new WF.ToolStripMenuItem("Capture specific display");
        displayMenu.DropDownItems.Add("(loading…)");
        displayMenu.DropDownOpening += (_, _) => PopulateDisplayMenu(displayMenu);
        menu.Items.Add(displayMenu);

        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add("Open save folder", null, (_, _) => OpenSaveFolder());
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => _app.Shutdown());
        return menu;
    }

    // Rebuilt each time the submenu opens so monitor hot-plug / layout changes are reflected.
    private void PopulateDisplayMenu(WF.ToolStripMenuItem parent)
    {
        parent.DropDownItems.Clear();
        var monitors = Monitors.GetAllMonitors();
        foreach (var m in monitors)
        {
            IntPtr handle = m.Handle;
            string label = $"Display {m.Index + 1}{(m.IsPrimary ? " (primary)" : "")}  —  {m.Bounds.Width}×{m.Bounds.Height}";
            parent.DropDownItems.Add(label, null, (_, _) => Fire(() => _controller.CaptureMonitorAsync(handle, null)));
        }
        if (monitors.Count > 1)
        {
            parent.DropDownItems.Add(new WF.ToolStripSeparator());
            parent.DropDownItems.Add("All displays (combined)", null, (_, _) => Fire(_controller.CaptureAllMonitorsAsync));
        }
    }

    private void RegisterHotkeys()
    {
        _hotkeys = new HotkeyManager();
        TryRegister(_settings.HotkeyFullscreen, () => Fire(_controller.CaptureFullscreenAsync));
        TryRegister(_settings.HotkeyRegion, () => Fire(CaptureRegionAsync));
        TryRegister(_settings.HotkeyWindow, () => Fire(CaptureWindowAsync));
    }

    private void TryRegister(string spec, Action action)
    {
        if (!_hotkeys!.Register(spec, action))
            OnNotify($"Hotkey '{spec}' is unavailable (already in use?)", true);
    }

    private Task CaptureRegionAsync() => _controller.CaptureRegionAsync();

    // For hotkey-triggered window capture, the foreground window is the user's target.
    private Task CaptureWindowAsync() => _controller.CaptureWindowAsync(Monitors.GetForeground());

    private void Fire(Func<Task> operation) => _ = RunSafe(operation);

    private async Task RunSafe(Func<Task> operation)
    {
        try
        {
            // Small delay so a closing menu / keypress UI doesn't end up in the shot.
            await Task.Delay(150).ConfigureAwait(false);
            await operation().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            OnNotify("Error: " + ex.Message, true);
        }
    }

    private void OnNotify(string message, bool isError)
    {
        if (!isError && !_settings.ShowNotifications)
            return;

        _app.Dispatcher.Invoke(() =>
            _tray.ShowBalloonTip(
                2500,
                "Lukit Tools",
                message,
                isError ? WF.ToolTipIcon.Error : WF.ToolTipIcon.Info));
    }

    private void OpenSaveFolder()
    {
        string dir = _settings.ResolvedSaveFolder;
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }

    private void ShowSettings()
    {
        var window = new SettingsWindow(_settings);
        window.Show();
        window.Activate();
    }

    public void Dispose()
    {
        _hotkeys?.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _controller.Dispose();
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Lukit.Capture;
using Lukit.Display;
using Lukit.Interop;
using Lukit.Localization;
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
        Strings.Apply(_settings.Language); // resolve the UI language before building any text
        _controller = new CaptureController(_settings);
        _controller.Notify += OnNotify;

        _tray = new WF.NotifyIcon
        {
            Icon = TrayIconFactory.Create(),
            Text = Strings.TrayTooltip,
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        _tray.DoubleClick += (_, _) => Fire(_controller.CaptureFullscreenAsync);

        RegisterHotkeys();
    }

    private WF.ContextMenuStrip BuildMenu()
    {
        var menu = new WF.ContextMenuStrip();
        menu.Items.Add(Strings.MenuCaptureFullScreen(_settings.HotkeyFullscreen), null, (_, _) => Fire(_controller.CaptureFullscreenAsync));
        menu.Items.Add(Strings.MenuCaptureRegion(_settings.HotkeyRegion), null, (_, _) => Fire(CaptureRegionAsync));
        menu.Items.Add(Strings.MenuCaptureWindow(_settings.HotkeyWindow), null, (_, _) => Fire(CaptureWindowAsync));

        var displayMenu = new WF.ToolStripMenuItem(Strings.MenuCaptureSpecificDisplay);
        displayMenu.DropDownItems.Add(Strings.MenuLoading);
        displayMenu.DropDownOpening += (_, _) => PopulateDisplayMenu(displayMenu);
        menu.Items.Add(displayMenu);

        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add(Strings.MenuOpenSaveFolder, null, (_, _) => OpenSaveFolder());
        menu.Items.Add(Strings.MenuSettings, null, (_, _) => ShowSettings());
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add(Strings.MenuExit, null, (_, _) => _app.Shutdown());
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
            string label = Strings.DisplayLabel(m.Index + 1, m.IsPrimary, m.Bounds.Width, m.Bounds.Height);
            parent.DropDownItems.Add(label, null, (_, _) => Fire(() => _controller.CaptureMonitorAsync(handle, null)));
        }
        if (monitors.Count > 1)
        {
            parent.DropDownItems.Add(new WF.ToolStripSeparator());
            parent.DropDownItems.Add(Strings.AllDisplaysCombined, null, (_, _) => Fire(_controller.CaptureAllMonitorsAsync));
        }
    }

    // Created once; re-registered live whenever the hotkey settings change. The manager's
    // message window persists across re-registration, so no app restart is needed.
    private void RegisterHotkeys()
    {
        _hotkeys ??= new HotkeyManager();
        _hotkeys.Clear();
        TryRegister(_settings.HotkeyFullscreen, () => Fire(_controller.CaptureFullscreenAsync));
        TryRegister(_settings.HotkeyRegion, () => Fire(CaptureRegionAsync));
        TryRegister(_settings.HotkeyWindow, () => Fire(CaptureWindowAsync));
    }

    private void TryRegister(string spec, Action action)
    {
        if (!_hotkeys!.Register(spec, action))
            OnNotify(Strings.HotkeyUnavailable(spec), true);
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
            OnNotify(Strings.ErrorPrefix(ex.Message), true);
        }
    }

    private void OnNotify(string message, bool isError)
    {
        if (!isError && !_settings.ShowNotifications)
            return;

        _app.Dispatcher.Invoke(() =>
            _tray.ShowBalloonTip(
                2500,
                Strings.AppName,
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
        // Apply everything the user may have changed as soon as the window closes — no
        // restart. Harmless on Cancel (settings are unchanged, so this just rebuilds
        // identical UI and re-registers the same hotkeys).
        window.Closed += (_, _) => ApplySettingsChanges();
        window.Show();
        window.Activate();
    }

    /// <summary>Re-applies live-changeable settings: UI language, tray text/menu, and hotkeys.</summary>
    private void ApplySettingsChanges()
    {
        Strings.Apply(_settings.Language);
        _tray.Text = Strings.TrayTooltip;

        WF.ContextMenuStrip old = _tray.ContextMenuStrip!;
        _tray.ContextMenuStrip = BuildMenu();
        old.Dispose();

        RegisterHotkeys();
    }

    public void Dispose()
    {
        _hotkeys?.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _controller.Dispose();
    }
}

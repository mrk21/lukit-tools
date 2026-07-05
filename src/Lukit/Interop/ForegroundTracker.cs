using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Lukit.Interop;

/// <summary>
/// Remembers the last "real" foreground window so a capture invoked from the tray menu can
/// target the window the user was actually using. By the time a tray menu item is clicked,
/// the foreground has already moved to the taskbar (Shell_TrayWnd) and our own menu, so
/// GetForegroundWindow() no longer points at the user's target. A WinEvent hook records each
/// foreground change as it happens — skipping shell surfaces and our own windows — and keeps
/// the last one. Must be created and disposed on the UI (STA) thread, whose message loop
/// delivers the out-of-context hook callbacks.
/// </summary>
public sealed class ForegroundTracker : IDisposable
{
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const int OBJID_WINDOW = 0;

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    private delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    private readonly WinEventProc _callback; // held in a field so the GC can't collect the thunk
    private readonly IntPtr _hook;
    private readonly uint _ownProcessId;
    private IntPtr _last;

    /// <summary>The last foreground window that passed <see cref="ShouldTrack"/>, or zero if none yet.</summary>
    public IntPtr LastWindow => _last;

    public ForegroundTracker()
    {
        _ownProcessId = (uint)Environment.ProcessId;
        _callback = OnForegroundChanged;
        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _callback, idProcess: 0, idThread: 0, WINEVENT_OUTOFCONTEXT);

        // Seed with whatever is in front at startup so a capture right away still has a target.
        Consider(GetForegroundWindow());
    }

    private void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (idObject == OBJID_WINDOW)
            Consider(hwnd);
    }

    private void Consider(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (ShouldTrack(ClassNameOf(hwnd), isOwnProcess: pid == _ownProcessId))
            _last = hwnd;
    }

    private static string ClassNameOf(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        int len = GetClassName(hwnd, sb, sb.Capacity);
        return len > 0 ? sb.ToString() : string.Empty;
    }

    /// <summary>
    /// Decides whether a newly-foregrounded window is a real capture target worth
    /// remembering for a tray-menu-triggered window capture. Excludes our own windows
    /// (tray menu, settings, overlay) and the shell's own surfaces (taskbar, desktop),
    /// which grab the foreground the moment the tray icon is clicked. Pure so it can be
    /// unit-tested without a live desktop.
    /// </summary>
    internal static bool ShouldTrack(string? className, bool isOwnProcess)
    {
        if (isOwnProcess) return false;
        if (string.IsNullOrWhiteSpace(className)) return false;
        return !IsShellSurface(className);
    }

    // The shell's own top-level windows that steal the foreground when the tray icon is
    // clicked (or when nothing else is focused). None of these is ever a window the user
    // means to "capture", so they must not overwrite the last real target.
    private static bool IsShellSurface(string className) => className switch
    {
        "Shell_TrayWnd" or             // primary taskbar
        "Shell_SecondaryTrayWnd" or    // taskbars on secondary monitors
        "Progman" or                   // desktop (Program Manager)
        "WorkerW" or                   // desktop wallpaper host
        "NotifyIconOverflowWindow"     // the tray's "show hidden icons" flyout
            => true,
        _ => false,
    };

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
            UnhookWinEvent(_hook);
    }
}

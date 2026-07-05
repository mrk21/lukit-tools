using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Lukit.Interop;

/// <summary>Parses a hotkey string like "Ctrl+Alt+2" into Win32 modifiers and a virtual-key code.</summary>
public readonly record struct Hotkey(uint Modifiers, uint VirtualKey)
{
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        uint mods = 0, vk = 0;
        foreach (string raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= MOD_CONTROL; break;
                case "alt": mods |= MOD_ALT; break;
                case "shift": mods |= MOD_SHIFT; break;
                case "win" or "windows": mods |= MOD_WIN; break;
                default:
                    if (!TryParseKey(raw, out vk)) return false;
                    break;
            }
        }
        if (vk == 0) return false;
        hotkey = new Hotkey(mods | MOD_NOREPEAT, vk);
        return true;
    }

    private static bool TryParseKey(string key, out uint vk)
    {
        vk = 0;
        key = key.ToUpperInvariant();

        if (key.Length == 1)
        {
            char c = key[0];
            if (c is >= '0' and <= '9') { vk = c; return true; }        // '0'..'9' == 0x30..0x39
            if (c is >= 'A' and <= 'Z') { vk = c; return true; }        // 'A'..'Z' == 0x41..0x5A
        }
        if (key.StartsWith('F') && int.TryParse(key.AsSpan(1), out int f) && f is >= 1 and <= 24)
        {
            vk = (uint)(0x70 + (f - 1)); // VK_F1 == 0x70
            return true;
        }
        switch (key)
        {
            case "PRINTSCREEN" or "PRTSC" or "PRTSCN" or "SNAPSHOT": vk = 0x2C; return true;
            case "INSERT" or "INS": vk = 0x2D; return true;
            case "HOME": vk = 0x24; return true;
            case "END": vk = 0x23; return true;
            case "PAGEUP" or "PGUP": vk = 0x21; return true;
            case "PAGEDOWN" or "PGDN": vk = 0x22; return true;
            case "SPACE": vk = 0x20; return true;
            default: return false;
        }
    }
}

/// <summary>
/// Registers global hotkeys against a message-only window and raises an event when one
/// is pressed. Must be created and disposed on the UI (STA) thread.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 1;

    public HotkeyManager()
    {
        var parameters = new HwndSourceParameters("LukitHotkeyWindow")
        {
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE — a message-only window
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    /// <summary>Registers a hotkey; returns false if the combination is invalid or already taken.</summary>
    public bool Register(string spec, Action action)
    {
        if (!Hotkey.TryParse(spec, out Hotkey hk))
            return false;

        int id = _nextId++;
        if (!RegisterHotKey(_source.Handle, id, hk.Modifiers, hk.VirtualKey))
            return false;

        _actions[id] = action;
        return true;
    }

    /// <summary>
    /// Unregisters every hotkey but keeps the message window alive so the manager can be
    /// re-registered. Lets settings changes take effect live, without an app restart.
    /// </summary>
    public void Clear()
    {
        foreach (int id in _actions.Keys)
            UnregisterHotKey(_source.Handle, id);
        _actions.Clear();
        _nextId = 1;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out Action? action))
        {
            handled = true;
            action();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Clear();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}

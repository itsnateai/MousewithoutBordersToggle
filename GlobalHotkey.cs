using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MWBToggle;

/// <summary>
/// Registers a system-wide hotkey using Win32 RegisterHotKey/UnregisterHotKey.
/// Parses AHK-style hotkey strings (e.g. "#^+f" for Win+Ctrl+Shift+F).
/// </summary>
internal sealed class GlobalHotkey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 0x4D57; // arbitrary unique ID ("MW")

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // Modifier flags for RegisterHotKey
    private const uint MOD_ALT     = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT   = 0x0004;
    private const uint MOD_WIN     = 0x0008;
    // Prevents autorepeat — without this, holding the hotkey fires DoToggle at keyboard
    // repeat rate (~20-30 Hz), which races the settings.json write/retry loop.
    private const uint MOD_NOREPEAT = 0x4000;

    private readonly HotkeyWindow _window;
    private bool _disposed;

    /// <summary>
    /// True if RegisterHotKey succeeded for the requested combo (before any fallback).
    /// Also true when the fallback binding succeeded. False only when registration
    /// failed AND fallback was disabled — caller should Dispose and surface the error.
    /// </summary>
    public bool IsRegistered { get; }

    /// <summary>
    /// Register a global hotkey.
    ///
    /// When <paramref name="allowFallback"/> is true (primary / required hotkey) and
    /// parsing or registration fails, this falls back to Win+Ctrl+Shift+F and updates
    /// <paramref name="ahkHotkey"/> to reflect the actual registered binding.
    ///
    /// When <paramref name="allowFallback"/> is false (optional / secondary hotkey),
    /// failure leaves the instance in an unregistered state with <see cref="IsRegistered"/>
    /// false, so the caller can show a specific error instead of silently clobbering
    /// the primary hotkey's combo.
    /// </summary>
    public GlobalHotkey(ref string ahkHotkey, Action callback,
                        Action<string>? onWarning = null,
                        bool allowFallback = true)
    {
        bool parsed = ParseAhkHotkey(ahkHotkey, out uint modifiers, out uint vk);

        _window = new HotkeyWindow(callback);

        if (parsed && RegisterHotKey(_window.Handle, HOTKEY_ID, modifiers | MOD_NOREPEAT, vk))
        {
            IsRegistered = true;
            return;
        }

        if (!allowFallback)
        {
            onWarning?.Invoke($"Could not register {ahkHotkey} — already taken.");
            IsRegistered = false;
            return;
        }

        onWarning?.Invoke($"Invalid hotkey: {ahkHotkey} — falling back to Win+Ctrl+Shift+F.");
        ahkHotkey = "#^+f";
        IsRegistered = RegisterHotKey(_window.Handle, HOTKEY_ID,
            MOD_WIN | MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, (uint)Keys.F);
    }

    /// <summary>
    /// Parse an AHK-style hotkey string into Win32 modifier flags and virtual key code.
    /// Supports: ^ (Ctrl), ! (Alt), + (Shift), # (Win) prefixes.
    /// Key names: single chars, or names like "F1"-"F12", "Space", "Enter", etc.
    /// Returns true if parsing succeeded; false if the key portion was empty or unrecognized.
    /// </summary>
    internal static bool ParseAhkHotkey(string hk, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        int i = 0;

        while (i < hk.Length && "#^!+".Contains(hk[i]))
        {
            modifiers |= hk[i] switch
            {
                '#' => MOD_WIN,
                '^' => MOD_CONTROL,
                '!' => MOD_ALT,
                '+' => MOD_SHIFT,
                _ => 0
            };
            i++;
        }

        if (i >= hk.Length) return false; // modifiers without a key

        string keyName = hk[i..];

        if (keyName.Length == 1)
        {
            char c = char.ToUpperInvariant(keyName[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                vk = (uint)c;
                return true;
            }
            return false;
        }

        vk = keyName.ToLowerInvariant() switch
        {
            "space"     => (uint)Keys.Space,
            "enter"     => (uint)Keys.Return,
            "return"    => (uint)Keys.Return,
            "tab"       => (uint)Keys.Tab,
            "escape"    => (uint)Keys.Escape,
            "esc"       => (uint)Keys.Escape,
            "backspace" => (uint)Keys.Back,
            "delete"    => (uint)Keys.Delete,
            "del"       => (uint)Keys.Delete,
            "insert"    => (uint)Keys.Insert,
            "ins"       => (uint)Keys.Insert,
            "home"      => (uint)Keys.Home,
            "end"       => (uint)Keys.End,
            "pgup"      => (uint)Keys.PageUp,
            "pgdn"      => (uint)Keys.PageDown,
            "up"        => (uint)Keys.Up,
            "down"      => (uint)Keys.Down,
            "left"      => (uint)Keys.Left,
            "right"     => (uint)Keys.Right,
            "f1"        => (uint)Keys.F1,
            "f2"        => (uint)Keys.F2,
            "f3"        => (uint)Keys.F3,
            "f4"        => (uint)Keys.F4,
            "f5"        => (uint)Keys.F5,
            "f6"        => (uint)Keys.F6,
            "f7"        => (uint)Keys.F7,
            "f8"        => (uint)Keys.F8,
            "f9"        => (uint)Keys.F9,
            "f10"       => (uint)Keys.F10,
            "f11"       => (uint)Keys.F11,
            "f12"       => (uint)Keys.F12,
            _           => 0
        };
        return vk != 0;
    }

    /// <summary>
    /// Trial-register a hotkey without keeping it. Returns true if Windows accepts the
    /// combo (i.e. not already held by another app / the OS). Note: some Win+Key combos
    /// (Win+L, Win+D, etc) may pass this check but still be intercepted by Windows at
    /// keystroke time — those cannot be detected without actually pressing the key.
    /// </summary>
    public static bool CanRegister(string ahkHotkey)
    {
        if (!ParseAhkHotkey(ahkHotkey, out uint modifiers, out uint vk))
            return false;

        var probe = new NativeWindow();
        try
        {
            probe.CreateHandle(new CreateParams());
            // Use a different probe ID so we don't collide with any instance's live binding.
            const int PROBE_ID = HOTKEY_ID ^ 0x1;
            if (!RegisterHotKey(probe.Handle, PROBE_ID, modifiers | MOD_NOREPEAT, vk))
                return false;
            UnregisterHotKey(probe.Handle, PROBE_ID);
            return true;
        }
        finally
        {
            if (probe.Handle != IntPtr.Zero)
                probe.DestroyHandle();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterHotKey(_window.Handle, HOTKEY_ID);
        _window.DestroyHandle();
    }

    /// <summary>
    /// Invisible NativeWindow that receives WM_HOTKEY messages.
    /// </summary>
    private sealed class HotkeyWindow : NativeWindow
    {
        private readonly Action _callback;

        public HotkeyWindow(Action callback)
        {
            _callback = callback;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && (int)m.WParam == HOTKEY_ID)
            {
                _callback();
                return;
            }
            base.WndProc(ref m);
        }
    }
}

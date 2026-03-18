using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MWBToggle;

/// <summary>
/// Registers a system-wide hotkey using Win32 RegisterHotKey/UnregisterHotKey.
/// Parses AHK-style hotkey strings (e.g. "^!c" for Ctrl+Alt+C).
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

    private readonly HotkeyWindow _window;
    private bool _disposed;

    /// <summary>
    /// Register a global hotkey. If registration fails, falls back to Ctrl+Alt+C
    /// and updates <paramref name="ahkHotkey"/> so the caller's field reflects
    /// the actual registered hotkey (mirrors AHK line 73: g_hotkey := "^!c").
    /// </summary>
    public GlobalHotkey(ref string ahkHotkey, Action callback)
    {
        ParseAhkHotkey(ahkHotkey, out uint modifiers, out uint vk);

        _window = new HotkeyWindow(callback);

        if (!RegisterHotKey(_window.Handle, HOTKEY_ID, modifiers, vk))
        {
            // Registration failed — show warning and fall back to Ctrl+Alt+C
            MessageBox.Show(
                $"Invalid hotkey: {ahkHotkey}\n\nCheck your MWBToggle.ini [Settings] Hotkey value.\n\nFalling back to Ctrl+Alt+C.",
                "MWBToggle", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            ahkHotkey = "^!c";
            RegisterHotKey(_window.Handle, HOTKEY_ID, MOD_CONTROL | MOD_ALT, (uint)Keys.C);
        }
    }

    /// <summary>
    /// Parse an AHK-style hotkey string into Win32 modifier flags and virtual key code.
    /// Supports: ^ (Ctrl), ! (Alt), + (Shift), # (Win) prefixes.
    /// Key names: single chars, or names like "F1"-"F12", "Space", "Enter", etc.
    /// </summary>
    internal static void ParseAhkHotkey(string hk, out uint modifiers, out uint vk)
    {
        modifiers = 0;
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

        string keyName = hk[i..];

        // Single character → direct VK mapping
        if (keyName.Length == 1)
        {
            char c = char.ToUpperInvariant(keyName[0]);
            if (c is >= 'A' and <= 'Z')
                vk = (uint)c;
            else if (c is >= '0' and <= '9')
                vk = (uint)c;
            else
                vk = (uint)Keys.C; // fallback
            return;
        }

        // Named keys
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
            _           => (uint)Keys.C  // fallback
        };
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
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
            {
                _callback();
                return;
            }
            base.WndProc(ref m);
        }
    }
}

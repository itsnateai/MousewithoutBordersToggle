using System.Runtime.InteropServices;

namespace MWBToggle;

/// <summary>
/// Low-level keyboard hook (WH_KEYBOARD_LL) that captures modifier+key combos
/// inside a hotkey-picker dialog — even ones another process has already bound
/// via <c>RegisterHotKey</c>. Without this, pressing a taken combo would trigger
/// the other app (e.g. MicMute) and never reach our dialog's KeyDown handler.
///
/// The hook:
///   1. Runs at OS-input level, before RegisterHotKey dispatching
///   2. Only fires while our dialog is the foreground window
///   3. Ignores bare keys (no modifiers) — Esc/Tab/Enter reach the dialog normally
///   4. Suppresses modifier+key combos (return 1) so the other app's hotkey doesn't fire
///   5. Marshals the captured AHK string back to the dialog via a callback
///
/// Caveats: a handful of Win+* combos are enforced by Windows at a deeper layer
/// (e.g. Win+L to lock) and will still trigger even with this hook installed.
/// Those same combos fail <c>RegisterHotKey</c> anyway, so our preview rejects them.
/// </summary>
internal sealed class HookHotkeyCapture : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN     = 0x0100;
    private const int WM_SYSKEYDOWN  = 0x0104;

    // Virtual key codes we care about
    private const int VK_SHIFT   = 0x10, VK_LSHIFT  = 0xA0, VK_RSHIFT = 0xA1;
    private const int VK_CONTROL = 0x11, VK_LCTRL   = 0xA2, VK_RCTRL  = 0xA3;
    private const int VK_MENU    = 0x12, VK_LMENU   = 0xA4, VK_RMENU  = 0xA5;
    private const int VK_LWIN    = 0x5B, VK_RWIN    = 0x5C;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SetWindowsHookExW(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private IntPtr _hookHandle;
    private readonly HookProc _proc; // kept rooted so GC doesn't yank it
    private readonly IntPtr _dialogHandle;
    private readonly Action<string> _onCombo;
    private bool _disposed;

    public HookHotkeyCapture(IntPtr dialogHandle, Action<string> onCombo)
    {
        _dialogHandle = dialogHandle;
        _onCombo = onCombo;
        _proc = HookCallback;

        // WH_KEYBOARD_LL is a global hook, but hModule must be the caller's
        // module handle (or the host process's, which is what GetModuleHandleW(null) returns).
        IntPtr hMod = GetModuleHandleW(null);
        _hookHandle = SetWindowsHookExW(WH_KEYBOARD_LL, _proc, hMod, 0);
        if (_hookHandle == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            Logger.Warn($"HookHotkeyCapture: SetWindowsHookEx failed, Win32 err={err}. " +
                         "Hotkey picker will fall back to WinForms KeyDown capture (cannot suppress other apps' hotkeys).");
        }
    }

    /// <summary>True if the low-level keyboard hook installed successfully.</summary>
    public bool IsInstalled => _hookHandle != IntPtr.Zero;

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // Per MS docs: if nCode < 0, must pass through without processing.
        if (nCode < 0)
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        int msg = wParam.ToInt32();
        if (msg != WM_KEYDOWN && msg != WM_SYSKEYDOWN)
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        // Only act when our dialog is foreground — don't suppress global user input.
        if (GetForegroundWindow() != _dialogHandle)
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        int vk = (int)info.vkCode;

        // Ignore raw modifier-key presses (the combo isn't complete yet).
        if (IsModifierVk(vk))
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        bool ctrl  = IsDown(VK_CONTROL);
        bool alt   = IsDown(VK_MENU);
        bool shift = IsDown(VK_SHIFT);
        bool win   = IsDown(VK_LWIN) || IsDown(VK_RWIN);

        // No modifiers → let the key through so the dialog can use Esc/Tab/Enter/etc.
        if (!ctrl && !alt && !shift && !win)
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        string? keyPart = VkToAhkKey(vk);
        if (keyPart == null)
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        string ahk = "";
        if (ctrl)  ahk += "^";
        if (alt)   ahk += "!";
        if (shift) ahk += "+";
        if (win)   ahk += "#";
        ahk += keyPart;

        // Deliver back to the dialog. The hook runs on the thread that installed it
        // (the UI thread in our case) during its message pump, so direct call is safe —
        // but BeginInvoke is defensive in case of future threading changes.
        //
        // The inner lambda re-checks disposal at invocation time, not queue time: there's
        // a race where Set/Cancel can close the form between our IsDisposed check here
        // and the UI thread actually running the delegate. Without the inner guard,
        // touching previewLabel.Text would throw ObjectDisposedException on the UI thread.
        try
        {
            var ctl = Control.FromHandle(_dialogHandle);
            if (ctl != null && ctl.IsHandleCreated && !ctl.IsDisposed)
            {
                ctl.BeginInvoke(() =>
                {
                    if (_disposed) return;
                    try { _onCombo(ahk); }
                    catch (ObjectDisposedException) { /* form torn down mid-flight */ }
                    catch (InvalidOperationException) { /* handle destroyed */ }
                });
            }
        }
        catch (InvalidOperationException)
        {
            // Handle was destroyed between check and call — fine, we're tearing down.
        }

        // Suppress: return non-zero so Windows does NOT dispatch to RegisterHotKey
        // targets, the focused control, or any other input consumer.
        return (IntPtr)1;
    }

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private static bool IsModifierVk(int vk) => vk is
        VK_SHIFT or VK_LSHIFT or VK_RSHIFT or
        VK_CONTROL or VK_LCTRL or VK_RCTRL or
        VK_MENU or VK_LMENU or VK_RMENU or
        VK_LWIN or VK_RWIN;

    private static string? VkToAhkKey(int vk)
    {
        // A-Z
        if (vk >= 'A' && vk <= 'Z') return ((char)vk).ToString().ToLowerInvariant();
        // 0-9 (top-row digits)
        if (vk >= '0' && vk <= '9') return ((char)vk).ToString();
        // F1-F12 (VK_F1 = 0x70)
        if (vk >= 0x70 && vk <= 0x7B) return $"F{vk - 0x6F}";
        return vk switch
        {
            0x20 => "Space",
            0x0D => "Enter",
            0x09 => "Tab",
            _    => null,
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }
}

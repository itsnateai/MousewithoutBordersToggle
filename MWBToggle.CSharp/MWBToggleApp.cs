using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MWBToggle;

/// <summary>
/// Main application context — lives in the system tray, no visible window.
/// Port of MWBToggle.ahk with all features intact.
/// </summary>
internal sealed class MWBToggleApp : ApplicationContext
{
    internal const string Version = "2.0.0";

    // UTF-8 without BOM — matches AHK's "UTF-8-RAW"
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    // P/Invoke for kernel32 Beep (Console.Beep may not work in WinExe)
    [DllImport("kernel32.dll")]
    private static extern bool Beep(uint dwFreq, uint dwDuration);

    // P/Invoke for TaskbarCreated message (Explorer restart recovery)
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    // ── Configuration (defaults, overridden by MWBToggle.ini) ──────────────
    private string _hotkey = "^!c";   // Ctrl+Alt+C
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        @"Microsoft\PowerToys\MouseWithoutBorders\settings.json");
    private readonly string _powerToysExe = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        @"PowerToys\PowerToys.exe");
    private bool _confirmToggle;
    private bool _soundFeedback;
    private bool _disposed;

    // ── UI ──────────────────────────────────────────────────────────────────
    private readonly NotifyIcon _trayIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _pause5;
    private readonly ToolStripMenuItem _pause15;
    private readonly ToolStripMenuItem _pause30;
    private readonly GlobalHotkey _globalHotkey;
    private readonly System.Windows.Forms.Timer _syncTimer;
    private readonly System.Windows.Forms.Timer _pauseTimer;
    private readonly MessageWindow _messageWindow;
    private AboutForm? _aboutForm;

    // ── Embedded icons ─────────────────────────────────────────────────────
    private readonly Icon _iconOn;
    private readonly Icon _iconOff;
    private readonly bool _iconsAreEmbedded; // track whether we own the icons

    // ── Startup shortcut path ──────────────────────────────────────────────
    private readonly string _startupShortcut = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        "MWBToggle.lnk");

    // ── OSD tooltip ────────────────────────────────────────────────────────
    private Form? _osdForm;
    private System.Windows.Forms.Timer? _osdTimer;

    // ── SyncTray state cache (avoid allocations every 5s) ────────────────
    private bool _lastSyncState;
    private bool _lastSyncInitialized;

    public MWBToggleApp()
    {
        // Load embedded icons — clone system icons to avoid corrupting shared instances
        var embeddedOn = LoadEmbeddedIcon("MWBToggle.on.ico");
        var embeddedOff = LoadEmbeddedIcon("MWBToggle.mwb.ico");
        _iconsAreEmbedded = embeddedOn != null && embeddedOff != null;
        _iconOn = embeddedOn ?? (Icon)SystemIcons.Application.Clone();
        _iconOff = embeddedOff ?? (Icon)SystemIcons.Shield.Clone();

        // Load INI config (may override _hotkey, _confirmToggle, _soundFeedback)
        LoadConfig();

        // ── Build context menu (mirrors AHK tray menu) ─────────────────────
        _menu = new ContextMenuStrip();

        var toggleItem = new ToolStripMenuItem("Toggle MWB Clipboard/Files", null, (_, _) => DoToggle());
        toggleItem.Font = new Font(toggleItem.Font, FontStyle.Bold); // default action
        _menu.Items.Add(toggleItem);

        var hotkeyLabel = new ToolStripMenuItem("Hotkey: " + HotkeyToReadable(_hotkey))
        { Enabled = false };
        _menu.Items.Add(hotkeyLabel);

        _menu.Items.Add(new ToolStripSeparator());

        // Pause sharing submenu
        _pause5 = new ToolStripMenuItem("5 minutes", null, (_, _) => PauseSharing(5));
        _pause15 = new ToolStripMenuItem("15 minutes", null, (_, _) => PauseSharing(15));
        _pause30 = new ToolStripMenuItem("30 minutes", null, (_, _) => PauseSharing(30));
        var pauseItem = new ToolStripMenuItem("Pause Sharing");
        pauseItem.DropDownItems.AddRange(new ToolStripItem[] { _pause5, _pause15, _pause30 });
        _menu.Items.Add(pauseItem);

        // Run at Startup
        _startupItem = new ToolStripMenuItem("Run at Startup", null, (_, _) => ToggleStartup());
        _startupItem.Checked = File.Exists(_startupShortcut);
        _menu.Items.Add(_startupItem);

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Open PowerToys", null, (_, _) => OpenPowerToys()));
        _menu.Items.Add(new ToolStripMenuItem("About", null, (_, _) => ShowAbout()));
        _menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitApplication()));

        // ── Tray icon ──────────────────────────────────────────────────────
        _trayIcon = new NotifyIcon
        {
            ContextMenuStrip = _menu,
            Visible = true
        };
        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                DoToggle();
        };

        // ── Global hotkey (may update _hotkey on fallback) ─────────────────
        _globalHotkey = new GlobalHotkey(ref _hotkey, DoToggle);

        // ── Message window for TaskbarCreated (Explorer restart recovery) ──
        _messageWindow = new MessageWindow(RegisterWindowMessage("TaskbarCreated"), () =>
        {
            _trayIcon.Visible = true;
            SyncTray();
        });

        // ── Sync timer (every 5 seconds, like AHK SetTimer) ───────────────
        _syncTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _syncTimer.Tick += (_, _) => SyncTray();
        _syncTimer.Start();

        // ── Pause resume timer (one-shot) ──────────────────────────────────
        _pauseTimer = new System.Windows.Forms.Timer();
        _pauseTimer.Tick += (_, _) => ResumeSharing();

        // Initial tray sync
        SyncTray();
    }

    // ╔══════════════════════════════════════════════════════════════════════╗
    // ║  Core                                                                ║
    // ╚══════════════════════════════════════════════════════════════════════╝

    private void DoToggle(bool confirm = true)
    {
        // Warn if MWB isn't running
        var processes = Process.GetProcessesByName("PowerToys.MouseWithoutBorders");
        bool mwbRunning = processes.Length > 0;
        foreach (var p in processes) p.Dispose();

        if (!mwbRunning)
        {
            ShowOSD("MWBToggle: Mouse Without Borders doesn't appear to be running.", 5000);
            return;
        }

        if (!File.Exists(_settingsPath))
        {
            MessageBox.Show("Settings file not found:\n" + _settingsPath,
                "MWBToggle", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // Read JSON — file may be briefly locked by MWB
        string json;
        try
        {
            json = File.ReadAllText(_settingsPath, Utf8NoBom);
        }
        catch (IOException)
        {
            ShowOSD("MWBToggle: Could not read settings.json — file may be locked. Try again.", 5000);
            return;
        }

        // Detect current ShareClipboard state
        var match = ShareClipboardRegex.Match(json);
        if (!match.Success)
        {
            MessageBox.Show(
                "Could not find ShareClipboard in settings.json.\n\nMake sure Mouse Without Borders has been run at least once.",
                "MWBToggle", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        bool currentlyOn = match.Groups[1].Value == "true";

        // Optional confirmation
        if (_confirmToggle && confirm)
        {
            string prompt = "Turn clipboard/file sharing " + (currentlyOn ? "OFF" : "ON") + "?";
            if (MessageBox.Show(prompt, "MWBToggle", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                != DialogResult.Yes)
                return;
        }

        // Flip both values
        string newVal = currentlyOn ? "false" : "true";
        json = ShareClipboardReplaceRegex.Replace(json, "$1" + newVal);
        json = TransferFileReplaceRegex.Replace(json, "$1" + newVal);

        // Verify replacement took effect
        var verify = ShareClipboardRegex.Match(json);
        if (!verify.Success || verify.Groups[1].Value != newVal)
        {
            MessageBox.Show(
                "Failed to update ShareClipboard in settings.json — the JSON structure may have changed.\n\nNo changes were written.",
                "MWBToggle", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // Backup before writing
        try { File.Copy(_settingsPath, _settingsPath + ".bak", overwrite: true); } catch { }

        // Retry loop — file may be briefly locked by MWB
        bool written = false;
        for (int i = 0; i < 3; i++)
        {
            try
            {
                File.WriteAllText(_settingsPath, json, Utf8NoBom);
                written = true;
                break;
            }
            catch (IOException)
            {
                // Yield to message pump instead of blocking the UI thread
                WaitWithMessagePump(200);
            }
        }

        if (!written)
        {
            MessageBox.Show(
                "Could not write to settings.json — the file may be locked by Mouse Without Borders.\n\nPlease try again in a moment.",
                "MWBToggle", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // Brief delay for MWB to detect file change — pump messages to stay responsive
        WaitWithMessagePump(300);
        SyncTray();

        string newState = currentlyOn ? "OFF" : "ON";
        ShowOSD("MWBToggle: Clipboard & File Transfer " + newState);

        // Optional sound feedback — use kernel32 Beep, not Console.Beep
        if (_soundFeedback)
            Beep(currentlyOn ? 400u : 800u, 150);
    }

    // Overload so GlobalHotkey can call with no args
    private void DoToggle() => DoToggle(confirm: true);

    /// <summary>
    /// Wait for the specified duration while pumping the WinForms message loop,
    /// so the UI stays responsive (tray icon, hotkeys, timers all keep working).
    /// Replaces Thread.Sleep which would freeze the UI.
    /// </summary>
    private static void WaitWithMessagePump(int milliseconds)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < milliseconds)
        {
            Application.DoEvents();
            Thread.Sleep(10); // Small yield to avoid spinning CPU
        }
    }

    // ╔══════════════════════════════════════════════════════════════════════╗
    // ║  Tray sync                                                           ║
    // ╚══════════════════════════════════════════════════════════════════════╝

    // Pre-compiled regexes — avoids re-parsing pattern strings on hot paths.
    // SyncTray alone runs every 5s (~84,000 times/week).
    private static readonly Regex ShareClipboardRegex = new(
        @"""ShareClipboard""\s*:\s*\{\s*""value""\s*:\s*(true|false)",
        RegexOptions.Compiled);
    private static readonly Regex ShareClipboardReplaceRegex = new(
        @"(""ShareClipboard""\s*:\s*\{\s*""value""\s*:\s*)(true|false)",
        RegexOptions.Compiled);
    private static readonly Regex TransferFileReplaceRegex = new(
        @"(""TransferFile""\s*:\s*\{\s*""value""\s*:\s*)(true|false)",
        RegexOptions.Compiled);

    // Pre-built tooltip strings — avoids allocating a new string every 5s
    private static readonly string TrayTextOn  = $"MWBToggle v{Version} — Clipboard/Files: ON";
    private static readonly string TrayTextOff = $"MWBToggle v{Version} — Clipboard/Files: OFF";

    private void SyncTray()
    {
        bool on = false;
        try
        {
            if (File.Exists(_settingsPath))
            {
                string json = File.ReadAllText(_settingsPath, Utf8NoBom);
                var m = ShareClipboardRegex.Match(json);
                if (m.Success)
                    on = m.Groups[1].Value == "true";
            }
        }
        catch
        {
            return; // File locked — skip, retry in 5s
        }

        // Only update tray icon/text when state actually changes
        if (!_lastSyncInitialized || on != _lastSyncState)
        {
            _lastSyncState = on;
            _lastSyncInitialized = true;
            _trayIcon.Icon = on ? _iconOn : _iconOff;
            _trayIcon.Text = on ? TrayTextOn : TrayTextOff;
        }
    }

    // ╔══════════════════════════════════════════════════════════════════════╗
    // ║  Pause / Resume                                                      ║
    // ╚══════════════════════════════════════════════════════════════════════╝

    private void PauseSharing(int minutes)
    {
        if (!File.Exists(_settingsPath)) return;

        string json;
        try { json = File.ReadAllText(_settingsPath, Utf8NoBom); }
        catch { return; }

        // Only toggle if currently ON
        var m = ShareClipboardRegex.Match(json);
        if (m.Success && m.Groups[1].Value == "true")
            DoToggle(confirm: false);

        // Update checkmarks
        _pause5.Checked = minutes == 5;
        _pause15.Checked = minutes == 15;
        _pause30.Checked = minutes == 30;

        // One-shot timer to resume
        _pauseTimer.Stop();
        _pauseTimer.Interval = minutes * 60_000;
        _pauseTimer.Start();

        ShowOSD($"MWBToggle: Sharing paused for {minutes} minutes.");
    }

    private void ResumeSharing()
    {
        _pauseTimer.Stop();

        if (!File.Exists(_settingsPath)) return;

        string json;
        try { json = File.ReadAllText(_settingsPath, Utf8NoBom); }
        catch { return; }

        // Clear checkmarks
        _pause5.Checked = false;
        _pause15.Checked = false;
        _pause30.Checked = false;

        // Only toggle if currently OFF
        var m = ShareClipboardRegex.Match(json);
        if (m.Success && m.Groups[1].Value == "false")
            DoToggle(confirm: false);

        ShowOSD("MWBToggle: Sharing resumed.");
    }

    // ╔══════════════════════════════════════════════════════════════════════╗
    // ║  Open PowerToys                                                      ║
    // ╚══════════════════════════════════════════════════════════════════════╝

    private void OpenPowerToys()
    {
        if (File.Exists(_powerToysExe))
        {
            using var _ = Process.Start(new ProcessStartInfo(_powerToysExe) { UseShellExecute = true });
            return;
        }

        string machinePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            @"PowerToys\PowerToys.exe");

        if (File.Exists(machinePath))
        {
            using var _ = Process.Start(new ProcessStartInfo(machinePath) { UseShellExecute = true });
        }
        else
        {
            MessageBox.Show(
                "Could not find PowerToys.\n\nExpected:\n" + _powerToysExe +
                "\n\nYou can open it manually from the Start menu.",
                "MWBToggle", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ╔══════════════════════════════════════════════════════════════════════╗
    // ║  Run at Startup                                                      ║
    // ╚══════════════════════════════════════════════════════════════════════╝

    private void ToggleStartup()
    {
        if (File.Exists(_startupShortcut))
        {
            File.Delete(_startupShortcut);
            _startupItem.Checked = false;
            ShowOSD("MWBToggle: Removed from startup.");
        }
        else
        {
            string exePath = Environment.ProcessPath ?? Application.ExecutablePath;
            string exeDir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;
            CreateShortcut(_startupShortcut, exePath, exeDir);
            _startupItem.Checked = true;
            ShowOSD("MWBToggle: Added to startup.");
        }
    }

    /// <summary>
    /// Create a .lnk shortcut using Windows Script Host COM interop.
    /// Mirrors the AHK WScript.Shell approach.
    /// </summary>
    private static void CreateShortcut(string lnkPath, string targetPath, string workingDir)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null) return;
        dynamic? shell = null;
        dynamic? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType)!;
            shortcut = shell.CreateShortcut(lnkPath);
            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = workingDir;
            shortcut.Description = "MWBToggle";
            shortcut.Save();
        }
        finally
        {
            if (shortcut != null) Marshal.ReleaseComObject(shortcut);
            if (shell != null) Marshal.ReleaseComObject(shell);
        }
    }

    // ╔══════════════════════════════════════════════════════════════════════╗
    // ║  About                                                               ║
    // ╚══════════════════════════════════════════════════════════════════════╝

    private void ShowAbout()
    {
        if (_aboutForm != null && !_aboutForm.IsDisposed)
        {
            _aboutForm.BringToFront();
            _aboutForm.Show();
            return;
        }
        _aboutForm = new AboutForm(_hotkey);
        _aboutForm.Show();
    }

    // ╔══════════════════════════════════════════════════════════════════════╗
    // ║  OSD notification — tooltip at cursor position                       ║
    // ╚══════════════════════════════════════════════════════════════════════╝

    /// <summary>
    /// Show a lightweight tooltip at the mouse cursor position — matches the AHK
    /// ToolTip() behavior exactly. Visible on whichever monitor the user is on,
    /// no Windows toast/Action Center spam.
    /// </summary>
    private void ShowOSD(string message, int durationMs = 3000)
    {
        // Dispose previous OSD if still showing
        if (_osdTimer != null)
        {
            _osdTimer.Stop();
            _osdTimer.Dispose();
            _osdTimer = null;
        }
        if (_osdForm != null)
        {
            _osdForm.Close();
            _osdForm.Dispose();
            _osdForm = null;
        }

        var form = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            TopMost = true,
            StartPosition = FormStartPosition.Manual,
            BackColor = Color.FromArgb(255, 255, 225), // classic tooltip yellow
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(6, 4, 6, 4)
        };

        var label = new Label
        {
            Text = message,
            AutoSize = true,
            ForeColor = Color.Black,
            Font = SystemFonts.StatusFont ?? SystemFonts.DefaultFont,
            Padding = new Padding(2)
        };
        form.Controls.Add(label);

        // Position at cursor
        var cursor = Cursor.Position;
        form.Location = new Point(cursor.X + 16, cursor.Y + 16);

        form.Show();
        _osdForm = form;

        // Auto-dismiss after duration — reuse timer pattern, dispose on fire
        var timer = new System.Windows.Forms.Timer { Interval = durationMs };
        _osdTimer = timer;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            form.Close();
            form.Dispose();
            if (_osdTimer == timer) _osdTimer = null;
            if (_osdForm == form) _osdForm = null;
        };
        timer.Start();
    }

    // ╔══════════════════════════════════════════════════════════════════════╗
    // ║  Config                                                              ║
    // ╚══════════════════════════════════════════════════════════════════════╝

    private void LoadConfig()
    {
        // Use the actual .exe location, not AppContext.BaseDirectory
        // (which points to a temp extraction dir for single-file publish)
        string exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? Application.ExecutablePath)
                        ?? AppContext.BaseDirectory;
        string iniPath = Path.Combine(exeDir, "MWBToggle.ini");
        if (!File.Exists(iniPath)) return;

        var config = IniConfig.Load(iniPath);

        string? val = config.Get("Settings", "Hotkey");
        if (!string.IsNullOrEmpty(val))
            _hotkey = val;

        val = config.Get("Settings", "ConfirmToggle");
        if (!string.IsNullOrEmpty(val))
            _confirmToggle = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase);

        val = config.Get("Settings", "SoundFeedback");
        if (!string.IsNullOrEmpty(val))
            _soundFeedback = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    // ╔══════════════════════════════════════════════════════════════════════╗
    // ║  Helpers                                                             ║
    // ╚══════════════════════════════════════════════════════════════════════╝

    /// <summary>
    /// Translate AHK-style hotkey string to human-readable form.
    /// e.g. "^!c" -> "Ctrl + Alt + C"
    /// Uses a HashSet to deduplicate modifiers (e.g. "^^c" → "Ctrl + C").
    /// </summary>
    internal static string HotkeyToReadable(string hk)
    {
        var seen = new HashSet<char>();
        string mods = "";
        int i = 0;

        while (i < hk.Length && "#^!+".Contains(hk[i]))
        {
            if (seen.Add(hk[i]))
            {
                mods += hk[i] switch
                {
                    '#' => "Win + ",
                    '^' => "Ctrl + ",
                    '!' => "Alt + ",
                    '+' => "Shift + ",
                    _ => ""
                };
            }
            i++;
        }

        string key = hk[i..].ToUpperInvariant();
        return mods + key;
    }

    /// <summary>
    /// Load an icon from embedded resources, properly disposing the stream.
    /// </summary>
    private static Icon? LoadEmbeddedIcon(string resourceName)
    {
        using var stream = typeof(MWBToggleApp).Assembly.GetManifestResourceStream(resourceName);
        if (stream == null) return null;
        // Clone the icon so it doesn't depend on the stream lifetime
        using var tempIcon = new Icon(stream);
        return (Icon)tempIcon.Clone();
    }

    private void ExitApplication()
    {
        if (_disposed) return;
        _disposed = true;

        _globalHotkey.Dispose();
        _syncTimer.Stop();
        _syncTimer.Dispose();
        _pauseTimer.Stop();
        _pauseTimer.Dispose();
        _messageWindow.DestroyHandle();
        _osdTimer?.Stop();
        _osdTimer?.Dispose();
        _osdForm?.Close();
        _osdForm?.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _globalHotkey.Dispose();
            _syncTimer.Dispose();
            _pauseTimer.Dispose();
            _messageWindow.DestroyHandle();
            _osdTimer?.Dispose();
            _osdForm?.Dispose();
            _trayIcon.Dispose();
            _menu.Dispose();
            _aboutForm?.Dispose();
            _iconOn.Dispose();
            _iconOff.Dispose();
        }
        base.Dispose(disposing);
    }

    // ╔══════════════════════════════════════════════════════════════════════╗
    // ║  TaskbarCreated message window (Explorer restart recovery)            ║
    // ╚══════════════════════════════════════════════════════════════════════╝

    /// <summary>
    /// Invisible window that listens for the TaskbarCreated message,
    /// which Windows broadcasts when Explorer restarts. This lets us
    /// re-show the tray icon automatically — mirrors AHK line 83-84.
    /// </summary>
    private sealed class MessageWindow : NativeWindow
    {
        private readonly uint _taskbarCreatedMsg;
        private readonly Action _callback;

        public MessageWindow(uint taskbarCreatedMsg, Action callback)
        {
            _taskbarCreatedMsg = taskbarCreatedMsg;
            _callback = callback;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (_taskbarCreatedMsg != 0 && m.Msg == (int)_taskbarCreatedMsg)
            {
                _callback();
            }
            base.WndProc(ref m);
        }
    }
}

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
    internal const string Version = "2.3.1";

    // UTF-8 without BOM — matches AHK's "UTF-8-RAW"
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    // P/Invoke for kernel32 Beep (Console.Beep may not work in WinExe)
    [DllImport("kernel32.dll")]
    private static extern bool Beep(uint dwFreq, uint dwDuration);

    // P/Invoke for TaskbarCreated message (Explorer restart recovery)
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    // P/Invoke for opening MWB settings (simulate tray icon click)
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

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
    private bool _middleClickOpensMwbSettings = true;
    private bool _singleClickToggles = true;
    private bool _disposed;

    // ── UI ──────────────────────────────────────────────────────────────────
    private readonly NotifyIcon _trayIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _pause5;
    private readonly ToolStripMenuItem _pause30;
    private readonly ToolStripMenuItem _pauseUnlimited;
    private readonly ToolStripMenuItem _middleClickItem;
    private readonly ToolStripMenuItem _transferFileItem;
    private readonly ToolStripMenuItem _singleClickItem;
    private GlobalHotkey _globalHotkey;
    private readonly System.Windows.Forms.Timer _pauseTimer;
    private readonly MessageWindow _messageWindow;
    private FileSystemWatcher? _fileWatcher;
    private AboutForm? _aboutForm;

    // ── Embedded icons ─────────────────────────────────────────────────────
    private readonly Icon _iconOn;
    private readonly Icon _iconOff;

    // ── Paths ────────────────────────────────────────────────────────────
    private readonly string _exeDir = Path.GetDirectoryName(
        Environment.ProcessPath ?? Application.ExecutablePath) ?? AppContext.BaseDirectory;
    private readonly string _startupShortcut = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        "MWBToggle.lnk");

    // ── OSD tooltip ────────────────────────────────────────────────────────
    private Form? _osdForm;
    private System.Windows.Forms.Timer? _osdTimer;

    // ── SyncTray state cache (avoid allocations every 5s) ────────────────
    private bool _lastSyncState;
    private bool _lastTransferFileState;
    private bool _lastSyncInitialized;

    public MWBToggleApp()
    {
        // Icon loading priority: user's icons on disk > embedded in .exe > system fallback
        // Users can drop on.ico / off.ico next to the .exe to override the built-in icons.
        _iconOn = LoadIconFromDisk(Path.Combine(_exeDir, "on.ico"))
                  ?? LoadEmbeddedIcon("MWBToggle.on.ico")
                  ?? (Icon)SystemIcons.Application.Clone();
        _iconOff = LoadIconFromDisk(Path.Combine(_exeDir, "off.ico"))
                   ?? LoadEmbeddedIcon("MWBToggle.off.ico")
                   ?? (Icon)SystemIcons.Shield.Clone();

        // Load INI config (may override _hotkey, _confirmToggle, _soundFeedback)
        LoadConfig();

        // ── Build context menu ─────────────────────────────────────────────
        _menu = new ContextMenuStrip();

        // Title bar
        var titleItem = new ToolStripMenuItem($"MWBToggle v{Version}") { Enabled = false };
        titleItem.Font = new Font(titleItem.Font, FontStyle.Bold);
        _menu.Items.Add(titleItem);
        _menu.Items.Add(new ToolStripSeparator());

        // Hotkey display (clickable — opens hotkey change dialog)
        var hotkeyItem = new ToolStripMenuItem("Hotkey: " + HotkeyToReadable(_hotkey));
        hotkeyItem.Click += (_, _) => ChangeHotkey(hotkeyItem);
        _menu.Items.Add(hotkeyItem);
        _menu.Items.Add(new ToolStripSeparator());

        // Toggle action
        _menu.Items.Add(new ToolStripMenuItem("Toggle Sharing", null, (_, _) => DoToggle()));

        // Pause sharing submenu
        _pause5 = new ToolStripMenuItem("5 minutes", null, (_, _) => PauseSharing(5));
        _pause30 = new ToolStripMenuItem("30 minutes", null, (_, _) => PauseSharing(30));
        _pauseUnlimited = new ToolStripMenuItem("Until resumed", null, (_, _) => PauseSharing(0));
        var pauseItem = new ToolStripMenuItem("Pause Sharing");
        pauseItem.DropDownItems.AddRange(new ToolStripItem[] { _pause5, _pause30, _pauseUnlimited });
        _menu.Items.Add(pauseItem);

        _menu.Items.Add(new ToolStripSeparator());

        // PowerToys submenu
        var powerToysMenu = new ToolStripMenuItem("PowerToys");
        powerToysMenu.DropDownItems.Add(new ToolStripMenuItem("About", null, (_, _) => ShowAbout()));
        powerToysMenu.DropDownItems.Add(new ToolStripSeparator());
        powerToysMenu.DropDownItems.Add(new ToolStripMenuItem("Open PowerToys", null, (_, _) => OpenPowerToys()));
        powerToysMenu.DropDownItems.Add(new ToolStripMenuItem("MWB Settings", null, (_, _) => OpenMwbSettings()));
        powerToysMenu.DropDownItems.Add(new ToolStripSeparator());
        _startupItem = new ToolStripMenuItem("Run at Startup", null, (_, _) => ToggleStartup());
        _startupItem.Checked = File.Exists(_startupShortcut);
        powerToysMenu.DropDownItems.Add(_startupItem);
        powerToysMenu.DropDownItems.Add(new ToolStripSeparator());
        _singleClickItem = new ToolStripMenuItem("Single-click toggles sharing", null, (_, _) => ToggleSingleClick());
        _singleClickItem.Checked = _singleClickToggles;
        powerToysMenu.DropDownItems.Add(_singleClickItem);
        _middleClickItem = new ToolStripMenuItem("Middle-click opens MWB Settings", null, (_, _) => ToggleMiddleClick());
        _middleClickItem.Checked = _middleClickOpensMwbSettings;
        powerToysMenu.DropDownItems.Add(_middleClickItem);
        powerToysMenu.DropDownItems.Add(new ToolStripSeparator());
        _transferFileItem = new ToolStripMenuItem("File Transfer", null, (_, _) => ToggleTransferFile());
        _transferFileItem.Checked = true; // SyncTray will set the real state
        powerToysMenu.DropDownItems.Add(_transferFileItem);
        _menu.Items.Add(powerToysMenu);

        _menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitApplication()));

        // ── Tray icon ──────────────────────────────────────────────────────
        _trayIcon = new NotifyIcon
        {
            ContextMenuStrip = _menu,
            Visible = true
        };
        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && _singleClickToggles)
                DoToggle();
            else if (e.Button == MouseButtons.Middle && _middleClickOpensMwbSettings)
                OpenMwbSettings();
        };

        // ── Global hotkey (may update _hotkey on fallback) ─────────────────
        _globalHotkey = new GlobalHotkey(ref _hotkey, DoToggle, msg => ShowOSD("MWBToggle: " + msg, 5000));

        // ── Message window for TaskbarCreated (Explorer restart recovery) ──
        _messageWindow = new MessageWindow(RegisterWindowMessage("TaskbarCreated"), () =>
        {
            _trayIcon.Visible = true;
            SyncTray();
        });

        // ── File watcher — replaces 5s polling timer ──────────────────────
        // Only reads settings.json when it actually changes on disk,
        // instead of polling every 5s (~12,000 reads/day of wasted I/O).
        StartFileWatcher();

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
            ShowOSD("MWBToggle: Settings file not found — check PowerToys MWB is configured.", 5000);
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
            ShowOSD("MWBToggle: ShareClipboard not found in settings.json — run MWB at least once.", 5000);
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
            ShowOSD("MWBToggle: Failed to update settings — JSON structure may have changed.", 5000);
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
            ShowOSD("MWBToggle: Could not write settings.json — file locked. Try again.", 5000);
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

    // Pre-compiled regexes — parsed once at startup, JIT-compiled to IL.
    private static readonly Regex ShareClipboardRegex = new(
        @"""ShareClipboard""\s*:\s*\{\s*""value""\s*:\s*(true|false)",
        RegexOptions.Compiled);
    private static readonly Regex ShareClipboardReplaceRegex = new(
        @"(""ShareClipboard""\s*:\s*\{\s*""value""\s*:\s*)(true|false)",
        RegexOptions.Compiled);
    private static readonly Regex TransferFileRegex = new(
        @"""TransferFile""\s*:\s*\{\s*""value""\s*:\s*(true|false)",
        RegexOptions.Compiled);
    private static readonly Regex TransferFileReplaceRegex = new(
        @"(""TransferFile""\s*:\s*\{\s*""value""\s*:\s*)(true|false)",
        RegexOptions.Compiled);

    // Pre-built tooltip strings — zero allocation on sync
    private static readonly string TrayTextOn  = $"MWBToggle v{Version} — Clipboard/Files: ON";
    private static readonly string TrayTextOff = $"MWBToggle v{Version} — Clipboard/Files: OFF";

    /// <summary>
    /// Watch settings.json for changes instead of polling every 5s.
    /// FileSystemWatcher uses OS-level notifications (ReadDirectoryChangesW)
    /// — zero CPU/memory when idle, fires only when MWB actually writes.
    /// </summary>
    private void StartFileWatcher()
    {
        string? dir = Path.GetDirectoryName(_settingsPath);
        string file = Path.GetFileName(_settingsPath);
        if (dir == null || !Directory.Exists(dir)) return;

        _fileWatcher = new FileSystemWatcher(dir, file)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        // FileSystemWatcher fires on a threadpool thread — marshal to UI thread
        _fileWatcher.Changed += (_, _) =>
        {
            try { _menu.BeginInvoke(SyncTray); } catch { }
        };

        // If the watcher errors (network drive disconnect, etc.), restart it
        _fileWatcher.Error += (_, _) =>
        {
            try
            {
                _fileWatcher.EnableRaisingEvents = false;
                _fileWatcher.EnableRaisingEvents = true;
            }
            catch { }
        };
    }

    private void SyncTray()
    {
        bool clipOn = false;
        bool fileOn = false;
        try
        {
            if (File.Exists(_settingsPath))
            {
                string json = File.ReadAllText(_settingsPath, Utf8NoBom);
                var cm = ShareClipboardRegex.Match(json);
                if (cm.Success)
                    clipOn = cm.Groups[1].Value == "true";
                var fm = TransferFileRegex.Match(json);
                if (fm.Success)
                    fileOn = fm.Groups[1].Value == "true";
            }
        }
        catch
        {
            return; // File locked — watcher will fire again on next write
        }

        // Only update tray icon/text when state actually changes
        if (!_lastSyncInitialized || clipOn != _lastSyncState)
        {
            _lastSyncState = clipOn;
            _lastSyncInitialized = true;
            _trayIcon.Icon = clipOn ? _iconOn : _iconOff;
            _trayIcon.Text = clipOn ? TrayTextOn : TrayTextOff;
        }

        if (fileOn != _lastTransferFileState)
        {
            _lastTransferFileState = fileOn;
            _transferFileItem.Checked = fileOn;
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
        _pause30.Checked = minutes == 30;
        _pauseUnlimited.Checked = minutes == 0;

        // One-shot timer to resume (skip for unlimited)
        _pauseTimer.Stop();
        if (minutes > 0)
        {
            _pauseTimer.Interval = minutes * 60_000;
            _pauseTimer.Start();
        }

        string msg = minutes > 0
            ? $"MWBToggle: Sharing paused for {minutes} minutes."
            : "MWBToggle: Sharing paused until resumed.";
        ShowOSD(msg);
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
        _pause30.Checked = false;
        _pauseUnlimited.Checked = false;

        // Only toggle if currently OFF
        var m = ShareClipboardRegex.Match(json);
        if (m.Success && m.Groups[1].Value == "false")
            DoToggle(confirm: false);

        ShowOSD("MWBToggle: Sharing resumed.");
    }

    // ╔══════════════════════════════════════════════════════════════════════╗
    // ║  PowerToys                                                            ║
    // ╚══════════════════════════════════════════════════════════════════════╝

    /// <summary>
    /// Find the PowerToys executable — checks user install then machine-wide install.
    /// </summary>
    private string? FindPowerToysExe()
    {
        if (File.Exists(_powerToysExe)) return _powerToysExe;

        string machinePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            @"PowerToys\PowerToys.exe");

        return File.Exists(machinePath) ? machinePath : null;
    }

    private void OpenPowerToys()
    {
        string? exe = FindPowerToysExe();
        if (exe != null)
        {
            using var _ = Process.Start(new ProcessStartInfo(exe)
            {
                Arguments = "--open-settings=MouseWithoutBorders",
                UseShellExecute = true
            });
        }
        else
        {
            ShowOSD("MWBToggle: Could not find PowerToys — open it from the Start menu.", 5000);
        }
    }

    private void OpenMwbSettings()
    {
        // Send WM_TRAYMOUSEMESSAGE with WM_LBUTTONDOWN to MWB's WinForms windows.
        // This triggers the same code path as clicking the MWB tray icon.
        const uint WM_TRAYMOUSEMESSAGE = 0x0800;
        const uint WM_LBUTTONDOWN = 0x0201;

        var pids = new HashSet<uint>();
        foreach (var p in Process.GetProcessesByName("PowerToys.MouseWithoutBorders"))
        {
            pids.Add((uint)p.Id);
            p.Dispose();
        }

        if (pids.Count == 0)
        {
            ShowOSD("MWBToggle: Mouse Without Borders is not running.", 5000);
            return;
        }

        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (!pids.Contains(pid)) return true;

            var cls = new StringBuilder(256);
            GetClassName(hWnd, cls, 256);
            if (cls.ToString().StartsWith("WindowsForms10", StringComparison.Ordinal))
            {
                for (int id = 0; id <= 2; id++)
                    PostMessage(hWnd, WM_TRAYMOUSEMESSAGE, (IntPtr)id, (IntPtr)WM_LBUTTONDOWN);
            }
            return true;
        }, IntPtr.Zero);
    }

    private void ToggleSingleClick()
    {
        _singleClickToggles = !_singleClickToggles;
        _singleClickItem.Checked = _singleClickToggles;
    }

    private void ToggleMiddleClick()
    {
        _middleClickOpensMwbSettings = !_middleClickOpensMwbSettings;
        _middleClickItem.Checked = _middleClickOpensMwbSettings;
    }

    /// <summary>
    /// Show a key-capture dialog to change the global hotkey.
    /// The old hotkey is unregistered and the new one registered immediately.
    /// </summary>
    private void ChangeHotkey(ToolStripMenuItem hotkeyMenuItem)
    {
        using var form = new Form
        {
            Text = "Set Hotkey",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            StartPosition = FormStartPosition.CenterScreen,
            TopMost = true,
            ClientSize = new Size(300, 115),
            KeyPreview = true
        };

        var label = new Label
        {
            Text = "Press a key combination...\n(Ctrl/Alt/Shift/Win + key)",
            AutoSize = false,
            Size = new Size(280, 40),
            Location = new Point(10, 10),
            TextAlign = ContentAlignment.MiddleCenter
        };
        form.Controls.Add(label);

        var resultLabel = new Label
        {
            Text = "Current: " + HotkeyToReadable(_hotkey),
            AutoSize = false,
            Size = new Size(280, 20),
            Location = new Point(10, 55),
            TextAlign = ContentAlignment.MiddleCenter
        };
        form.Controls.Add(resultLabel);

        var cancelBtn = new Button
        {
            Text = "Cancel",
            Size = new Size(80, 28),
            Location = new Point(110, 80)
        };
        cancelBtn.Click += (_, _) => form.Close();
        form.Controls.Add(cancelBtn);

        form.KeyDown += (_, e) =>
        {
            // Need at least one modifier
            if (e.Modifiers == Keys.None) return;

            // Build AHK-style hotkey string
            string ahk = "";
            if (e.Control) ahk += "^";
            if (e.Alt) ahk += "!";
            if (e.Shift) ahk += "+";
            if ((e.Modifiers & Keys.LWin) != 0 || e.KeyCode == Keys.LWin) ahk += "#";

            // Get the actual key (not modifier keys themselves)
            var key = e.KeyCode & ~Keys.Modifiers;
            if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
                return; // Still pressing modifiers, wait for the actual key

            ahk += key switch
            {
                >= Keys.A and <= Keys.Z => ((char)key).ToString().ToLowerInvariant(),
                >= Keys.D0 and <= Keys.D9 => ((char)(key - Keys.D0 + '0')).ToString(),
                >= Keys.F1 and <= Keys.F12 => key.ToString(),
                Keys.Space => "Space",
                Keys.Return => "Enter",
                Keys.Escape => "Escape",
                Keys.Tab => "Tab",
                Keys.Back => "Backspace",
                Keys.Delete => "Delete",
                _ => key.ToString()
            };

            // Re-register the hotkey
            _globalHotkey.Dispose();
            _hotkey = ahk;
            _globalHotkey = new GlobalHotkey(ref _hotkey, DoToggle, msg => ShowOSD("MWBToggle: " + msg, 5000));

            hotkeyMenuItem.Text = "Hotkey: " + HotkeyToReadable(_hotkey);
            ShowOSD("MWBToggle: Hotkey set to " + HotkeyToReadable(_hotkey));
            form.Close();

            e.Handled = true;
            e.SuppressKeyPress = true;
        };

        form.ShowDialog();
    }

    private void ToggleTransferFile()
    {
        if (!File.Exists(_settingsPath)) return;

        string json;
        try { json = File.ReadAllText(_settingsPath, Utf8NoBom); }
        catch { return; }

        // Read current states
        var clipMatch = ShareClipboardRegex.Match(json);
        var fileMatch = TransferFileRegex.Match(json);
        if (!fileMatch.Success) return;

        bool clipboardOn = clipMatch.Success && clipMatch.Groups[1].Value == "true";
        bool transferOn = fileMatch.Groups[1].Value == "true";

        // Can't enable file transfer without clipboard sharing
        if (!transferOn && !clipboardOn)
        {
            ShowOSD("MWBToggle: ShareClipboard must be ON for file transfer.", 5000);
            return;
        }

        // Flip only TransferFile
        string newVal = transferOn ? "false" : "true";
        json = TransferFileReplaceRegex.Replace(json, "$1" + newVal);

        try { File.Copy(_settingsPath, _settingsPath + ".bak", overwrite: true); } catch { }

        bool written = false;
        for (int i = 0; i < 3; i++)
        {
            try
            {
                File.WriteAllText(_settingsPath, json, Utf8NoBom);
                written = true;
                break;
            }
            catch (IOException) { WaitWithMessagePump(200); }
        }

        if (!written)
        {
            ShowOSD("MWBToggle: Could not write settings.json — file locked.", 5000);
            return;
        }

        WaitWithMessagePump(300);
        SyncTray();
        ShowOSD("MWBToggle: File Transfer " + (transferOn ? "OFF" : "ON"));
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
            CreateShortcut(_startupShortcut, exePath, _exeDir);
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
        string iniPath = Path.Combine(_exeDir, "MWBToggle.ini");
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

        val = config.Get("Settings", "MiddleClickMwbSettings");
        if (!string.IsNullOrEmpty(val))
            _middleClickOpensMwbSettings = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase);

        val = config.Get("Settings", "SingleClickToggles");
        if (!string.IsNullOrEmpty(val))
            _singleClickToggles = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase);
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
    /// Load an icon from a file on disk (user override). Returns null if not found.
    /// </summary>
    private static Icon? LoadIconFromDisk(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return new Icon(path);
        }
        catch
        {
            return null; // Corrupt or unreadable icon file — skip
        }
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
        _fileWatcher?.Dispose();
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
            _fileWatcher?.Dispose();
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

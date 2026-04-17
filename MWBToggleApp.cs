using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MWBToggle;

/// <summary>
/// Main application context — lives in the system tray, no visible window.
/// Port of MWBToggle.ahk with all features intact.
/// </summary>
internal sealed class MWBToggleApp : ApplicationContext
{
    internal static readonly string Version = typeof(MWBToggleApp).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

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

    // Win state isn't exposed in Keys.Modifiers (only Ctrl/Alt/Shift). Read directly.
    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

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
    private bool _toggleInProgress;
    // Window during which FileSystemWatcher events on settings.json are ours and should be ignored.
    private DateTime _suppressWatcherUntilUtc;
    // Absolute UTC time when a pause should auto-resume. null = no active timed pause.
    // Using absolute time (instead of relying on WinForms Timer.Interval) makes pause
    // survive OS sleep: PowerModeChanged.Resume recomputes the remaining interval.
    private DateTime? _pauseResumeAtUtc;

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
    private System.Windows.Forms.Timer? _debounceTimer;
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
    private string _configDir = null!; // Resolved in LoadConfig()

    // ── OSD tooltip (discreet, pinned above the system tray) ──────────────
    private readonly OsdForm _osd = new();

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

        // Title bar — click opens About
        var titleItem = new ToolStripMenuItem($"MWBToggle v{Version}", null, (_, _) => ShowAbout());
        titleItem.Font = new Font(titleItem.Font, FontStyle.Bold);
        titleItem.ToolTipText = "About MWBToggle";
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
        _pauseTimer.Tick += (_, _) =>
        {
            // Tick may fire earlier than expected if we recomputed the interval after
            // wake — check absolute time and only fire if we've truly reached the deadline.
            if (_pauseResumeAtUtc is DateTime due && DateTime.UtcNow >= due)
            {
                ResumeSharing();
            }
            else if (_pauseResumeAtUtc is DateTime pending)
            {
                _pauseTimer.Stop();
                _pauseTimer.Interval = Math.Max(1000, (int)(pending - DateTime.UtcNow).TotalMilliseconds);
                _pauseTimer.Start();
            }
        };

        // Re-check pause deadline on wake-from-sleep. WinForms Timer.Interval is
        // wall-clock-blind — without this, a 30 min pause through a 2 hr sleep would
        // resume 30 min after wake instead of the moment we woke up.
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        // Validate startup shortcut target (self-healing after winget upgrades)
        ValidateStartupShortcut();

        // Initial tray sync
        SyncTray();

        // Tell the self-updater we reached running state — safe to drop .old rollback
        // on the next launch. If we crashed before this, .old persists for recovery.
        UpdateDialog.WriteStartupSentinel();
        Logger.Info($"MWBToggle v{Version} started.");
    }

    // ╔══════════════════════════════════════════════════════════════════════╗
    // ║  Core                                                                ║
    // ╚══════════════════════════════════════════════════════════════════════╝

    private void DoToggle(bool confirm = true)
    {
        // Reentrancy guard — WaitWithMessagePump pumps messages, which can dispatch
        // another WM_HOTKEY (or tray click) into DoToggle before the current one completes.
        if (_toggleInProgress) return;
        _toggleInProgress = true;
        try
        {
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

            // Reject files over 1MB — valid MWB settings.json is <5KB
            var toggleFileInfo = new FileInfo(_settingsPath);
            if (toggleFileInfo.Length > 1_000_000)
            {
                ShowOSD("MWBToggle: Settings file is unexpectedly large — aborting.", 5000);
                return;
            }

            string json;
            try
            {
                json = File.ReadAllText(_settingsPath, Utf8NoBom);
            }
            catch (IOException ex)
            {
                Logger.Warn($"DoToggle read failed: {ex.Message}");
                ShowOSD("MWBToggle: Could not read settings.json — file may be locked. Try again.", 5000);
                return;
            }

            var match = ShareClipboardRegex.Match(json);
            if (!match.Success)
            {
                ShowOSD("MWBToggle: ShareClipboard not found in settings.json — run MWB at least once.", 5000);
                return;
            }

            bool currentlyOn = match.Groups[1].Value == "true";

            if (_confirmToggle && confirm)
            {
                string prompt = "Turn clipboard/file sharing " + (currentlyOn ? "OFF" : "ON") + "?";
                if (MessageBox.Show(prompt, "MWBToggle", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                    != DialogResult.Yes)
                    return;
            }

            // A manual toggle supersedes any pending pause — but only clear once we've
            // committed to toggling (past the confirm dialog), so cancelling doesn't
            // silently destroy the user's active pause.
            _pauseTimer.Stop();
            _pauseResumeAtUtc = null;
            _pause5.Checked = false;
            _pause30.Checked = false;
            _pauseUnlimited.Checked = false;

            string newVal = currentlyOn ? "false" : "true";
            json = ShareClipboardReplaceRegex.Replace(json, "$1" + newVal);
            json = TransferFileReplaceRegex.Replace(json, "$1" + newVal);

            // Verify BOTH replacements landed — guards against future MWB schema drift.
            var verifyClip = ShareClipboardRegex.Match(json);
            var verifyFile = TransferFileRegex.Match(json);
            if (!verifyClip.Success || verifyClip.Groups[1].Value != newVal
             || !verifyFile.Success || verifyFile.Groups[1].Value != newVal)
            {
                Logger.Warn("DoToggle verify failed — regex replace did not update both fields.");
                ShowOSD("MWBToggle: Failed to update settings — JSON structure may have changed.", 5000);
                return;
            }

            if (!WriteSettingsAtomic(json))
            {
                ShowOSD("MWBToggle: Could not write settings.json — file locked. Try again.", 5000);
                return;
            }

            WaitWithMessagePump(300);
            SyncTray();

            bool nowOn = !currentlyOn;
            ShowOSDState("MWBToggle: Clipboard & File Transfer " + (nowOn ? "ON" : "OFF"), nowOn);

            if (_soundFeedback)
                Beep(currentlyOn ? 400u : 800u, 150);
        }
        finally
        {
            _toggleInProgress = false;
        }
    }

    // Overload so GlobalHotkey can call with no args
    private void DoToggle() => DoToggle(confirm: true);

    /// <summary>
    /// Write settings.json atomically: write to .tmp, then NTFS-atomic Replace with
    /// backup rotation into .bak. This avoids truncate-then-write data loss on power
    /// cuts or AV quarantines, and gives us a recoverable prior-state rollback target.
    /// </summary>
    private bool WriteSettingsAtomic(string json)
    {
        string tmpPath = _settingsPath + ".tmp";
        string bakPath = _settingsPath + ".bak";

        // Suppress our own FileSystemWatcher event — this is a self-write, not an external one.
        _suppressWatcherUntilUtc = DateTime.UtcNow.AddMilliseconds(500);

        for (int i = 0; i < 3; i++)
        {
            // Refresh the self-write window inside the loop — a contended 3-retry path
            // can exceed 500 ms, at which point our own successful write would leak
            // through as an external change and trigger a redundant SyncTray.
            _suppressWatcherUntilUtc = DateTime.UtcNow.AddMilliseconds(500);
            try
            {
                File.WriteAllText(tmpPath, json, Utf8NoBom);
                if (File.Exists(_settingsPath))
                    File.Replace(tmpPath, _settingsPath, bakPath, ignoreMetadataErrors: true);
                else
                    File.Move(tmpPath, _settingsPath);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // UnauthorizedAccessException covers AV quarantine / DACL denial mid-retry,
                // which otherwise escapes and crashes the toggle path.
                Logger.Warn($"WriteSettingsAtomic attempt {i + 1} failed: {ex.Message}");
                WaitWithMessagePump(200);
            }
        }

        try { if (File.Exists(tmpPath)) File.Delete(tmpPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Logger.Warn($"tmp cleanup: {ex.Message}"); }
        return false;
    }

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
        if (dir == null) return;

        // MWB settings dir doesn't exist yet — watch nearest existing ancestor
        // for the subdir to appear, then re-initialize. Covers install-before-PowerToys
        // and PowerToys-reinstall cases without polling.
        if (!Directory.Exists(dir))
        {
            WatchForSettingsDir(dir);
            return;
        }

        // Create debounce timer once — rapid FileSystemWatcher events restart it
        _debounceTimer = new System.Windows.Forms.Timer { Interval = 300 };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            // Ignore our own writes — WriteSettingsAtomic sets a short suppression window.
            if (DateTime.UtcNow < _suppressWatcherUntilUtc) return;
            SyncTray();
        };

        _fileWatcher = new FileSystemWatcher(dir, file)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        // FileSystemWatcher fires on a threadpool thread — marshal to UI thread
        _fileWatcher.Changed += (_, _) =>
        {
            try
            {
                _menu.BeginInvoke(() =>
                {
                    _debounceTimer?.Stop();
                    _debounceTimer?.Start();
                });
            }
            catch { }
        };

        // If the watcher errors (network drive disconnect, settings dir removed, etc.),
        // attempt to restart it in-place. If that fails (common case: the dir is gone
        // entirely), tear down and fall back to the bootstrap watcher so the tray
        // recovers when MWB reappears. Without the fallback, a vanished settings dir
        // leaves the app silently blind to future state changes.
        _fileWatcher.Error += (_, errArgs) =>
        {
            try
            {
                _fileWatcher!.EnableRaisingEvents = false;
                _fileWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                Logger.Warn($"FileSystemWatcher Error — in-place restart failed ({ex.Message}); falling back to bootstrap watcher.");
                try
                {
                    _menu.BeginInvoke(() =>
                    {
                        if (_disposed) return;
                        try { _fileWatcher?.Dispose(); } catch { }
                        _fileWatcher = null;
                        string? settingsDir = Path.GetDirectoryName(_settingsPath);
                        if (!string.IsNullOrEmpty(settingsDir))
                            WatchForSettingsDir(settingsDir);
                    });
                }
                catch (Exception ex2) { Logger.Warn($"FileSystemWatcher Error — bootstrap re-init marshal failed: {ex2.Message}"); }
            }
        };
    }

    // Single-shot guard for the bootstrap watcher: IncludeSubdirectories means we can get
    // several `Created` events in quick succession as PowerToys builds out its dir tree.
    // Without this flag, multiple in-flight BeginInvokes would each call StartFileWatcher
    // and orphan the previous _fileWatcher with EnableRaisingEvents still true.
    private bool _watcherBootstrapped;

    /// <summary>
    /// Watch the nearest existing ancestor of the settings dir. When the target dir
    /// finally appears (MWB first launch after install), tear this down and re-init
    /// the real watcher + refresh the tray.
    /// </summary>
    private void WatchForSettingsDir(string targetDir)
    {
        string? ancestor = Path.GetDirectoryName(targetDir);
        while (!string.IsNullOrEmpty(ancestor) && !Directory.Exists(ancestor))
            ancestor = Path.GetDirectoryName(ancestor);

        if (string.IsNullOrEmpty(ancestor))
        {
            Logger.Warn($"No existing ancestor for {targetDir} — watcher not started.");
            return;
        }

        var bootstrap = new FileSystemWatcher(ancestor)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.DirectoryName,
            EnableRaisingEvents = true
        };
        _fileWatcher = bootstrap;
        _watcherBootstrapped = false;

        bootstrap.Created += (_, _) =>
        {
            if (!Directory.Exists(targetDir)) return;
            try
            {
                _menu.BeginInvoke(() =>
                {
                    if (_disposed || _watcherBootstrapped) return;
                    _watcherBootstrapped = true;
                    bootstrap.EnableRaisingEvents = false;
                    bootstrap.Dispose();
                    _fileWatcher = null;
                    StartFileWatcher();
                    SyncTray();
                });
            }
            catch (Exception ex) { Logger.Warn($"watcher bootstrap marshal: {ex.Message}"); }
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
                // Reject files over 1MB — valid MWB settings.json is <5KB
                var fileInfo = new FileInfo(_settingsPath);
                if (fileInfo.Length > 1_000_000) return;

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
        if (new FileInfo(_settingsPath).Length > 1_000_000) return;

        string json;
        try { json = File.ReadAllText(_settingsPath, Utf8NoBom); }
        catch (Exception ex) { Logger.Warn($"PauseSharing read: {ex.Message}"); return; }

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
            _pauseResumeAtUtc = DateTime.UtcNow.AddMinutes(minutes);
            _pauseTimer.Interval = minutes * 60_000;
            _pauseTimer.Start();
        }
        else
        {
            _pauseResumeAtUtc = null;
        }

        string msg = minutes > 0
            ? $"MWBToggle: Sharing paused for {minutes} minutes."
            : "MWBToggle: Sharing paused until resumed.";
        ShowOSDState(msg, on: false);
    }

    private void ResumeSharing()
    {
        _pauseTimer.Stop();
        _pauseResumeAtUtc = null;

        if (!File.Exists(_settingsPath)) return;
        if (new FileInfo(_settingsPath).Length > 1_000_000) return;

        string json;
        try { json = File.ReadAllText(_settingsPath, Utf8NoBom); }
        catch (Exception ex) { Logger.Warn($"ResumeSharing read: {ex.Message}"); return; }

        // Clear checkmarks
        _pause5.Checked = false;
        _pause30.Checked = false;
        _pauseUnlimited.Checked = false;

        // Only toggle if currently OFF
        var m = ShareClipboardRegex.Match(json);
        if (m.Success && m.Groups[1].Value == "false")
            DoToggle(confirm: false);

        ShowOSDState("MWBToggle: Sharing resumed.", on: true);
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        // SystemEvents callbacks fire on a dedicated SystemEvents pump thread, not the UI
        // thread. WinForms Timer and form controls are UI-thread-only, so marshal over.
        if (e.Mode != PowerModes.Resume) return;
        try
        {
            _menu.BeginInvoke(() =>
            {
                if (_disposed) return;
                if (_pauseResumeAtUtc is not DateTime due) return;

                _pauseTimer.Stop();
                if (DateTime.UtcNow >= due)
                {
                    ResumeSharing();
                }
                else
                {
                    _pauseTimer.Interval = Math.Max(1000, (int)(due - DateTime.UtcNow).TotalMilliseconds);
                    _pauseTimer.Start();
                }
            });
        }
        catch (InvalidOperationException)
        {
            // Menu handle destroyed during shutdown — nothing to do.
        }
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
            Location = new Point(110, 80),
            AccessibleName = "Cancel hotkey change and keep current binding"
        };
        cancelBtn.Click += (_, _) => form.Close();
        form.Controls.Add(cancelBtn);

        // Esc closes without changing the hotkey. The KeyDown handler below bails early
        // on bare Esc (no modifiers), so the keypress bubbles to CancelButton.
        form.CancelButton = cancelBtn;

        form.KeyDown += (_, e) =>
        {
            // Win key isn't in e.Modifiers (Keys.Modifiers = Shift|Ctrl|Alt only) — read via Win32.
            bool winDown = (GetKeyState(VK_LWIN) & 0x8000) != 0
                        || (GetKeyState(VK_RWIN) & 0x8000) != 0;

            if (e.Modifiers == Keys.None && !winDown) return;

            // Ignore while the user is still pressing only modifier keys.
            var key = e.KeyCode & ~Keys.Modifiers;
            if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
                return;

            string? keyPart = key switch
            {
                >= Keys.A and <= Keys.Z    => ((char)key).ToString().ToLowerInvariant(),
                >= Keys.D0 and <= Keys.D9  => ((char)(key - Keys.D0 + '0')).ToString(),
                >= Keys.F1 and <= Keys.F12 => key.ToString(),
                Keys.Space  => "Space",
                Keys.Return => "Enter",
                Keys.Escape => "Escape",
                Keys.Tab    => "Tab",
                Keys.Back   => "Backspace",
                Keys.Delete => "Delete",
                _ => null
            };

            if (keyPart == null)
            {
                resultLabel.Text = "Unsupported key — use A-Z, 0-9, or F1-F12.";
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            string ahk = "";
            if (e.Control) ahk += "^";
            if (e.Alt)     ahk += "!";
            if (e.Shift)   ahk += "+";
            if (winDown)   ahk += "#";
            ahk += keyPart;

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
        if (new FileInfo(_settingsPath).Length > 1_000_000) return;

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

        var verifyFile = TransferFileRegex.Match(json);
        if (!verifyFile.Success || verifyFile.Groups[1].Value != newVal)
        {
            Logger.Warn("ToggleTransferFile verify failed — regex replace did not update.");
            ShowOSD("MWBToggle: Failed to update settings — JSON structure may have changed.", 5000);
            return;
        }

        if (!WriteSettingsAtomic(json))
        {
            ShowOSD("MWBToggle: Could not write settings.json — file locked.", 5000);
            return;
        }

        WaitWithMessagePump(300);
        SyncTray();
        // transferOn reflects the PREVIOUS state (pre-toggle), so "nowOn" inverts it.
        bool nowOn = !transferOn;
        ShowOSDState("MWBToggle: File Transfer " + (nowOn ? "ON" : "OFF"), nowOn);
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

    /// <summary>
    /// Self-heal the startup shortcut if the exe has moved (e.g. after a winget upgrade
    /// places the new version in a different versioned subfolder of
    /// <c>%LOCALAPPDATA%\Microsoft\WinGet\Packages\</c>). Reads the existing .lnk
    /// TargetPath and updates it if it doesn't match the current exe.
    /// Static so <see cref="Program"/> can invoke it BEFORE the single-instance mutex
    /// check — that way even a duplicate launch that immediately exits still leaves the
    /// startup shortcut pointing at the current exe for the next reboot.
    /// </summary>
    internal static void ValidateStartupShortcut()
    {
        string startupShortcut = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            "MWBToggle.lnk");
        if (!File.Exists(startupShortcut)) return;

        string currentPath = Environment.ProcessPath ?? Application.ExecutablePath;
        if (string.IsNullOrEmpty(currentPath)) return;
        string currentDir = Path.GetDirectoryName(currentPath) ?? "";

        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            dynamic shell = Activator.CreateInstance(shellType)!;
            try
            {
                dynamic shortcut = shell.CreateShortcut(startupShortcut);
                try
                {
                    var targetPath = (string)shortcut.TargetPath;
                    if (!targetPath.Equals(currentPath, StringComparison.OrdinalIgnoreCase))
                    {
                        CreateShortcut(startupShortcut, currentPath, currentDir);
                    }
                }
                finally { Marshal.FinalReleaseComObject(shortcut); }
            }
            finally { Marshal.FinalReleaseComObject(shell); }
        }
        catch (Exception ex) { Logger.Warn($"ValidateStartupShortcut: {ex.Message}"); }
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
    /// Show a discreet OSD pinned above the system tray. Mirrors MicMute/SyncTray
    /// style: dark bubble + state dot, click-through, auto-dismiss, no Action Center spam.
    /// </summary>
    private void ShowOSD(string message, int durationMs = 3000)
        => _osd.ShowMessage(message, durationMs, OsdForm.State.Info);

    /// <summary>
    /// Show the OSD with a colored state dot — green for ON, red for OFF.
    /// Use for toggle/pause/resume state changes so the user can read the result
    /// from the dot color at a glance.
    /// </summary>
    private void ShowOSDState(string message, bool on, int durationMs = 3000)
        => _osd.ShowMessage(message, durationMs, on ? OsdForm.State.On : OsdForm.State.Off);

    // ╔══════════════════════════════════════════════════════════════════════╗
    // ║  Config                                                              ║
    // ╚══════════════════════════════════════════════════════════════════════╝

    private void LoadConfig()
    {
        // Resolve INI path with fallback for winget portable installs:
        // 1. Next to exe (backwards compat / traditional portable)
        // 2. %APPDATA%\MWBToggle\ (roaming — winget or multi-user)
        // 3. If winget-managed → create in %APPDATA%\MWBToggle\
        // 4. Else → create next to exe (traditional portable)
        string portableIni = Path.Combine(_exeDir, "MWBToggle.ini");
        string appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MWBToggle");
        string appDataIni = Path.Combine(appDataDir, "MWBToggle.ini");

        string iniPath;
        if (File.Exists(portableIni))
        {
            iniPath = portableIni;
            _configDir = _exeDir;
        }
        else if (File.Exists(appDataIni))
        {
            iniPath = appDataIni;
            _configDir = appDataDir;
        }
        else if (UpdateDialog.IsWingetManaged())
        {
            Directory.CreateDirectory(appDataDir);
            iniPath = appDataIni;
            _configDir = appDataDir;
        }
        else
        {
            iniPath = portableIni;
            _configDir = _exeDir;
        }

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

        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _globalHotkey.Dispose();
        _fileWatcher?.Dispose();
        _debounceTimer?.Stop();
        _debounceTimer?.Dispose();
        _pauseTimer.Stop();
        _pauseTimer.Dispose();
        _messageWindow.DestroyHandle();
        _osd.Close();
        _osd.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _globalHotkey.Dispose();
            _fileWatcher?.Dispose();
            _debounceTimer?.Stop();
            _debounceTimer?.Dispose();
            _pauseTimer.Dispose();
            _messageWindow.DestroyHandle();
            _osd.Dispose();
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

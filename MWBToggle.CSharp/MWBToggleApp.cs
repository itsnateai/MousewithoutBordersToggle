using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
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

    // ── Configuration (defaults, overridden by MWBToggle.ini) ──────────────
    private string _hotkey = "^!c";   // Ctrl+Alt+C
    private string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        @"Microsoft\PowerToys\MouseWithoutBorders\settings.json");
    private string _powerToysExe = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        @"PowerToys\PowerToys.exe");
    private bool _confirmToggle;
    private bool _soundFeedback;

    // ── UI ──────────────────────────────────────────────────────────────────
    private readonly NotifyIcon _trayIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _pause5;
    private readonly ToolStripMenuItem _pause15;
    private readonly ToolStripMenuItem _pause30;
    private readonly GlobalHotkey _globalHotkey;
    private readonly System.Windows.Forms.Timer _syncTimer;
    private readonly System.Windows.Forms.Timer _pauseTimer;
    private ToolTip? _osdTip;
    private AboutForm? _aboutForm;

    // ── Embedded icons ─────────────────────────────────────────────────────
    private readonly Icon _iconOn;
    private readonly Icon _iconOff;

    // ── Startup shortcut path ──────────────────────────────────────────────
    private readonly string _startupShortcut = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        "MWBToggle.lnk");

    public MWBToggleApp()
    {
        // Load embedded icons
        _iconOn = LoadEmbeddedIcon("MWBToggle.on.ico") ?? SystemIcons.Application;
        _iconOff = LoadEmbeddedIcon("MWBToggle.mwb.ico") ?? SystemIcons.Shield;

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
        _pauseItem = new ToolStripMenuItem("Pause Sharing");
        _pauseItem.DropDownItems.AddRange(new ToolStripItem[] { _pause5, _pause15, _pause30 });
        _menu.Items.Add(_pauseItem);

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

        // ── Global hotkey ──────────────────────────────────────────────────
        _globalHotkey = new GlobalHotkey(_hotkey, DoToggle);

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
        if (Process.GetProcessesByName("PowerToys.MouseWithoutBorders").Length == 0)
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
            json = File.ReadAllText(_settingsPath, System.Text.Encoding.UTF8);
        }
        catch (IOException)
        {
            ShowOSD("MWBToggle: Could not read settings.json — file may be locked. Try again.", 5000);
            return;
        }

        // Detect current ShareClipboard state
        var match = Regex.Match(json, @"""ShareClipboard""\s*:\s*\{\s*""value""\s*:\s*(true|false)");
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
        json = Regex.Replace(json,
            @"(""ShareClipboard""\s*:\s*\{\s*""value""\s*:\s*)(true|false)", "$1" + newVal);
        json = Regex.Replace(json,
            @"(""TransferFile""\s*:\s*\{\s*""value""\s*:\s*)(true|false)", "$1" + newVal);

        // Verify replacement took effect
        if (!Regex.IsMatch(json, @"""ShareClipboard""\s*:\s*\{\s*""value""\s*:\s*" + newVal))
        {
            MessageBox.Show(
                "Failed to update ShareClipboard in settings.json — the JSON structure may have changed.\n\nNo changes were written.",
                "MWBToggle", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // Backup before writing
        try { File.Copy(_settingsPath, _settingsPath + ".bak", overwrite: true); } catch { }

        // Retry loop — file may be briefly locked
        bool written = false;
        for (int i = 0; i < 3; i++)
        {
            try
            {
                File.WriteAllText(_settingsPath, json, System.Text.Encoding.UTF8);
                written = true;
                break;
            }
            catch (IOException)
            {
                Thread.Sleep(200);
            }
        }

        if (!written)
        {
            MessageBox.Show(
                "Could not write to settings.json — the file may be locked by Mouse Without Borders.\n\nPlease try again in a moment.",
                "MWBToggle", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // Delay for MWB to detect file change
        Thread.Sleep(300);
        SyncTray();

        string newState = currentlyOn ? "OFF" : "ON";
        ShowOSD("MWBToggle: Clipboard & File Transfer " + newState);

        // Optional sound feedback
        if (_soundFeedback)
            Console.Beep(currentlyOn ? 400 : 800, 150);
    }

    // Overload so GlobalHotkey can call with no args
    private void DoToggle() => DoToggle(confirm: true);

    // ╔══════════════════════════════════════════════════════════════════════╗
    // ║  Tray sync                                                           ║
    // ╚══════════════════════════════════════════════════════════════════════╝

    private void SyncTray()
    {
        bool on = false;
        try
        {
            if (File.Exists(_settingsPath))
            {
                string json = File.ReadAllText(_settingsPath, System.Text.Encoding.UTF8);
                var m = Regex.Match(json, @"""ShareClipboard""\s*:\s*\{\s*""value""\s*:\s*(true|false)");
                if (m.Success)
                    on = m.Groups[1].Value == "true";
            }
        }
        catch
        {
            return; // File locked — skip, retry in 5s
        }

        _trayIcon.Icon = on ? _iconOn : _iconOff;
        _trayIcon.Text = $"MWBToggle v{Version} — Clipboard/Files: {(on ? "ON" : "OFF")}";
    }

    // ╔══════════════════════════════════════════════════════════════════════╗
    // ║  Pause / Resume                                                      ║
    // ╚══════════════════════════════════════════════════════════════════════╝

    private void PauseSharing(int minutes)
    {
        if (!File.Exists(_settingsPath)) return;

        string json;
        try { json = File.ReadAllText(_settingsPath, System.Text.Encoding.UTF8); }
        catch { return; }

        // Only toggle if currently ON
        if (Regex.IsMatch(json, @"""ShareClipboard""\s*:\s*\{\s*""value""\s*:\s*true"))
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
        try { json = File.ReadAllText(_settingsPath, System.Text.Encoding.UTF8); }
        catch { return; }

        // Clear checkmarks
        _pause5.Checked = false;
        _pause15.Checked = false;
        _pause30.Checked = false;

        // Only toggle if currently OFF
        if (Regex.IsMatch(json, @"""ShareClipboard""\s*:\s*\{\s*""value""\s*:\s*false"))
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
            Process.Start(new ProcessStartInfo(_powerToysExe) { UseShellExecute = true });
            return;
        }

        string machinePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            @"PowerToys\PowerToys.exe");

        if (File.Exists(machinePath))
        {
            Process.Start(new ProcessStartInfo(machinePath) { UseShellExecute = true });
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
            CreateShortcut(_startupShortcut, Application.ExecutablePath, AppContext.BaseDirectory);
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
        // Use IWshRuntimeLibrary via late-bound COM — no extra dependency needed
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null) return;
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(lnkPath);
        shortcut.TargetPath = targetPath;
        shortcut.WorkingDirectory = workingDir;
        shortcut.Description = "MWBToggle";
        shortcut.Save();
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
    // ║  OSD notification                                                    ║
    // ╚══════════════════════════════════════════════════════════════════════╝

    /// <summary>
    /// Show a balloon tooltip from the tray icon — replaces AHK's ToolTip-at-cursor OSD.
    /// Uses BalloonTip which is native to NotifyIcon and non-intrusive.
    /// </summary>
    private void ShowOSD(string message, int durationMs = 3000)
    {
        _trayIcon.BalloonTipTitle = "MWBToggle";
        _trayIcon.BalloonTipText = message;
        _trayIcon.BalloonTipIcon = ToolTipIcon.Info;
        _trayIcon.ShowBalloonTip(durationMs);
    }

    // ╔══════════════════════════════════════════════════════════════════════╗
    // ║  Config                                                              ║
    // ╚══════════════════════════════════════════════════════════════════════╝

    private void LoadConfig()
    {
        string iniPath = Path.Combine(AppContext.BaseDirectory, "MWBToggle.ini");
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
    /// </summary>
    internal static string HotkeyToReadable(string hk)
    {
        string mods = "";
        int i = 0;

        // Consume modifier prefix characters
        while (i < hk.Length && "#^!+".Contains(hk[i]))
        {
            mods += hk[i] switch
            {
                '#' => "Win + ",
                '^' => "Ctrl + ",
                '!' => "Alt + ",
                '+' => "Shift + ",
                _ => ""
            };
            i++;
        }

        string key = hk[i..].ToUpperInvariant();
        return mods + key;
    }

    private static Icon? LoadEmbeddedIcon(string resourceName)
    {
        var stream = typeof(MWBToggleApp).Assembly.GetManifestResourceStream(resourceName);
        return stream != null ? new Icon(stream) : null;
    }

    private void ExitApplication()
    {
        _globalHotkey.Dispose();
        _syncTimer.Stop();
        _pauseTimer.Stop();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _globalHotkey.Dispose();
            _syncTimer.Dispose();
            _pauseTimer.Dispose();
            _trayIcon.Dispose();
            _menu.Dispose();
            _aboutForm?.Dispose();
            _iconOn.Dispose();
            _iconOff.Dispose();
        }
        base.Dispose(disposing);
    }
}

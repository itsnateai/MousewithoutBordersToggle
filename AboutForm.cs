using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace MWBToggle;

/// <summary>
/// About dialog — mirrors the AHK ShowAbout() GUI. Hosts the user's Theme
/// preference dropdown (System / Dark / Light) — restart-to-apply via the
/// onThemeChanged callback supplied by MWBToggleApp.
/// </summary>
internal sealed class AboutForm : Form
{
    private readonly Label _primaryHotkeyLabel;
    private readonly Label _fileTransferHotkeyLabel;
    private readonly ComboBox _cboTheme;
    private readonly Action<string> _onThemeChanged;
    // Live snapshot of the theme mode the dropdown is currently showing. Used
    // by the SelectionChangeCommitted lambda to detect "real" changes. Updated
    // both by the user's pick AND by SetThemeMode() on form re-show — so the
    // form is cached (Hide-on-close) and a fallback auto-restart that left the
    // app running with a new persisted ThemeMode doesn't strand a stale
    // ctor-captured value in the closure.
    private string _currentThemeMode;
    // All Font allocations made in the ctor — tracked so Dispose can release
    // them. The form is cached for the process lifetime via Hide-on-close, so
    // without tracking the per-control Fonts leak GDI handles for as long as
    // the app runs. Listed in allocation order for parity-readability.
    private readonly List<Font> _ownedFonts = new();

    public AboutForm(string primaryHotkey, string fileTransferHotkey,
        string currentThemeMode, Action<string> onThemeChanged)
    {
        _onThemeChanged = onThemeChanged;
        _currentThemeMode = currentThemeMode;

        Text = $"MWBToggle v{MWBToggleApp.Version} — About";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        BackColor = Theme.BgColor;
        ForeColor = Theme.FgColor;
        // Pin design baseline to 96 DPI BEFORE setting AutoScaleMode so every
        // Size/Point literal below is interpreted as 96-DPI design pixels
        // regardless of which monitor first realizes this form.
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(300, 245);

        var titleLabel = new Label
        {
            Text = $"MWBToggle v{MWBToggleApp.Version}",
            Font = TrackFont(new Font(Font.FontFamily, 11, FontStyle.Bold)),
            AutoSize = false,
            Size = new Size(280, 25),
            Location = new Point(10, 15),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Theme.FgColor,
            BackColor = Theme.BgColor,
        };
        Controls.Add(titleLabel);

        var descLabel = new Label
        {
            Text = "Toggle Mouse Without Borders\nclipboard and file sharing.",
            AutoSize = false,
            Size = new Size(280, 35),
            Location = new Point(10, 45),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Theme.FgColor,
            BackColor = Theme.BgColor,
        };
        Controls.Add(descLabel);

        // Hotkey rows — title (bold, dim) stacked above the key combo (regular, primary).
        var primaryTitle = new Label
        {
            Text = "Clipboard + File Transfer",
            AutoSize = false,
            Size = new Size(280, 16),
            Location = new Point(10, 82),
            Font = TrackFont(new Font(Font.FontFamily, 8.25f, FontStyle.Bold)),
            ForeColor = Theme.DimColor,
            BackColor = Theme.BgColor,
            TextAlign = ContentAlignment.MiddleCenter
        };
        Controls.Add(primaryTitle);

        _primaryHotkeyLabel = new Label
        {
            AutoSize = false,
            Size = new Size(280, 20),
            Location = new Point(10, 99),
            Font = TrackFont(new Font(Font.FontFamily, 9.5f)),
            ForeColor = Theme.FgColor,
            BackColor = Theme.BgColor,
            TextAlign = ContentAlignment.MiddleCenter
        };
        Controls.Add(_primaryHotkeyLabel);

        var fileTitle = new Label
        {
            Text = "File Transfer",
            AutoSize = false,
            Size = new Size(280, 16),
            Location = new Point(10, 125),
            Font = TrackFont(new Font(Font.FontFamily, 8.25f, FontStyle.Bold)),
            ForeColor = Theme.DimColor,
            BackColor = Theme.BgColor,
            TextAlign = ContentAlignment.MiddleCenter
        };
        Controls.Add(fileTitle);

        _fileTransferHotkeyLabel = new Label
        {
            AutoSize = false,
            Size = new Size(280, 20),
            Location = new Point(10, 142),
            Font = TrackFont(new Font(Font.FontFamily, 9.5f)),
            ForeColor = Theme.FgColor,
            BackColor = Theme.BgColor,
            TextAlign = ContentAlignment.MiddleCenter
        };
        Controls.Add(_fileTransferHotkeyLabel);

        SetHotkeys(primaryHotkey, fileTransferHotkey);

        // ── Theme dropdown (left) + Open log folder link (right) ──────────
        // New in v2.5.17: Theme dropdown lives in About so users can pick
        // System / Dark / Light without a separate Settings dialog. Moved
        // "Open log folder" to the right of the same row to make room.
        var themeLabel = new Label
        {
            Text = "Theme:",
            AutoSize = false,
            Size = new Size(42, 18),
            Location = new Point(10, 173),
            Font = TrackFont(new Font(Font.FontFamily, 8.25f)),
            ForeColor = Theme.FgColor,
            BackColor = Theme.BgColor,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        Controls.Add(themeLabel);

        _cboTheme = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(55, 170),
            Size = new Size(80, 22),
            ForeColor = Theme.FgColor,
            BackColor = Theme.EditBgColor,
            FlatStyle = FlatStyle.Flat,
            Font = TrackFont(new Font(Font.FontFamily, 8.25f)),
        };
        _cboTheme.Items.AddRange(new object[] { "System", "Dark", "Light" });
        int themeIdx = _cboTheme.Items.IndexOf(currentThemeMode);
        _cboTheme.SelectedIndex = themeIdx >= 0 ? themeIdx : 0;
        // Fire on commit only — SelectionChangeCommitted ignores programmatic
        // SelectedIndex assignment above. Without this guard, the ctor's seed
        // would itself fire ApplyThemeMode and the app would restart on every
        // About-open. Compare against _currentThemeMode (live field), NOT the
        // ctor-param `currentThemeMode` — the form is cached, and after a
        // failed auto-restart SetThemeMode() updates the field while the
        // ctor-param stays frozen. Closing over the stale param made the
        // re-pick of a new persisted theme look like "no change" and silently
        // dropped it (T2/T3 verifier finding 2026-05-17).
        _cboTheme.SelectionChangeCommitted += (_, _) =>
        {
            if (_cboTheme.SelectedItem is string mode && mode != _currentThemeMode)
            {
                _currentThemeMode = mode;
                _onThemeChanged(mode);
            }
        };
        Controls.Add(_cboTheme);

        var logLink = new LinkLabel
        {
            Text = "Open log folder",
            AutoSize = false,
            Size = new Size(145, 18),
            Location = new Point(145, 173),
            TextAlign = ContentAlignment.MiddleRight,
            LinkColor = Theme.AccentBlue,
            ActiveLinkColor = Theme.AccentBlue,
            VisitedLinkColor = Theme.AccentBlue,
            BackColor = Theme.BgColor,
        };
        logLink.LinkClicked += (_, _) =>
        {
            var dir = System.IO.Path.GetDirectoryName(Logger.LogPath);
            if (!string.IsNullOrEmpty(dir))
            {
                try { System.IO.Directory.CreateDirectory(dir); } catch { }
                // nosemgrep: gitlab.security_code_scan.SCS0001-1 -- dir is derived from Logger.LogPath, a hardcoded %LOCALAPPDATA%\MWBToggle\mwbtoggle.log constant; no user input reaches this sink
                using var _ = Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
            }
        };
        Controls.Add(logLink);

        // ── Bottom action row: GitHub | Update | Close ────────────────────
        var githubBtn = new Button
        {
            Text = "GitHub",
            Size = new Size(80, 30),
            Location = new Point(25, 200),
            AccessibleName = "Open MWBToggle GitHub page"
        };
        ThemeButton(githubBtn);
        githubBtn.Click += (_, _) =>
        {
            // nosemgrep: gitlab.security_code_scan.SCS0001-1 -- URL is a string literal, zero user input
            using var _ = Process.Start(new ProcessStartInfo("https://github.com/itsnateai/MousewithoutBordersToggle")
            { UseShellExecute = true });
        };
        Controls.Add(githubBtn);

        var updateBtn = new Button
        {
            Text = "Update",
            Size = new Size(70, 30),
            Location = new Point(115, 200),
            AccessibleName = "Check for updates"
        };
        ThemeButton(updateBtn);
        updateBtn.Click += (_, _) =>
        {
            using var dlg = new UpdateDialog();
            dlg.ShowDialog(this);
        };
        Controls.Add(updateBtn);

        var closeBtn = new Button
        {
            Text = "Close",
            Size = new Size(80, 30),
            Location = new Point(195, 200),
            AccessibleName = "Close About dialog"
        };
        ThemeButton(closeBtn);
        closeBtn.Click += (_, _) => Hide();
        Controls.Add(closeBtn);

        AcceptButton = closeBtn;
        CancelButton = closeBtn;
    }

    /// <summary>
    /// Add a Font to the disposal-tracked list and return it. Use as a
    /// pass-through wrapper around <c>new Font(...)</c> at allocation
    /// sites so the Dispose override can release every Font deterministically.
    /// </summary>
    private Font TrackFont(Font font)
    {
        _ownedFonts.Add(font);
        return font;
    }

    private static void ThemeButton(Button btn)
    {
        btn.FlatStyle = FlatStyle.Flat;
        btn.ForeColor = Theme.FgColor;
        btn.BackColor = Theme.BgColor;
        btn.FlatAppearance.BorderColor = Theme.DividerColor;
        // Without explicit hover/pressed colors, FlatStyle.Flat falls back to
        // SystemColors.ButtonHighlight on hover — a light grey that flashes
        // against the dark palette every time the user mouses over a button.
        btn.FlatAppearance.MouseOverBackColor = Theme.HighlightBg;
        btn.FlatAppearance.MouseDownBackColor = Theme.EditBgColor;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyTitleBarMode(Handle);
    }

    /// <summary>
    /// Update the hotkey labels to reflect the caller's current hotkey bindings.
    /// Called on every ShowAbout so a rebind via the picker doesn't leave a stale
    /// label in the cached form instance.
    /// </summary>
    public void SetHotkeys(string primaryHotkey, string fileTransferHotkey)
    {
        _primaryHotkeyLabel.Text = string.IsNullOrEmpty(primaryHotkey)
            ? "(none)"
            : MWBToggleApp.HotkeyToReadable(primaryHotkey);
        _fileTransferHotkeyLabel.Text = string.IsNullOrEmpty(fileTransferHotkey)
            ? "(none)"
            : MWBToggleApp.HotkeyToReadable(fileTransferHotkey);
    }

    /// <summary>
    /// Refresh the Theme dropdown selection. Called on every ShowAbout so a
    /// fallback path (TryAutoRestartForTheme returned false → app kept running
    /// with old palette but new ThemeMode persisted) doesn't leave the cached
    /// form's dropdown showing a stale value. Programmatic SelectedIndex
    /// assignment does NOT fire SelectionChangeCommitted, so this is safe.
    /// Also updates _currentThemeMode so the SelectionChangeCommitted lambda's
    /// "is this a real change" comparison uses the live value, not the
    /// ctor-captured one.
    /// </summary>
    public void SetThemeMode(string mode)
    {
        _currentThemeMode = mode;
        int idx = _cboTheme.Items.IndexOf(mode);
        if (idx >= 0 && idx != _cboTheme.SelectedIndex)
            _cboTheme.SelectedIndex = idx;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Release every Font allocated in the ctor (titleLabel bold, two
            // hotkey-row title fonts, two hotkey-value fonts, themeLabel font,
            // cbo font). The form is cached for process lifetime via
            // Hide-on-close — without this, every Font's GDI handle stays
            // allocated until the process exits, regardless of how many forms
            // were created. Each handle survives a theme-restart respawn
            // because the new process gets its own AboutForm and tracks anew.
            foreach (var f in _ownedFonts)
            {
                try { f.Dispose(); } catch { /* font already disposed */ }
            }
            _ownedFonts.Clear();
        }
        base.Dispose(disposing);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Hide instead of close, so it can be re-shown (mirrors AHK behavior)
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
    }
}

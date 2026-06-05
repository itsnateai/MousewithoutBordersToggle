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
    private readonly Button _primaryHotkeyField;
    private readonly Button _fileTransferHotkeyField;
    private readonly ComboBox _cboTheme;
    private readonly Action<string> _onThemeChanged;
    private readonly Action _onEditPrimaryHotkey;
    private readonly Action _onEditFileTransferHotkey;
    private readonly Button _closeBtn;
    // Hover hint on the two click-to-edit hotkey fields. Disposed explicitly — a
    // ToolTip is a Component, not a child Control, so the base Form.Dispose sweep
    // of the Controls collection never releases it.
    private readonly ToolTip _hotkeyFieldTip = new();
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
        string currentThemeMode, Action<string> onThemeChanged,
        Action onEditPrimaryHotkey, Action onEditFileTransferHotkey)
    {
        _onThemeChanged = onThemeChanged;
        _onEditPrimaryHotkey = onEditPrimaryHotkey;
        _onEditFileTransferHotkey = onEditFileTransferHotkey;
        _currentThemeMode = currentThemeMode;

        Text = $"MWBToggle v{MWBToggleApp.Version} — About";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        BackColor = Theme.BgColor;
        ForeColor = Theme.FgColor;
        // Pin design baseline to 96 DPI before AutoScaleMode. The window then
        // SIZES ITSELF to its (font-scaled) content via AutoSize instead of a
        // fixed ClientSize — a direct high-DPI launch fails to grow a fixed
        // ClientSize (fonts scale, bounds don't → clipped text), so the layout
        // is relational (TableLayoutPanel + AutoSize) and correct by construction
        // at 100% and 150% alike. See feedback_winforms_layout_first_not_magic_numbers.
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        // ── Root vertical stack — one AutoSize column; every row sizes to content ──
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            BackColor = Theme.BgColor,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // Title
        var titleLabel = new Label
        {
            Text = $"MWBToggle v{MWBToggleApp.Version}",
            Font = TrackFont(new Font(Font.FontFamily, 11, FontStyle.Bold)),
            AutoSize = true,
            Anchor = AnchorStyles.None,
            ForeColor = Theme.FgColor,
            BackColor = Theme.BgColor,
            Margin = new Padding(3, 3, 3, 8),
        };
        root.Controls.Add(titleLabel);

        // Description (two lines, centred)
        var descLabel = new Label
        {
            Text = "Toggle Mouse Without Borders\nclipboard and file sharing.",
            AutoSize = true,
            Anchor = AnchorStyles.None,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Theme.FgColor,
            BackColor = Theme.BgColor,
            Margin = new Padding(3, 0, 3, 10),
        };
        root.Controls.Add(descLabel);

        // Hotkey rows — bold dim title stacked above the regular-weight combo, both centred.
        var primaryTitle = new Label
        {
            Text = "Clipboard + File Transfer",
            AutoSize = true,
            Anchor = AnchorStyles.None,
            Font = TrackFont(new Font(Font.FontFamily, 8.25f, FontStyle.Bold)),
            ForeColor = Theme.DimColor,
            BackColor = Theme.BgColor,
            Margin = new Padding(3, 0, 3, 1),
        };
        root.Controls.Add(primaryTitle);

        _primaryHotkeyField = MakeHotkeyField(new Padding(3, 0, 3, 6), () => _onEditPrimaryHotkey());
        _hotkeyFieldTip.SetToolTip(_primaryHotkeyField, "Click to change");
        root.Controls.Add(_primaryHotkeyField);

        var fileTitle = new Label
        {
            Text = "File Transfer",
            AutoSize = true,
            Anchor = AnchorStyles.None,
            Font = TrackFont(new Font(Font.FontFamily, 8.25f, FontStyle.Bold)),
            ForeColor = Theme.DimColor,
            BackColor = Theme.BgColor,
            Margin = new Padding(3, 0, 3, 1),
        };
        root.Controls.Add(fileTitle);

        _fileTransferHotkeyField = MakeHotkeyField(new Padding(3, 0, 3, 10), () => _onEditFileTransferHotkey());
        _hotkeyFieldTip.SetToolTip(_fileTransferHotkeyField, "Click to change");
        root.Controls.Add(_fileTransferHotkeyField);

        SetHotkeys(primaryHotkey, fileTransferHotkey);

        // ── Theme dropdown (left) + Open log folder link (right), one full-width row ──
        // New in v2.5.17: Theme dropdown lives in About so users can pick System /
        // Dark / Light without a separate Settings dialog.
        var themeRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Theme.BgColor,
            Margin = new Padding(3, 4, 3, 10),
        };
        themeRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // "Theme:"
        themeRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // combo
        themeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // link (right)

        var themeLabel = new Label
        {
            Text = "Theme:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = TrackFont(new Font(Font.FontFamily, 8.25f)),
            ForeColor = Theme.FgColor,
            BackColor = Theme.BgColor,
            Margin = new Padding(0, 3, 6, 0),
        };
        themeRow.Controls.Add(themeLabel, 0, 0);

        _cboTheme = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor = AnchorStyles.Left,
            // Width sized to the longest item ("System") at the current DPI rather than a
            // pixel literal — AutoSize isn't supported on ComboBox, so derive from the font.
            ForeColor = Theme.FgColor,
            BackColor = Theme.EditBgColor,
            FlatStyle = FlatStyle.Flat,
            Font = TrackFont(new Font(Font.FontFamily, 8.25f)),
            Margin = new Padding(0, 0, 0, 0),
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
        themeRow.Controls.Add(_cboTheme, 1, 0);

        var logLink = new LinkLabel
        {
            Text = "Open log folder",
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            LinkColor = Theme.AccentBlue,
            ActiveLinkColor = Theme.AccentBlue,
            VisitedLinkColor = Theme.AccentBlue,
            BackColor = Theme.BgColor,
            Margin = new Padding(12, 3, 0, 0),
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
        themeRow.Controls.Add(logLink, 2, 0);

        root.Controls.Add(themeRow);

        // ── Bottom action row: GitHub | Update | Close, centred and evenly spaced ──
        var buttonRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            Anchor = AnchorStyles.None,
            WrapContents = false,
            BackColor = Theme.BgColor,
            Margin = new Padding(3, 0, 3, 0),
        };

        var githubBtn = new Button
        {
            Text = "GitHub",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10, 5, 10, 5),
            Margin = new Padding(0, 0, 8, 0),
            AccessibleName = "Open MWBToggle GitHub page",
        };
        ThemeButton(githubBtn);
        githubBtn.Click += (_, _) =>
        {
            // nosemgrep: gitlab.security_code_scan.SCS0001-1 -- URL is a string literal, zero user input
            using var _ = Process.Start(new ProcessStartInfo("https://github.com/itsnateai/MousewithoutBordersToggle")
            { UseShellExecute = true });
        };
        buttonRow.Controls.Add(githubBtn);

        var updateBtn = new Button
        {
            Text = "Update",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10, 5, 10, 5),
            Margin = new Padding(0, 0, 8, 0),
            AccessibleName = "Check for updates",
        };
        ThemeButton(updateBtn);
        updateBtn.Click += (_, _) =>
        {
            using var dlg = new UpdateDialog();
            dlg.ShowDialog(this);
        };
        buttonRow.Controls.Add(updateBtn);

        var closeBtn = new Button
        {
            Text = "Close",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10, 5, 10, 5),
            Margin = new Padding(0, 0, 0, 0),
            AccessibleName = "Close About dialog",
        };
        ThemeButton(closeBtn);
        closeBtn.Click += (_, _) => Hide();
        buttonRow.Controls.Add(closeBtn);
        _closeBtn = closeBtn;

        root.Controls.Add(buttonRow);

        Controls.Add(root);

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

    /// <summary>
    /// Build a click-to-edit hotkey field: a flat, edit-field-styled button showing
    /// the current combo that opens the hotkey picker on click. Styled to match the
    /// Theme combo (EditBg fill + divider border) so it reads as an editable field,
    /// not a command button. AutoSize so it scales with the font at 150% DPI (the
    /// whole form is layout-first — see feedback_winforms_layout_first_not_magic_numbers).
    /// </summary>
    private Button MakeHotkeyField(Padding margin, Action onClick)
    {
        var b = new Button
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.None,
            Font = TrackFont(new Font(Font.FontFamily, 9.5f)),
            ForeColor = Theme.FgColor,
            BackColor = Theme.EditBgColor,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            Padding = new Padding(10, 4, 10, 4),
            Margin = margin,
            AccessibleName = "Change hotkey",
        };
        b.FlatAppearance.BorderColor = Theme.DividerColor;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.MouseOverBackColor = Theme.HighlightBg;
        b.FlatAppearance.MouseDownBackColor = Theme.EditBgColor;
        b.Click += (_, _) =>
        {
            // The picker is itself TopMost + app-modal. This About dialog is ALSO
            // TopMost; two TopMost windows race for z-order, so drop ours for the
            // duration of the (blocking) picker and restore it after — guarantees
            // the picker sits above us regardless of activation timing.
            bool wasTop = TopMost;
            TopMost = false;
            try { onClick(); }
            finally { TopMost = wasTop; }
        };
        return b;
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
        // The Theme ComboBox is fixed-width and AutoScale won't grow it at 150% — size it
        // to its longest item ("System") at the device DPI. See DpiFit (EQSwitch CardLayout).
        DpiFit.SizeFitFields(this);
        // AutoSize form grows from its top-left; re-center if SetHotkeys changes the label
        // widths on a cached re-show so the dialog doesn't drift off-centre.
        DpiFit.KeepCentered(this);
        // Open with focus on Close, not the first hotkey field — the dialog reads as
        // informational, and a focused edit-styled field would otherwise show a
        // capture-armed focus cue the moment it opens.
        ActiveControl = _closeBtn;
    }

    /// <summary>
    /// Update the hotkey labels to reflect the caller's current hotkey bindings.
    /// Called on every ShowAbout so a rebind via the picker doesn't leave a stale
    /// label in the cached form instance.
    /// </summary>
    public void SetHotkeys(string primaryHotkey, string fileTransferHotkey)
    {
        _primaryHotkeyField.Text = string.IsNullOrEmpty(primaryHotkey)
            ? "(none)"
            : MWBToggleApp.HotkeyToReadable(primaryHotkey);
        _fileTransferHotkeyField.Text = string.IsNullOrEmpty(fileTransferHotkey)
            ? "(none)"
            : MWBToggleApp.HotkeyToReadable(fileTransferHotkey);
        // A rebind via the picker changes the field width; if the form is already
        // on-screen (AutoSize grows from the top-left), re-center so it doesn't drift.
        if (IsHandleCreated && Visible)
            DpiFit.KeepCentered(this);
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
            // ToolTip is a Component, not a child Control — release it explicitly
            // (the base Form.Dispose only sweeps the Controls collection).
            _hotkeyFieldTip.Dispose();
            // Release every Font allocated in the ctor (titleLabel bold, two
            // hotkey-row title fonts, two hotkey-field value fonts, themeLabel
            // font, cbo font). The form is cached for process lifetime via
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

using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace MWBToggle;

/// <summary>
/// About dialog — mirrors the AHK ShowAbout() GUI.
/// </summary>
internal sealed class AboutForm : Form
{
    private readonly Label _primaryHotkeyLabel;
    private readonly Label _fileTransferHotkeyLabel;
    private static readonly Color HeaderColor = Color.FromArgb(90, 95, 105);
    private static readonly Color ValueColor = Color.FromArgb(30, 30, 30);

    public AboutForm(string primaryHotkey, string fileTransferHotkey)
    {
        Text = $"MWBToggle v{MWBToggleApp.Version} — About";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        ClientSize = new Size(300, 245);

        var titleLabel = new Label
        {
            Text = $"MWBToggle v{MWBToggleApp.Version}",
            Font = new Font(Font.FontFamily, 11, FontStyle.Bold),
            AutoSize = false,
            Size = new Size(280, 25),
            Location = new Point(10, 15),
            TextAlign = ContentAlignment.MiddleCenter
        };
        Controls.Add(titleLabel);

        var descLabel = new Label
        {
            Text = "Toggle Mouse Without Borders\nclipboard and file sharing.",
            AutoSize = false,
            Size = new Size(280, 35),
            Location = new Point(10, 45),
            TextAlign = ContentAlignment.MiddleCenter
        };
        Controls.Add(descLabel);

        // Hotkey rows — title label (bold, muted) stacked above the key combo (regular,
        // darker). Makes the hotkey values the visual anchor instead of burying them
        // in a single "Label: value" line where the prose overwhelms the combo.
        var primaryTitle = new Label
        {
            Text = "Clipboard + File Transfer",
            AutoSize = false,
            Size = new Size(280, 16),
            Location = new Point(10, 82),
            Font = new Font(Font.FontFamily, 8.25f, FontStyle.Bold),
            ForeColor = HeaderColor,
            TextAlign = ContentAlignment.MiddleCenter
        };
        Controls.Add(primaryTitle);

        _primaryHotkeyLabel = new Label
        {
            AutoSize = false,
            Size = new Size(280, 20),
            Location = new Point(10, 99),
            Font = new Font(Font.FontFamily, 9.5f),
            ForeColor = ValueColor,
            TextAlign = ContentAlignment.MiddleCenter
        };
        Controls.Add(_primaryHotkeyLabel);

        var fileTitle = new Label
        {
            Text = "File Transfer",
            AutoSize = false,
            Size = new Size(280, 16),
            Location = new Point(10, 125),
            Font = new Font(Font.FontFamily, 8.25f, FontStyle.Bold),
            ForeColor = HeaderColor,
            TextAlign = ContentAlignment.MiddleCenter
        };
        Controls.Add(fileTitle);

        _fileTransferHotkeyLabel = new Label
        {
            AutoSize = false,
            Size = new Size(280, 20),
            Location = new Point(10, 142),
            Font = new Font(Font.FontFamily, 9.5f),
            ForeColor = ValueColor,
            TextAlign = ContentAlignment.MiddleCenter
        };
        Controls.Add(_fileTransferHotkeyLabel);

        SetHotkeys(primaryHotkey, fileTransferHotkey);

        var logLink = new LinkLabel
        {
            Text = "Open log folder",
            AutoSize = false,
            Size = new Size(280, 18),
            Location = new Point(10, 170),
            TextAlign = ContentAlignment.MiddleCenter
        };
        logLink.LinkClicked += (_, _) =>
        {
            var dir = System.IO.Path.GetDirectoryName(Logger.LogPath);
            if (!string.IsNullOrEmpty(dir))
            {
                try { System.IO.Directory.CreateDirectory(dir); } catch { }
                using var _ = Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
            }
        };
        Controls.Add(logLink);

        var githubBtn = new Button
        {
            Text = "GitHub",
            Size = new Size(80, 30),
            Location = new Point(25, 200),
            AccessibleName = "Open MWBToggle GitHub page"
        };
        githubBtn.Click += (_, _) =>
        {
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
        closeBtn.Click += (_, _) => Hide();
        Controls.Add(closeBtn);

        // Enter activates Close, Esc also closes — mirrors a standard About dialog.
        AcceptButton = closeBtn;
        CancelButton = closeBtn;
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

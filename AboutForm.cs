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

    public AboutForm(string primaryHotkey, string fileTransferHotkey)
    {
        Text = $"MWBToggle v{MWBToggleApp.Version} — About";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        ClientSize = new Size(300, 225);

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

        _primaryHotkeyLabel = new Label
        {
            AutoSize = false,
            Size = new Size(280, 20),
            Location = new Point(10, 85),
            TextAlign = ContentAlignment.MiddleCenter
        };
        Controls.Add(_primaryHotkeyLabel);

        _fileTransferHotkeyLabel = new Label
        {
            AutoSize = false,
            Size = new Size(280, 20),
            Location = new Point(10, 107),
            TextAlign = ContentAlignment.MiddleCenter
        };
        Controls.Add(_fileTransferHotkeyLabel);

        SetHotkeys(primaryHotkey, fileTransferHotkey);

        var logLink = new LinkLabel
        {
            Text = "Open log folder",
            AutoSize = false,
            Size = new Size(280, 18),
            Location = new Point(10, 132),
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

        var copyrightLabel = new Label
        {
            Text = "© 2026 itsnateai · MIT License",
            AutoSize = false,
            Size = new Size(280, 18),
            Location = new Point(10, 153),
            ForeColor = Color.FromArgb(110, 110, 110),
            Font = new Font(Font.FontFamily, 8.25f),
            TextAlign = ContentAlignment.MiddleCenter
        };
        Controls.Add(copyrightLabel);

        var githubBtn = new Button
        {
            Text = "GitHub",
            Size = new Size(80, 30),
            Location = new Point(25, 175),
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
            Location = new Point(115, 175),
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
            Location = new Point(195, 175),
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
        _primaryHotkeyLabel.Text = "Clipboard + File Transfer: " + MWBToggleApp.HotkeyToReadable(primaryHotkey);
        _fileTransferHotkeyLabel.Text = "File Transfer: " + (
            string.IsNullOrEmpty(fileTransferHotkey)
                ? "(none)"
                : MWBToggleApp.HotkeyToReadable(fileTransferHotkey));
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

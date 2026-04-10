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
    public AboutForm(string hotkey)
    {
        Text = $"MWBToggle v{MWBToggleApp.Version} — About";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        ClientSize = new Size(300, 170);

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

        var hotkeyLabel = new Label
        {
            Text = "Hotkey: " + MWBToggleApp.HotkeyToReadable(hotkey),
            AutoSize = false,
            Size = new Size(280, 20),
            Location = new Point(10, 85),
            TextAlign = ContentAlignment.MiddleCenter
        };
        Controls.Add(hotkeyLabel);

        var githubBtn = new Button
        {
            Text = "GitHub",
            Size = new Size(80, 30),
            Location = new Point(25, 120)
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
            Location = new Point(115, 120)
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
            Location = new Point(195, 120)
        };
        closeBtn.Click += (_, _) => Hide();
        Controls.Add(closeBtn);
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

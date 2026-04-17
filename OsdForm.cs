using System.Runtime.InteropServices;

namespace MWBToggle;

/// <summary>
/// Discreet borderless OSD pinned above the system tray. Ported from MicMute's
/// OsdForm — canonical tooltip template for tray utilities. Click-through,
/// no-activate, auto-dismiss, cached GDI resources.
/// </summary>
internal sealed class OsdForm : Form
{
    internal enum State { Info, On, Off }

    private readonly System.Windows.Forms.Timer _dismissTimer;
    private bool _disposed;

    private string _text = string.Empty;
    private State _state;

    private static readonly Font s_dotFont = new("Segoe UI", 10f);
    private static readonly Font s_labelFont = new("Segoe UI Semibold", 9f);
    private static readonly SolidBrush s_bgBrush = new(Color.FromArgb(0x1E, 0x1E, 0x1E));
    private static readonly SolidBrush s_textBrush = new(Color.FromArgb(0xE0, 0xE0, 0xE0));
    private static readonly SolidBrush s_onDotBrush = new(Color.FromArgb(0x2E, 0xCC, 0x71));   // green
    private static readonly SolidBrush s_offDotBrush = new(Color.FromArgb(0xE0, 0x40, 0x40));  // red
    private static readonly SolidBrush s_infoDotBrush = new(Color.FromArgb(0x9A, 0x9A, 0x9A)); // neutral gray

    private const string DotChar = "\u25CF"; // ●

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    public OsdForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(0x1E, 0x1E, 0x1E);

        _dismissTimer = new System.Windows.Forms.Timer();
        _dismissTimer.Tick += (_, _) =>
        {
            _dismissTimer.Stop();
            if (!_disposed) Hide();
        };
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW — no taskbar entry
            cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT — click-through
            cp.ExStyle |= 0x00000008; // WS_EX_TOPMOST
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    public void ShowMessage(string text, int durationMs, State state)
    {
        if (_disposed) return;
        if (string.IsNullOrWhiteSpace(text)) return;

        _text = text;
        _state = state;

        using (var g = CreateGraphics())
        {
            var labelSize = g.MeasureString(_text, s_labelFont);
            int w = 12 + 14 + (int)Math.Ceiling(labelSize.Width) + 16;
            int h = 32;

            // Default anchor: bottom-right corner of the working area. WorkingArea
            // already excludes the taskbar regardless of its edge (top / left / right /
            // bottom), so this is safe for every taskbar orientation.
            var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            int xPos = screen.WorkingArea.Right - w - 12;
            int yPos = screen.WorkingArea.Bottom - h - 8;

            // Refine only when the taskbar is at the BOTTOM edge (rect.Top > 0).
            // For top / left / right taskbars, Shell_TrayWnd's rect has Top == 0,
            // which would place yPos off-screen — fall back to WorkingArea in those
            // cases (handled by the default above).
            nint trayHwnd = FindWindow("Shell_TrayWnd", null);
            if (trayHwnd != 0 && GetWindowRect(trayHwnd, out var rect) && rect.Top > 0)
            {
                xPos = rect.Right - w - 12;
                yPos = rect.Top - h - 8;
            }

            SetBounds(xPos, yPos, w, h);
        }

        // DwmSetWindowAttribute returns HRESULT (not throws). Non-zero on pre-Win11
        // where DWMWA_WINDOW_CORNER_PREFERENCE isn't supported — ignore silently, rounded
        // corners are an enhancement, not a requirement.
        int preference = DWMWCP_ROUND;
        _ = DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));

        Opacity = 235.0 / 255.0;
        Invalidate();

        if (!Visible) Show();

        _dismissTimer.Stop();
        _dismissTimer.Interval = Math.Max(500, durationMs);
        _dismissTimer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.FillRectangle(s_bgBrush, ClientRectangle);

        var dotBrush = _state switch
        {
            State.On => s_onDotBrush,
            State.Off => s_offDotBrush,
            _ => s_infoDotBrush
        };
        g.DrawString(DotChar, s_dotFont, dotBrush, 12, 6);
        g.DrawString(_text, s_labelFont, s_textBrush, 28, 6);
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _dismissTimer.Stop();
            _dismissTimer.Dispose();
            _disposed = true;
        }
        base.Dispose(disposing);
    }
}

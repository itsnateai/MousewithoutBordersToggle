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

    // Cached label measurement from the most recent ShowMessage() call.
    // OnPaint uses .Height to vertically centre the text against the pill,
    // which itself grows DPI-aware via Math.Max(28, _labelSize.Height + 10).
    // Re-measured each Show* so it tracks the current text + DPI.
    private Size _labelSize;

    // Discreet palette — softened from the original high-contrast variant.
    // Regular weight (not semibold), slightly warmer greys, muted accent dots.
    private static readonly Font s_dotFont = new("Segoe UI", 9f);
    private static readonly Font s_labelFont = new("Segoe UI", 9f);
    private static readonly SolidBrush s_bgBrush = new(Color.FromArgb(0x24, 0x24, 0x26));
    private static readonly SolidBrush s_textBrush = new(Color.FromArgb(0xC8, 0xC8, 0xCC));
    private static readonly SolidBrush s_onDotBrush = new(Color.FromArgb(0x4C, 0xB8, 0x74));   // muted green
    private static readonly SolidBrush s_offDotBrush = new(Color.FromArgb(0xCC, 0x5A, 0x5A));  // muted red
    private static readonly SolidBrush s_infoDotBrush = new(Color.FromArgb(0x80, 0x80, 0x84)); // neutral gray

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
        BackColor = Color.FromArgb(0x24, 0x24, 0x26);
        // Pin design baseline to 96 DPI BEFORE setting AutoScaleMode. SetBounds()
        // below bypasses the AutoScale walk for the form chrome itself (we
        // compute the pill dimensions imperatively from the measured text),
        // but the pin is canonical per _.claude/_templates/snippets/csharp/winforms-dpi-scaling.md
        // and prevents any child controls (none today, but future-proof) from
        // double-scaling on 125%+ monitors.
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

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
            var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            var workArea = screen.WorkingArea;

            var labelSize = g.MeasureString(_text, s_labelFont);
            // Cache for OnPaint vertical centring. MeasureString returns SizeF;
            // promote to Size (ceil) so the int arithmetic in OnPaint is stable.
            _labelSize = Size.Ceiling(labelSize);
            // Padding: left 10 · dot 12 · text · right 12. Tighter than before so the
            // pill doesn't feel oversized for the shorter phrasing we now use.
            int w = 10 + 12 + _labelSize.Width + 12;
            // Pathological long messages would otherwise extend past the screen edge.
            // Cap to half the working-area width; overflow ellipsizes cleanly at paint.
            int maxW = Math.Max(160, workArea.Width / 2);
            if (w > maxW) w = maxW;
            // Pill height must clear the rendered text + padding at any display scale.
            // At 100% scale 9pt Segoe UI is ~15px tall and 28px gave generous breathing
            // room; at 175% scale it renders ~26px and the hardcoded 28 clipped
            // descenders. Floor at 28 preserves the intended visual weight at 100%.
            int h = Math.Max(28, _labelSize.Height + 10);

            // Default anchor: bottom-right corner of the working area. WorkingArea
            // already excludes the taskbar regardless of its edge (top / left / right /
            // bottom), so this is the safe fallback for every taskbar orientation.
            // Canonical positioning pattern — ported from MicMute's OsdForm
            // (the "tooltip template" standard for tray utilities).
            int xPos = workArea.Right - w - 12;
            int yPos = workArea.Bottom - h - 8;

            // Try precise Shell_TrayWnd anchoring; accept it only if it stays
            // inside the working area. Top / left / right taskbars put the
            // naive anchor off-screen — the bounds check rejects those and
            // the working-area fallback wins.
            nint trayHwnd = FindWindow("Shell_TrayWnd", null);
            if (trayHwnd != 0 && GetWindowRect(trayHwnd, out var rect))
            {
                int anchoredX = rect.Right - w - 12;
                int anchoredY = rect.Top - h - 8;
                if (anchoredY >= workArea.Top &&
                    anchoredX >= workArea.Left &&
                    anchoredX + w <= workArea.Right)
                {
                    xPos = anchoredX;
                    yPos = anchoredY;
                }
            }

            // Final safety clamp: even with the anchor/fallback logic above, a
            // pathological width + narrow screen could leave xPos past the left edge.
            if (xPos < workArea.Left + 8) xPos = workArea.Left + 8;

            SetBounds(xPos, yPos, w, h);
        }

        // DwmSetWindowAttribute returns HRESULT (not throws). Non-zero on pre-Win11
        // where DWMWA_WINDOW_CORNER_PREFERENCE isn't supported — ignore silently, rounded
        // corners are an enhancement, not a requirement.
        int preference = DWMWCP_ROUND;
        _ = DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));

        Opacity = 215.0 / 255.0;
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

        // Defensive: a stray paint that fires before any ShowMessage() call (e.g.,
        // a hypothetical future caller that Show()s the form without ShowMessage())
        // would otherwise centre against an empty label height and render the dot
        // + empty _text at the form's vertical centre. Today there's no such path,
        // but the guard costs nothing and keeps the assumption explicit.
        if (_labelSize.IsEmpty) return;

        var dotBrush = _state switch
        {
            State.On => s_onDotBrush,
            State.Off => s_offDotBrush,
            _ => s_infoDotBrush
        };
        // Vertically centre text + dot against the pill — pill height is DPI-aware
        // (Math.Max(28, _labelSize.Height + 10)), so a fixed top-padding like the
        // previous y=5 left both glyphs top-pinned at higher scales. Use the cached
        // label height to centre regardless of current pill height.
        int textY = Math.Max(0, (ClientSize.Height - _labelSize.Height) / 2);
        g.DrawString(DotChar, s_dotFont, dotBrush, 10, textY);
        g.DrawString(_text, s_labelFont, s_textBrush, 24, textY);
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

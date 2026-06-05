#if DEBUG
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace MWBToggle;

/// <summary>
/// DEBUG-only offscreen renderer for DPI verification. Constructs each UI surface
/// with stub args, renders it to a PNG, and records the form's <c>DeviceDpi</c> so a
/// 100% (96 DPI) vs 150% (144 DPI) image diff is ground truth — beating any static
/// "this will clip" reasoning. Screen-DPI-aware: on a multi-monitor host it renders
/// the OSD on every distinct-DPI panel, so both scales are captured locally with no VM.
/// Writes a <c>_summary.txt</c> directly (Logger output is lost to Environment.Exit's
/// no-flush teardown). Invoked from <see cref="Program"/> via
/// <c>--diag-render-form &lt;about|update|osd|all&gt; --out &lt;dir&gt;</c>.
/// Never compiled into Release (the whole file is under <c>#if DEBUG</c>).
/// </summary>
internal static class DiagRender
{
    public static int Run(string which, string outDir)
    {
        var summary = new StringBuilder();
        void Note(string s) { Logger.Info("DiagRender " + s); summary.AppendLine(s); }

        try
        {
            Directory.CreateDirectory(outDir);
            // OsdForm + ThemedMenuRenderer capture Theme.* into static GDI caches at
            // first touch, and About/Update read Theme.* at construction — palette first.
            Theme.Initialize(Theme.ResolveIsDark("System"));
            which = (which ?? "all").Trim().ToLowerInvariant();

            var withDpi = Screen.AllScreens.Select(s => (s, dpi: ProbeDpi(s))).ToArray();
            foreach (var (s, dpi) in withDpi)
                Note($"screen {s.DeviceName} primary={s.Primary} bounds={s.Bounds} dpi={dpi}");

            var hi = withDpi.OrderByDescending(x => x.dpi).First();
            Note($"which={which} out={outDir} dialogTarget={hi.s.DeviceName}@{hi.dpi}");

            // Dialogs: render on the highest-DPI panel (that's where bugs hide; 100% is
            // the design intent already). AutoScale runs in the Show pipeline.
            if (which is "all" or "about") Note(RenderForm("about", new AboutForm("#^+f", "!m", "System", _ => { }, () => { }, () => { }), outDir, hi.s));
            if (which is "all" or "update") Note(RenderForm("update", new UpdateDialog(), outDir, hi.s));
            if (which is "all" or "picker") Note(RenderForm("picker", MWBToggleApp.BuildHotkeyPickerForm("Set Primary Hotkey", "#^+f", allowUnbind: true).Form, outDir, hi.s));

            // OSD: once per DISTINCT panel DPI so the owner-draw dot/text offsets are
            // captured at every scale present on the host.
            if (which is "all" or "osd")
            {
                var perDpi = withDpi.GroupBy(x => x.dpi).Select(g => g.First()).ToArray();
                foreach (var (text, state, tag) in OsdCases())
                    foreach (var (s, _) in perDpi)
                        Note(RenderOsd(tag, state, text, outDir, s));
            }

            Note("done.");
            return 0;
        }
        catch (Exception ex)
        {
            Note($"FATAL: {ex}");
            return 1;
        }
        finally
        {
            try { File.WriteAllText(Path.Combine(outDir, "_summary.txt"), summary.ToString()); }
            catch (Exception ex) { Logger.Warn($"DiagRender summary write: {ex.Message}"); }
        }
    }

    /// <summary>
    /// Show a single dialog under a REAL message loop (Application.Run) so PerMonitorV2's
    /// full initial AutoScale applies exactly as in the running app, then auto-close after
    /// a capture window so an external screenshot (lab shot) records the true rendering.
    /// This distinguishes a genuine DPI bug from a harness limitation (Show+DoEvents has no
    /// real pump). Run in the guest's interactive session; capture with lab shot.
    /// </summary>
    public static int RunShow(string which)
    {
        try
        {
            Theme.Initialize(Theme.ResolveIsDark("System"));
            Form f = which.Trim().ToLowerInvariant() switch
            {
                "update" => new UpdateDialog(),
                "picker" => MWBToggleApp.BuildHotkeyPickerForm("Set Primary Hotkey", "#^+f", allowUnbind: true).Form,
                _        => new AboutForm("#^+f", "!m", "System", _ => { }, () => { }, () => { }),
            };
            // Show the form ONLY once the message loop is actively pumping (a Timer tick
            // fires from inside Application.Run), so PerMonitorV2's initial AutoScale runs
            // under the SAME conditions as the real app — which shows its dialogs from an
            // already-running MWBToggleApp pump, not via a pre-Run f.Show(). A pre-Run show
            // is itself a suspected artifact for the "design-size" captures.
            var showTimer = new System.Windows.Forms.Timer { Interval = 250 };
            showTimer.Tick += (_, _) => { showTimer.Stop(); f.Show(); };
            showTimer.Start();
            var closeTimer = new System.Windows.Forms.Timer { Interval = 20000 };
            closeTimer.Tick += (_, _) => { closeTimer.Stop(); Application.ExitThread(); };
            closeTimer.Start();
            Application.Run(); // real message pump — full PerMonitorV2 form init
            return 0;
        }
        catch (Exception ex)
        {
            Logger.Error($"DiagRender.RunShow: {ex}");
            return 1;
        }
    }

    private static (string text, OsdForm.State state, string tag)[] OsdCases() => new[]
    {
        ("Clipboard + File · ON", OsdForm.State.On, "on"),
        ("Clipboard + File · OFF", OsdForm.State.Off, "off"),
        ("Paused · 30 min", OsdForm.State.Info, "info"),
    };

    /// <summary>Effective DPI of a monitor — Show a 1px PerMonitorV2 form on it and read DeviceDpi.
    /// Show (not bare Handle) is required: the per-monitor DPI is resolved in the Show pipeline.</summary>
    private static int ProbeDpi(Screen s)
    {
        using var probe = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Size = new Size(1, 1),
            Location = new Point(s.Bounds.X + s.Bounds.Width / 2, s.Bounds.Y + s.Bounds.Height / 2),
        };
        probe.Show();
        Application.DoEvents();
        int dpi = probe.DeviceDpi;
        probe.Hide();
        return dpi;
    }

    /// <summary>
    /// Show a normal Form on <paramref name="target"/> so the full Show pipeline (handle
    /// creation → PerformAutoScale → layout → paint) runs at that monitor's DPI exactly as
    /// a user sees it, then snapshot via DrawToBitmap (WM_PRINT — visibility-independent).
    /// </summary>
    private static string RenderForm(string name, Form f, string outDir, Screen target)
    {
        try
        {
            using (f)
            {
                // Respect the form's own StartPosition (dialogs use CenterScreen + TopMost)
                // so this mirrors EXACTLY how the real app shows them — a manual location
                // override was masking PerMonitorV2's initial AutoScale.
                f.Show();
                Application.DoEvents();
                Application.DoEvents();

                // Dual capture to settle DrawToBitmap-vs-real-pixels:
                //   draw   = DrawToBitmap (WM_PRINT, logical render)
                //   screen = CopyFromScreen of the shown window (true on-screen pixels)
                // If their sizes disagree, DrawToBitmap was the artifact and the app is fine.
                int dw = Math.Max(1, f.Width), dh = Math.Max(1, f.Height);
                using (var draw = new Bitmap(dw, dh))
                {
                    f.DrawToBitmap(draw, new Rectangle(0, 0, dw, dh));
                    draw.Save(Path.Combine(outDir, $"{name}_draw_dpi{f.DeviceDpi}.png"), ImageFormat.Png);
                }
                var b = f.Bounds;
                int sw = Math.Max(1, b.Width), sh = Math.Max(1, b.Height);
                using (var scr = new Bitmap(sw, sh))
                {
                    using (var g = Graphics.FromImage(scr))
                        g.CopyFromScreen(b.Location, Point.Empty, new Size(sw, sh));
                    scr.Save(Path.Combine(outDir, $"{name}_screen_dpi{f.DeviceDpi}.png"), ImageFormat.Png);
                }
                f.Hide();
                return $"{name}: DeviceDpi={f.DeviceDpi} client={f.ClientSize} draw={dw}x{dh} screen={sw}x{sh} on={target.DeviceName}";
            }
        }
        catch (Exception ex)
        {
            return $"{name} FAILED: {ex.Message}";
        }
    }

    /// <summary>
    /// The OSD is a layered (Opacity &lt; 1), click-through, owner-drawn pill — DrawToBitmap
    /// can come back blank on layered windows, so capture on-screen via CopyFromScreen. We
    /// Show the form on <paramref name="target"/> FIRST (resolves DeviceDpi there), THEN call
    /// ShowMessage so its in-paint MeasureString runs at that DPI; the DEBUG seam pins the
    /// pill onto that monitor.
    /// </summary>
    private static string RenderOsd(string tag, OsdForm.State state, string text, string outDir, Screen target)
    {
        try
        {
            using var osd = new OsdForm();
            osd.StartPosition = FormStartPosition.Manual;
            var pt = new Point(target.WorkingArea.X + 60, target.WorkingArea.Y + 60);
            osd.Location = pt;
            OsdForm.DiagForceTopLeft = pt;

            osd.Show();              // resolve DeviceDpi to target monitor
            Application.DoEvents();
            osd.ShowMessage(text, 4000, state); // measure+paint at resolved DPI, pinned at pt
            Application.DoEvents();

            var b = osd.Bounds;
            int w = Math.Max(1, b.Width);
            int h = Math.Max(1, b.Height);
            using var bmp = new Bitmap(w, h);
            using (var g = Graphics.FromImage(bmp))
                g.CopyFromScreen(b.Location, Point.Empty, new Size(w, h));

            string file = $"osd_{tag}_dpi{osd.DeviceDpi}.png";
            bmp.Save(Path.Combine(outDir, file), ImageFormat.Png);
            osd.Hide();
            return $"osd_{tag}: DeviceDpi={osd.DeviceDpi} size={w}x{h} -> {file}";
        }
        catch (Exception ex)
        {
            return $"osd_{tag} FAILED: {ex.Message}";
        }
        finally
        {
            OsdForm.DiagForceTopLeft = null;
        }
    }
}
#endif

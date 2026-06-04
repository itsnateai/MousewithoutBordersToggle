using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace MWBToggle;

/// <summary>
/// Manual DPI width-sizing for fixed-width fields (ComboBox, NumericUpDown, fixed TextBox)
/// inside a layout-container form. The containers (TableLayoutPanel + AutoSize) handle
/// positions + heights (font-driven) and the form AutoSizes to content, but a fixed field's
/// literal WIDTH does NOT grow on its own: AutoScaleMode.Dpi does not scale control widths on
/// a direct high-DPI launch (empirically dead for these forms at 150%). So size each fit field
/// to ITS content at the device DPI, once after the handle exists.
///
/// Ported from EQSwitch <c>UI/CardLayout.cs DpiScale.SizeFitFields</c> — the technique verified
/// pixel-proportional at real 150%. Fields that stretch (Anchor includes Right, or Dock != None)
/// are skipped — they already fill their cell. Call ONCE (the TextBox arm scales its design Width
/// and is not idempotent), AFTER any ComboBox is populated: an empty ComboBox (no items at call
/// time) is left at its design width and would clip at 150% if its items are filled later.
/// </summary>
internal static class DpiFit
{
    public static void SizeFitFields(Control root)
    {
        foreach (Control c in root.Controls)
        {
            if (c.Dock == DockStyle.None && !c.Anchor.HasFlag(AnchorStyles.Right))
            {
                int w = 0;
                if (c is ComboBox cb && cb.Items.Count > 0)
                {
                    int t = 0;
                    foreach (var it in cb.Items)
                        t = Math.Max(t, TextRenderer.MeasureText(it?.ToString() ?? string.Empty, cb.Font).Width);
                    w = t + cb.LogicalToDeviceUnits(32); // longest item + dropdown arrow + border + pad
                }
                else if (c is NumericUpDown nud)
                {
                    string max = nud.Maximum.ToString(
                        nud.DecimalPlaces > 0 ? "F" + nud.DecimalPlaces : "0", CultureInfo.InvariantCulture);
                    w = TextRenderer.MeasureText(max, nud.Font).Width + nud.LogicalToDeviceUnits(26); // digits + spinner + border + pad
                }
                else if (c is TextBox tb)
                {
                    w = tb.LogicalToDeviceUnits(tb.Width); // variable content — scale the design width to device px
                }
                if (w > 0)
                {
                    c.Width = w;
                    c.MinimumSize = new Size(w, c.MinimumSize.Height);
                }
            }
            // Don't descend into a leaf field's own internals; only walk container children.
            if (c is not (NumericUpDown or ComboBox or TextBox))
                SizeFitFields(c);
        }
    }

    /// <summary>
    /// Keep an AutoSize dialog centered on its current screen as its content (and therefore
    /// size) changes after it's shown. AutoSize forms resize from their top-left, so a
    /// CenterScreen dialog drifts off-center when state changes (e.g. Update's checking→result
    /// transition, a picker keypress growing the status line). Re-centers on each size change
    /// once the form is visible — the initial CenterScreen placement stays the form's own.
    /// Setting Location does not raise SizeChanged, so this can't recurse.
    /// </summary>
    public static void KeepCentered(Form form)
    {
        form.SizeChanged += (_, _) =>
        {
            if (!form.Visible || !form.IsHandleCreated) return;
            var wa = Screen.FromControl(form).WorkingArea;
            form.Location = new Point(
                wa.X + Math.Max(0, (wa.Width - form.Width) / 2),
                wa.Y + Math.Max(0, (wa.Height - form.Height) / 2));
        };
    }
}

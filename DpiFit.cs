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
/// and is not idempotent).
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
}

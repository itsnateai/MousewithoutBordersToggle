namespace MWBToggle;

/// <summary>
/// Theme palette for window chrome (About, hotkey picker, Update dialog, OSD, context menu).
/// Two palettes — Catppuccin Mocha (Dark) and Latte (Light) — selected once at
/// startup via <see cref="Initialize"/> based on the user's saved preference
/// (resolved through <see cref="ResolveIsDark"/>). The active palette is then
/// exposed via the static colour properties (BgColor, FgColor, etc.) that all
/// chrome surfaces read from.
///
/// Tray icons are NOT driven by this — the on/off icons are colour-coded
/// (green=ON, red=OFF) and read fine on either taskbar, so they don't need
/// theme-swapped variants.
///
/// Why static state instead of a flowing instance: ThemedMenuRenderer's GDI
/// brush/pen cache + OsdForm's bg/text brushes capture Theme.* at first
/// class load. They are write-once per process. <see cref="Initialize"/> MUST
/// be called before any of those classes is first touched (currently: before
/// `new ThemedMenuRenderer()` and before `new OsdForm()` in MWBToggleApp's
/// constructor body). Changing theme at runtime is intentionally not supported
/// — restart-to-apply keeps the GDI caches honest.
/// </summary>
internal static class Theme
{
    private static bool _isDark = true;
    private static bool _initialized;

    /// <summary>True if the active palette is the dark (Mocha) one.</summary>
    public static bool IsDark => _isDark;

    /// <summary>
    /// Selects the active palette. Call once at startup, before any class
    /// with a <c>static readonly</c> Theme.* capture is first touched
    /// (ThemedMenuRenderer, OsdForm).
    /// </summary>
    public static void Initialize(bool isDark)
    {
        // Idempotent guard: second call CAN'T take effect because the GDI
        // brush/pen caches in ThemedMenuRenderer + OsdForm already captured
        // Theme.* colours at first class load. Log loudly (rather than silently
        // returning) so a future maintainer who tries to add live-theme-swap
        // gets a Trace entry pointing at the constraint instead of debugging a
        // mixed palette in production. Rule 12: fail loud, proactively.
        if (_initialized)
        {
            System.Diagnostics.Trace.WriteLine(
                $"MWBToggle: Theme.Initialize called twice (was isDark={_isDark}, requested {isDark}) — ignored. " +
                "Theme is restart-to-apply by design (static GDI caches captured at first class load).");
            return;
        }
        _isDark = isDark;
        _initialized = true;
    }

    /// <summary>
    /// Resolves the user's saved <c>ThemeMode</c> value ("System", "Dark",
    /// "Light", or empty) into a concrete is-dark decision. "System" (or any
    /// unrecognized value, including empty) reads the Windows
    /// <c>SystemUsesLightTheme</c> registry value.
    /// </summary>
    public static bool ResolveIsDark(string? configValue)
    {
        if (string.Equals(configValue, "Dark", System.StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(configValue, "Light", System.StringComparison.OrdinalIgnoreCase))
            return false;
        // "System" / null / empty / typo → follow OS.
        return !IsSystemLightTheme();
    }

    // DWM dark-titlebar attributes. 20 = DWMWA_USE_IMMERSIVE_DARK_MODE on
    // Win10 20H1+ (build 19041) and Win11. On Win10 1809–19H2 (builds 17763–
    // 18363) attribute 20 is unrecognised — DwmSetWindowAttribute returns
    // E_INVALIDARG (0x80070057). Callers should try 20 first and only fall
    // back to 19 (the undocumented pre-20H1 predecessor) on non-zero HRESULT.
    // On pre-1809 both fail silently and the form keeps its default titlebar.
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(System.IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    /// <summary>
    /// Flip the titlebar (non-client area) to match the active theme's dark/light state.
    /// Call from <c>Form.OnHandleCreated</c> BEFORE the window becomes visible (handle
    /// creation precedes the first WM_NCPAINT). Silent no-op on pre-1809 Win10.
    /// </summary>
    public static void ApplyTitleBarMode(System.IntPtr handle)
    {
        int dark = _isDark ? 1 : 0;
        int hr = DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        if (hr != 0)
        {
            // Fallback to the legacy attribute for Win10 1809–19H2 only.
            DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref dark, sizeof(int));
        }
    }

    /// <summary>
    /// Reads <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\SystemUsesLightTheme</c>.
    /// Returns false on any failure (locked key, missing value, registry exception).
    /// </summary>
    public static bool IsSystemLightTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            object? val = key?.GetValue("SystemUsesLightTheme");
            return val is int i && i == 1;
        }
        catch (System.Exception ex)
        {
            // A registry read failure here silently sends the user to dark mode
            // regardless of their actual OS theme. Trace so the unexpected case
            // (locked HKCU, AppContainer sandbox, group policy) is at least
            // diagnosable instead of a silent palette drift.
            System.Diagnostics.Trace.WriteLine(
                $"MWBToggle: Theme.IsSystemLightTheme registry read failed " +
                $"(err={ex.GetType().Name}: {ex.Message}) — assuming dark theme");
            return false;
        }
    }

    // ── Active palette accessors ───────────────────────────────────────────
    // Each property routes to the matching slot on the active palette. Form
    // code reads these once during construction (e.g. BackColor = Theme.BgColor)
    // and never again — no per-paint indirection cost.

    public static System.Drawing.Color BgColor         => _isDark ? Dark.Bg         : Light.Bg;
    public static System.Drawing.Color FgColor         => _isDark ? Dark.Fg         : Light.Fg;
    public static System.Drawing.Color FgDisabledColor => _isDark ? Dark.FgDisabled : Light.FgDisabled;
    public static System.Drawing.Color DimColor        => _isDark ? Dark.Dim        : Light.Dim;
    public static System.Drawing.Color HighlightBg     => _isDark ? Dark.HighlightBg: Light.HighlightBg;
    public static System.Drawing.Color EditBgColor     => _isDark ? Dark.EditBg     : Light.EditBg;
    public static System.Drawing.Color DividerColor    => _isDark ? Dark.Divider    : Light.Divider;
    public static System.Drawing.Color AccentBlue      => _isDark ? Dark.AccentBlue : Light.AccentBlue;
    public static System.Drawing.Color AccentGreen     => _isDark ? Dark.AccentGreen: Light.AccentGreen;
    public static System.Drawing.Color AccentWarn      => _isDark ? Dark.AccentWarn : Light.AccentWarn;

    /// <summary>
    /// CheckBox glyph + label colour. Dark uses pure white because the body Fg
    /// (#CDD6F3) renders thin against the dark BG at 9pt through FlatStyle.Flat's
    /// grayscale-AA path; Light uses the normal Fg (dark text reads fine on
    /// light BG without the boost).
    /// </summary>
    public static System.Drawing.Color CheckboxFgColor => _isDark ? System.Drawing.Color.White : Light.Fg;

    // ── Dark palette — Catppuccin Mocha ────────────────────────────────────
    private static class Dark
    {
        public static readonly System.Drawing.Color Bg          = System.Drawing.Color.FromArgb(0x1E, 0x1E, 0x2E);
        public static readonly System.Drawing.Color Fg          = System.Drawing.Color.FromArgb(0xCD, 0xD6, 0xF3);
        public static readonly System.Drawing.Color FgDisabled  = System.Drawing.Color.FromArgb(0x80, 0x80, 0x95);
        public static readonly System.Drawing.Color Dim         = System.Drawing.Color.FromArgb(0xA0, 0xA0, 0xC0);
        public static readonly System.Drawing.Color HighlightBg = System.Drawing.Color.FromArgb(0x35, 0x35, 0x50);
        public static readonly System.Drawing.Color EditBg      = System.Drawing.Color.FromArgb(0x2A, 0x2A, 0x3E);
        public static readonly System.Drawing.Color Divider     = System.Drawing.Color.FromArgb(0x40, 0x40, 0x50);
        public static readonly System.Drawing.Color AccentBlue  = System.Drawing.Color.FromArgb(0x89, 0xB4, 0xFA);
        public static readonly System.Drawing.Color AccentGreen = System.Drawing.Color.FromArgb(0xA6, 0xE3, 0xA1);
        public static readonly System.Drawing.Color AccentWarn  = System.Drawing.Color.FromArgb(0xFA, 0xB3, 0x87);
    }

    // ── Light palette — v2.1.x classic ─────────────────────────────────────
    // Replaces the original Latte port (2026-05-17). User feedback: the cool
    // Latte tint hurt eyes at dialog scale. Rebuilt to match the v2.1.x feel
    // pixel-for-pixel on the slots the original app had:
    //   Bg          = pure white
    //   Fg          = #1E1E1E (near-black)
    //   AccentBlue  = #2255AA (brand blue — used for links / header accents)
    //   HighlightBg = cornsilk (#FFF8DC) — warm focus/hover tint
    // The other slots (Dim/FgDisabled/EditBg/Divider/Green/Warn) are picked to
    // harmonise: deep enough greys for WCAG-AA against white, forest/deep-red
    // accents that read at small sizes, EditBg a faint off-white so ComboBox /
    // input fields read as gently inset against the form bg.
    private static class Light
    {
        public static readonly System.Drawing.Color Bg          = System.Drawing.Color.FromArgb(0xFF, 0xFF, 0xFF); // pure white
        public static readonly System.Drawing.Color Fg          = System.Drawing.Color.FromArgb(0x1E, 0x1E, 0x1E); // near-black text
        public static readonly System.Drawing.Color FgDisabled  = System.Drawing.Color.FromArgb(0x99, 0x99, 0x99); // muted grey (menu disabled)
        public static readonly System.Drawing.Color Dim         = System.Drawing.Color.FromArgb(0x55, 0x55, 0x55); // secondary text (~7.5:1 on white)
        public static readonly System.Drawing.Color HighlightBg = System.Drawing.Color.FromArgb(0xFF, 0xF8, 0xDC); // cornsilk — warm focus tint
        public static readonly System.Drawing.Color EditBg      = System.Drawing.Color.FromArgb(0xF8, 0xF8, 0xF8); // faint off-white — input field inset
        public static readonly System.Drawing.Color Divider     = System.Drawing.Color.FromArgb(0xC8, 0xC8, 0xC8); // light grey divider
        public static readonly System.Drawing.Color AccentBlue  = System.Drawing.Color.FromArgb(0x22, 0x55, 0xAA); // brand blue
        public static readonly System.Drawing.Color AccentGreen = System.Drawing.Color.FromArgb(0x2E, 0x7D, 0x32); // forest green
        public static readonly System.Drawing.Color AccentWarn  = System.Drawing.Color.FromArgb(0xC6, 0x28, 0x28); // deep red
    }
}

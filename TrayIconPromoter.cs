using System;
using Microsoft.Win32;

namespace MWBToggle;

/// <summary>
/// On Windows 11, new tray icons default to hidden-in-overflow until the user
/// manually flips "Show icon in taskbar" under Settings → Personalization →
/// Taskbar → Other system tray icons. For a tray-only app like MWBToggle that
/// delivers no value while hidden, that's a painful first-run experience.
///
/// Windows 11 22H2+ stores per-icon visibility at
/// <c>HKCU\Control Panel\NotifyIconSettings\&lt;hash&gt;</c> with a DWORD value
/// named <c>IsPromoted</c> (1 = visible in taskbar, 0 = hidden, missing =
/// Explorer's default, which shows as hidden). Explorer creates the subkey on
/// the first <c>Shell_NotifyIcon(NIM_ADD)</c> call and polls the key about once
/// a second — no Explorer restart or broadcast message required.
///
/// This class enumerates the registry, finds the subkey(s) whose
/// <c>ExecutablePath</c> matches the current exe, and promotes the icon if —
/// and only if — Explorer hasn't already written an explicit value. We never
/// override a user's deliberate 0; we only fill in the missing default.
///
/// ──────────────────────────────────────────────────────────────────
/// This mechanism is undocumented. It has been stable across Win11
/// 22H2, 23H2, 24H2, and 25H2. All registry interaction is wrapped in
/// try/catch so a schema change in a future build silently no-ops
/// instead of crashing.
/// </summary>
internal static class TrayIconPromoter
{
    private const string KeyPath = @"Control Panel\NotifyIconSettings";
    private const int MinWin11Build = 22000;

    /// <summary>
    /// Ensure the current exe's tray icon is promoted to visible in the taskbar.
    /// Safe to call multiple times — no-ops on already-promoted entries, and
    /// never overrides a user's explicit 0. Safe to call before Explorer has
    /// registered our icon; in that case there's simply nothing to promote and
    /// the call returns false.
    /// </summary>
    /// <returns>True if at least one matching subkey was promoted this call.</returns>
    internal static bool TryPromote(string exePath)
    {
        // Win10 uses a different schema (IconStreams blob). Skip — most Win10
        // users have "Always show all icons" from the Win10 era anyway.
        if (Environment.OSVersion.Version.Build < MinWin11Build) return false;
        if (string.IsNullOrWhiteSpace(exePath)) return false;

        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
            if (root is null) return false;

            bool promoted = false;
            foreach (var subName in root.GetSubKeyNames())
            {
                try
                {
                    using var sub = root.OpenSubKey(subName, writable: true);
                    if (sub is null) continue;

                    var path = sub.GetValue("ExecutablePath") as string;
                    if (string.IsNullOrEmpty(path)) continue;
                    if (!string.Equals(path, exePath, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Respect user intent — if they explicitly hid the icon
                    // (IsPromoted is present and 0), don't force it back on.
                    var current = sub.GetValue("IsPromoted");
                    if (current is int i && i == 0)
                    {
                        Logger.Info($"TrayIconPromoter: {subName} already set to 0 — leaving user's choice intact.");
                        continue;
                    }
                    if (current is int already && already == 1) continue;

                    sub.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                    Logger.Info($"TrayIconPromoter: promoted {subName} for {path}.");
                    promoted = true;
                }
                catch (Exception ex)
                {
                    Logger.Warn($"TrayIconPromoter: subkey {subName}: {ex.Message}");
                }
            }
            return promoted;
        }
        catch (Exception ex)
        {
            // Registry access denied, schema moved, hive locked — anything.
            // This is a UX polish, never a crash surface.
            Logger.Warn($"TrayIconPromoter: {ex.Message}");
            return false;
        }
    }
}

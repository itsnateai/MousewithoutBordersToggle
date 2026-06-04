using System;
using System.Threading;
using System.Windows.Forms;

namespace MWBToggle;

internal static class Program
{
    // Per-session (Local\) rather than machine-wide (Global\). A tray app is inherently
    // per-user — each Windows session gets its own tray, its own settings.json view,
    // its own startup shortcut. Global\ allowed any unprivileged user to DoS every
    // other user's instance by squatting the name at login.
    private const string MutexName = "Local\\MWBToggle_SingleInstance";

    [STAThread]
    static void Main(string[] args)
    {
#if DEBUG
        // DEBUG-only DPI verification entry point. Renders UI surfaces to PNG (with
        // DeviceDpi logged) so a 100% dev-host vs 150% Tiny11 diff is ground truth.
        // Runs BEFORE the single-instance mutex so it never disturbs a live instance.
        // Stripped entirely from Release builds.
        {
            int diagIdx = Array.FindIndex(args, a => string.Equals(a, "--diag-render-form", StringComparison.OrdinalIgnoreCase));
            if (diagIdx >= 0)
            {
                string which = diagIdx + 1 < args.Length ? args[diagIdx + 1] : "all";
                int outIdx = Array.FindIndex(args, a => string.Equals(a, "--out", StringComparison.OrdinalIgnoreCase));
                string outDir = outIdx >= 0 && outIdx + 1 < args.Length ? args[outIdx + 1] : ".";
                ApplicationConfiguration.Initialize();
                Environment.Exit(DiagRender.Run(which, outDir));
            }
            int showIdx = Array.FindIndex(args, a => string.Equals(a, "--diag-show", StringComparison.OrdinalIgnoreCase));
            if (showIdx >= 0)
            {
                string showWhich = showIdx + 1 < args.Length ? args[showIdx + 1] : "about";
                ApplicationConfiguration.Initialize();
                Environment.Exit(DiagRender.RunShow(showWhich));
            }
        }
#endif
        bool isAfterUpdate = args.Contains("--after-update");
        // Theme changes also force a process restart (Theme.cs static GDI caches
        // are write-once per process). Same mutex-handoff dance as --after-update:
        // wait briefly for the old instance's finally-block ReleaseMutex.
        bool isAfterThemeRestart = args.Contains("--after-theme-restart");
        bool isRelaunch = isAfterUpdate || isAfterThemeRestart;

        // Self-heal the startup shortcut BEFORE the mutex check. A winget upgrade places
        // the new exe in a versioned subfolder, so the Startup-folder .lnk points at the
        // old path. Running this early means even a duplicate launch that immediately
        // loses the mutex race still refreshes the .lnk — so the NEXT reboot's startup
        // shortcut finds the current version.
        try { MWBToggleApp.ValidateStartupShortcut(); } catch { /* never block startup */ }

        var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            if (isRelaunch)
            {
                // Post-update OR post-theme-restart relaunch: old instance is still
                // shutting down. Wait for it to release the mutex before proceeding.
                mutex.Dispose();
                Thread.Sleep(1500);
                mutex = new Mutex(true, MutexName, out createdNew);
                if (!createdNew)
                {
                    // Still held after 1.5s — give up
                    mutex.Dispose();
                    return;
                }
            }
            else
            {
                mutex.Dispose();
                return;
            }
        }

        try
        {
            UpdateDialog.CleanupUpdateArtifacts();

            ApplicationConfiguration.Initialize();

            // ShowUpdateToast is invoked from MWBToggleApp's restoreTimer once
            // Theme.Initialize has run, so the toast inherits the user's chosen
            // palette instead of a hardcoded one. The 1500ms internal delay is
            // unchanged — Application.Run starts the message pump either way.

            Application.Run(new MWBToggleApp());
        }
        finally
        {
            // Belt-and-suspenders. In this specific code path — [STAThread] Main,
            // initiallyOwned: true, createdNew == true gate, Application.Run on the
            // same thread — ReleaseMutex() cannot actually throw: mutex ownership is
            // thread-affine and the message pump never hops threads. The try/catch
            // is purely defensive so that if some future refactor (async Main, STA
            // hand-off, etc.) breaks that invariant, mutex.Dispose() still runs
            // and the original exception (if any) isn't masked by ApplicationException.
            // If you're editing Main, understand that *today* this catch is never
            // entered; keep it only if you're introducing a change that could.
            try { mutex.ReleaseMutex(); }
            catch (ApplicationException) { /* unreachable today — see comment above */ }
            mutex.Dispose();
        }
    }

}

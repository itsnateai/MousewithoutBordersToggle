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
        bool isAfterUpdate = args.Contains("--after-update");

        // Self-heal the startup shortcut BEFORE the mutex check. A winget upgrade places
        // the new exe in a versioned subfolder, so the Startup-folder .lnk points at the
        // old path. Running this early means even a duplicate launch that immediately
        // loses the mutex race still refreshes the .lnk — so the NEXT reboot's startup
        // shortcut finds the current version.
        try { MWBToggleApp.ValidateStartupShortcut(); } catch { /* never block startup */ }

        var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            if (isAfterUpdate)
            {
                // Post-update relaunch: old instance is still shutting down.
                // Wait for it to release the mutex before proceeding.
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

            if (isAfterUpdate)
                UpdateDialog.ShowUpdateToast();

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

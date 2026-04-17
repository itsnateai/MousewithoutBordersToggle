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
            // ReleaseMutex throws ApplicationException if the current thread doesn't
            // own the mutex. Normal exits own it (we acquired on construction with
            // initiallyOwned: true and createdNew == true). But in rare edge cases
            // — thread-identity shift during Application.Run, an abandoned mutex,
            // STA-thread-pool hand-off — it can fire. Swallow it so:
            //   1. The original exception from Application.Run (if any) isn't masked.
            //   2. mutex.Dispose() still runs and the handle is released to the OS.
            // Dispose itself is documented not to throw.
            try { mutex.ReleaseMutex(); }
            catch (ApplicationException) { /* not owned by this thread — we're exiting anyway */ }
            mutex.Dispose();
        }
    }

}

using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace MWBToggle;

internal static class Program
{
    private const string MutexName = "Global\\MWBToggle_SingleInstance";

    [STAThread]
    static void Main(string[] args)
    {
        // Mirror AHK's #SingleInstance Force — kill any previous instance
        KillPreviousInstance();

        var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // Race condition: another instance started between kill and mutex.
            // Wait briefly for it to exit, then retry.
            mutex.Dispose();
            Thread.Sleep(500);
            mutex = new Mutex(true, MutexName, out createdNew);
            if (!createdNew)
            {
                mutex.Dispose();
                return;
            }
        }

        try
        {
            bool isAfterUpdate = args.Contains("--after-update");
            UpdateDialog.CleanupUpdateArtifacts();

            ApplicationConfiguration.Initialize();

            if (isAfterUpdate)
                UpdateDialog.ShowUpdateToast();

            Application.Run(new MWBToggleApp());
        }
        finally
        {
            mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }

    private static void KillPreviousInstance()
    {
        int myPid = Environment.ProcessId;
        using var self = Process.GetCurrentProcess();
        string myName = self.ProcessName;

        foreach (var proc in Process.GetProcessesByName(myName))
        {
            try
            {
                if (proc.Id != myPid)
                    proc.Kill();
            }
            catch
            {
                // Process may have already exited — ignore
            }
            finally
            {
                proc.Dispose();
            }
        }
    }
}

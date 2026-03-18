using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace MWBToggle;

internal static class Program
{
    private const string MutexName = "Global\\MWBToggle_SingleInstance";

    [STAThread]
    static void Main()
    {
        // Mirror AHK's #SingleInstance Force — kill any previous instance
        // so the new one (with potentially updated config) takes over.
        KillPreviousInstance();

        using var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // Race condition: another instance started between kill and mutex.
            // Wait briefly for it to exit, then retry.
            Thread.Sleep(500);
            mutex.Dispose();
            using var retryMutex = new Mutex(true, MutexName, out bool retryCreated);
            if (!retryCreated)
                return; // Still couldn't acquire — give up
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MWBToggleApp());
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

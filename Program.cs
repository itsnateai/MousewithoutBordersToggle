using System;
using System.Threading;
using System.Windows.Forms;

namespace MWBToggle;

internal static class Program
{
    private const string MutexName = "Global\\MWBToggle_SingleInstance";

    [STAThread]
    static void Main(string[] args)
    {
        var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // Another instance already holds the mutex — exit silently.
            mutex.Dispose();
            return;
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

}

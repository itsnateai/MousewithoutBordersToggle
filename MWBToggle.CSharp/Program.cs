using System;
using System.Threading;
using System.Windows.Forms;

namespace MWBToggle;

internal static class Program
{
    private const string MutexName = "Global\\MWBToggle_SingleInstance";

    [STAThread]
    static void Main()
    {
        // Single-instance guard — mirrors AHK's #SingleInstance Force
        using var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // Another instance is already running — silently exit
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MWBToggleApp());
    }
}

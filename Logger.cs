using System;
using System.IO;
using System.Text;

namespace MWBToggle;

/// <summary>
/// Tiny append-only log at %LOCALAPPDATA%\MWBToggle\mwbtoggle.log.
/// Caps at ~100 KB by truncating to the last ~50 KB when it grows too large.
/// Never throws — every log call is best-effort. Written for LTR support triage.
/// </summary>
internal static class Logger
{
    private static readonly object _gate = new();
    private const long MaxBytes = 100 * 1024;
    private const long KeepBytes = 50 * 1024;

    internal static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MWBToggle", "mwbtoggle.log");

    internal static void Info(string msg)  => Write("INFO",  msg);
    internal static void Warn(string msg)  => Write("WARN",  msg);
    internal static void Error(string msg) => Write("ERROR", msg);

    private static void Write(string level, string msg)
    {
        try
        {
            lock (_gate)
            {
                var dir = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                Truncate();

                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {msg}{Environment.NewLine}";
                File.AppendAllText(LogPath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never break the app.
        }
    }

    private static void Truncate()
    {
        try
        {
            var fi = new FileInfo(LogPath);
            if (!fi.Exists || fi.Length < MaxBytes) return;

            using var fs = new FileStream(LogPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            fs.Seek(-KeepBytes, SeekOrigin.End);
            var tail = new byte[KeepBytes];
            int read = fs.Read(tail, 0, tail.Length);
            fs.SetLength(0);
            fs.Write(tail, 0, read);
        }
        catch
        {
            // Leave the file as-is if truncation fails.
        }
    }
}

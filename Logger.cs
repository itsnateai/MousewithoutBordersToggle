using System;
using System.IO;
using System.Text;

namespace MWBToggle;

/// <summary>
/// Tiny append-only log at %LOCALAPPDATA%\MWBToggle\mwbtoggle.log.
/// Caps at ~100 KB by truncating to the last ~50 KB when it grows too large.
/// Never throws — every log call is best-effort. Written for LTR support triage.
///
/// Backed by a long-lived StreamWriter so steady-state logging is one append —
/// no Dir.Exists + FileInfo stat + open/close per line. The directory is ensured
/// once on first write and cached; Truncate is amortized by only stat-ing every
/// N writes instead of every write.
/// </summary>
internal static class Logger
{
    private static readonly object _gate = new();
    private const long MaxBytes = 100 * 1024;
    private const long KeepBytes = 50 * 1024;
    private const int TruncateCheckInterval = 64;

    internal static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MWBToggle", "mwbtoggle.log");

    private static StreamWriter? _writer;
    private static bool _dirEnsured;
    private static int _writesSinceTruncateCheck;

    internal static void Info(string msg) => Write("INFO", msg);
    internal static void Warn(string msg) => Write("WARN", msg);

    private static void Write(string level, string msg)
    {
        try
        {
            lock (_gate)
            {
                if (!_dirEnsured)
                {
                    var dir = Path.GetDirectoryName(LogPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    _dirEnsured = true;
                }

                // Amortize the truncate check — the path that actually trims runs at
                // most every N writes, not every write. The first write of the session
                // always checks so a stale oversized log from a crashed prior session
                // gets trimmed immediately.
                if (_writesSinceTruncateCheck == 0 || _writesSinceTruncateCheck >= TruncateCheckInterval)
                {
                    MaybeTruncate();
                    _writesSinceTruncateCheck = 0;
                }
                _writesSinceTruncateCheck++;

                _writer ??= new StreamWriter(
                    new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.Read),
                    Encoding.UTF8) { AutoFlush = true };

                _writer.Write($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {msg}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never break the app. Drop the writer so the next write
            // re-opens (covers the rare case where the FileStream went bad).
            try { _writer?.Dispose(); } catch { }
            _writer = null;
        }
    }

    private static void MaybeTruncate()
    {
        try
        {
            var fi = new FileInfo(LogPath);
            if (!fi.Exists || fi.Length < MaxBytes) return;

            // Release the long-lived writer so we can open the file exclusively.
            try { _writer?.Dispose(); } catch { }
            _writer = null;

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

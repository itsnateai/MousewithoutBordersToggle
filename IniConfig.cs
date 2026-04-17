using System;
using System.Collections.Generic;
using System.IO;

namespace MWBToggle;

/// <summary>
/// Minimal INI file reader — no external dependencies.
/// Supports [Section] headers and Key=Value pairs.
/// Mirrors the AHK IniRead() functionality used by LoadConfig().
/// </summary>
internal sealed class IniConfig
{
    // section -> key -> value
    private readonly Dictionary<string, Dictionary<string, string>> _data = new(
        StringComparer.OrdinalIgnoreCase);

    public string? Get(string section, string key)
    {
        if (_data.TryGetValue(section, out var sectionDict) &&
            sectionDict.TryGetValue(key, out var value))
            return value;
        return null;
    }

    // Defensive cap: the INI file is user-writeable, so any process running as the
    // same user can replace it. A 100 MB file would OOM the tray app on startup.
    // Valid configs are well under 4 KB; 64 KB is generous room for comments.
    private const long MaxBytes = 64 * 1024;

    public static IniConfig Load(string path)
    {
        var config = new IniConfig();
        string currentSection = "";

        var fi = new FileInfo(path);
        if (fi.Exists && fi.Length > MaxBytes)
        {
            // Don't throw — the app must still start with defaults. But DO log, so a
            // user whose custom hotkey/flags silently reverted to defaults has a
            // breadcrumb pointing at the oversized INI instead of assuming the app
            // is broken.
            Logger.Warn($"IniConfig: {path} is {fi.Length} bytes (> {MaxBytes} cap) — ignoring user config, using defaults.");
            return config;
        }

        foreach (string rawLine in File.ReadAllLines(path))
        {
            string line = rawLine.Trim();

            // Skip empty lines and comments
            if (line.Length == 0 || line[0] == ';' || line[0] == '#')
                continue;

            // Section header
            if (line[0] == '[' && line[^1] == ']')
            {
                currentSection = line[1..^1].Trim();
                continue;
            }

            // Key=Value
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;

            string key = line[..eq].Trim();
            string val = line[(eq + 1)..].Trim();

            if (!config._data.ContainsKey(currentSection))
                config._data[currentSection] = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);

            config._data[currentSection][key] = val;
        }

        return config;
    }
}

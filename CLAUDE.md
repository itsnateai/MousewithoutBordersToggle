# MWBToggle — Project Architecture

## Overview
MWBToggle toggles Mouse Without Borders clipboard/file sharing via system tray.
Two implementations exist: the original AHK v2 script and the C# port (recommended).

## Project Structure

```
MWBToggle.ahk              # Legacy AHK v2 script (v1.5.0)
MWBToggle.CSharp/           # C# port (.NET 8 Windows Forms, v2.0.0)
├── MWBToggle.csproj        # Project file — WinExe, net8.0-windows, embeds icons
├── Program.cs              # Entry point — single-instance (kills old), STAThread
├── MWBToggleApp.cs         # Main tray app — toggle, sync, pause, OSD, config
├── GlobalHotkey.cs         # Win32 RegisterHotKey with AHK-style string parsing
├── AboutForm.cs            # About dialog with GitHub link
└── IniConfig.cs            # Minimal INI file reader (no dependencies)
on.ico                      # Tray icon — sharing ON (green), embedded as resource
mwb.ico                     # Tray icon — sharing OFF (red), embedded as resource
```

## C# Architecture

- **No polling**: Uses `FileSystemWatcher` on settings.json (OS-level ReadDirectoryChangesW)
- **Zero idle allocations**: Pre-compiled regexes, cached tray strings, state-change-only updates
- **Single UI thread**: All WinForms — no async, no cross-thread issues
- **P/Invoke**: `RegisterHotKey`/`UnregisterHotKey` for global hotkey, `kernel32.Beep` for sound
- **COM interop**: `WScript.Shell` for startup shortcut (.lnk) creation
- **Explorer restart recovery**: Listens for `TaskbarCreated` window message

## Key Design Decisions

- **UTF-8 without BOM** (`UTF8Encoding(false)`) — MWB's JSON parser expects no BOM
- **Icons are embedded resources** — compiled into the .exe, no external files needed
- **`Environment.ProcessPath`** for INI/icon paths — correct for single-file publish
- **`Process.Kill()` for single-instance** — matches AHK's `#SingleInstance Force`
- **OSD is a borderless Form at cursor** — matches AHK ToolTip, avoids BalloonTip toast spam

## Settings File

MWB settings path: `%LOCALAPPDATA%\Microsoft\PowerToys\MouseWithoutBorders\settings.json`

Toggled keys (regex-based replacement):
- `"ShareClipboard": { "value": true|false }`
- `"TransferFile": { "value": true|false }`

## Build

```bash
cd MWBToggle.CSharp
dotnet run                          # Run directly
dotnet publish -c Release           # Build .exe (requires .NET 8 runtime)
dotnet publish -c Release --self-contained true  # Standalone .exe (~60MB)
```

## Conventions

- Version is in two places: `MWBToggle.csproj` `<Version>` and `MWBToggleApp.Version` const
- AHK-style hotkey strings (`^!c` = Ctrl+Alt+C) are used in INI config and parsed at runtime
- All disposable resources have explicit cleanup in both `ExitApplication()` and `Dispose(bool)`
- Process handles from `Process.Start`/`GetProcessesByName` are always disposed

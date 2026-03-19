# CLAUDE.md — MWBToggle

## Overview

System tray companion for PowerToys Mouse Without Borders. Toggles clipboard and file sharing on/off with a hotkey or tray click, so you can quickly disable sharing for privacy (passwords, sensitive data) without opening PowerToys settings.

**Repo:** `itsnateai/MousewithoutBordersToggle` (public) | **Branch:** master

## Tech Stack

- **C# .NET 8 WinForms** — system tray app, no visible window
- **Win32 P/Invoke** — `RegisterHotKey` for global hotkeys
- **FileSystemWatcher** — zero-polling config change detection
- **Single-file publish** — portable exe, no installer

## Build Commands

```bash
# Framework-dependent (~280KB, requires .NET 8 runtime)
dotnet publish -c Release

# Self-contained (~147MB, no runtime needed)
dotnet publish -c Release --self-contained true

# Output: bin/Release/net8.0-windows/win-x64/publish/MWBToggle.exe
```

## Key Files

| File | Purpose |
|------|---------|
| `Program.cs` | Entry point — single-instance Mutex enforcement |
| `MWBToggleApp.cs` | Main tray app — toggle logic, FileSystemWatcher, pause timers, OSD, config loading |
| `GlobalHotkey.cs` | Win32 RegisterHotKey wrapper with AHK-style hotkey string parsing |
| `IniConfig.cs` | Minimal INI file reader for `MWBToggle.ini` |
| `AboutForm.cs` | About dialog with GitHub link |
| `MWBToggle.csproj` | .NET 8 WinForms project, embedded icons |
| `on.ico` / `mwb.ico` | Tray icons (green=ON, red=OFF), embedded as resources |
| `legacy/MWBToggle.ahk` | Original AHK v2 script (archived, not used) |

## How It Works

Reads/writes the PowerToys MWB `settings.json` directly:
```
%LOCALAPPDATA%\Microsoft\PowerToys\MouseWithoutBorders\settings.json
```

Toggles `ShareClipboard` and `TransferFile` via regex replacement. Creates backup before each write. Retries on file lock. FileSystemWatcher detects external changes (no polling).

## Configuration

Optional `MWBToggle.ini` next to the exe:
- `Hotkey` — AHK-style string (`^!c` = Ctrl+Alt+C)
- `ConfirmToggle` — prompt before toggle
- `SoundFeedback` — beep on toggle
- `MiddleClickMwbSettings` — middle-click opens MWB settings

## Conventions

- OSD uses floating tooltip at cursor (no toast/TrayTip spam)
- Left-click tray icon = toggle, middle-click = MWB settings
- Pause feature: temporary disable with auto-resume (5/15/30 min)
- Run at startup via Windows Startup folder shortcut (not registry)
- Dual release: framework-dependent (~280KB) + self-contained (~147MB)

## Status

**v2.3.0 — Current release**

Menu restructure, hotkey picker dialog, single-click toggle, unlimited pause duration. All audit items resolved.

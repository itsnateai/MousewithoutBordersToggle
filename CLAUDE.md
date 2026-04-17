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
| `Program.cs` | Entry point — per-session `Local\` mutex, post-update relaunch handoff |
| `MWBToggleApp.cs` | Main tray app — toggle logic, FileSystemWatcher, pause timers, OSD, config |
| `GlobalHotkey.cs` | Win32 RegisterHotKey wrapper (MOD_NOREPEAT) with AHK-style parsing |
| `UpdateDialog.cs` | Self-update UI, SHA256 verify, torn-state + `.ok` sentinel rollback |
| `Logger.cs` | Append-only log at `%LOCALAPPDATA%\MWBToggle\mwbtoggle.log` (100 KB cap) |
| `IniConfig.cs` | Minimal INI file reader for `MWBToggle.ini` |
| `AboutForm.cs` | About dialog with GitHub + Update + Open-log-folder links |
| `MWBToggle.csproj` | .NET 8 WinForms project, embedded icons |
| `on.ico` / `off.ico` | Tray icons (green=ON, red=OFF), embedded as resources |
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

- OSD is a discreet dark bubble pinned above the system tray with a colored state dot (green = sharing ON, red = OFF/paused, gray = info). Canonical template at `_.claude/_templates/snippets/csharp/osd-tooltip.md`. Never use toast/TrayTip/MessageBox/cursor-anchored tooltips.
- Left-click tray icon = toggle, middle-click = MWB settings
- Pause feature: temporary disable with auto-resume (5 min / 30 min / Until resumed). Pause tracks absolute UTC time and survives sleep via `SystemEvents.PowerModeChanged`.
- Manual toggle (hotkey or menu) clears any active pause.
- settings.json writes are atomic: `.tmp` → `File.Replace(tmp, settings, .bak)`.
- FileSystemWatcher has a short self-write suppression window; if the settings dir doesn't exist yet, a bootstrap watcher waits for it to appear.
- Run at startup via Windows Startup folder shortcut (not registry).
- Self-update: SHA256-verified download, `.old` kept until new version writes `.ok` sentinel on successful startup.
- Release artifact: self-contained single-file exe (~147MB, no .NET runtime needed). Distributed via GitHub Releases + WinGet.

## Status

**v2.5.1 — LTR**

Carries all v2.5.0 hardening: MOD_NOREPEAT on hotkey, atomic settings writes, TransferFile regex verify, hotkey picker handles Win-modifier + rejects unsupported keys with a visible error, pause timer is sleep-aware and cleared by manual toggles, watcher bootstraps from parent dir when MWB isn't installed yet, per-session mutex, SHA256SUMS published by release workflow, rollback sentinel, tiny rolling log at `%LOCALAPPDATA%\MWBToggle\`.

**v2.5.1 adds:** discreet OSD bubble pinned above the system tray (replaces the cursor-anchored tooltip that clipped off-screen on tray clicks) with a green/red state dot. Canonical C# tooltip template codified at `_.claude/_templates/snippets/csharp/osd-tooltip.md`.

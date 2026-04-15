# MWBToggle

*LTR — Long-Term Release. Feature-complete; maintenance-only updates.*

Toggle **Mouse Without Borders** clipboard and file sharing on/off with a hotkey or tray icon click.

A lightweight system tray companion for [PowerToys Mouse Without Borders](https://learn.microsoft.com/en-us/windows/powertoys/mouse-without-borders) that lets you quickly disable clipboard/file sharing for privacy (passwords, sensitive data) — for when you're doing a lot of local copy/paste and don't want it constantly going to the MWB watcher. Also helps with large file copies not lagging you when you're not using the feature. Since it runs per-machine, you can also disable sharing on one side to create **one-way sharing**. MWB stays enabled — this only manipulates the clipboard server in the background.

## Features

- **Hotkey toggle**: `Ctrl + Alt + C` (configurable via menu or INI) flips both ShareClipboard and TransferFile
- **Tray icon**: Green = sharing ON, Red = sharing OFF. Left-click to toggle.
- **Middle-click**: Opens the MWB settings window directly
- **Pause sharing**: Temporarily disable for 5 minutes, 30 minutes, or indefinitely with auto-resume
- **Independent file transfer toggle**: Control TransferFile separately from ShareClipboard
- **PowerToys submenu**: Quick access to PowerToys MWB settings and the legacy MWB configuration
- **Run at startup**: One-click toggle to add/remove from Windows Startup folder
- **One-way sharing**: Disable sharing on one machine while keeping it on another for directional clipboard/file transfer
- **Zero polling**: Uses OS-level FileSystemWatcher — no CPU usage when idle
- **On-screen display**: Floating tooltip at cursor position (no toast notification spam)

## Screenshots

| Sharing ON | Sharing OFF | Tray Menu |
|:---:|:---:|:---:|
| ![ON](screenshots/mwbiconon.png) | ![OFF](screenshots/mwbiconoff.png) | ![Menu](screenshots/mwbmenu.png) |

## Requirements

- Windows 10/11
- [PowerToys](https://github.com/microsoft/PowerToys) with Mouse Without Borders enabled

## Installation

### Option 1: Download

Grab **[MWBToggle.exe](https://github.com/itsnateai/MousewithoutBordersToggle/releases/latest)** from the latest release — single file, self-contained, no .NET runtime needed.

### Option 2: WinGet (coming soon)

```powershell
winget install itsnateai.MWBToggle
```

### Option 3: Build from source

```bash
git clone https://github.com/itsnateai/MousewithoutBordersToggle.git
cd MousewithoutBordersToggle

# Framework-dependent (~280KB, requires .NET 8 runtime)
dotnet publish -c Release

# Self-contained (~147MB, no runtime needed)
dotnet publish -c Release --self-contained true
```

Output: `bin/Release/net8.0-windows/win-x64/publish/MWBToggle.exe`

## Customization

Create a `MWBToggle.ini` file in the same folder as the exe:

```ini
[Settings]
Hotkey=^!c
ConfirmToggle=false
SoundFeedback=false
MiddleClickMwbSettings=true
```

| Key | Default | Description |
|-----|---------|-------------|
| `Hotkey` | `^!c` | Hotkey string (`#` Win, `^` Ctrl, `!` Alt, `+` Shift) |
| `ConfirmToggle` | `false` | Prompt before each toggle |
| `SoundFeedback` | `false` | Beep on toggle (high tone ON, low tone OFF) |
| `MiddleClickMwbSettings` | `true` | Middle-click tray icon opens MWB settings |

## How It Works

MWBToggle reads and writes the PowerToys Mouse Without Borders `settings.json` file directly:

```
%LOCALAPPDATA%\Microsoft\PowerToys\MouseWithoutBorders\settings.json
```

It toggles the `ShareClipboard` and `TransferFile` values using regex replacement, creates a backup before each write, and retries if the file is locked by MWB.

## Troubleshooting

**"Mouse Without Borders doesn't appear to be running"**
- Make sure PowerToys is running and Mouse Without Borders is enabled in PowerToys settings.

**"Settings file not found"**
- Mouse Without Borders must be run at least once to create its settings file.

**"Could not write to settings.json"**
- The file may be locked by MWB. Wait a moment and try again.

## Project Structure

| Path | Description |
|------|-------------|
| `MWBToggle.csproj` | .NET 8 project file |
| `Program.cs` | Entry point — single-instance enforcement |
| `MWBToggleApp.cs` | Main tray app — toggle, sync, pause, OSD, config |
| `GlobalHotkey.cs` | Win32 RegisterHotKey with AHK-style string parsing |
| `AboutForm.cs` | About dialog with GitHub link |
| `IniConfig.cs` | Minimal INI file reader |
| `on.ico` / `mwb.ico` | Tray icons (embedded as resources) |

## License

[MIT](LICENSE)

# MWBToggle

*LTR — Long-Term Release · one-click self-update built in.*

Toggle **Mouse Without Borders** clipboard and file sharing on/off with a hotkey or tray icon click.

A lightweight system tray companion for [PowerToys Mouse Without Borders](https://learn.microsoft.com/en-us/windows/powertoys/mouse-without-borders) that lets you quickly disable clipboard/file sharing for privacy (passwords, sensitive data) — for when you're doing a lot of local copy/paste and don't want it constantly going to the MWB watcher. Also helps with large file copies not lagging you when you're not using the feature. Since it runs per-machine, you can also disable sharing on one side to create **one-way sharing**. MWB stays enabled — this only manipulates the clipboard server in the background.

## Features

- **Hotkey toggle**: `Ctrl + Alt + C` (configurable via menu or INI) flips both ShareClipboard and TransferFile
- **Tray icon**: Green = sharing ON, Red = sharing OFF. Left-click to toggle.
- **Middle-click**: Opens the MWB settings window directly
- **Pause sharing**: Temporarily disable for 5 minutes, 30 minutes, or indefinitely with auto-resume (survives laptop sleep)
- **Independent file transfer toggle**: Control TransferFile separately from ShareClipboard
- **PowerToys submenu**: Quick access to PowerToys MWB settings and the legacy MWB configuration
- **Run at startup**: One-click toggle to add/remove from Windows Startup folder
- **One-way sharing**: Disable sharing on one machine while keeping it on another for directional clipboard/file transfer
- **Zero polling**: Uses OS-level FileSystemWatcher — no CPU usage when idle
- **On-screen display**: Discreet dark bubble pinned above the system tray with a green/red state dot — no toast notification spam, no cursor tracking
- **One-click self-update**: Check for new versions from the About dialog — SHA256-verified downloads
- **Atomic settings writes**: Toggles can't leave your MWB config half-written, even on power loss or AV interruption

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

### Option 2: WinGet (recommended)

```powershell
winget install itsnateai.MWBToggle
winget upgrade itsnateai.MWBToggle   # later, to update
```

WinGet installs stay current automatically. The in-app **Update** button detects WinGet installs and points you back at `winget upgrade` instead of trying to overwrite the managed binary.

### Option 3: Build from source

```bash
git clone https://github.com/itsnateai/MousewithoutBordersToggle.git
cd MousewithoutBordersToggle

# Framework-dependent (~280KB, requires .NET 8 runtime)
dotnet publish -c Release -r win-x64

# Self-contained single-file (~147MB, no runtime needed) — matches the release exe
dotnet publish -c Release --self-contained true -r win-x64 -p:PublishSingleFile=true
```

Output: `bin/Release/net8.0-windows/win-x64/publish/MWBToggle.exe`

### Self-update integrity

Releases publish a `SHA256SUMS` file alongside the exe. The in-app **Update** button downloads it, verifies the hash, and fails closed if anything is missing or doesn't match. A `.ok` sentinel rolls back to the prior version if the new exe doesn't successfully start.

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
| `Hotkey` | `^!c` (Ctrl+Alt+C) | Hotkey string (`#` Win, `^` Ctrl, `!` Alt, `+` Shift) |
| `ConfirmToggle` | `false` | Prompt before each toggle |
| `SoundFeedback` | `false` | Beep on toggle (high tone ON, low tone OFF) |
| `MiddleClickMwbSettings` | `true` | Middle-click tray icon opens MWB settings |
| `SingleClickToggles` | `true` | Left-click tray icon toggles sharing. Set to `false` if you want to use the menu or hotkey only — prevents accidental toggles from stray clicks. |

## How It Works

MWBToggle reads and writes the PowerToys Mouse Without Borders `settings.json` file directly:

```
%LOCALAPPDATA%\Microsoft\PowerToys\MouseWithoutBorders\settings.json
```

It toggles the `ShareClipboard` and `TransferFile` values and swaps the file in atomically (write to `.tmp` → `File.Replace` with `.bak` rotation), so a crash or power loss mid-toggle can't leave your MWB config half-written. If MWB has the file open, the toggle retries a few times before giving up.

## Troubleshooting

**"Mouse Without Borders doesn't appear to be running"**
- Make sure PowerToys is running and Mouse Without Borders is enabled in PowerToys settings.

**"Settings file not found"**
- Mouse Without Borders must be run at least once to create its settings file. MWBToggle will detect it automatically once it appears — no restart required.

**"Could not write to settings.json"**
- The file may be locked by MWB or quarantined by antivirus. Wait a moment and try again.

**Nothing happens when I press the hotkey**
- Open the About dialog and click *Open log folder* — MWBToggle writes a small diagnostic log at `%LOCALAPPDATA%\MWBToggle\mwbtoggle.log` whenever something goes wrong.

## FAQ

**How do I uninstall?**
- WinGet: `winget uninstall itsnateai.MWBToggle`.
- Direct install: exit from the tray menu, then delete the exe and the `%LOCALAPPDATA%\MWBToggle\` folder (which holds the log, INI, and pause sidecar). Also remove the `MWBToggle` shortcut from `shell:startup` if you'd enabled run-at-startup.

**How do I stop it running at startup without uninstalling?**
- Tray menu → uncheck *Run at startup*. This deletes the shortcut from `shell:startup`. Re-check it to restore.

**What's the `.ok` sentinel file I see after an update?**
- Self-update safety net. The update downloads the new exe as `MWBToggle.exe.new`, swaps it in, and keeps the prior version as `MWBToggle.exe.old`. On a successful launch of the new version, MWBToggle writes `.ok` to confirm the new build started cleanly. If that file is missing next launch, the old exe is restored — so a broken update can't leave you unable to run the app.

**Does this modify PowerToys itself?**
- No. MWBToggle only reads/writes PowerToys' Mouse Without Borders `settings.json` — the same file PowerToys already edits when you toggle those switches in its UI. PowerToys keeps running the whole time.

**Why does the pause icon stay up after I exit?**
- It doesn't — if the tray still shows a pause icon after a proper *Exit* from the menu, the tray cache is stale. Hover the notification area to force Explorer to refresh.

## Project Structure

| Path | Description |
|------|-------------|
| `MWBToggle.csproj` | .NET 8 project file |
| `Program.cs` | Entry point — per-session single-instance enforcement |
| `MWBToggleApp.cs` | Main tray app — toggle, sync, pause, OSD, config |
| `GlobalHotkey.cs` | Win32 RegisterHotKey (with MOD_NOREPEAT) + AHK-style parsing |
| `UpdateDialog.cs` | Self-update UI with SHA256 verification and rollback sentinel |
| `Logger.cs` | Tiny rolling log at `%LOCALAPPDATA%\MWBToggle\` |
| `AboutForm.cs` | About dialog with GitHub, Update, and Open-log-folder buttons |
| `IniConfig.cs` | Minimal INI file reader |
| `on.ico` / `off.ico` | Tray icons (embedded as resources) |

## License

[MIT](LICENSE)

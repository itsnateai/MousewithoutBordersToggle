# MWBToggle

Toggle **Mouse Without Borders** clipboard and file sharing on/off with a hotkey or tray icon click.

## What It Does

Toggles the `ShareClipboard` and `TransferFile` settings in PowerToys Mouse Without Borders. Useful when you want to quickly disable clipboard sharing for privacy (passwords, sensitive data) without opening PowerToys settings.

- **Hotkey**: `Ctrl + Alt + C` (configurable in script)
- **Tray icon**: Green = sharing ON, Red = sharing OFF
- **Left-click** tray icon to toggle
- **Right-click** tray icon for menu

## Requirements

- Windows 10/11
- [AutoHotkey v2](https://www.autohotkey.com/)
- [PowerToys](https://github.com/microsoft/PowerToys) with Mouse Without Borders enabled

## Installation

1. Install [AutoHotkey v2](https://www.autohotkey.com/)
2. Clone or download this repo
3. Double-click `MWBToggle.ahk` to run

## Customization

Edit `MWBToggle.ahk` line 23 to change the hotkey:

```ahk
global g_hotkey := "^!c"   ; Ctrl + Alt + C
```

Modifier symbols: `#` = Win, `^` = Ctrl, `!` = Alt, `+` = Shift

## Compiling to .exe

To distribute without requiring AutoHotkey installed:

1. Install [AutoHotkey v2](https://www.autohotkey.com/)
2. Run the compiler:
   ```
   Ahk2Exe.exe /in MWBToggle.ahk /out MWBToggle.exe /compress 0
   ```
   Use `/compress 0` to avoid Windows Defender false positives.
3. Place `on.ico` and `mwb.ico` in the same folder as `MWBToggle.exe`

## Troubleshooting

**"Mouse Without Borders doesn't appear to be running"**
- Make sure PowerToys is running and Mouse Without Borders is enabled in PowerToys settings.

**"Settings file not found"**
- Mouse Without Borders must be run at least once to create its settings file.
- Default path: `%LOCALAPPDATA%\Microsoft\PowerToys\MouseWithoutBorders\settings.json`

**"Could not write to settings.json"**
- The file may be locked by MWB. Wait a moment and try again.
- If persistent, close PowerToys, toggle, then reopen PowerToys.

**Tray icon doesn't update**
- The icon syncs every 5 seconds. If it seems stuck, right-click the tray icon and select "Toggle" to force a sync.

## Files

| File | Purpose |
|------|---------|
| `MWBToggle.ahk` | Main script |
| `on.ico` | Tray icon — sharing ON (green) |
| `mwb.ico` | Tray icon — sharing OFF (red) |

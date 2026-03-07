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

Edit `MWBToggle.ahk` line 20 to change the hotkey:

```ahk
global g_hotkey := "^!c"   ; Ctrl + Alt + C
```

Modifier symbols: `#` = Win, `^` = Ctrl, `!` = Alt, `+` = Shift

## Files

| File | Purpose |
|------|---------|
| `MWBToggle.ahk` | Main script |
| `on.ico` | Tray icon — sharing ON (green) |
| `off.ico` | Tray icon — sharing OFF (red) |

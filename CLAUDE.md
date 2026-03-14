# CLAUDE.md — MWBToggle

## Project Overview

MWBToggle is a lightweight AutoHotkey v2 tray utility that toggles Mouse Without Borders (PowerToys) clipboard and file sharing on/off via hotkey or tray icon click. Single-file script (~300 lines), no dependencies beyond AHK v2.

**Repo:** https://github.com/itsnateai/MousewithoutBordersToggle
**Version:** 1.4.2
**Stack:** AutoHotkey v2 (Windows only)
**License:** MIT

## Architecture

Everything lives in `MWBToggle.ahk`. No build system, no external dependencies.

```
MWBToggle.ahk      — All logic: config, tray menu, toggle, helpers
on.ico              — Tray icon for ON state (green)
mwb.ico             — Tray icon for OFF state (red)
MWBToggle.ini       — Optional user config (gitignored, not shipped)
```

### How It Works

1. Reads PowerToys MWB `settings.json` via regex match on `ShareClipboard` and `TransferFile`
2. Flips `true`/`false` via `RegExReplace`, validates result, writes back
3. Tray icon syncs every 5 seconds by re-reading the settings file
4. All notifications use `ToolTip()` at cursor position (not `TrayTip`)

### Settings File Path

`%LOCALAPPDATA%\Microsoft\PowerToys\MouseWithoutBorders\settings.json`

## Conventions

### Notifications
- Use `ShowOSD(msg, duration)` — never `MsgBox` for transient info, never `TrayTip`
- `MsgBox` only for errors that need user acknowledgment or the About dialog
- Duration: 3000ms for info, 5000ms for warnings

### Icon Fallback Chain
1. Custom `.ico` file on disk (`g_icoOn`, `g_icoOff`)
2. System icon from `imageres.dll` (icon 101 for ON, 98 for OFF)

### Error Handling
- All `FileRead` calls wrapped in `try/catch` — MWB briefly locks `settings.json`
- `FileOpen` for writes uses a 3-attempt retry loop (200ms delay)
- `FileCopy` backup before every write
- `Hotkey()` registration wrapped in try/catch with fallback to default

### Config (MWBToggle.ini)
- Optional file, not shipped — user creates it
- Keys: `Hotkey`, `ConfirmToggle`, `SoundFeedback` under `[Settings]`
- Empty/missing values silently use defaults
- Invalid hotkey triggers MsgBox warning + fallback to `^!c`

### Tray Menu Pattern
- `A_TrayMenu.ClickCount := 1` — single-click to toggle
- Default action: "Toggle MWB Clipboard/Files"
- Hotkey label shown as disabled menu item (display only)

## Compilation

```
Ahk2Exe.exe /in MWBToggle.ahk /out MWBToggle.exe /icon on.ico /compress 0
```

- `/compress 0` is mandatory — compression triggers Windows Defender false positives
- Place `on.ico` and `mwb.ico` next to the compiled `.exe`

## Gotchas

1. **MWB locks settings.json** — writes and reads can fail if MWB is actively processing. Always use try/catch on FileRead, retry loop on FileOpen.
2. **Icon filenames** — OFF state uses `mwb.ico` (not `off.ico`). Changed in v1.4.1.
3. **Explorer restart** — `TaskbarCreated` message handler re-registers the tray icon. Without it, the icon disappears permanently after Explorer crash/restart.
4. **Pause timer replacement** — `SetTimer(ResumeSharing, -N)` replaces any previous timer for `ResumeSharing`. Clicking "Pause 5 min" then "Pause 30 min" correctly extends, doesn't stack.
5. **ShowOSD closure cleanup** — `SetTimer(() => ToolTip(), -duration)` creates a one-shot timer. AHK v2 automatically deletes one-shot timers after they fire, so the closure is GC'd. No leak.

## Known P3-P4 Items (see AUDIT_TASKS.md)

- Settings write is not fully atomic (truncate+write pattern; backup exists but no auto-restore on corruption)
- Inconsistent terminology: "Clipboard/Files" vs "Clipboard & File Transfer" vs "clipboard and file sharing"

## Version History

See CHANGELOG.md for full history. Key versions:
- 1.0.0 — Initial release
- 1.3.0 — INI config, sound feedback
- 1.4.0 — ToolTip OSD (replaced TrayTip)
- 1.4.1 — OFF icon fix (mwb.ico)
- 1.4.2 — FileRead error handling, hotkey validation, Explorer restart recovery

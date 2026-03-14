# Changelog

## [1.5.0] — 2026-03-14

### New Features
- **About dialog with GitHub button** — replaced plain MsgBox with a proper GUI window showing version, description, hotkey, and a GitHub button that opens the repo
- **Pause timer checkmark** — the active pause duration (5/15/30 min) now shows a checkmark in the Pause Sharing submenu; clears when the timer expires and sharing resumes

### Settings/GUI
- **Screenshots** — added tray icon and menu screenshots to README

## [1.4.2] — 2026-03-13

### Fixed
- **FileRead error handling** — `DoToggle()` and `SyncTray()` now catch exceptions when `settings.json` is locked by MWB, preventing unhandled crashes
- **Hotkey validation** — invalid hotkey string from `MWBToggle.ini` no longer crashes on startup; shows warning and falls back to `Ctrl+Alt+C`
- **Explorer restart recovery** — tray icon re-registers on `TaskbarCreated` message so the icon reappears after Explorer crashes/restarts

### Changed
- **README** — removed screenshot placeholder text, added full INI config documentation
- **.gitignore** — added `MWBToggle.ini` (user config should not be committed)

## [1.4.1] — 2026-03-12

### Fixed
- **OFF state icon** — tray icon for the OFF state now uses `mwb.ico` instead of the previously hardcoded `off.ico` filename, matching the documented file layout.

## [1.4.0] — 2026-03-10

### Changed
- **Multi-monitor OSD notifications** — replaced all `TrayTip` calls with `ShowOSD()` helper using `ToolTip()` at cursor position. Notifications now appear on whichever monitor the user is working on, eliminating the primary-monitor-only limitation.

## [1.3.0] — 2026-03-09

### Added
- **INI config file support** — read hotkey, ConfirmToggle, and SoundFeedback from `MWBToggle.ini` (falls back to defaults if file missing)
- **Sound feedback option** — optional audible beep on toggle (high tone for ON, low tone for OFF), enabled via config

## [1.2.0] — 2026-03-09

### Added
- **Run at Startup** toggle in tray menu — creates/removes shortcut in Windows Startup folder
- **About** dialog showing version and hotkey info
- **Toggle confirmation** option (`g_confirmToggle`) — prompts before each toggle when enabled
- **Pause Sharing** submenu — temporarily disable sharing for 5, 15, or 30 minutes with auto-resume
- Troubleshooting section in README
- Compilation instructions in README

## [1.1.0] — 2026-03-06

### Added
- Version string (`g_version`) shown in tray tooltip
- Periodic tray icon sync every 5 seconds (detects external settings changes)
- JSON structure validation after regex replacement before writing
- Retry logic (3 attempts, 200ms delay) for locked settings file
- Backup (`settings.json.bak`) created before each write
- Comment explaining the `Sleep(300)` delay purpose

### Fixed
- Indentation in `OpenPowerToys()` function

## [1.0.0] — 2026-03-06

### Initial release
- Toggle ShareClipboard and TransferFile via hotkey (Ctrl+Alt+C)
- Tray icon reflects current state (green ON / red OFF)
- Left-click tray icon to toggle, right-click for menu
- Custom icon support (on.ico / off.ico)
- MWB process detection — warns if not running
- Machine-wide PowerToys install fallback

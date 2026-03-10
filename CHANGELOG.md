# Changelog

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

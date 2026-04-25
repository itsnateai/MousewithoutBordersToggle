# Changelog

*LTR — Long-Term Release · one-click self-update built in.*

## [2.5.14] — 2026-04-25

### Security
- **Self-update download is now capped at 200 MB.** A compromised CDN edge or hostile redirect target inside the allowlist could previously stream multi-GB of garbage to disk inside the 30-second timeout window — filling your free space well before the SHA256 verification step would have caught the mismatch. The cap fires both on the advertised `Content-Length` and inline as bytes are written, so chunked-transfer responses (which omit `Content-Length` entirely) are also caught. Real releases are ~150 MB; the 200 MB cap leaves comfortable headroom.
- **Update integrity-check responses are now capped at 1 MB in memory.** Same threat class for RAM rather than disk: the SHA256SUMS body and releases JSON load entirely into memory via the shared `HttpClient`. The previous default ceiling was ~2 GB; the 1 MB cap is plenty for these small text bodies and prevents a hostile server from blowing up the tray with a giant text response.

## [2.5.13] — 2026-04-23

### Self-update fix
- **The in-app updater now works again.** GitHub recently started serving release-asset downloads from a new CDN host (`release-assets.githubusercontent.com`) alongside the legacy one (`objects.githubusercontent.com`). MWBToggle's update check follows redirects manually and validates each hop against an explicit allowlist — and the new host wasn't on it, so clicking *Upgrade Now* failed with `URL not in allowlist`. Both hosts are now allowlisted. **Affected versions:** v2.5.0–v2.5.12 — those builds shipped with the old allowlist, so they can't self-update to this fix. To get past it, run `winget upgrade itsnateai.MWBToggle` or download `MWBToggle.exe` from the GitHub release page once and replace your copy. After v2.5.13 the self-updater works again as normal.

## [2.5.12] — 2026-04-23

### Self-update integrity
- **Updates now abort if the release is missing its SHA256SUMS file.** Previously, when a release didn't publish a SHA256SUMS asset, the updater logged a warning and applied the download anyway — a pragmatic fallback meant for releases older than v2.5.0 that predate the workflow publishing checksums. That fallback is now version-gated: a release at v2.5.0 or newer without SHA256SUMS is treated as a supply-chain error and the update is aborted with a clear prompt to use `winget upgrade` or download manually. Pre-v2.5.0 releases stay grandfathered so very old installs can still reach a hash-emitting version. No visible change for normal updates from GitHub Releases — this only tightens a path that was never meant to be hit post-v2.5.0. Because MWBToggle isn't Authenticode-signed, SHA256SUMS is the primary integrity control for self-updates.

## [2.5.11] — 2026-04-23

### Menu toggles now persist across restarts
- **"Single-click toggles sharing" and "Middle-click opens MWB Settings" now survive app restart.** Previously, flipping either checkbox from the PowerToys submenu only applied to the current session — on next launch the setting reverted to the default (or whatever was in `MWBToggle.ini`). Now each click persists the new value to the INI immediately, matching the behavior of every other setting in the menu (hotkeys, Run at Startup, etc.). If you'd changed either of these via the menu and been confused why it kept coming back — this was why.

## [2.5.10] — 2026-04-18

### Explorer-restart recovery
- **Tray icon stays visible even if Explorer crashes and restarts while MWBToggle is running.** Previously the `TaskbarCreated` handler re-added the icon but didn't re-run the Win11 auto-promote logic — so if your per-icon registry entry had been cleared in the meantime (rare, but possible via Settings UI), the icon would silently fall back to overflow. Now the promoter re-runs on every Explorer restart, idempotently, and the icon stays pinned.

## [2.5.9] — 2026-04-18

### Tray icon on Windows 11
- **Tray icon now auto-shows in the taskbar on first run.** Previously, Windows 11 hid every new tray icon in the overflow flyout until you went to Settings → Personalization → Taskbar → *Other system tray icons* and flipped the toggle on. For a tray-only app like MWBToggle that's a lousy first-run experience. First launch on Win11 22H2+ now sets the visibility flag for our icon automatically. If you've already toggled the icon off yourself, that choice is respected — we never flip a deliberate OFF back to ON.
- **Tray hover text now shows immediately at startup** (reads `MWB Toggle` before the state-driven text kicks in a second later). Minor on its own, but also what lets Windows populate our registry entry cleanly so the Settings UI lists us correctly.

## [2.5.8] — 2026-04-17

### The headline fix
- **Toggling now actually takes effect in Mouse Without Borders.** Previous versions wrote the new values to `settings.json` using an atomic tmp-file + rename, which is clean on paper but made PowerToys' own file watcher skip the change — so the tray icon flipped but MWB kept sharing the clipboard and files in memory as if nothing had happened. This version writes the file in place the same way the original AHK tool did, which PT's watcher sees and MWB reloads from immediately. The tray toggle now actually toggles sharing.

### Tray tooltip
- **Hover text shows each channel separately.** Previously read `Clipboard/Files: ON` as one combined state, which was misleading when the two channels were in different states. Now reads e.g. `MWBToggle v2.5.8 — Clipboard ON · Files OFF`.

### About dialog
- **Hotkey rows redesigned.** Each hotkey now stacks a bold muted title (`Clipboard + File Transfer`, `File Transfer`) above the key combo in regular weight — much more scannable than the previous single-line `Label: combo` layout.
- **Bottom padding restored.** v2.5.7's redesign put the button row too close to the window chrome; buttons are now nudged up for a cleaner margin.
- **Dropped the `© 2026 itsnateai · MIT License` line** from the dialog. Copyright is already embedded in the exe metadata (Windows Properties → Details), and the GitHub button opens the repo with its LICENSE for anyone who wants it.

### Update dialog
- **"You're on the latest version!" popup has a smaller OK button.** The Cancel-sized button on a simple acknowledgment popup looked out of proportion. Shrunk and re-centered.

### Diagnostics
- **Every successful toggle is now logged** to `%LOCALAPPDATA%\MWBToggle\mwbtoggle.log` as an INFO line with the new state and the settings.json path written. Makes support triage one `tail` away instead of requiring file-system forensics.

## [2.5.7] — 2026-04-17

### About dialog
- **Copyright line** now shown under the "Open log folder" link: `© 2026 itsnateai · MIT License`.

### Documentation
- **README adds an FAQ section** covering uninstall, disabling run-at-startup without uninstalling, what the `.ok` sentinel is for, and why a stale tray icon after Exit means Explorer hasn't refreshed.
- **`SingleClickToggles` INI key is documented** in the customization table (feature has been live since v2.4.0 but wasn't in the README).

### Housekeeping
- **Release workflow has a per-ref `concurrency:` guard.** A re-push of the same tag now queues behind the first run instead of racing two GitHub Release creations. Different tags still publish in parallel.
- **No user-visible behavior change** beyond the About line — the remaining changes are documentation and CI.

## [2.5.6] — 2026-04-17

### Tray
- **Tray tooltip shows pause state.** Hovering the tray icon during a pause now reads `MWBToggle v2.5.6 — Paused (resumes 14:32)` for timed pauses, or `Paused` for an open-ended pause — instead of the old ON/OFF text which made it look as though sharing had simply been turned off.
- **The Exit item has a visible separator** above it, so it no longer crowds the PowerToys submenu.

### Reliability
- **Fewer handle leaks on Exit.** Exiting from the tray menu now disposes the context menu, About dialog, and both state icons — they previously leaked until process teardown.
- **FileSystemWatcher error-recovery no longer leaks a timer per cycle.** When the watcher has to be re-initialized (e.g. network drive reconnect, PowerToys reinstall), the old debounce timer is disposed before the new one replaces it.
- **Logging is lighter on the disk.** The log is kept open with a single handle instead of opening and closing the file on every line, and the truncation check runs periodically instead of on every write. Steady-state logging cost drops significantly.
- **Update dialog: cancelling after close is quiet.** Rapid double-clicks on Cancel could throw `ObjectDisposedException` into the unhandled-exception handler; this path is now silent as intended.
- **Update dialog: the integrity-check URL is held to the same tight origin as the download URL** (previously the general allowlist). A stray redirect target from the GitHub API can no longer reach the hash-verify step.

### UI polish
- **OSD stays on-screen even on narrow displays.** The bubble's width is capped at half the working area, and its left edge is clamped inside the working area — pathological long messages no longer extend past the screen edge.
- **The "update complete" toast drops the decorative check-mark** to match the rest of the app's no-emoji tone.

### Housekeeping
- **`mwbtoggle.ico` is now a proper multi-resolution app icon** with 16/32/48/128/256 frames, distinct from the green-dot tray icon. Win11 Explorer's large-thumbnail view is no longer a blurry upscaled 48×48.
- **GitHub Actions (`actions/checkout`, `actions/setup-dotnet`) are pinned to commit SHAs**, matching the `softprops/action-gh-release` pin already in place. Supply-chain tightening, no behavior change.
- **Dead `Logger.Error` method removed** (zero callers). Warnings and errors both route through `Logger.Warn` as they have since v2.4.x.

## [2.5.5] — 2026-04-17

### Pause
- **Resume now restores what was on before you paused.** If you were running with Clipboard on and File Transfer off, resuming leaves you in that exact state — not both-on as a surprise. Applies to the 5-minute, 30-minute, and "Until resumed" modes, and survives app exit and reboot.
- **The PowerToys submenu's Clipboard Sharing and File Transfer toggles cleanly cancel an active pause.** Previously, toggling either one mid-pause left the pause timer running in the background with stale menu state. Now the pause ends the moment you take manual control — and the on-screen confirmation says so explicitly (e.g. `Clipboard · ON · pause cancelled`), so a misclick doesn't silently discard your pre-pause config.
- **"Until resumed" pauses survive restart.** An unlimited pause no longer silently forgets itself when you exit the app.
- **The primary hotkey asks the right question during a pause.** If you hit the toggle hotkey or tray-click while paused (with "Prompt before toggle" enabled), the confirmation now says "Resume sharing now?" and restores your pre-pause state — instead of the old generic prompt that turned both back on.
- **External changes during a pause are honored.** If you open PowerToys and change a sharing toggle yourself while MWBToggle is paused, the pause silently steps aside and keeps your change instead of overwriting it on auto-resume. You'll see `Pause ended · kept your change`.

### Reliability
- **Pause survives a crash or task-kill between writes.** The sidecar that remembers your pre-pause state is now written before the settings file, and written atomically, so a power-cut mid-pause never leaves you stuck with sharing off and no way to auto-resume.
- **Failed pause attempts no longer leave the UI claiming to be paused.** If Windows has the settings file locked when you hit Pause, the menu state rolls back and an OSD tells you to try again — no more ghost pause timers.
- **Resume shows an error when the settings file is locked** instead of failing silently.
- **Corrupt or truncated pause sidecars are now logged** before being discarded, making the (rare) recovery path easier to diagnose.

### Migration
- Upgrading mid-pause from v2.5.4 is safe. An in-flight pause from the previous version is detected on startup and continues to tick down; because it predates this release's snapshot, resume falls back to turning both back on — matching v2.5.4's behavior exactly. Any pause started after upgrading uses the new snapshot-and-restore behavior.

## [2.5.4] — 2026-04-17

### Tray menu polish
- **Hotkeys submenu is narrower.** Each binding now stacks as a clickable title with the key combo on a greyed line beneath it, instead of one wide `Title: combo` line. The submenu's width follows the longest label, not the longest combo.
- **"Clipboard + File Transfer"** is the full label everywhere it appears (tray menu and About dialog), replacing the tighter `Clipboard + Transfer`.

### Hotkey picker
- **Preview and Current no longer duplicate each other on open.** Preview starts as `—` until you press a combo; the currently-bound hotkey appears on its own greyed line below.
- **Enter now commits Set, not Unbind.** Focus jumps to the Set button the moment a valid combo is captured, so hitting Enter binds it instead of clearing.
- **Unbind / Set / Cancel are evenly spread** across the dialog instead of crammed to the left.
- **The primary toggle hotkey can be unbound.** If you clear it, the app is still usable via the tray icon and menu — and the empty hotkey survives restart instead of silently falling back to the default.

### About dialog
- **Both hotkeys are now listed.** Clipboard + File Transfer and File Transfer each show on their own line.
- **Values refresh when you reopen About.** Previously the labels were cached from first open and showed stale hotkeys after a rebind.

### OSD polish
- **Shorter, more glanceable messages.** `Paused · 5 min`, `Resumed`, `Clipboard · ON`, `Hotkey · Win + Ctrl + Shift + F`. The `MWBToggle: ` prefix is gone from every message — the bubble sits next to your tray icon, you already know which app did it.
- **Softer pill.** Regular-weight font, 84% opacity, muted greens and reds, slightly shorter height. Reads as ambient confirmation, not a system alert.

## [2.5.3] — 2026-04-17

### Pause
- **A timed pause now survives app exit and reboot.** If you paused sharing for 30 minutes and then exited MWBToggle, restarted Windows, or lost power, the app no longer forgets — on next launch it either resumes immediately (if the window has already passed) or keeps counting down the remainder.

### Tray menu
- **Clipboard Sharing has its own toggle.** The tray menu now lets you flip clipboard sharing independently of file transfer, instead of both moving together.
- **A second hotkey can be bound for File Transfer alone.** Leaves the primary hotkey free to toggle Clipboard + File together, so you can disable just the file side without losing copy/paste across screens.
- **Clicking a pause option that's already active now resumes instead of restarting the timer.** Previously clicking "30 minutes" while it was already checked just re-armed another 30 minutes, with no way to cancel an "Until resumed" pause except the manual toggle.
- **Picker no longer triggers other apps' hotkeys.** While capturing a new hotkey, the key combo is suppressed from the rest of Windows — pressing a shortcut to rebind it won't also fire whatever else is listening for it.

### Polish
- **OSD bubble matches MicMute and SyncTray exactly.**
- **"Clipboard + File Transfer"** reads more clearly than the old `Clipboard/Transfer` label in the Hotkeys submenu.
- **Fresh installs now default to `Win+Ctrl+Shift+F`** (was `Ctrl+Alt+C`). Existing `MWBToggle.ini` files keep whatever hotkey they already had.

## [2.5.2] — 2026-04-17 — new LTR

### Security
- **Update downloads can no longer be hijacked by a redirect.** The update check now validates every hop of the download chain against an explicit allowlist — a compromised or crafted redirect from GitHub can't silently hand off to an attacker-controlled server.

### Accessibility & keyboard
- **Enter and Esc now work in every dialog.** About / Update / Set-Hotkey all respond to Enter for the primary action and Esc to close — previously you had to click with a mouse.
- **Screen-reader descriptions** added to all dialog buttons, so Narrator announces what each button does instead of just the label.

### Reliability
- **"Run at Startup" heals itself after a winget upgrade.** When winget moves the executable into a new versioned folder, the Startup-folder shortcut is refreshed on the next app launch — even a duplicate launch that exits immediately still updates the shortcut.
- **Corrupted or oversized `MWBToggle.ini` no longer crashes startup.** If the INI is larger than 64 KB (malformed, not what MWBToggle writes), the app falls back to defaults instead of out-of-memory.

### Polish
- **App file properties** now show Product, Company, Copyright, and File Version in Explorer's Properties → Details tab. Helps Windows SmartScreen reputation and makes the exe look like a real signed-ish app.
- **README feature list** updated — the "floating tooltip at cursor" line was stale after v2.5.1's OSD rewrite.

## [2.5.1] — 2026-04-16

### OSD polish
- **Tooltip no longer shows up off the bottom of the screen** when you click the tray icon. It's now pinned just above the system tray, same as MicMute and SyncTray.
- **Discreet dark bubble** with a colored state dot — green when sharing turns ON, red when it turns OFF or pauses. Easier to read the result from the corner of your eye without parsing the text.

## [2.5.0] — 2026-04-16

### Reliability
- **Holding the hotkey is safe.** The toggle fires once per press, even if you lean on the key — no more rapid-fire flipping.
- **Your MWB config can't get corrupted mid-toggle.** A crash, power loss, or antivirus lock in the middle of a toggle can no longer leave the settings file half-written, and the backup reliably holds the previous good state.
- **Catches a silent MWB schema change.** If a future PowerToys update changes how it stores file-transfer state, MWBToggle now tells you the toggle didn't land instead of pretending it did.
- **Double-tapping the hotkey during a toggle is harmless.** The second press is ignored until the first one finishes.

### Pause
- **Pause survives sleep.** If you pause sharing for 30 minutes and your laptop goes to sleep for two hours, sharing resumes the moment you wake up — not 30 minutes after.
- **Manually toggling no longer fights an active pause.** Hitting the hotkey or menu item during a pause cancels the auto-resume so it can't flip sharing off unexpectedly later. If the toggle is cancelled from the confirm dialog, the pause is preserved.

### Tray & watcher
- **Works even if PowerToys isn't installed yet.** Install MWBToggle first, set up PowerToys later — the tray icon catches up the moment MWB's settings file appears, no restart required.

### Hotkey picker
- **Win-key combos can be captured.** Hotkeys like `Win+C` or `Win+Alt+X` now work in the picker dialog, not just in the INI file.
- **Clear feedback on unsupported keys.** Punctuation and unusual keys no longer quietly rebind to Ctrl+Alt+C — the dialog tells you to use a letter, digit, or F-key.

### Self-update
- **Every download is now integrity-checked.** Updates verify a published SHA256 checksum before swapping the exe; if the check fails or can't run, the update aborts cleanly and tells you to try again (or use `winget upgrade`).
- **Safer rollback on a bad update.** If a fresh version crashes on first launch, the previous version is still sitting on disk next to it as `MWBToggle.exe.old` — rename it back to recover manually.

### Troubleshooting
- **There's finally a log.** If something doesn't work, open the About dialog and click *Open log folder* — a small rolling log at `%LOCALAPPDATA%\MWBToggle\` captures what went wrong. Previously silent failures (pause reads, settings writes, startup shortcut, update paths) now leave a trail.

### Multi-user / shared PCs
- **Each Windows session runs its own instance cleanly.** Another user on the same PC can't block your tray icon from starting.

## [2.4.3] — 2026-04-15

### Fixed
- **Updates can be integrity-checked.** MWBToggle now validates a published checksum on downloaded updates before swapping the exe. (Checksums weren't actually published until 2.5.0; this release shipped the client-side plumbing for it.)

## [2.4.2] — 2026-04-15

### Fixed
- **Post-update relaunch is more reliable.** The fresh instance after an upgrade no longer sometimes exits immediately because the old one hasn't finished shutting down yet.

## [2.4.1] — 2026-04-13

### Fixed
- **WinGet installs remember their config.** When installed via winget, the settings file now lives in `%APPDATA%\MWBToggle\` so it survives upgrades and reinstalls. Portable installs still keep it next to the exe.
- **Small distribution hardening** — extra checks on where updates are downloaded from and where they're applied.

## [2.4.0] — 2026-04-13

### New Features
- **One-click self-update built in.** Open *About* → *Update* and MWBToggle checks GitHub, shows you what's new, downloads it with a progress bar, and relaunches into the new version. No more manual download-and-replace.
- **Post-update confirmation toast** — brief floating message near the tray after a successful upgrade so you know it worked.

### Changed
- **Legacy AutoHotkey script removed from the repo.** Only referenced during the 2.x rewrite; no longer needed.
- **Single version source of truth** — the version displayed in the About dialog, the tray tooltip, and `winget list` all match automatically.

## [2.3.0] — 2026-03-18

### New Features
- **Hotkey picker** — click the hotkey label in the menu to capture a new key combo on the fly
- **Single-click toggle option** — disable left-click toggling if you only want hotkey control
- **"Until resumed" pause** — pause sharing indefinitely (no auto-resume timer)
- **Restructured menu** — version title bar, About moved into PowerToys submenu, Run at Startup moved into PowerToys submenu

### Changed
- Pause options simplified to 5 minutes, 30 minutes, and Until resumed (removed 15 min)
- Repo restructured for public release — C# source at root, AHK archived in `legacy/`

## [2.2.0] — 2026-03-18

### New Features
- **Independent File Transfer toggle** — new "File Transfer" checkmark in PowerToys submenu controls TransferFile without touching ShareClipboard
- **Guard rail** — prevents enabling file transfer when clipboard sharing is OFF (ShareClipboard must be ON)
- **Real-time sync** — FileSystemWatcher updates the File Transfer checkmark when settings change externally

## [2.1.0] — 2026-03-18

### New Features
- **Middle-click opens MWB Settings** — middle-click the tray icon to jump straight to PowerToys Mouse Without Borders settings (uses `--open-settings=MouseWithoutBorders` deep link)
- **PowerToys submenu** — "Open PowerToys" and "MWB Settings" grouped under a PowerToys submenu with a toggleable "Middle-click opens MWB Settings" option
- **INI config** — new `MiddleClickMwbSettings=true` setting in `[Settings]` section

## [2.0.0] — 2026-03-18

### New Features
- **C# port** — complete rewrite from AutoHotkey v2 to C# (.NET 8 Windows Forms) with full feature parity.
- **Instant state updates** — the tray now reflects changes the moment you flip ShareClipboard in PowerToys, instead of on a 5-second delay. Zero CPU when idle.
- **Embedded icons** — tray icons are built into the .exe, no external `.ico` files needed (custom icons on disk still override).

### Performance
- **Zero idle overhead** — the tray sits quietly in the background and only does work when something actually changes.
- **No mouse interference** — scroll wheels and clicks in other apps are no longer affected while MWBToggle is running.

### Bug Fixes
- **No memory creep over long sessions.**
- **Clean build and clean background updates** — the tray no longer gets into a weird state when PowerToys writes its settings file while the menu is open.
- **No more pop-up error dialogs** — errors now appear as a brief floating tooltip instead of a modal you have to dismiss.

### Code Quality
- **Icon fallback chain** — your custom icons on disk > built-in icons > a system fallback, so the tray is never blank.
- **Tray icon recovers after Explorer restarts.**
- **No resource leak during startup shortcut handling.**
- **Only one copy runs at a time** — launching MWBToggle twice cleanly replaces the old instance instead of leaving two tray icons fighting.

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

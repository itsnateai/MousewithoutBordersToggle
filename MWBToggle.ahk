; ╔══════════════════════════════════════════════════════════════════════════╗
; ║  MWBToggle.ahk  —  Toggle Mouse Without Borders clipboard & file share  ║
; ║  Requires: AutoHotKey v2  (https://www.autohotkey.com/)                 ║
; ║                                                                          ║
; ║  • Hotkey below  → toggle ShareClipboard + TransferFile on/off          ║
; ║  • Tray icon shows current state (green = ON, red = OFF)                ║
; ║  • Left-click  tray icon → toggle                                       ║
; ║  • Right-click tray icon → menu                                         ║
; ║                                                                          ║
; ║  Files (place in the same folder as this script):                       ║
; ║    on.ico   — shown when sharing is ON  (falls back to Windows icon)    ║
; ║    off.ico  — shown when sharing is OFF (falls back to Windows icon)    ║
; ╚══════════════════════════════════════════════════════════════════════════╝

#Requires AutoHotkey v2.0
#SingleInstance Force
Persistent

; ── CONFIGURATION ────────────────────────────────────────────────────────────
global g_version      := "1.1.0"
global g_hotkey       := "^!c"   ; Ctrl + Alt + C  — change to whatever you like
global g_settingsPath := EnvGet("LOCALAPPDATA") "\Microsoft\PowerToys\MouseWithoutBorders\settings.json"

; PowerToys executable — used for the "Open PowerToys" tray item and
; the MWB-running check. Adjust if your install location differs.
global g_powerToysExe := EnvGet("LOCALAPPDATA") "\PowerToys\PowerToys.exe"

; Custom icon paths — place on.ico / off.ico in the same folder as this script
global g_icoOn  := A_ScriptDir "\on.ico"
global g_icoOff := A_ScriptDir "\off.ico"

; ── TRAY MENU ────────────────────────────────────────────────────────────────
; FIX: hotkey label is now added BEFORE the separator so it sits logically
; under the toggle item, not orphaned below Exit.
hotkeyLabel := "Hotkey: " HotkeyToReadable(g_hotkey)
A_TrayMenu.Delete()
A_TrayMenu.Add("Toggle MWB Clipboard/Files", (*) => DoToggle())
A_TrayMenu.Add(hotkeyLabel, (*) => 0)
A_TrayMenu.Disable(hotkeyLabel)
A_TrayMenu.Add()
A_TrayMenu.Add("Open PowerToys",  (*) => OpenPowerToys())
A_TrayMenu.Add("Exit",            (*) => ExitApp())
A_TrayMenu.Default    := "Toggle MWB Clipboard/Files"
A_TrayMenu.ClickCount := 1

; ── HOTKEY ───────────────────────────────────────────────────────────────────
Hotkey(g_hotkey, (*) => DoToggle())

; ── INITIAL STATE ─────────────────────────────────────────────────────────────
SyncTray()
; P1-01: Periodically sync tray icon in case settings change externally
SetTimer(SyncTray, 5000)

; ╔══════════════════════════════════════════════════════════════════════════╗
; ║  Core                                                                    ║
; ╚══════════════════════════════════════════════════════════════════════════╝

DoToggle() {
    global g_settingsPath

    ; FIX: warn if Mouse Without Borders isn't actually running — the file
    ; write would succeed but MWB wouldn't pick up the change.
    if !ProcessExist("PowerToys.MouseWithoutBorders.exe") {
        TrayTip("MWBToggle", "Mouse Without Borders doesn't appear to be running.", 3)
        return
    }

    if !FileExist(g_settingsPath) {
        MsgBox("Settings file not found:`n" g_settingsPath, "MWBToggle", "IconX")
        return
    }

    ; Read JSON
    json := FileRead(g_settingsPath, "UTF-8")

    ; Detect current ShareClipboard state
    if RegExMatch(json, '"ShareClipboard"\s*:\s*\{\s*"value"\s*:\s*(true|false)', &m) {
        currentlyOn := (m[1] = "true")
    } else {
        MsgBox("Could not find ShareClipboard in settings.json.`n`nMake sure Mouse Without Borders has been run at least once.", "MWBToggle", "IconX")
        return
    }

    ; Flip both values
    newVal := currentlyOn ? "false" : "true"
    json := RegExReplace(json, '("ShareClipboard"\s*:\s*\{\s*"value"\s*:\s*)(true|false)', "$1" newVal)
    json := RegExReplace(json, '("TransferFile"\s*:\s*\{\s*"value"\s*:\s*)(true|false)',   "$1" newVal)

    ; P1-04: Verify the replacement took effect before writing
    if !RegExMatch(json, '"ShareClipboard"\s*:\s*\{\s*"value"\s*:\s*' newVal) {
        MsgBox("Failed to update ShareClipboard in settings.json — the JSON structure may have changed.`n`nNo changes were written.", "MWBToggle", "IconX")
        return
    }

    ; P0-02: Backup before writing in case of crash/power loss
    FileCopy(g_settingsPath, g_settingsPath ".bak", true)

    ; P0-01: Retry loop — settings.json may be briefly locked by MWB
    f := false
    loop 3 {
        f := FileOpen(g_settingsPath, "w", "UTF-8-RAW")
        if f
            break
        Sleep(200)
    }
    if !f {
        MsgBox("Could not write to settings.json — the file may be locked by Mouse Without Borders.`n`nPlease try again in a moment.", "MWBToggle", "IconX")
        return
    }
    f.Write(json)
    f.Close()

    ; P1-03: Delay for MWB to detect the file change and reload settings
    Sleep(300)
    SyncTray()

    newState := currentlyOn ? "OFF" : "ON"
    TrayTip("MWBToggle", "Clipboard & File Transfer: " newState, 2)
}

SyncTray() {
    global g_settingsPath, g_icoOn, g_icoOff
    json := FileExist(g_settingsPath) ? FileRead(g_settingsPath, "UTF-8") : ""
    if RegExMatch(json, '"ShareClipboard"\s*:\s*\{\s*"value"\s*:\s*(true|false)', &m) {
        on := (m[1] = "true")
    } else {
        on := false
    }
    if on {
        if FileExist(g_icoOn)
            TraySetIcon(g_icoOn)
        else
            TraySetIcon(A_WinDir "\System32\imageres.dll", 101)
        A_IconTip := "MWBToggle v" g_version " — Clipboard/Files: ON"
    } else {
        if FileExist(g_icoOff)
            TraySetIcon(g_icoOff)
        else
            TraySetIcon(A_WinDir "\System32\imageres.dll", 98)
        A_IconTip := "MWBToggle v" g_version " — Clipboard/Files: OFF"
    }
}

OpenPowerToys() {
    global g_powerToysExe
    if FileExist(g_powerToysExe) {
        Run(g_powerToysExe)
    } else {
        ; PowerToys not found at the expected path — try the machine-wide install
        machinePath := EnvGet("ProgramFiles") "\PowerToys\PowerToys.exe"
        if FileExist(machinePath) {
            Run(machinePath)
        } else {
            MsgBox("Could not find PowerToys.`n`nExpected:`n" g_powerToysExe "`n`nYou can open it manually from the Start menu.", "MWBToggle", "Icon!")
        }
    }
}

; ╔══════════════════════════════════════════════════════════════════════════╗
; ║  Helpers                                                                 ║
; ╚══════════════════════════════════════════════════════════════════════════╝

; Translate AHK hotkey symbols into a readable string e.g. "^!c" → "Ctrl + Alt + C"
; Uses a prefix-only match so key names like "Numpad+" are never mistaken for Shift.
HotkeyToReadable(hk) {
    mods   := ""
    prefix := RegExMatch(hk, "^([#^!+]+)", &m) ? m[1] : ""
    key    := RegExReplace(hk, "^[#^!+]+", "")
    if (InStr(prefix, "#"))
        mods .= "Win + "
    if (InStr(prefix, "^"))
        mods .= "Ctrl + "
    if (InStr(prefix, "!"))
        mods .= "Alt + "
    if (InStr(prefix, "+"))
        mods .= "Shift + "
    return mods . StrUpper(key)
}

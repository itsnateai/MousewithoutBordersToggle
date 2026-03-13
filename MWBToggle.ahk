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
; ║    mwb.ico  — shown when sharing is OFF (falls back to Windows icon)    ║
; ╚══════════════════════════════════════════════════════════════════════════╝

#Requires AutoHotkey v2.0
#SingleInstance Force
Persistent

; ── CONFIGURATION ────────────────────────────────────────────────────────────
global g_version      := "1.4.1"

; Default values — overridden by MWBToggle.ini if it exists
global g_hotkey         := "^!c"
global g_settingsPath   := EnvGet("LOCALAPPDATA") "\Microsoft\PowerToys\MouseWithoutBorders\settings.json"
global g_powerToysExe   := EnvGet("LOCALAPPDATA") "\PowerToys\PowerToys.exe"
global g_icoOn          := A_ScriptDir "\on.ico"
global g_icoOff         := A_ScriptDir "\mwb.ico"
global g_confirmToggle  := false
global g_soundFeedback  := false
global g_startupShortcut := A_Startup "\MWBToggle.lnk"

; P4-01: Load config from INI file (falls back to defaults above)
LoadConfig()

; ── TRAY MENU ────────────────────────────────────────────────────────────────
hotkeyLabel := "Hotkey: " HotkeyToReadable(g_hotkey)
A_TrayMenu.Delete()
A_TrayMenu.Add("Toggle MWB Clipboard/Files", (*) => DoToggle())
A_TrayMenu.Add(hotkeyLabel, (*) => 0)
A_TrayMenu.Disable(hotkeyLabel)
A_TrayMenu.Add()

; P2-04: Pause sharing submenu
pauseMenu := Menu()
pauseMenu.Add("5 minutes",  (*) => PauseSharing(5))
pauseMenu.Add("15 minutes", (*) => PauseSharing(15))
pauseMenu.Add("30 minutes", (*) => PauseSharing(30))
A_TrayMenu.Add("Pause Sharing", pauseMenu)

; P2-01: Run at Startup toggle
A_TrayMenu.Add("Run at Startup", (*) => ToggleStartup())
if FileExist(g_startupShortcut)
    A_TrayMenu.Check("Run at Startup")

A_TrayMenu.Add()
A_TrayMenu.Add("Open PowerToys",  (*) => OpenPowerToys())
A_TrayMenu.Add("About",           (*) => ShowAbout())
A_TrayMenu.Add("Exit",            (*) => ExitApp())
A_TrayMenu.Default    := "Toggle MWB Clipboard/Files"
A_TrayMenu.ClickCount := 1

; ── HOTKEY ───────────────────────────────────────────────────────────────────
try {
    Hotkey(g_hotkey, (*) => DoToggle())
} catch as e {
    MsgBox("Invalid hotkey: " g_hotkey "`n`nCheck your MWBToggle.ini [Settings] Hotkey value.`n`nFalling back to Ctrl+Alt+C.", "MWBToggle", "Icon!")
    g_hotkey := "^!c"
    Hotkey(g_hotkey, (*) => DoToggle())
}

; ── INITIAL STATE ─────────────────────────────────────────────────────────────
SyncTray()
; P1-01: Periodically sync tray icon in case settings change externally
SetTimer(SyncTray, 5000)

; P2: Re-register tray icon when Explorer restarts (taskbar re-created)
global WM_TASKBARCREATED := DllCall("RegisterWindowMessage", "Str", "TaskbarCreated")
OnMessage(WM_TASKBARCREATED, (*) => SyncTray())

; ╔══════════════════════════════════════════════════════════════════════════╗
; ║  Core                                                                    ║
; ╚══════════════════════════════════════════════════════════════════════════╝

DoToggle(confirm := true) {
    global g_settingsPath, g_confirmToggle

    ; FIX: warn if Mouse Without Borders isn't actually running — the file
    ; write would succeed but MWB wouldn't pick up the change.
    if !ProcessExist("PowerToys.MouseWithoutBorders.exe") {
        ShowOSD("MWBToggle: Mouse Without Borders doesn't appear to be running.", 5000)
        return
    }

    if !FileExist(g_settingsPath) {
        MsgBox("Settings file not found:`n" g_settingsPath, "MWBToggle", "IconX")
        return
    }

    ; Read JSON — file may be briefly locked by MWB
    try {
        json := FileRead(g_settingsPath, "UTF-8")
    } catch as e {
        ShowOSD("MWBToggle: Could not read settings.json — file may be locked. Try again.", 5000)
        return
    }

    ; Detect current ShareClipboard state
    if RegExMatch(json, '"ShareClipboard"\s*:\s*\{\s*"value"\s*:\s*(true|false)', &m) {
        currentlyOn := (m[1] = "true")
    } else {
        MsgBox("Could not find ShareClipboard in settings.json.`n`nMake sure Mouse Without Borders has been run at least once.", "MWBToggle", "IconX")
        return
    }

    ; P2-03: Optional confirmation before toggling
    if g_confirmToggle && confirm {
        prompt := "Turn clipboard/file sharing " (currentlyOn ? "OFF" : "ON") "?"
        if MsgBox(prompt, "MWBToggle", "YesNo") != "Yes"
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
    ShowOSD("MWBToggle: Clipboard & File Transfer " newState)

    ; P4-02: Optional sound feedback — different tones for ON vs OFF
    if g_soundFeedback
        SoundBeep(currentlyOn ? 400 : 800, 150)
}

SyncTray() {
    global g_settingsPath, g_icoOn, g_icoOff
    try
        json := FileExist(g_settingsPath) ? FileRead(g_settingsPath, "UTF-8") : ""
    catch
        return  ; File locked — skip this sync cycle, retry in 5s
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

; P2-01: Toggle startup shortcut
ToggleStartup() {
    global g_startupShortcut
    if FileExist(g_startupShortcut) {
        FileDelete(g_startupShortcut)
        A_TrayMenu.Uncheck("Run at Startup")
        ShowOSD("MWBToggle: Removed from startup.")
    } else {
        shell := ComObject("WScript.Shell")
        shortcut := shell.CreateShortcut(g_startupShortcut)
        shortcut.TargetPath := A_ScriptFullPath
        shortcut.WorkingDirectory := A_ScriptDir
        shortcut.Description := "MWBToggle"
        shortcut.Save()
        A_TrayMenu.Check("Run at Startup")
        ShowOSD("MWBToggle: Added to startup.")
    }
}

; P2-02: About dialog
ShowAbout() {
    global g_version, g_hotkey
    MsgBox(
        "MWBToggle v" g_version "`n`n"
        "Toggle Mouse Without Borders clipboard and file sharing.`n`n"
        "Hotkey: " HotkeyToReadable(g_hotkey),
        "About MWBToggle"
    )
}

; P2-04: Pause sharing for a set number of minutes
PauseSharing(minutes) {
    global g_settingsPath
    if !FileExist(g_settingsPath)
        return
    json := FileRead(g_settingsPath, "UTF-8")
    ; Only toggle if currently ON
    if RegExMatch(json, '"ShareClipboard"\s*:\s*\{\s*"value"\s*:\s*true')
        DoToggle(false)
    SetTimer(ResumeSharing, -(minutes * 60000))
    ShowOSD("MWBToggle: Sharing paused for " minutes " minutes.")
}

; P2-04: Resume sharing after pause expires
ResumeSharing() {
    global g_settingsPath
    if !FileExist(g_settingsPath)
        return
    json := FileRead(g_settingsPath, "UTF-8")
    ; Only toggle if currently OFF
    if RegExMatch(json, '"ShareClipboard"\s*:\s*\{\s*"value"\s*:\s*false')
        DoToggle(false)
    ShowOSD("MWBToggle: Sharing resumed.")
}

; ╔══════════════════════════════════════════════════════════════════════════╗
; ║  Helpers                                                                 ║
; ╚══════════════════════════════════════════════════════════════════════════╝

; P4-04: OSD notification — ToolTip at cursor position instead of TrayTip toast.
;        Visible on whichever monitor the user is working on, no Windows toast spam.
;        duration: 3000 for info, 5000 for warnings.
ShowOSD(msg, duration := 3000) {
    ToolTip(msg)
    SetTimer(() => ToolTip(), -duration)
}

; P4-01: Read settings from MWBToggle.ini if it exists, otherwise keep defaults
LoadConfig() {
    global g_hotkey, g_confirmToggle, g_soundFeedback
    iniPath := A_ScriptDir "\MWBToggle.ini"
    if !FileExist(iniPath)
        return
    val := IniRead(iniPath, "Settings", "Hotkey", "")
    if (val != "")
        g_hotkey := val
    val := IniRead(iniPath, "Settings", "ConfirmToggle", "")
    if (val != "")
        g_confirmToggle := (val = "1" || val = "true")
    val := IniRead(iniPath, "Settings", "SoundFeedback", "")
    if (val != "")
        g_soundFeedback := (val = "1" || val = "true")
}

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

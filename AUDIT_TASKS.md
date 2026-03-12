# Production Readiness Audit — MWBToggle v1.4.1

**Audit date:** 2026-03-12
**Auditor:** Claude (automated)
**Scope:** Full codebase — code correctness, user-facing text, documentation & metadata

---

## Findings

| ID | Category | File | Description | Fix Applied | Version |
|----|----------|------|-------------|-------------|---------|
| F-001 | Documentation | README.md | Line reference for hotkey config said "line 20" but `g_hotkey` is on line 23 | Changed "line 20" to "line 23" | 1.4.1 |
| F-002 | Documentation | README.md | Compilation instructions referenced `off.ico` but the OFF-state icon was renamed to `mwb.ico` in v1.4.1 | Changed `off.ico` to `mwb.ico` | 1.4.1 |
| F-003 | Documentation | README.md | Files table listed `off.ico` instead of `mwb.ico` | Changed `off.ico` to `mwb.ico` in table | 1.4.1 |

---

## Production Readiness Checklist

### Code Correctness

- [x] **Logic errors** — No off-by-one, wrong operator, or unreachable code found
- [x] **Race conditions / thread safety** — AHK v2 is cooperatively single-threaded; timer callbacks cannot interrupt running functions (only Sleep/MsgBox yield). SyncTray is read-only, so timer overlap with DoToggle during Sleep(300) is harmless
- [x] **Error handling** — All critical paths guarded: file existence checked before read, file-open uses retry loop, JSON replacement verified before write, MWB process existence checked before toggle
- [x] **Scope declarations** — All globals properly declared at file scope with `global` keyword; functions that modify globals redeclare them. No leaked locals or shadowed variables
- [x] **Resource leaks** — FileOpen handle is opened and closed in the same function. One-shot timers (negative period) self-destruct. No orphaned handles, timers, or COM objects
- [x] **API contract compliance** — FileOpen flags ("w", "UTF-8-RAW"), RegExMatch/RegExReplace patterns, ComObject("WScript.Shell") shortcut creation all use correct signatures
- [x] **Edge cases** — Empty INI values handled (skip assignment, keep defaults). Missing settings.json handled (early return with message). Empty regex match handled (early return). PauseSharing/ResumeSharing check current state before toggling to avoid double-flip

### User-Facing Text

- [x] **Spelling and typos** — All tooltips, menu labels, MsgBox messages, and OSD notifications verified clean
- [x] **Grammar and consistency** — ON/OFF capitalization consistent throughout. Punctuation consistent (periods at end of OSD messages, no trailing periods on MsgBox text). Em-dash usage consistent
- [x] **Version numbers** — `g_version` matches CHANGELOG header (1.4.1)

### Documentation & Metadata

- [x] **README.md** — Accurate after fixes F-001, F-002, F-003. Installation steps, troubleshooting, and customization sections verified correct
- [x] **CHANGELOG.md** — All entries accurate, dates in descending order, version numbers sequential, descriptions match actual code changes
- [x] **LICENSE** — Standard MIT, copyright year (2026) current, holder name matches repository owner
- [x] **Script header comment** — Lines 1-13 accurately describe functionality, icon filenames (`on.ico`, `mwb.ico`), and behavior
- [x] **.gitignore** — Appropriate exclusions for AHK build artifacts, Windows system files, and editor temp files
- [x] **Comments** — No stale TODO/FIXME markers. All priority comments (P0-xx through P4-xx) reference features that exist in the code. No comments contradict the code they describe

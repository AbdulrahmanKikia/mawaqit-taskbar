# Spec Sheet: Patch 0.2

## Overview
**Goal:** Make the widget feel like a native Windows 11 taskbar control by **injecting a WinUI 3 island into `Shell_TrayWnd`** (Awqat Salaat WinUI host), while keeping Mawaqit prayer data and the v0.1.8 feature set.

**Target platform:** Windows 11, minimum build **22000** (21H2)  
**Owner:** AbdulrahmanKikia | **Status:** 0.2.0 (inject host)

## Scope
**In scope**
- Unpackaged WinUI 3 host: `CreateWindowEx(WS_EX_LAYERED, WS_POPUP)` + `DesktopWindowXamlSource`, then **`SetParent` into `Shell_TrayWnd`**
- Chip sized to ReBar height; X clock-adjacent (left of `TrayNotifyWnd`) or Widgets-adjacent when the bar is centered
- UI Automation + registry watchers for layout; `TaskbarCreated` recreate
- Mawaqit protocol (`/api/v1` prayer-times, calendar-iqama, `confData` scrape, mosque map)
- Feature parity with v0.1.8 (favorites, countdown target, notifications, autostart, updater, `config.toml`)

**Out of scope**
- Independent `WS_EX_TOPMOST` overlay (retired; that was the failed 0.2 prototype)
- Full-width AppBar strip (v0.1.x)
- Fork of Awqat’s Salah Hour / Al-Adhan / CSV / calendar backends
- Windows 10
- Code signing

## Host decision (M1 → pivot)

Microsoft has no public deskband API on Windows 11. The overlay that *followed* `Shell_TrayWnd` never hid cleanly with auto-hide and never looked like a taskbar control.

| Path | 0.2 |
|------|-----|
| `IDeskBand` | Unsupported on Windows 11 |
| Windows Widgets (`WidgetProvider`) | Widgets Board only |
| `SHAppBarMessage` (v0.1.x) | Separate strip above the bar |
| TOPMOST overlay on the unused gap | **Retired** — not a child of Explorer |
| **`SetParent` into `Shell_TrayWnd`** (Awqat WinUI) | **Chosen** — clipped/moved by Explorer |

Tradeoff (same as Awqat): not a real deskband; can overlap when the bar is packed; Explorer does not start it (we autostart).

## Screen inventory (v0.1.8 → 0.2)

| Screen | v0.1.8 | 0.2 |
|--------|--------|-----|
| AppBar strip | GDI `SHAppBarMessage` | **Removed** |
| Compact preview | n/a | WinUI chip in the taskbar (inject) |
| Schedule flyout | GDI popup | WinUI `Flyout` from the chip |
| Preferences | Win32 dialog | WinUI window, same fields |
| Mosque map | WebView2 | WinUI WebView2 (Leaflet) |
| Tray icon + menu | NotifyIcon | H.NotifyIcon (Show/Hide/Re-position/Quit) |
| Favorites | Tray popup, max 5 | Chip context menu + prefs, max 5 |
| Update check | GitHub Releases | Unchanged |

## Feature parity checklist (v0.1.8)

- [x] Mosque map pick (Use selected / Set as favorite)
- [x] Favorites (max 5)
- [x] Countdown target adhan / iqama
- [x] Notifications (per-prayer, adhan, iqama, minutes-before)
- [x] Autostart
- [x] GitHub updater
- [x] Hide / show chip (tray recovery)
- [x] Click-away flyout
- [x] AppData `config.toml` migrates without loss
- [x] Multi-monitor / DPI / taskbar auto-hide / Explorer restart
- [x] Light / dark / accent (WinUI theme dictionaries)
- [x] Compact chip mode

## Major deliverables

### 1. Taskbar integration
- [x] Inject host adapted from Awqat (`TaskBarWidget` / `TaskBarManager` / UIA watcher), class `MawaqitWidgetWinRT`
- [x] On inject failure: retry dialog, then tray-only
- [x] `TaskbarCreated` destroys and recreates the child

### 2. UI
- [x] Regular + compact chip
- [x] Flyout: today’s adhan + iqama, mosque label, Change mosque
- [x] Map picker (Use selected / Set as favorite)

### 3. Data
- [x] C# `MawaqitClient` mirroring the Rust protocol
- [x] Existing roaming `config.toml`

### 4. Ship
- [x] Inno + portable zip install the self-contained WinUI exe (WASDK 1.5 bundled)
- [x] Win11 22000+ only
- [x] README MIT attribution to Awqat Salaat

## Acceptance Criteria
- Chip is a **child** of `Shell_TrayWnd`, not a TOPMOST overlay.
- Feature parity checklist is complete.
- Testing: Windows 11 22H2+, 100% and 150% DPI, primary + secondary monitor, bottom taskbar (center and left align), auto-hide, Explorer restart.

## Risks
| Risk | Mitigation |
|---|---|
| Inject fails if the bar is busy | Retry + tray fallback |
| Packed taskbar overlap | Compact mode + drag-to-place |
| WASDK missing on a clean PC | Self-contained publish bundles 1.5 |
| Explorer restart kills the child | Recreate from `TaskbarCreated` |

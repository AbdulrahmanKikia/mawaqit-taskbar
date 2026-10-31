# Mawaqit Widget — user guide

Windows 11 prayer-times chip. Times come from [Mawaqit](https://mawaqit.net). Needs Windows 11 (build 22000+) and internet.

For install, uninstall, and SmartScreen / error 4551, see [README.md](README.md).

## First launch

1. Start **Mawaqit Widget** (it also starts with Windows unless you turn that off).
2. Pick your mosque on the map: pan/search → **Use selected mosque**.  
   **Set as favorite** keeps it in a list of up to 5.
3. Look for the chip on the **taskbar**, or a **strip** above the taskbar if you chose that placement.
4. If you see nothing, click the **tray icon** near the clock → **Show**.

## Chip (in the taskbar)

- **Left-click** — today’s adhan and iqama. A dot marks the prayer the countdown is aimed at. Click away or Esc to close.
- **Right-click** — change mosque, favorites, chip size, countdown, placement, preferences, hide, quit.

### Chip size

| Size | What you see |
|------|----------------|
| **Compact** | Countdown only |
| **Expanded** | Prayer, time, and countdown on one line (same idea as v0.1). Sits on the **left** of the taskbar. |

### Placement

| Placement | What it does |
|-----------|----------------|
| **In the taskbar** | Chip lives inside the taskbar (hides with auto-hide). |
| **Screen strip** | Full-width bar above the taskbar. Windows stay above it. Mosque name is on the **right**. |

Switch from right-click → **Placement**, the tray **Placement** menu, or Preferences.

## Countdown

Right-click → **Countdown**, or Preferences:

- **Adhan** — next call to prayer
- **Iqama** — next iqama
- **Adhan, then iqama** — adhan first, then that prayer’s iqama, then the next adhan

The chip label shows `iqama` when it is counting to iqama.

## Preferences

Right-click → **Preferences**, or the gear in the flyout.

- Mosque, countdown, placement, chip size
- Start with Windows
- Notifications: at adhan, at iqama, minutes-before reminder, per-prayer on/off
- Favorites (max 5) — remove one here if the list is full
- Check for updates

## Tray icon

Near the clock: **Show**, **Hide**, **Re-position**, **Manual position** (taskbar chip only), display, placement, **Quit**.

Hide keeps the app running; Show brings the chip or strip back.

## Tips

- After Explorer restarts, the chip should come back by itself.
- A packed taskbar can overlap the chip — use **Compact**, or **Manual position** from the tray.
- The mosque map needs [WebView2](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) (already on most Windows 11 PCs).
- Settings are saved in `%APPDATA%\Mawaqit\Mawaqit Widget\config.toml`.

# Mawaqit Widget — user guide

Windows 11 prayer-times chip. Times come from [Mawaqit](https://mawaqit.net). Needs Windows 11 (build 22000+). Internet is needed to pick a mosque; after that the whole year is saved, so times keep working offline.

For install, uninstall, and SmartScreen / error 4551, see [README.md](README.md).

## First launch

1. Start **Mawaqit Widget** (it also starts with Windows unless you turn that off).
2. Pick your mosque on the map: tap a name → **Use selected** (or **Set as favorite**, up to 5).
3. Look for the chip on the **taskbar**, or a **strip** above the taskbar if you chose that placement.
4. If you see nothing, click the **tray icon** near the clock → **Show**.

## Chip (in the taskbar)

- **Tap / left-click** — today’s adhan and iqama. A dot marks the prayer the countdown is aimed at. Tap away or Esc to close.
- **Long-press / right-click** — change mosque, favorites, Qibla direction, chip size, countdown, placement, manual position (taskbar chip), preferences, hide, quit.
- In the flyout, **⋯** opens the same menu (useful on touch).

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

Switch from the chip menu → **Placement**, the tray **Placement** menu, or Preferences.

### Moving the chip

With **In the taskbar**, open the chip menu → **Manual position**, then drag left/right on the taskbar (works with touch). Esc cancels. Tray **Manual position** does the same.

### Favorites

Save up to 5 mosques from the map. With 2 or more favorites, the flyout shows **‹ ›** arrows next to the mosque name for a quick swap. You can also pick a favorite from the chip menu.

## Prayer times: mosque or where you are

Preferences → **Prayer times** → *Times from*:

- **My mosque** — your mosque's own timetable (with its iqama). This is the default.
- **Where I am** — calculated for your location; turn on *Detect with Windows location*, or pick a spot on the map. Times change every day through the year.
- **Automatic** — your mosque while you are near it; once you are more than 50 km away (travelling), times for where you are. The flyout then says e.g. *350 km from Masjid Al-Hadi · Calculated · ISNA*.

**Calculation method** defaults to the usual one for your Windows region (ISNA in Canada/US, Umm al-Qura in Saudi Arabia, Karachi + Hanafi Asr in Pakistan, …); change it, the Asr school, or the high-latitude rule in the same section.

If Windows location is off for desktop apps, the section shows a link to turn it on (Settings → Privacy & security → Location → *Let desktop apps access your location*).

## Mosques that are not on Mawaqit

The map also shows mosques from OpenStreetMap (white dot with a blue outline, *Calculated*), so places Mawaqit does not cover, like many in Brampton, still appear. For those, adhan is calculated for the mosque's exact location; type the mosque's iqama times in Preferences (*13:30* or *+10* minutes after adhan). A link to the mosque's own website is there when OpenStreetMap knows it.

## Offline

The widget saves your mosque's full yearly timetable (adhan and iqama) the first time it loads it, and refreshes it every few hours while online. With no connection it keeps counting down from the saved times; the flyout then shows **Saved times from …** in amber so you know they may be a few days old. When the connection comes back it updates by itself. Restarting offline uses the same saved timetable — no connection required after the first successful load.

## Qibla direction

Chip menu → **Qibla direction**, or the **Qibla** button in the flyout. It works offline, using your location when known, otherwise your mosque's.

- The needle shows the Qibla on a north-up dial, with the angle from **true north** (e.g. *119° ESE* in London).
- **Using the sun:** in daylight it tells you how far to turn after facing the sun (*Face the sun, then turn 60° to the left*).
- **Shadow days:** twice a year the sun passes directly over the Kaaba (around 27–28 May and 15–16 July), and every shadow points straight away from the Qibla. Around 28 November and 13 January it is directly opposite, so shadows point towards it. The window shows the next one you can see from where you are, if any.
- A phone or hiking compass points to **magnetic** north, which can be several degrees off true north depending on where you live.
- On a tablet with a compass sensor the dial turns with the device.

## Countdown

Chip menu → **Countdown**, or Preferences:

- **Adhan** — next call to prayer
- **Iqama** — next iqama
- **Adhan, then iqama** — adhan first, then that prayer’s iqama, then the next adhan

The chip label shows `iqama` when it is counting to iqama.

## Preferences

Chip menu → **Preferences**, or the gear in the flyout.

- Language (Windows default, English, Français, العربية)
- Mosque, countdown, placement, chip size
- Prayer times source and calculation
- Start with Windows
- Notifications: at adhan, at iqama, minutes-before reminder, per-prayer on/off
- Favorites (max 5) — remove one here if the list is full
- Check for updates

## Tray icon

Near the clock: **Show**, **Hide**, **Re-position**, **Manual position** (taskbar chip only), display, placement, **Quit**.

Hide keeps the app running; Show brings the chip or strip back.

## Tips

- After Explorer restarts, the chip should come back by itself.
- A packed taskbar can overlap the chip — use **Compact**, or **Manual position**.
- If the settings file is ever damaged, the app starts with defaults and tells you where it kept the old file.
- The mosque map needs [WebView2](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) (already on most Windows 11 PCs).
- Settings are saved in `%APPDATA%\Mawaqit\Mawaqit Widget\config.toml`.

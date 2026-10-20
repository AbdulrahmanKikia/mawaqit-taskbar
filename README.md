# Mawaqit Widget

Windows 10/11 **taskbar prayer-times widget** inspired by Awqat Salaat.

It docks as a small AppBar strip above the taskbar, shows the next prayer + countdown, opens a schedule flyout on click, and lets you pick a real mosque from a map (Mawaqit data) via WebView2.

| Surface | What you get |
|---------|----------------|
| **AppBar strip** | Next prayer, countdown, mosque name |
| **Left-click** | Today’s adhan / iqama flyout |
| **Right-click / tray** | Change mosque, Refresh, Hide, Quit |
| **Mosque picker** | Interactive map of Mawaqit mosque names |

> **Primary binary:** `widget` (shell AppBar).  
> There is also a legacy egui UI (`mawaqit-widget`) kept for development; testers should use `widget`.

---

## Requirements

- **OS:** Windows 10 or Windows 11 (x64)
- **[WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)** — mosque map picker (usually preinstalled on Windows 11)
- **Network** — loads mosque list / prayer times from [mawaqit.net](https://mawaqit.net); map tiles from OpenStreetMap
- **To build from source:** [Rust](https://rustup.rs/) (stable), MSVC build tools (`Desktop development with C++` / `link.exe`)

Optional (richer API for times): Docker **or** Python 3 for a local [mrsofiane/mawaqit-api](https://github.com/mrsofiane/mawaqit-api). The widget can still resolve times from public mosque pages if the local API is down.

---

## Quick test (clone → run)

```powershell
git clone https://github.com/AbdulrahmanKikia/mawaqit-taskbar.git
cd mawaqit-taskbar

# One-time: Rust toolchain (https://rustup.rs/) if you don't have cargo
# One-time: WebView2 Runtime if the map window stays blank

# Build + launch the AppBar widget (~few minutes first time)
.\scripts\run-widget.ps1
```

Or install permanently (Start Menu + start with Windows):

```powershell
.\scripts\install.ps1
```

Uninstall:

```powershell
.\scripts\uninstall.ps1
```

### If `scripts\*.ps1` is blocked

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\run-widget.ps1
```

First `cargo` build downloads crates and compiles; later builds are much faster. Commit `Cargo.lock` with the repo so everyone gets the same dependency versions.

---

## What to try (smoke checklist)

1. A strip appears above the taskbar (left): **Mawaqit · Next … · countdown**.
2. **Left-click** the strip → schedule flyout with adhan / iqama.
3. **Right-click** the strip → **Change mosque…**
4. Map opens → pan / **My location** / filter → click a mosque name (or double-click to confirm) → **Use selected mosque**.
5. Strip updates to the new mosque; times refresh.
6. Tray icon (notification area) → **Refresh times** / **Hide bar** / **Quit**.
7. After **Hide bar**, left-click the tray icon to show the strip again.

---

## Build from source

```powershell
# AppBar widget only
cargo build --release --bin widget

# Or helper that also copies into dist\
.\scripts\build-release.ps1
```

| Output | Role |
|--------|------|
| `dist\widget.exe` | **Primary** — AppBar + tray + map picker |
| `dist\mawaqit-widget.exe` | Legacy egui window (optional) |
| `dist\icon.ico` | Icon asset |

Run without installing:

```powershell
.\dist\widget.exe
# or
.\scripts\run-widget.ps1
```

Dev (debug):

```powershell
cargo run --bin widget
```

Standalone map window (debug the picker only):

```powershell
cargo run --release --example open_map_picker
```

---

## Optional local API

Default config points at `http://127.0.0.1:8000`. Useful if you want the FastAPI backend; not required for basic map pick + page scrape.

**Docker:**

```powershell
docker compose up -d
# API → http://127.0.0.1:8000
```

**Python (venv, clones into `.mawaqit-api-src/`):**

```powershell
.\scripts\run-api.ps1
```

Docker env knobs for this repo’s compose file are documented in `.env.example`.

---

## Configuration paths

| Path | Purpose |
|------|---------|
| `%LOCALAPPDATA%\Mawaqit Widget\Mawaqit Widget.exe` | Installed app (`install.ps1`) |
| `%APPDATA%\Mawaqit\Mawaqit Widget\config.toml` | Mosque id, API base, autostart flag |
| `%LOCALAPPDATA%\Mawaqit\Mawaqit Widget\cache\cache.json` | Offline day cache (when used) |

Example `config.toml` after picking a mosque:

```toml
api_base_url = "http://127.0.0.1:8000"
start_with_windows = true

[mosque]
masjid_id = "al-andalous-mtl"
display_name = "Al Andalous Islamic Center — …"
```

You can also paste a full `https://mawaqit.net/…` URL (including mobile `/m/…` links) into flows that accept a slug/URL; `/m` is stripped automatically.

---

## Project layout

```
assets/           Icons, mosque map HTML/JS, vendored Leaflet
src/bin/widget.rs AppBar shell (primary UX)
src/map_picker.rs WebView2 + Leaflet mosque map
src/mawaqit.rs    Mawaqit HTTP client (country map feed, pages, optional API)
src/main.rs       Legacy egui entry (`mawaqit-widget`)
scripts/          install / uninstall / build / run helpers
examples/         open_map_picker — map only
docker-compose.yml  Optional local mawaqit-api
```

---

## Troubleshooting

| Symptom | What to check |
|---------|----------------|
| Map window blank / “Starting…” | Install [WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/); allow network for OSM tiles |
| No strip on taskbar | Another instance may be hidden — use the **tray** icon; or Quit from tray and relaunch |
| Build fails on `link.exe` | Install VS Build Tools with C++ workload |
| `cargo` not found | Install from https://rustup.rs/ and reopen the terminal |
| Prayer times empty after pick | Check network; try **Refresh times**; optional local API on `:8000` |
| Scripts won’t run | `Set-ExecutionPolicy -Scope Process Bypass` |

---

## Notes / limitations

- Uses a real shell **AppBar** (`SHAppBarMessage`), not a fragile SetParent overlay into Explorer.
- Official Mawaqit private mobile API is **not** used; mosque discovery uses the public country map feed; times prefer a local API then fall back to public mosque pages.
- Map UI embeds Leaflet (Apache-2.0) under `assets/leaflet/`; tiles come from OpenStreetMap — respect their [tile usage policy](https://operations.osmfoundation.org/policies/tiles/) for heavy use.
- Still evolving: packaging, notifications/adhan, multi-monitor polish, etc.

---

## License

MIT — see [LICENSE](LICENSE).

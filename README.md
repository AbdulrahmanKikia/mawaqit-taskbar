# Mawaqit Widget

Windows 10/11 **taskbar prayer-times widget** inspired by Awqat Salaat.

A small strip above the taskbar shows the next prayer and countdown. Click for today’s schedule; pick your mosque from a map.

| Surface | What you get |
|---------|----------------|
| **AppBar strip** | Next prayer, countdown, mosque name |
| **Left-click** | Today’s adhan / iqama flyout |
| **Right-click / tray** | Change mosque, Refresh, Hide, Quit |
| **Mosque picker** | Interactive map of Mawaqit mosque names |

---

## Install (no programming needed)

### Option A — Setup wizard (recommended)

1. Open **[Releases](https://github.com/AbdulrahmanKikia/mawaqit-taskbar/releases)**
2. Download **`MawaqitWidget-Setup-….exe`**
3. Double-click → **Next** → **Finish**
4. Look for the prayer strip above the taskbar (left side)
5. Right-click the strip → **Change mosque…** and choose your mosque

If Windows shows a SmartScreen warning: **More info** → **Run anyway**  
(the installer is not code-signed yet).

### Option B — Portable zip

1. From the same [Releases](https://github.com/AbdulrahmanKikia/mawaqit-taskbar/releases) page, download **`MawaqitWidget-Portable-….zip`**
2. Extract the folder
3. Double-click **`Install.bat`**

### Option C — One command in PowerShell

```powershell
irm https://raw.githubusercontent.com/AbdulrahmanKikia/mawaqit-taskbar/master/scripts/install-from-release.ps1 | iex
```

### Uninstall

- **Setup wizard:** Windows Settings → Apps → **Mawaqit Widget** → Uninstall  
  or Start Menu → Mawaqit Widget → Uninstall  
- **Portable:** run **`Uninstall.bat`** from the extracted zip (or from the install folder if you kept a copy)

### Requirements

- Windows 10 or Windows 11 (64-bit)
- Internet connection
- [WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) — usually already installed on Windows 11

---

## What to try

1. Strip appears above the taskbar (left): **Mawaqit · Next … · countdown**
2. **Left-click** → schedule flyout
3. **Right-click** → **Change mosque…**
4. Map → pick a mosque → **Use selected mosque**
5. Tray icon → Refresh / Hide / Quit

---

## For developers

### Clone and run

```powershell
git clone https://github.com/AbdulrahmanKikia/mawaqit-taskbar.git
cd mawaqit-taskbar
.\scripts\run-widget.ps1
```

Needs [Rust](https://rustup.rs/) and MSVC C++ build tools. Primary binary: `widget` (legacy egui UI: `mawaqit-widget`).

```powershell
cargo build --release --bin widget
.\scripts\install.ps1          # local install from a build
.\scripts\build-installer.ps1  # portable zip + Setup.exe (needs Inno Setup 6)
```

Tag a version to publish installers via GitHub Actions:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

### Optional local API

Default config may point at `http://127.0.0.1:8000`. Not required for map pick + page scrape.

```powershell
docker compose up -d
# or
.\scripts\run-api.ps1
```

### Config paths

| Path | Purpose |
|------|---------|
| `%LOCALAPPDATA%\Mawaqit Widget\` | Installed app |
| `%APPDATA%\Mawaqit\Mawaqit Widget\config.toml` | Mosque + settings |
| `%LOCALAPPDATA%\Mawaqit\Mawaqit Widget\cache\cache.json` | Day cache |

### Project layout

```
installer/        Inno Setup script + portable Install.bat
scripts/          build / package / install helpers
src/bin/widget.rs AppBar shell (primary)
assets/           Icons, mosque map, Leaflet
.github/workflows/release.yml   Builds Setup.exe + portable zip
```

### Troubleshooting

| Symptom | What to check |
|---------|----------------|
| Map blank / “Starting…” | Install WebView2; allow network |
| No strip | Use the **tray** icon; or Quit and relaunch |
| SmartScreen warning | More info → Run anyway |
| `cargo` / `link.exe` errors | Install Rust + VS C++ build tools |

### Notes

- Uses a real shell **AppBar** (`SHAppBarMessage`), not a SetParent overlay.
- Mosque discovery uses the public Mawaqit country map feed; times prefer a local API then fall back to public pages.
- Leaflet is vendored under `assets/leaflet/` (Apache-2.0).

## License

MIT — see [LICENSE](LICENSE).

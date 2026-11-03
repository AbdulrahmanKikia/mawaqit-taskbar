# Mawaqit Widget

Windows 11 **taskbar prayer-times widget**. The chip is a WinUI 3 island **injected into `Shell_TrayWnd`** (the same host pattern as [Awqat Salaat](https://github.com/Khiro95/Awqat-Salaat), MIT). Prayer times come from [Mawaqit](https://mawaqit.net) — this is not a fork of Awqat’s Salah Hour / Al-Adhan / CSV services.

| Surface | What you get |
|---------|----------------|
| **Taskbar chip** | Next prayer, countdown (compact: countdown only) |
| **Left-click** | Today’s adhan / iqama flyout |
| **Right-click / tray** | Change mosque, Favorites, Preferences, Hide, Re-position, Quit |
| **Mosque picker** | Map of Mawaqit mosques **plus OpenStreetMap mosques** (e.g. Brampton, which Mawaqit does not cover) |
| **Prayer times for where you are** | Offline calculation (22 methods, Hanafi/standard Asr, high-latitude rules), following Windows location; *Automatic* switches to it when you are 50+ km from your mosque |
| **Offline** | The mosque's whole year (adhan + iqama) is saved; times keep working without internet |
| **Qibla direction** | Offline compass, sun guide, and the yearly "shadow days" |

Requires **Windows 11** (build 22000+).

**Site:** [abdulrahmankikia.github.io/mawaqit-taskbar](https://abdulrahmankikia.github.io/mawaqit-taskbar/) (download Setup.exe from there)  
**How to use it:** [USER_GUIDE.md](USER_GUIDE.md) (chip vs strip, compact vs expanded, countdown, tray).

---

## Install (no programming needed)

### Option A — Setup wizard (recommended)

1. Open the **[product site](https://abdulrahmankikia.github.io/mawaqit-taskbar/)** and click **Download Setup.exe**  
   (or [Releases](https://github.com/AbdulrahmanKikia/mawaqit-taskbar/releases) for the same file)
2. Save **`MawaqitWidget-Setup-….exe`**
3. Double-click → **Next** → **Finish**
4. Look for the prayer chip on the taskbar (next to the clock / Widgets)
5. Right-click the chip → **Change mosque…** and choose your mosque

If Windows shows a SmartScreen warning: **More info** → **Run anyway**  
(the installer is not code-signed yet).

Before installing on Windows 11, right-click the downloaded Setup → **Properties** → check **Unblock** → Apply (avoids some blocks).

If the chip cannot sit on the taskbar (the bar is full, or inject failed), use the **tray icon** near the clock.

#### “Application Control policy has blocked this file” (error 4551)

Install usually still succeeded; Windows blocked *starting* the unsigned `.exe`.

1. Start menu → open **Mawaqit Widget** (or unblock the Setup file and reinstall).
2. **Settings → Privacy & security → Windows Security → App & browser control → Smart App Control** → turn **Off** (or Evaluation), then launch again.
3. On a **work/school** PC, only IT can allow it under company Application Control / WDAC.

Long-term fix is a paid code-signing certificate (not included yet).

### Option B — Portable zip

1. From the same [Releases](https://github.com/AbdulrahmanKikia/mawaqit-taskbar/releases) page, download **`MawaqitWidget-Portable-….zip`**
2. Extract the folder
3. Double-click **`Install.bat`**

### Option C — One command in PowerShell

```powershell
irm https://raw.githubusercontent.com/AbdulrahmanKikia/mawaqit-taskbar/master/scripts/install-from-release.ps1 | iex
```

The script downloads the release asset, verifies its SHA-256 against the published checksum, and only then launches Setup (or Install.bat).

### Uninstall

- **Setup wizard:** Windows Settings → Apps → **Mawaqit Widget** → Uninstall  
  or Start Menu → Mawaqit Widget → Uninstall  
- **Portable:** run **`Uninstall.bat`** from the extracted zip (or from the install folder if you kept a copy)

### Requirements

- Windows 11 (64-bit), build 22000 or later
- Internet for the first run (to pick a mosque); prayer times and Qibla then work offline
- [WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) — usually already installed on Windows 11
- **Windows App SDK 1.5 is bundled** in the installer and portable zip (self-contained unpackaged WinUI 3). You do not install WASDK separately.

---

## What to try

See [USER_GUIDE.md](USER_GUIDE.md) for the full walkthrough.

1. Chip appears on the taskbar (expanded sits on the left; compact auto-places)
2. **Left-click** → schedule flyout (Esc or click away to close)
3. **Right-click** → **Change mosque…**
4. Map → pick a mosque → **Use selected mosque** (or **Set as favorite**, max 5)
5. Tray **Placement** → **Screen strip** if you want the v0.1 bar above the taskbar
6. Tray icon → Show / Hide / Re-position / Quit

Injected chips hide with taskbar auto-hide and come back after Explorer restarts. The strip reserves desktop space so windows do not cover it.

---

## For developers

### Clone and run

```powershell
git clone https://github.com/AbdulrahmanKikia/mawaqit-taskbar.git
cd mawaqit-taskbar
.\scripts\run-widget.ps1
```

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). Primary binary: unpackaged WinUI 3 host in `host-winui/` (`Mawaqit Widget.exe`).

```powershell
dotnet publish host-winui\Mawaqit.WinUI.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64
.\scripts\install.ps1          # local install from a publish
.\scripts\build-installer.ps1  # portable zip + Setup.exe (needs Inno Setup 6)
```

The Rust `widget` crate remains as the Mawaqit HTTP protocol reference; it is **not** the 0.2 UI.

### Tests and house rules

```powershell
dotnet test tests\Mawaqit.Tests\Mawaqit.Tests.csproj
```

`tests/Mawaqit.Tests/Architecture/RulesTests.cs` enforces the house rules on every CI run, and each failure says how to fix it:

- `core/` has no UI or Windows API references, so it stays testable.
- HTTP clients come only from `Http.Create` (identified User-Agent, timeout).
- Every message from the map page is parsed by `MapIpc.Parse`; the host calls the page only through `CallJs` (JSON-encoded arguments).
- Shipped JavaScript has no `eval`, no `innerHTML` built from data, no HTML-string tooltips.
- Only the updater starts a process, and only after the installer's SHA-256 matches the release.
- App files are written with `AtomicFile` (no half-written config); no empty `catch` in data code; no unsafe deserializers.
- Secret scan over every tracked and new file (no `.env` except `.env.example`, no signing keys, no provider tokens).
- Version is identical in the csproj, `AppState`, and the installer; the toast app id matches the installer shortcut; every string exists in English, French and Arabic.

Fix the code rather than adding allowlist entries. `release.yml` runs the same tests before publishing and refuses a tag that does not match the app version.

Tag a version to publish installers via GitHub Actions (bump `<Version>` in the csproj, `AppState.Version`, and `MyAppVersion` in the installer first):

```powershell
git tag v0.2.0
git push origin v0.2.0
```

### Where times come from

Preferences → **Prayer times** → *Times from*:

| Choice | Times |
|--------|-------|
| **My mosque** (default) | The mosque's Mawaqit timetable. Mosques that are only on OpenStreetMap get adhan calculated for their location, plus the iqama you type in. |
| **Where I am** | Calculated offline for your location (Windows location services, or a pin on the map). |
| **Automatic** | Your mosque when you are within 50 km of it, otherwise where you are. |

Calculation is the PrayTimes algorithm (`core/Calculation/PrayerCalculator.cs`). It matches api.aladhan.com within a minute for Fajr, sunrise, Dhuhr, Maghrib and Isha (15 cities × 5 dates in `tests/Mawaqit.Tests/Fixtures/aladhan-reference.json`). Asr is checked against the exact shadow-length definition instead, because Aladhan's Asr drifts by up to 5 minutes at high latitudes. Our ISNA results match Masjid Al-Hadi's (Toronto) own calendar exactly on all 365 days for Fajr, Shuruq, Dhuhr, Maghrib and Isha (`dotnet run --project tools/Mawaqit.Probe -- --compare masjid-al-hadi-toronto Isna`).

For Mawaqit mosques the widget reads the mosque's public page on mawaqit.net once and keeps its yearly `calendar` / `iqamaCalendar`. Before trusting the year, it checks it against the times Mawaqit publishes for "today"; rows that fail validation (e.g. Fajr after sunrise) are skipped and logged, never guessed. Each published sunrise is also compared with the astronomical one, which flags typos (Birmingham's calendar lists 05:00 for 05:50 on 24 April) and misconfigured mosque time zones in the log. See `core/Timetable/ConfData.cs` and the real-data fixtures in `tests/Mawaqit.Tests/Fixtures/`.

The WinUI host no longer calls the optional local API (`docker compose up -d`, `scripts/run-api.ps1`); those remain for the legacy Rust build only. `api_base_url` stays in `config.toml` for compatibility.

Spot-check real mosques with the same parser the app ships:

```powershell
dotnet run --project tools/Mawaqit.Probe -- assahaba-mtl quba-islamic-cultural-centre-birmingham-b7-4ny-united-kingdom
```

### Config paths

| Path | Purpose |
|------|---------|
| `%LOCALAPPDATA%\Mawaqit Widget\` | Installed app (self-contained WinUI + WASDK) |
| `%APPDATA%\Mawaqit\Mawaqit Widget\config.toml` | Mosque + settings (same schema as v0.1.8) |
| `%LOCALAPPDATA%\Mawaqit\Mawaqit Widget\cache\<mosque>.json` | Saved yearly timetable (offline) |
| `%LOCALAPPDATA%\Mawaqit\Mawaqit Widget\webview2\` | Map browser profile |
| `%LOCALAPPDATA%\Mawaqit\Mawaqit Widget\logs\` | Serilog files |

`config.toml` keeps the v0.1.8 schema (favorites, countdown, notifications) and adds optional `lat` / `lon` per mosque for the Qibla. A file that cannot be read is moved aside as `config.toml.broken-<time>` and the app starts with defaults.

### Project layout

```
host-winui/     Unpackaged WinUI 3 host (0.2 UI) — SetParent into Shell_TrayWnd
core/           Pure, tested logic: Mawaqit parsing, schedule, alerts, config, cache, Qibla
tests/          Unit tests + Architecture/RulesTests.cs (house rules, run in CI)
tools/          Mawaqit.Probe — check real mosque pages with the shipped parser
installer/      Inno Setup script + portable Install.bat
scripts/        publish / package / install helpers
src/            Rust protocol reference (not shipped as the 0.2 UI)
assets/         Icons, mosque map, Leaflet
.github/workflows/ci.yml        Tests + app build on every push / PR
.github/workflows/release.yml   Tests, then builds Setup.exe + portable zip + SHA-256 files
```

### Troubleshooting

| Symptom | What to check |
|---------|----------------|
| Map error / WebView2 | Install [WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/). |
| No chip on the taskbar | Use the **tray** icon; retry Show. Packed bars can overlap — compact mode or drag-to-place. |
| Chip gone after Explorer restart | Tray listens for `TaskbarCreated` and reinjects. |
| SmartScreen warning | More info → Run anyway |
| Error **4551** / Application Control blocked | Unblock the download; disable Smart App Control; or ask IT (unsigned app) |
| `dotnet` restore errors | Reach nuget.org; this build uses Windows App SDK 1.5 |

### Notes

- 0.2 injects a transparent WinUI island into `Shell_TrayWnd` (adapted from Awqat Salaat). It is not a deskband and not a `WS_EX_TOPMOST` overlay. Explorer does not start it — autostart is ours.
- Mosque discovery uses the public Mawaqit country map feed; times prefer a local API then fall back to public pages.
- Leaflet is vendored under `host-winui/Assets/map/` (Apache-2.0).

## License

MIT — see [LICENSE](LICENSE).  
Taskbar inject host adapted from [Awqat Salaat](https://github.com/Khiro95/Awqat-Salaat) (Copyright (c) 2023 Khiro, MIT). See [THIRD_PARTY.md](THIRD_PARTY.md).

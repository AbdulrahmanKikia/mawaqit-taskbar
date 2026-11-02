# Contributing / testing from source

## Prerequisites

1. Windows 11 x64 (build 22000+)
2. [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (the .NET 9 SDK also builds it)
3. [WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) for the mosque map

The shipped app is the WinUI 3 host in `host-winui/`. Pure logic lives in `core/` and is unit-tested in `tests/`. The Rust crate in `src/` is the legacy v0.1 build and is not shipped.

## Day-to-day

```powershell
.\scripts\run-widget.ps1                                   # build + run the widget
dotnet test tests\Mawaqit.Tests\Mawaqit.Tests.csproj        # unit tests + house rules
dotnet run --project tools\Mawaqit.Probe -- <mosque-slug>  # check a real mosque page
dotnet run --project tools\Mawaqit.Probe -- --compare <mosque-slug> Isna  # calculated vs published, whole year
.\scripts\build-installer.ps1                               # portable zip + Setup.exe
```

## House rules

They are enforced by `tests/Mawaqit.Tests/Architecture/RulesTests.cs`, in CI and before every release. When a rule fails, its message says what to do. Fix the code; do not grow the allowlists.

- Put new logic in `core/` with tests. `core/` must not reference WinUI or Windows APIs.
- Anything from outside (mosque pages, the map page, config files, release metadata) is validated by a parser in `core/` before use. Rows that do not validate are skipped and reported, not guessed.
- Network: `Http.Create(...)` only. Map ⇄ host: `MapIpc.Parse` in, `CallJs` out.
- Files: `AtomicFile.WriteAllText` only. Processes: only the updater, after the SHA-256 check.
- No secrets in the repo. Secrets live in `.env` (gitignored) or CI secrets; rotate anything that leaked.
- New UI text gets English, French and Arabic entries in `LocaleManager.cs`.
- Releasing: bump `<Version>` (csproj), `AppState.Version` and `MyAppVersion` (installer) together, then tag `vX.Y.Z`.

## Before opening a PR

- `dotnet test tests\Mawaqit.Tests\Mawaqit.Tests.csproj` passes
- `dotnet build host-winui\Mawaqit.WinUI.csproj -c Release -p:Platform=x64 -r win-x64` succeeds
- Smoke test: the chip appears (or the tray fallback), the flyout opens, the map picks a mosque, times refresh, the Qibla window opens; then turn Wi-Fi off and restart: times still show
- Do not commit `dist/`, `.env`, or `.mawaqit-api-src/`

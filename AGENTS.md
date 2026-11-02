# Notes for AI coding agents

Read [CONTRIBUTING.md](CONTRIBUTING.md) first. In short:

- **Shipped app:** `host-winui/` (WinUI 3, .NET 8). **Logic:** `core/` (pure, no UI/Windows APIs). **Tests:** `tests/Mawaqit.Tests`. `src/` (Rust) is legacy; do not extend it.
- **Run before saying you are done:**
  - `dotnet test tests/Mawaqit.Tests/Mawaqit.Tests.csproj`
  - `dotnet build host-winui/Mawaqit.WinUI.csproj -c Release -p:Platform=x64 -r win-x64`
- **House rules** are enforced by `tests/Mawaqit.Tests/Architecture/RulesTests.cs`. If one fails, change the code the way its message says. Do **not** add allowlist entries or weaken the regexes to get green.
- **Mawaqit data is messy.** Parse it in `core/Timetable/ConfData.cs`, validate it, and report skipped rows. Never use Mawaqit's `times` field as "today": it follows Mawaqit's server clock. Check new parsing against a real page with `tools/Mawaqit.Probe` and add a trimmed fixture.
- **Boundaries:**
  - HTTP only via `Http.Create`.
  - Map messages only via `MapIpc.Parse`; script calls only via `CallJs`.
  - File writes only via `AtomicFile`.
  - Process start only in `Updater`, after the SHA-256 check.
- **Prayer-time calculation** lives in `core/Calculation/`. Any change must keep `PrayerCalculatorTests` green (Aladhan reference + exact Asr definition). Do not add per-website scrapers for mosque times: they break silently and show wrong times.
- **UI text:** add English, French and Arabic strings in `LocaleManager.cs` (a test checks this).
- **Git:** do not commit, push, or tag; the maintainer does that.

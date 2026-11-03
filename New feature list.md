# New features — 0.5

- [x] Offline prayer times: yearly timetable is saved per mosque (restart works offline after the first successful load)
- [x] Touch-friendly navigation: overflow menu (and long-press) for actions that need right-click; map uses tap + Use/Favorite; flyout scrolls if needed
- [x] Move chip from the taskbar UI: Manual position in the chip menu (injected host), with touch-safe dragging
- [x] Favorite swap arrows: prev/next on the flyout when 2+ favorites are saved

# 0.6 — Trust & cleanup

Already on master (0.5.1+): install-script SHA-256, updater URL allowlist + re-hash before launch, Map WebView document-nav + IPC source host checks.

Next security / trust (ordered by value vs break risk):

- [x] Temp installer cleanup after successful `Process.Start` in Updater
- [x] WebView settings pass (disable host objects / unused UI; keep OSM tiles)
- [x] Offline-cache integrity UX: refresh `FetchedAt` / iqama on merge-into-cache
- [x] Retire or quarantine legacy Rust updater/map IPC (`src/`) — documented in `src/README.md` (full archive later)
- [ ] Authenticode signing (paid cert + CI secrets) — real SmartScreen fix
- [ ] MOTW clearing: leave until signing exists

Correctness (not security; separate pass):

- [x] Expanded chip respects manual position
- [x] Favorite ‹ › when current mosque is not in Favorites

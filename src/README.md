# Legacy Rust reference (`src/`)

This tree is the **v0.1 protocol reference** (Mawaqit HTTP, old egui/tray host). It is **not** the shipped product.

- **Shipped app:** `host-winui/` (WinUI 3).
- **Do not** extend this crate for product features.
- **Do not** run or distribute the Rust updater or localhost map IPC for production — those paths lack the checksum and Map IPC hardening that WinUI has.
- Full archive/removal of `src/` is a later product decision; until then treat builds here as developer archaeology only.

See `AGENTS.md` and `CONTRIBUTING.md`.

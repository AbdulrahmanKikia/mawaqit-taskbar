# Contributing / testing from source

## Prerequisites

1. Windows 10/11 x64  
2. [Rust](https://rustup.rs/) stable (`rustc`, `cargo`)  
3. Visual Studio Build Tools with the **Desktop development with C++** workload (`link.exe`)  
4. [WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) for the mosque map  

## Day-to-day

```powershell
# AppBar widget (primary)
cargo run --bin widget

# Map picker only
cargo run --release --example open_map_picker

# Release artifacts → dist\
.\scripts\build-release.ps1
```

Prefer `widget` over `mawaqit-widget` unless you are working on the legacy egui UI.

## Before opening a PR

- `cargo build --release --bin widget`
- Smoke-test: strip appears, flyout opens, map picker selects a mosque, times refresh
- Do not commit `dist/`, `.env`, or `.mawaqit-api-src/`

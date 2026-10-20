#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod api_launch;
mod autostart;
mod config;
mod error;
mod mawaqit;
mod schedule;
mod single_instance;
mod taskbar;
mod theme;
mod ui;

pub use error::Error;

use config::APP_DISPLAY_NAME;
use eframe::egui;

fn main() -> eframe::Result<()> {
    if !single_instance::ensure_single_instance() {
        // Another instance is already running (and was activated).
        return Ok(());
    }

    let cfg = config::load_config().unwrap_or_default();
    let _ = autostart::set_enabled(cfg.start_with_windows);

    // Widget-first: always boot as an opaque taskbar strip. Setup is a flyout panel.
    let options = eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default()
            .with_decorations(false)
            .with_transparent(false)
            .with_always_on_top()
            .with_taskbar(false)
            .with_inner_size([260.0, 48.0])
            .with_min_inner_size([200.0, 40.0])
            .with_resizable(false)
            .with_title(APP_DISPLAY_NAME)
            .with_visible(true),
        ..Default::default()
    };

    eframe::run_native(
        APP_DISPLAY_NAME,
        options,
        Box::new(|cc| Ok(Box::new(ui::App::new(cc)))),
    )
}

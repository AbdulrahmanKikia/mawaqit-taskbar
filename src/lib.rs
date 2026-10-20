//! Shared Mawaqit data layer used by the egui app and the shell AppBar widget.

pub mod autostart;
pub mod config;
pub mod error;
pub mod map_picker;
pub mod mawaqit;
pub mod notify;
pub mod schedule;
pub mod update;

pub use error::Error;

//! Dev helper: open the WebView2 mosque map picker alone.
#![windows_subsystem = "windows"]

fn main() {
    if let Err(e) = mawaqit_widget::map_picker::run(|hit| match hit {
        Some(h) => println!("selected: {} ({})", h.label, h.slug),
        None => println!("cancelled"),
    }) {
        eprintln!("{e}");
    }
}

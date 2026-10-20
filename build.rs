fn main() {
    if std::env::var("CARGO_CFG_TARGET_OS").as_deref() == Ok("windows") {
        let mut res = winresource::WindowsResource::new();
        res.set("ProductName", "Mawaqit Widget");
        res.set("FileDescription", "Mawaqit Widget");
        res.set("CompanyName", "Mawaqit Widget");
        res.set("LegalCopyright", "MIT");
        let icon = std::path::Path::new("assets/icon.ico");
        if icon.exists() {
            res.set_icon("assets/icon.ico");
        }
        // Ignore compile errors if the toolchain lacks RC; binary still builds.
        let _ = res.compile();
    }
}

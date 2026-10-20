mod setup;
mod widget;

pub use setup::{ApiStatus, SetupPanel};
pub use widget::WidgetPanel;

use eframe::egui::{self, CornerRadius, Frame, Margin, Vec2};
use std::sync::{Arc, Mutex};
use std::thread;
use std::time::{Duration, Instant};

use crate::api_launch::{open_url, start_local_api};
use crate::config::{self, save_cache, save_config, AppConfig, CacheData, MosqueSelection};
use crate::mawaqit::{
    normalize_mosque_input, IqamaTimes, MawaqitClient, MosqueHit, PrayerTimes,
};
use crate::schedule::{
    build_day_schedule, format_countdown, next_prayer_with_tomorrow, now_local, today_local,
    DaySchedule, NextPrayer, PrayerKind,
};
use crate::theme::ShellTheme;
use chrono::Datelike;

const COLLAPSED_WIDTH: f32 = 260.0;
const PRAYERS_SIZE: Vec2 = Vec2::new(340.0, 380.0);
const SETUP_SIZE: Vec2 = Vec2::new(420.0, 520.0);
const REFRESH_INTERVAL: Duration = Duration::from_secs(6 * 60 * 60);
const DOCK_INTERVAL: Duration = Duration::from_secs(2);

/// What the widget shell is showing. Chrome never changes — only size/content.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Panel {
    Strip,
    Prayers,
    Setup,
}

#[derive(Debug, Clone)]
enum TrayCmd {
    ToggleVisible,
    ChangeMosque,
    ToggleAutostart,
    Quit,
}

#[derive(Default)]
struct FetchState {
    in_flight: bool,
    last_error: Option<String>,
    result: Option<(PrayerTimes, IqamaTimes)>,
    search_results: Option<Result<Vec<MosqueHit>, String>>,
    api_check: Option<Result<String, String>>,
    preview: Option<Result<(String, String, PrayerTimes), String>>,
}

pub struct App {
    config: AppConfig,
    client: MawaqitClient,
    panel: Panel,
    visible: bool,
    schedule: Option<DaySchedule>,
    next: Option<NextPrayer>,
    last_refresh: Option<Instant>,
    last_dock: Option<Instant>,
    last_dock_key: Option<(i32, i32, i32, i32)>,
    fetch: Arc<Mutex<FetchState>>,
    search_query: String,
    slug_input: String,
    search_hits: Vec<MosqueHit>,
    status: String,
    api_status: ApiStatus,
    preview_times: Option<PrayerTimes>,
    preview_slug: Option<String>,
    preview_name: Option<String>,
    tray_rx: Option<std::sync::mpsc::Receiver<TrayCmd>>,
    _tray: Option<tray_icon::TrayIcon>,
    shell_theme: ShellTheme,
    last_theme_poll: Option<Instant>,
    needs_dock: bool,
}

impl App {
    pub fn new(cc: &eframe::CreationContext<'_>) -> Self {
        let shell_theme = ShellTheme::current();
        configure_style(&cc.egui_ctx, shell_theme);

        let config = config::load_config().unwrap_or_default();
        let client =
            MawaqitClient::new(config.api_base_url.clone()).expect("http client");

        let needs_setup = config.mosque.is_none();
        let mut app = Self {
            config,
            client,
            panel: if needs_setup {
                Panel::Setup
            } else {
                Panel::Strip
            },
            visible: true,
            schedule: None,
            next: None,
            last_refresh: None,
            last_dock: None,
            last_dock_key: None,
            fetch: Arc::new(Mutex::new(FetchState::default())),
            search_query: String::new(),
            slug_input: String::new(),
            search_hits: Vec::new(),
            status: if needs_setup {
                "Paste your mosque URL, then Find mosque.".into()
            } else {
                String::new()
            },
            api_status: ApiStatus::Unknown,
            preview_times: None,
            preview_slug: None,
            preview_name: None,
            tray_rx: None,
            _tray: None,
            shell_theme,
            last_theme_poll: None,
            needs_dock: true,
        };

        if let Ok(Some(cache)) = config::load_cache() {
            app.apply_times(cache.prayer_times, cache.iqama);
            if !needs_setup {
                app.status = format!("Cached for {}", cache.day);
            }
        }

        if let Some((tray, rx)) = build_tray() {
            app._tray = Some(tray);
            app.tray_rx = Some(rx);
        }

        if needs_setup {
            app.request_api_check();
        } else {
            app.request_refresh();
        }

        app
    }

    fn apply_times(&mut self, times: PrayerTimes, iqama: IqamaTimes) {
        let date = today_local();
        let schedule = build_day_schedule(date, &times, &iqama);
        let fajr = schedule
            .slots
            .iter()
            .find(|s| s.kind == PrayerKind::Fajr)
            .map(|s| s.adhan);
        self.next = next_prayer_with_tomorrow(&schedule, fajr, now_local());
        self.schedule = Some(schedule);
    }

    fn request_api_check(&mut self) {
        {
            let mut st = self.fetch.lock().unwrap();
            if st.in_flight {
                return;
            }
            st.in_flight = true;
            st.api_check = None;
        }
        self.api_status = ApiStatus::Checking;
        let api = self.config.api_base_url.clone();
        let fetch = Arc::clone(&self.fetch);
        thread::spawn(move || {
            let result = MawaqitClient::new(api)
                .and_then(|c| c.health_check())
                .map_err(|e| e.to_string());
            let mut st = fetch.lock().unwrap();
            st.in_flight = false;
            st.api_check = Some(result);
        });
    }

    fn request_preview(&mut self) {
        let input = self.slug_input.trim().to_string();
        if input.is_empty() {
            self.status =
                "Paste your mosque page URL from mawaqit.net, then Find mosque.".into();
            return;
        }
        {
            let mut st = self.fetch.lock().unwrap();
            if st.in_flight {
                return;
            }
            st.in_flight = true;
            st.preview = None;
        }
        let api = self.config.api_base_url.clone();
        let fetch = Arc::clone(&self.fetch);
        thread::spawn(move || {
            let result = (|| {
                let client = MawaqitClient::new(api)?;
                let resolved = client.resolve_mosque(&input)?;
                Ok::<_, crate::Error>((resolved.slug, resolved.name, resolved.times))
            })()
            .map_err(|e| e.to_string());
            let mut st = fetch.lock().unwrap();
            st.in_flight = false;
            st.preview = Some(result);
        });
    }

    fn request_refresh(&mut self) {
        let Some(mosque) = self.config.mosque.clone() else {
            return;
        };
        {
            let mut st = self.fetch.lock().unwrap();
            if st.in_flight {
                return;
            }
            st.in_flight = true;
            st.last_error = None;
        }

        let api = self.config.api_base_url.clone();
        let fetch = Arc::clone(&self.fetch);
        let masjid_id = mosque.masjid_id;

        thread::spawn(move || {
            let result = (|| {
                let client = MawaqitClient::new(api)?;
                let times = client.prayer_times(&masjid_id)?;
                let now = today_local();
                let iqama = client
                    .iqama_for_day(&masjid_id, now.month(), now.day())
                    .unwrap_or_default();
                Ok::<_, crate::Error>((times, iqama))
            })();

            let mut st = fetch.lock().unwrap();
            st.in_flight = false;
            match result {
                Ok(pair) => {
                    st.result = Some(pair);
                    st.last_error = None;
                }
                Err(e) => st.last_error = Some(e.to_string()),
            }
        });
    }

    fn request_search(&mut self) {
        let q = self.search_query.trim().to_string();
        if q.is_empty() {
            return;
        }
        {
            let mut st = self.fetch.lock().unwrap();
            if st.in_flight {
                return;
            }
            st.in_flight = true;
            st.search_results = None;
        }
        let fetch = Arc::clone(&self.fetch);
        let api = self.config.api_base_url.clone();
        thread::spawn(move || {
            let result = MawaqitClient::new(api)
                .and_then(|c| c.search_mosques(&q))
                .map_err(|e| e.to_string());
            let mut st = fetch.lock().unwrap();
            st.in_flight = false;
            st.search_results = Some(result);
        });
    }

    fn poll_fetch(&mut self) {
        let taken = {
            let mut st = self.fetch.lock().unwrap();
            (
                st.result.take(),
                st.last_error.take(),
                st.search_results.take(),
                st.api_check.take(),
                st.preview.take(),
            )
        };

        if let Some((times, iqama)) = taken.0 {
            self.apply_times(times.clone(), iqama.clone());
            self.last_refresh = Some(Instant::now());
            let day = today_local().to_string();
            let _ = save_cache(&CacheData {
                fetched_at: chrono::Utc::now().to_rfc3339(),
                day,
                prayer_times: times,
                iqama,
            });
            self.status = "Updated".into();
        }
        if let Some(err) = taken.1 {
            self.status = err;
        }
        if let Some(search) = taken.2 {
            match search {
                Ok(hits) => {
                    self.status = format!("{} mosque(s)", hits.len());
                    self.search_hits = hits;
                }
                Err(e) => self.status = e,
            }
        }
        if let Some(check) = taken.3 {
            match check {
                Ok(_) => {
                    self.api_status = ApiStatus::Online;
                    self.status = "API is online.".into();
                }
                Err(e) => {
                    self.api_status = ApiStatus::Offline;
                    self.status = format!("API offline: {e}");
                }
            }
        }
        if let Some(preview) = taken.4 {
            match preview {
                Ok((slug, name, times)) => {
                    self.slug_input = slug.clone();
                    self.preview_slug = Some(slug);
                    self.preview_name = Some(name.clone());
                    self.preview_times = Some(times);
                    self.status = format!("Found: {name} — Save & show on taskbar.");
                }
                Err(e) => {
                    self.preview_times = None;
                    self.preview_slug = None;
                    self.preview_name = None;
                    self.status = e;
                }
            }
        }
    }

    fn select_mosque(&mut self, hit: MosqueHit) {
        self.slug_input = hit.slug.clone();
        self.preview_slug = Some(hit.slug);
        self.preview_name = Some(hit.label);
        self.preview_times = None;
        self.request_preview();
    }

    fn confirm_mosque(&mut self) {
        let Some(slug) = self
            .preview_slug
            .clone()
            .or_else(|| normalize_mosque_input(&self.slug_input))
        else {
            self.status = "Find a mosque first.".into();
            return;
        };
        let name = self
            .preview_name
            .clone()
            .or_else(|| self.preview_slug.clone())
            .unwrap_or_else(|| slug.clone());
        self.config.mosque = Some(MosqueSelection {
            masjid_id: slug,
            display_name: name,
        });
        let _ = save_config(&self.config);
        self.panel = Panel::Strip;
        self.needs_dock = true;
        self.request_refresh();
    }

    fn open_setup(&mut self) {
        self.panel = Panel::Setup;
        self.needs_dock = true;
        self.visible = true;
        crate::taskbar::show_widget(true);
        if let Some(m) = &self.config.mosque {
            self.slug_input = m.masjid_id.clone();
        }
        self.request_api_check();
    }

    fn poll_theme(&mut self) {
        let due = self
            .last_theme_poll
            .map(|t| t.elapsed() >= Duration::from_secs(5))
            .unwrap_or(true);
        if !due {
            return;
        }
        self.last_theme_poll = Some(Instant::now());
        let next = ShellTheme::current();
        if next != self.shell_theme {
            self.shell_theme = next;
        }
    }

    fn dock_if_needed(&mut self, ctx: &egui::Context) {
        let due = self
            .last_dock
            .map(|t| t.elapsed() >= DOCK_INTERVAL)
            .unwrap_or(true);
        if !self.needs_dock && !due {
            return;
        }

        let ppp = ctx.pixels_per_point().max(0.5);
        let tb = crate::taskbar::primary_taskbar(ppp).unwrap_or(crate::taskbar::TaskbarGeom {
            x: 0.0,
            y: 1040.0,
            width: 1920.0,
            height: 48.0,
            left_inset: 200.0,
        });

        let strip_h = tb.height.clamp(40.0, 56.0);
        let x = tb.x + tb.left_inset;
        let (size, y) = match self.panel {
            Panel::Strip => (Vec2::new(COLLAPSED_WIDTH, strip_h), tb.y),
            Panel::Prayers => {
                let size = PRAYERS_SIZE;
                (size, tb.y + tb.height - size.y)
            }
            Panel::Setup => {
                let size = SETUP_SIZE;
                (size, tb.y + tb.height - size.y)
            }
        };

        // Physical pixels for SetWindowPos.
        let px = (x * ppp).round() as i32;
        let py = (y * ppp).round() as i32;
        let pw = (size.x * ppp).round() as i32;
        let ph = (size.y * ppp).round() as i32;
        let key = (px, py, pw, ph);

        if self.last_dock_key == Some(key) && !self.needs_dock {
            self.last_dock = Some(Instant::now());
            return;
        }

        ctx.send_viewport_cmd(egui::ViewportCommand::InnerSize(size));
        ctx.send_viewport_cmd(egui::ViewportCommand::OuterPosition(egui::pos2(x, y)));
        crate::taskbar::place_widget(px, py, pw, ph);

        self.last_dock_key = Some(key);
        self.last_dock = Some(Instant::now());
        self.needs_dock = false;
    }

    fn poll_tray(&mut self, ctx: &egui::Context) {
        let cmds: Vec<TrayCmd> = self
            .tray_rx
            .as_ref()
            .map(|rx| {
                let mut out = Vec::new();
                while let Ok(cmd) = rx.try_recv() {
                    out.push(cmd);
                }
                out
            })
            .unwrap_or_default();

        for cmd in cmds {
            match cmd {
                TrayCmd::ToggleVisible => {
                    self.visible = !self.visible;
                    crate::taskbar::show_widget(self.visible);
                    if self.visible {
                        self.needs_dock = true;
                        self.dock_if_needed(ctx);
                    }
                }
                TrayCmd::ChangeMosque => self.open_setup(),
                TrayCmd::ToggleAutostart => {
                    self.config.start_with_windows = !self.config.start_with_windows;
                    let _ = save_config(&self.config);
                    match crate::autostart::set_enabled(self.config.start_with_windows) {
                        Ok(()) => {
                            self.status = if self.config.start_with_windows {
                                "Starts with Windows: on".into()
                            } else {
                                "Starts with Windows: off".into()
                            };
                        }
                        Err(e) => self.status = e.to_string(),
                    }
                }
                TrayCmd::Quit => {
                    ctx.send_viewport_cmd(egui::ViewportCommand::Close);
                }
            }
        }
    }

    fn on_strip_clicked(&mut self) {
        if self.config.mosque.is_none() {
            self.open_setup();
            return;
        }
        self.panel = match self.panel {
            Panel::Strip => Panel::Prayers,
            Panel::Prayers | Panel::Setup => Panel::Strip,
        };
        self.needs_dock = true;
    }
}

impl eframe::App for App {
    fn clear_color(&self, _visuals: &egui::Visuals) -> [f32; 4] {
        self.shell_theme.clear_rgba
    }

    fn update(&mut self, ctx: &egui::Context, _frame: &mut eframe::Frame) {
        let prev_panel = self.panel;

        self.poll_tray(ctx);
        self.poll_fetch();
        self.poll_theme();
        configure_style(ctx, self.shell_theme);
        self.dock_if_needed(ctx);

        ctx.request_repaint_after(Duration::from_millis(250));

        if let Some(schedule) = &self.schedule {
            let fajr = schedule
                .slots
                .iter()
                .find(|s| s.kind == PrayerKind::Fajr)
                .map(|s| s.adhan);
            self.next = next_prayer_with_tomorrow(schedule, fajr, now_local());
        }

        let needs_refresh = match self.last_refresh {
            None => self.config.mosque.is_some() && self.panel != Panel::Setup,
            Some(t) => {
                t.elapsed() >= REFRESH_INTERVAL
                    || self
                        .schedule
                        .as_ref()
                        .map(|s| s.date != today_local())
                        .unwrap_or(false)
            }
        };
        if needs_refresh {
            self.request_refresh();
        }

        let t = self.shell_theme;
        let frame = Frame::NONE
            .fill(t.fill)
            .stroke(egui::Stroke::new(1.0_f32, t.stroke))
            .corner_radius(CornerRadius::same(0))
            .inner_margin(match self.panel {
                Panel::Strip => Margin::symmetric(10, 0),
                _ => Margin::same(12),
            });

        egui::CentralPanel::default().frame(frame).show(ctx, |ui| {
            match self.panel {
                Panel::Setup => {
                    let in_flight = self.fetch.lock().map(|s| s.in_flight).unwrap_or(false);
                    let action = SetupPanel {
                        query: &mut self.search_query,
                        slug_input: &mut self.slug_input,
                        status: &self.status,
                        in_flight,
                        hits: &self.search_hits,
                        api_base: &mut self.config.api_base_url,
                        api_status: self.api_status,
                        preview: self.preview_times.as_ref(),
                        preview_slug: self.preview_slug.as_deref(),
                        preview_name: self.preview_name.as_deref(),
                    }
                    .show(ui);

                    // Collapse control always available.
                    ui.add_space(8.0);
                    if self.config.mosque.is_some()
                        && ui.button("◀ Back to widget").clicked()
                    {
                        self.panel = Panel::Strip;
                        self.needs_dock = true;
                    }

                    match action {
                        SetupAction::Search => self.request_search(),
                        SetupAction::Select(hit) => self.select_mosque(hit),
                        SetupAction::CheckApi => {
                            let _ = save_config(&self.config);
                            self.client = self.client.with_base(self.config.api_base_url.clone());
                            self.request_api_check();
                        }
                        SetupAction::StartApi => match start_local_api() {
                            Ok(()) => {
                                self.status = "Started API in a new terminal. Wait, then Check connection.".into();
                                self.api_status = ApiStatus::Unknown;
                            }
                            Err(e) => self.status = e.to_string(),
                        },
                        SetupAction::OpenMawaqit => {
                            if let Err(e) = open_url("https://mawaqit.net") {
                                self.status = e.to_string();
                            }
                        }
                        SetupAction::PreviewMosque => {
                            let _ = save_config(&self.config);
                            self.client = self.client.with_base(self.config.api_base_url.clone());
                            self.request_preview();
                        }
                        SetupAction::ConfirmMosque => self.confirm_mosque(),
                        SetupAction::None => {}
                    }
                }
                Panel::Strip | Panel::Prayers => {
                    let mosque_name = self
                        .config
                        .mosque
                        .as_ref()
                        .map(|m| m.display_name.as_str())
                        .unwrap_or("Set up mosque");
                    let collapsed_label = if self.config.mosque.is_none() {
                        "Tap to set up mosque".into()
                    } else {
                        match &self.next {
                            Some(n) => format!(
                                "{} {} · {}",
                                n.kind.label(),
                                n.at.format("%H:%M"),
                                format_countdown(n.remaining)
                            ),
                            None => "Loading…".into(),
                        }
                    };

                    let toggle = WidgetPanel {
                        expanded: self.panel == Panel::Prayers,
                        mosque_name,
                        collapsed_label: &collapsed_label,
                        schedule: self.schedule.as_ref(),
                        next: self.next.as_ref(),
                        status: &self.status,
                        theme: self.shell_theme,
                    }
                    .show(ui);

                    if toggle {
                        self.on_strip_clicked();
                    }
                }
            }
        });

        if self.panel != prev_panel {
            self.needs_dock = true;
        }
    }
}

pub enum SetupAction {
    None,
    Search,
    Select(MosqueHit),
    CheckApi,
    StartApi,
    OpenMawaqit,
    PreviewMosque,
    ConfirmMosque,
}

fn configure_style(ctx: &egui::Context, theme: ShellTheme) {
    let mut style = (*ctx.style()).clone();
    style.spacing.item_spacing = Vec2::new(8.0, 8.0);
    style.text_styles.insert(
        egui::TextStyle::Body,
        egui::FontId::new(14.0, egui::FontFamily::Proportional),
    );
    style.text_styles.insert(
        egui::TextStyle::Button,
        egui::FontId::new(14.0, egui::FontFamily::Proportional),
    );
    style.text_styles.insert(
        egui::TextStyle::Heading,
        egui::FontId::new(18.0, egui::FontFamily::Proportional),
    );
    ctx.set_style(style);

    let mut visuals = if theme.dark {
        egui::Visuals::dark()
    } else {
        egui::Visuals::light()
    };
    visuals.panel_fill = theme.fill;
    visuals.window_fill = theme.fill;
    visuals.override_text_color = Some(theme.text);
    visuals.widgets.noninteractive.fg_stroke.color = theme.text;
    visuals.widgets.inactive.fg_stroke.color = theme.text;
    visuals.widgets.hovered.fg_stroke.color = theme.accent;
    visuals.widgets.active.fg_stroke.color = theme.accent;
    ctx.set_visuals(visuals);
}

fn build_tray() -> Option<(tray_icon::TrayIcon, std::sync::mpsc::Receiver<TrayCmd>)> {
    use tray_icon::menu::{Menu, MenuEvent, MenuItem};
    use tray_icon::TrayIconBuilder;

    let (tx, rx) = std::sync::mpsc::channel();

    let toggle = MenuItem::new("Show / Hide", true, None);
    let change = MenuItem::new("Change mosque…", true, None);
    let autostart_label = if crate::autostart::is_enabled() {
        "Start with Windows ✓"
    } else {
        "Start with Windows"
    };
    let autostart = MenuItem::new(autostart_label, true, None);
    let quit = MenuItem::new("Quit", true, None);
    let menu = Menu::new();
    let _ = menu.append(&toggle);
    let _ = menu.append(&change);
    let _ = menu.append(&autostart);
    let _ = menu.append(&quit);

    let toggle_id = toggle.id().clone();
    let change_id = change.id().clone();
    let autostart_id = autostart.id().clone();
    let quit_id = quit.id().clone();

    MenuEvent::set_event_handler(Some(move |event: MenuEvent| {
        let cmd = if event.id == toggle_id {
            TrayCmd::ToggleVisible
        } else if event.id == change_id {
            TrayCmd::ChangeMosque
        } else if event.id == autostart_id {
            TrayCmd::ToggleAutostart
        } else if event.id == quit_id {
            TrayCmd::Quit
        } else {
            return;
        };
        let _ = tx.send(cmd);
    }));

    let icon = tray_icon_rgba();
    let tray = TrayIconBuilder::new()
        .with_menu(Box::new(menu))
        .with_tooltip(crate::config::APP_DISPLAY_NAME)
        .with_icon(icon)
        .build()
        .ok()?;

    std::mem::forget(toggle);
    std::mem::forget(change);
    std::mem::forget(autostart);
    std::mem::forget(quit);

    Some((tray, rx))
}

fn tray_icon_rgba() -> tray_icon::Icon {
    const PNG: &[u8] = include_bytes!("../../assets/icon.png");
    let img = image::load_from_memory(PNG)
        .expect("icon.png")
        .into_rgba8();
    let resized = image::imageops::resize(&img, 32, 32, image::imageops::FilterType::Lanczos3);
    let (w, h) = resized.dimensions();
    tray_icon::Icon::from_rgba(resized.into_raw(), w, h).expect("tray icon")
}

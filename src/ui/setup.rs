use eframe::egui::{self, Color32, RichText, ScrollArea, TextEdit};

use crate::mawaqit::{MosqueHit, PrayerTimes};
use crate::ui::SetupAction;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ApiStatus {
    Unknown,
    Checking,
    Online,
    Offline,
}

impl ApiStatus {
    pub fn label(self) -> &'static str {
        match self {
            Self::Unknown => "Not checked yet",
            Self::Checking => "Checking…",
            Self::Online => "Online",
            Self::Offline => "Offline — start the API first",
        }
    }

    pub fn color(self) -> Color32 {
        match self {
            Self::Unknown => Color32::GRAY,
            Self::Checking => Color32::from_rgb(220, 180, 80),
            Self::Online => Color32::from_rgb(80, 200, 140),
            Self::Offline => Color32::from_rgb(220, 100, 100),
        }
    }
}

pub struct SetupPanel<'a> {
    pub query: &'a mut String,
    pub slug_input: &'a mut String,
    pub status: &'a str,
    pub in_flight: bool,
    pub hits: &'a [MosqueHit],
    pub api_base: &'a mut String,
    pub api_status: ApiStatus,
    pub preview: Option<&'a PrayerTimes>,
    pub preview_slug: Option<&'a str>,
    pub preview_name: Option<&'a str>,
}

impl SetupPanel<'_> {
    pub fn show(self, ui: &mut egui::Ui) -> SetupAction {
        let mut action = SetupAction::None;

        ui.heading("Mawaqit Widget — Setup");
        ui.label("Use this window to start the API, pick your mosque, then show the taskbar widget.");
        ui.add_space(12.0);

        // --- Step 1: API ---
        ui.group(|ui| {
            ui.label(RichText::new("1. Local prayer API").strong().size(16.0));
            ui.add_space(4.0);
            ui.horizontal(|ui| {
                ui.label("API address");
                ui.add(
                    TextEdit::singleline(self.api_base)
                        .desired_width(260.0)
                        .hint_text("http://127.0.0.1:8000"),
                );
            });
            ui.horizontal(|ui| {
                ui.label("Status:");
                ui.colored_label(self.api_status.color(), self.api_status.label());
            });
            ui.horizontal(|ui| {
                if ui
                    .add_enabled(!self.in_flight, egui::Button::new("Check connection"))
                    .clicked()
                {
                    action = SetupAction::CheckApi;
                }
                if ui
                    .button("Start API")
                    .on_hover_text("Runs scripts/run-api.ps1 in a new terminal")
                    .clicked()
                {
                    action = SetupAction::StartApi;
                }
                if ui.button("Open mawaqit.net").clicked() {
                    action = SetupAction::OpenMawaqit;
                }
            });
            ui.label(
                RichText::new("Docker: docker compose up -d   ·   Or click Start API (Python)")
                    .small()
                    .weak(),
            );
        });

        ui.add_space(10.0);

        // --- Step 2: Mosque ---
        ui.group(|ui| {
            ui.label(RichText::new("2. Your mosque").strong().size(16.0));
            ui.label(
                RichText::new(
                    "Name search on mawaqit.net is often blocked. Best method: open your mosque page → copy the URL from the address bar → paste below.",
                )
                .weak(),
            );
            ui.add_space(4.0);

            ui.horizontal(|ui| {
                let resp = ui.add(
                    TextEdit::singleline(self.slug_input)
                        .desired_width(320.0)
                        .hint_text("https://mawaqit.net/en/your-mosque-slug"),
                );
                let go = ui
                    .add_enabled(!self.in_flight, egui::Button::new("Find mosque"))
                    .clicked()
                    || (resp.lost_focus() && ui.input(|i| i.key_pressed(egui::Key::Enter)));
                if go {
                    action = SetupAction::PreviewMosque;
                }
            });

            ui.add_space(6.0);
            ui.collapsing("Try find by name (may miss)", |ui| {
                ui.horizontal(|ui| {
                    let resp = ui.add(
                        TextEdit::singleline(self.query)
                            .desired_width(200.0)
                            .hint_text("Name or city"),
                    );
                    let search = ui
                        .add_enabled(!self.in_flight, egui::Button::new("Search"))
                        .clicked()
                        || (resp.lost_focus()
                            && ui.input(|i| i.key_pressed(egui::Key::Enter))
                            && !self.query.is_empty());
                    if search {
                        action = SetupAction::Search;
                    }
                });

                if !self.hits.is_empty() {
                    ui.label("Click a result:");
                    ScrollArea::vertical().max_height(120.0).show(ui, |ui| {
                        for hit in self.hits {
                            if ui
                                .selectable_label(false, format!("{}  ({})", hit.label, hit.slug))
                                .clicked()
                            {
                                action = SetupAction::Select(hit.clone());
                            }
                        }
                    });
                }
            });
        });

        ui.add_space(10.0);

        // --- Preview ---
        if let (Some(times), Some(slug)) = (self.preview, self.preview_slug) {
            let title = self.preview_name.unwrap_or(slug);
            ui.group(|ui| {
                ui.label(RichText::new(format!("Found — {title}")).strong().size(16.0));
                ui.label(RichText::new(format!("slug: {slug}")).small().weak());
                ui.monospace(format!(
                    "Fajr {}   Shuruq {}   Dhuhr {}\nAsr {}   Maghrib {}   Isha {}",
                    times.fajr, times.sunrise, times.dohr, times.asr, times.maghreb, times.icha
                ));
                ui.add_space(6.0);
                if ui
                    .add(
                        egui::Button::new(RichText::new("Save & show on taskbar").strong())
                            .min_size(egui::vec2(220.0, 32.0)),
                    )
                    .clicked()
                {
                    action = SetupAction::ConfirmMosque;
                }
            });
        }

        ui.add_space(8.0);
        if self.in_flight {
            ui.spinner();
            ui.label("Working…");
        } else if !self.status.is_empty() {
            ui.label(RichText::new(self.status).color(Color32::from_rgb(200, 210, 220)));
        }

        action
    }
}

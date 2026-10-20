use eframe::egui::{self, RichText, Sense, Vec2};

use crate::schedule::{DaySchedule, NextPrayer, PrayerKind};
use crate::theme::ShellTheme;

pub struct WidgetPanel<'a> {
    pub expanded: bool,
    pub mosque_name: &'a str,
    pub collapsed_label: &'a str,
    pub schedule: Option<&'a DaySchedule>,
    pub next: Option<&'a NextPrayer>,
    pub status: &'a str,
    pub theme: ShellTheme,
}

impl WidgetPanel<'_> {
    /// Returns true if the user toggled expand/collapse.
    pub fn show(self, ui: &mut egui::Ui) -> bool {
        let mut toggled = false;
        let t = self.theme;

        if !self.expanded {
            let desired = ui.available_size_before_wrap();
            let (rect, response) =
                ui.allocate_exact_size(desired.max(Vec2::new(180.0, 28.0)), Sense::click());

            let fill = if response.hovered() {
                t.fill_hover
            } else {
                t.fill
            };
            // Soft fill over acrylic — keep some transparency for the blur.
            ui.painter().rect_filled(rect, 0.0, fill);
            if response.hovered() {
                ui.painter().rect_stroke(
                    rect,
                    0.0,
                    egui::Stroke::new(1.0_f32, t.accent),
                    egui::StrokeKind::Inside,
                );
            }

            let text_color = if response.hovered() {
                t.accent
            } else {
                t.text
            };
            let text = format!("▶  {}", self.collapsed_label);
            ui.painter().text(
                rect.left_center() + Vec2::new(10.0, 0.0),
                egui::Align2::LEFT_CENTER,
                text,
                egui::FontId::proportional(13.0),
                text_color,
            );

            if response.clicked() {
                toggled = true;
            }
            response.on_hover_text("Click to expand prayer times");
            return toggled;
        }

        ui.horizontal(|ui| {
            let btn = egui::Button::new(RichText::new("◀ Collapse").color(t.text))
                .fill(t.fill_hover)
                .min_size(Vec2::new(100.0, 28.0));
            if ui.add(btn).clicked() {
                toggled = true;
            }
            ui.label(RichText::new(self.mosque_name).strong().size(15.0).color(t.text));
        });

        if let Some(next) = self.next {
            ui.label(
                RichText::new(format!(
                    "Next: {} at {}",
                    next.kind.label(),
                    next.at.format("%H:%M")
                ))
                .color(t.accent),
            );
        }

        ui.separator();
        ui.horizontal(|ui| {
            ui.label(RichText::new("Prayer").strong().color(t.text_muted));
            ui.add_space(48.0);
            ui.label(RichText::new("Adhan").strong().color(t.text_muted));
            ui.add_space(24.0);
            ui.label(RichText::new("Iqama").strong().color(t.text_muted));
        });

        if let Some(schedule) = self.schedule {
            for slot in &schedule.slots {
                let is_next = self.next.map(|n| n.kind == slot.kind).unwrap_or(false);
                let color = if is_next {
                    t.accent
                } else if slot.kind == PrayerKind::Shuruq {
                    t.text_muted
                } else {
                    t.text
                };

                ui.horizontal(|ui| {
                    ui.label(
                        RichText::new(format!("{:<8}", slot.kind.label()))
                            .color(color)
                            .monospace(),
                    );
                    ui.label(RichText::new(&slot.adhan_display).color(color).monospace());
                    ui.add_space(28.0);
                    let iq = slot.iqama_display.as_deref().unwrap_or("—");
                    ui.label(RichText::new(iq).color(color).monospace());
                });
            }
        } else {
            ui.label(RichText::new("No times loaded yet.").color(t.text_muted));
        }

        if !self.status.is_empty() {
            ui.add_space(6.0);
            ui.label(RichText::new(self.status).small().color(t.text_muted));
        }

        toggled
    }
}

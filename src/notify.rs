//! Decide which prayer alerts should fire right now.

use chrono::{Duration, NaiveDate, NaiveDateTime};

use crate::config::NotificationPrefs;
use crate::schedule::{parse_hhmm, PrayerKind, PrayerSlot};

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum AlertKind {
    BeforeAdhan,
    Adhan,
    Iqama,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Alert {
    pub key: String,
    pub title: String,
    pub body: String,
}

/// Fire if `now` is in `[at, at + window)`.
const WINDOW: Duration = Duration::seconds(90);

pub fn pending_alerts(
    date: NaiveDate,
    slots: &[PrayerSlot],
    now: NaiveDateTime,
    prefs: &NotificationPrefs,
    already: &std::collections::HashSet<String>,
) -> Vec<Alert> {
    if !prefs.enabled {
        return Vec::new();
    }

    let mut out = Vec::new();
    for slot in slots {
        if !prefs.prayer_enabled(slot.kind.label()) {
            continue;
        }
        let adhan_at = date.and_time(slot.adhan);

        if prefs.minutes_before > 0 {
            let before_at = adhan_at - Duration::minutes(prefs.minutes_before as i64);
            push_if_due(
                &mut out,
                already,
                date,
                slot.kind,
                AlertKind::BeforeAdhan,
                before_at,
                now,
                &format!("{} in {} min", slot.kind.label(), prefs.minutes_before),
                &format!(
                    "Adhan at {}.",
                    slot.adhan_display
                ),
            );
        }

        if prefs.at_adhan {
            push_if_due(
                &mut out,
                already,
                date,
                slot.kind,
                AlertKind::Adhan,
                adhan_at,
                now,
                &format!("It's time for {}", slot.kind.label()),
                &format!("Adhan {}.", slot.adhan_display),
            );
        }

        if prefs.at_iqama {
            if let Some(iq) = slot
                .iqama_display
                .as_deref()
                .and_then(parse_hhmm)
            {
                push_if_due(
                    &mut out,
                    already,
                    date,
                    slot.kind,
                    AlertKind::Iqama,
                    date.and_time(iq),
                    now,
                    &format!("Iqama — {}", slot.kind.label()),
                    &format!("Iqama at {}.", slot.iqama_display.as_deref().unwrap_or("")),
                );
            }
        }
    }
    out
}

fn push_if_due(
    out: &mut Vec<Alert>,
    already: &std::collections::HashSet<String>,
    date: NaiveDate,
    kind: PrayerKind,
    alert: AlertKind,
    at: NaiveDateTime,
    now: NaiveDateTime,
    title: &str,
    body: &str,
) {
    if now < at || now >= at + WINDOW {
        return;
    }
    let tag = match alert {
        AlertKind::BeforeAdhan => "before",
        AlertKind::Adhan => "adhan",
        AlertKind::Iqama => "iqama",
    };
    let key = format!("{}|{}|{tag}", date, kind.label());
    if already.contains(&key) {
        return;
    }
    out.push(Alert {
        key,
        title: title.to_string(),
        body: body.to_string(),
    });
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::schedule::PrayerSlot;
    use chrono::NaiveTime;
    use std::collections::HashSet;

    fn slot(kind: PrayerKind, hh: u32, mm: u32) -> PrayerSlot {
        let t = NaiveTime::from_hms_opt(hh, mm, 0).unwrap();
        PrayerSlot {
            kind,
            adhan: t,
            adhan_display: format!("{hh:02}:{mm:02}"),
            iqama: NaiveTime::from_hms_opt(hh, mm + 10, 0),
            iqama_display: Some(format!("{hh:02}:{}", mm + 10)),
        }
    }

    #[test]
    fn fires_adhan_inside_window() {
        let date = NaiveDate::from_ymd_opt(2026, 10, 20).unwrap();
        let slots = [slot(PrayerKind::Dhuhr, 12, 30)];
        let now = date.and_hms_opt(12, 30, 20).unwrap();
        let prefs = NotificationPrefs {
            minutes_before: 0,
            at_iqama: false,
            ..NotificationPrefs::default()
        };
        let alerts = pending_alerts(date, &slots, now, &prefs, &HashSet::new());
        assert_eq!(alerts.len(), 1);
        assert!(alerts[0].title.contains("Dhuhr"));
    }

    #[test]
    fn skips_already_sent() {
        let date = NaiveDate::from_ymd_opt(2026, 10, 20).unwrap();
        let slots = [slot(PrayerKind::Dhuhr, 12, 30)];
        let now = date.and_hms_opt(12, 30, 20).unwrap();
        let prefs = NotificationPrefs {
            minutes_before: 0,
            at_iqama: false,
            ..NotificationPrefs::default()
        };
        let mut already = HashSet::new();
        already.insert("2026-10-20|Dhuhr|adhan".into());
        assert!(pending_alerts(date, &slots, now, &prefs, &already).is_empty());
    }

    #[test]
    fn respects_prayer_toggle() {
        let date = NaiveDate::from_ymd_opt(2026, 10, 20).unwrap();
        let slots = [slot(PrayerKind::Dhuhr, 12, 30)];
        let now = date.and_hms_opt(12, 30, 0).unwrap();
        let prefs = NotificationPrefs {
            dhuhr: false,
            minutes_before: 0,
            ..NotificationPrefs::default()
        };
        assert!(pending_alerts(date, &slots, now, &prefs, &HashSet::new()).is_empty());
    }
}

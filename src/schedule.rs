use chrono::{Duration, Local, NaiveDate, NaiveDateTime, NaiveTime, Timelike};

use crate::mawaqit::{IqamaTimes, PrayerTimes};

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum PrayerKind {
    Fajr,
    Shuruq,
    Dhuhr,
    Asr,
    Maghrib,
    Isha,
}

impl PrayerKind {
    pub fn label(self) -> &'static str {
        match self {
            Self::Fajr => "Fajr",
            Self::Shuruq => "Shuruq",
            Self::Dhuhr => "Dhuhr",
            Self::Asr => "Asr",
            Self::Maghrib => "Maghrib",
            Self::Isha => "Isha",
        }
    }

    pub fn all() -> [Self; 6] {
        [
            Self::Fajr,
            Self::Shuruq,
            Self::Dhuhr,
            Self::Asr,
            Self::Maghrib,
            Self::Isha,
        ]
    }

    fn adhan_raw(self, times: &PrayerTimes) -> &str {
        match self {
            Self::Fajr => &times.fajr,
            Self::Shuruq => &times.sunrise,
            Self::Dhuhr => &times.dohr,
            Self::Asr => &times.asr,
            Self::Maghrib => &times.maghreb,
            Self::Isha => &times.icha,
        }
    }

    fn iqama_raw(self, iqama: &IqamaTimes) -> Option<&str> {
        let raw = match self {
            Self::Fajr => iqama.fajr.as_str(),
            Self::Shuruq => return None,
            Self::Dhuhr => iqama.dohr.as_str(),
            Self::Asr => iqama.asr.as_str(),
            Self::Maghrib => iqama.maghreb.as_str(),
            Self::Isha => iqama.icha.as_str(),
        };
        if raw.is_empty() {
            None
        } else {
            Some(raw)
        }
    }
}

#[derive(Debug, Clone)]
pub struct PrayerSlot {
    pub kind: PrayerKind,
    pub adhan: NaiveTime,
    pub adhan_display: String,
    pub iqama_display: Option<String>,
}

#[derive(Debug, Clone)]
pub struct DaySchedule {
    pub date: NaiveDate,
    pub slots: Vec<PrayerSlot>,
}

#[derive(Debug, Clone)]
pub struct NextPrayer {
    pub kind: PrayerKind,
    pub at: NaiveDateTime,
    pub remaining: Duration,
}

pub fn build_day_schedule(
    date: NaiveDate,
    times: &PrayerTimes,
    iqama: &IqamaTimes,
) -> DaySchedule {
    let mut slots = Vec::new();
    for kind in PrayerKind::all() {
        let Some(adhan) = parse_hhmm(kind.adhan_raw(times)) else {
            continue;
        };
        let iqama_display = kind
            .iqama_raw(iqama)
            .and_then(|raw| resolve_iqama_display(adhan, raw));
        slots.push(PrayerSlot {
            kind,
            adhan,
            adhan_display: format_hhmm(adhan),
            iqama_display,
        });
    }
    DaySchedule { date, slots }
}

pub fn next_prayer(schedule: &DaySchedule, now: NaiveDateTime) -> Option<NextPrayer> {
    for slot in &schedule.slots {
        if slot.kind == PrayerKind::Shuruq {
            continue;
        }
        let at = schedule.date.and_time(slot.adhan);
        if at > now {
            return Some(NextPrayer {
                kind: slot.kind,
                at,
                remaining: at - now,
            });
        }
    }
    None
}

/// After Isha, treat next as tomorrow Fajr when we have tomorrow's times.
pub fn next_prayer_with_tomorrow(
    today: &DaySchedule,
    tomorrow_fajr: Option<NaiveTime>,
    now: NaiveDateTime,
) -> Option<NextPrayer> {
    if let Some(n) = next_prayer(today, now) {
        return Some(n);
    }
    let fajr = tomorrow_fajr.or_else(|| {
        today
            .slots
            .iter()
            .find(|s| s.kind == PrayerKind::Fajr)
            .map(|s| s.adhan)
    })?;
    let at = (today.date + Duration::days(1)).and_time(fajr);
    if at <= now {
        return None;
    }
    Some(NextPrayer {
        kind: PrayerKind::Fajr,
        at,
        remaining: at - now,
    })
}

pub fn format_countdown(remaining: Duration) -> String {
    let total = remaining.num_seconds().max(0);
    let h = total / 3600;
    let m = (total % 3600) / 60;
    let s = total % 60;
    if h > 0 {
        format!("{h}:{m:02}:{s:02}")
    } else {
        format!("{m:02}:{s:02}")
    }
}

pub fn format_hhmm(t: NaiveTime) -> String {
    format!("{:02}:{:02}", t.hour(), t.minute())
}

pub fn parse_hhmm(raw: &str) -> Option<NaiveTime> {
    let raw = raw.trim();
    if raw.is_empty() {
        return None;
    }
    if let Ok(t) = NaiveTime::parse_from_str(raw, "%H:%M") {
        return Some(t);
    }
    if let Ok(t) = NaiveTime::parse_from_str(raw, "%H:%M:%S") {
        return Some(t);
    }
    None
}

fn resolve_iqama_display(adhan: NaiveTime, raw: &str) -> Option<String> {
    let raw = raw.trim();
    if raw.is_empty() {
        return None;
    }
    if let Some(rest) = raw.strip_prefix('+') {
        let mins: i64 = rest.parse().ok()?;
        let base = NaiveDate::from_ymd_opt(2000, 1, 1)?.and_time(adhan);
        let resolved = base + Duration::minutes(mins);
        return Some(format_hhmm(resolved.time()));
    }
    if parse_hhmm(raw).is_some() {
        return Some(raw.to_string());
    }
    Some(raw.to_string())
}

pub fn today_local() -> NaiveDate {
    Local::now().date_naive()
}

pub fn now_local() -> NaiveDateTime {
    Local::now().naive_local()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn relative_iqama() {
        let adhan = NaiveTime::from_hms_opt(5, 30, 0).unwrap();
        assert_eq!(
            resolve_iqama_display(adhan, "+15").as_deref(),
            Some("05:45")
        );
    }

    #[test]
    fn countdown_format() {
        assert_eq!(format_countdown(Duration::seconds(65)), "01:05");
        assert_eq!(format_countdown(Duration::seconds(3661)), "1:01:01");
    }
}

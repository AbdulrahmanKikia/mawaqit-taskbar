use reqwest::blocking::Client;
use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::collections::HashMap;
use std::sync::Mutex;
use std::time::Duration;

use crate::Error;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PrayerTimes {
    pub fajr: String,
    pub sunrise: String,
    pub dohr: String,
    pub asr: String,
    pub maghreb: String,
    pub icha: String,
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
pub struct IqamaTimes {
    #[serde(default)]
    pub fajr: String,
    #[serde(default)]
    pub dohr: String,
    #[serde(default)]
    pub asr: String,
    #[serde(default)]
    pub maghreb: String,
    #[serde(default)]
    pub icha: String,
}

#[derive(Debug, Clone, PartialEq)]
pub struct MosqueHit {
    pub slug: String,
    pub label: String,
    pub lat: Option<f64>,
    pub lon: Option<f64>,
}

#[derive(Debug, Clone)]
pub struct ResolvedMosque {
    pub slug: String,
    pub name: String,
    pub times: PrayerTimes,
}

#[derive(Debug, Deserialize)]
struct MapItem {
    #[serde(default)]
    slug: Option<String>,
    #[serde(default)]
    name: Option<String>,
    #[serde(default)]
    city: Option<String>,
    #[serde(default)]
    zipcode: Option<String>,
    #[serde(default)]
    lat: Option<f64>,
    #[serde(default)]
    lng: Option<f64>,
    #[serde(default)]
    lon: Option<f64>,
}

pub struct MawaqitClient {
    http: Client,
    api_base: String,
    country_cache: Mutex<HashMap<String, Vec<MosqueHit>>>,
}

impl MawaqitClient {
    pub fn new(api_base: impl Into<String>) -> Result<Self, Error> {
        let http = Client::builder()
            .timeout(Duration::from_secs(30))
            .user_agent(
                "MawaqitWidget/0.1 (Windows; +https://mawaqit.net) Mozilla/5.0",
            )
            .build()?;
        Ok(Self {
            http,
            api_base: api_base.into().trim_end_matches('/').to_string(),
            country_cache: Mutex::new(HashMap::new()),
        })
    }

    pub fn with_base(&self, api_base: impl Into<String>) -> Self {
        Self {
            http: self.http.clone(),
            api_base: api_base.into().trim_end_matches('/').to_string(),
            country_cache: Mutex::new(HashMap::new()),
        }
    }

    pub fn health_check(&self) -> Result<String, Error> {
        let url = format!("{}/api/v1/", self.api_base);
        let resp = self.http.get(&url).send()?.error_for_status()?;
        let body: Value = resp.json()?;
        Ok(body
            .get("Greetings")
            .and_then(|v| v.as_str())
            .unwrap_or("API OK")
            .to_string())
    }

    pub fn prayer_times(&self, masjid_id: &str) -> Result<PrayerTimes, Error> {
        // Prefer local mrsofiane API; fall back to scraping the public mosque page.
        match self.prayer_times_via_api(masjid_id) {
            Ok(t) => Ok(t),
            Err(api_err) => self
                .resolve_mosque(masjid_id)
                .map(|r| r.times)
                .map_err(|_| api_err),
        }
    }

    fn prayer_times_via_api(&self, masjid_id: &str) -> Result<PrayerTimes, Error> {
        let url = format!(
            "{}/api/v1/{}/prayer-times",
            self.api_base,
            urlencoding_simple(masjid_id)
        );
        let resp = self.http.get(&url).send()?.error_for_status()?;
        Ok(resp.json()?)
    }

    pub fn calendar_iqama_month(
        &self,
        masjid_id: &str,
        month: u32,
    ) -> Result<Vec<IqamaTimes>, Error> {
        let url = format!(
            "{}/api/v1/{}/calendar-iqama/{}",
            self.api_base,
            urlencoding_simple(masjid_id),
            month
        );
        let resp = self.http.get(&url).send()?.error_for_status()?;
        Ok(resp.json()?)
    }

    pub fn iqama_for_day(
        &self,
        masjid_id: &str,
        month: u32,
        day: u32,
    ) -> Result<IqamaTimes, Error> {
        let month_rows = self.calendar_iqama_month(masjid_id, month)?;
        let idx = day.saturating_sub(1) as usize;
        Ok(month_rows.get(idx).cloned().unwrap_or_default())
    }

    /// Resolve a mosque from URL/slug by loading its public page `confData`.
    pub fn resolve_mosque(&self, input: &str) -> Result<ResolvedMosque, Error> {
        let slug = normalize_mosque_input(input).ok_or_else(|| {
            Error::msg(
                "Could not read a mosque id. Paste a full mawaqit.net URL, e.g. https://mawaqit.net/en/your-mosque-slug",
            )
        })?;

        let conf = self
            .fetch_conf_data(&slug)
            .map_err(|e| {
                Error::msg(format!(
                    "Mosque “{slug}” not found on mawaqit.net ({e}). Open mawaqit.net, open your mosque page, copy the address bar URL, and paste it here."
                ))
            })?;

        let name = conf
            .get("name")
            .and_then(|v| v.as_str())
            .filter(|s| !s.is_empty())
            .unwrap_or(&slug)
            .to_string();

        let times = times_from_conf(&conf).ok_or_else(|| {
            Error::msg(format!("Found “{name}” but could not read today's prayer times."))
        })?;

        Ok(ResolvedMosque { slug, name, times })
    }

    /// Find mosques by name/city/URL.
    ///
    /// The official `mosque/search?word=` endpoint is locked behind auth (401).
    /// We resolve URLs/slugs directly, then filter the public per-country map feed.
    pub fn search_mosques(&self, word: &str) -> Result<Vec<MosqueHit>, Error> {
        let word = word.trim();
        if word.is_empty() {
            return Ok(Vec::new());
        }

        // Direct URL / slug → resolve for a real display name.
        if normalize_mosque_input(word).is_some() {
            let resolved = self.resolve_mosque(word)?;
            return Ok(vec![MosqueHit {
                slug: resolved.slug,
                label: resolved.name,
                lat: None,
                lon: None,
            }]);
        }

        // Geocode the query → country map → filter by name/city/zip/slug.
        let mut hits = Vec::new();
        if let Ok(Some((lat, lon, cc))) = self.geocode_place(word) {
            let country = self.mosques_by_country(&cc)?;
            hits = filter_mosques(&country, word);
            if hits.is_empty() {
                hits = nearest_mosques(&country, lat, lon, 30);
            }
        }

        // Also try a few common countries if geocode missed (offline / ambiguous).
        if hits.is_empty() {
            for cc in ["CA", "US", "FR", "GB", "BE", "DE"] {
                if let Ok(country) = self.mosques_by_country(cc) {
                    let mut part = filter_mosques(&country, word);
                    hits.append(&mut part);
                    if hits.len() >= 40 {
                        break;
                    }
                }
            }
            dedupe_hits(&mut hits);
            hits.truncate(40);
        }

        if !hits.is_empty() {
            return Ok(hits);
        }

        // Last resort: slugified name candidates against public pages.
        let mut seen = std::collections::HashSet::new();
        for candidate in slug_candidates(word) {
            if !seen.insert(candidate.clone()) {
                continue;
            }
            if let Ok(resolved) = self.resolve_mosque(&candidate) {
                hits.push(MosqueHit {
                    slug: resolved.slug,
                    label: resolved.name,
                    lat: None,
                    lon: None,
                });
            }
            if hits.len() >= 8 {
                break;
            }
        }

        if !hits.is_empty() {
            return Ok(hits);
        }

        Err(Error::msg(
            "No mosques found. Pan the map, try another city, or paste a mawaqit.net mosque URL.",
        ))
    }

    /// Mosques near a coordinate (powers the map picker).
    ///
    /// Uses the public country map feed (`/mosque/map/{CC}`) — nearby search is 401.
    pub fn search_nearby(&self, lat: f64, lon: f64) -> Result<Vec<MosqueHit>, Error> {
        self.search_in_view(lat, lon, None)
    }

    /// Mosques in the current map view (name markers). Falls back to nearest if the
    /// viewport is empty or bounds are missing.
    pub fn search_in_view(
        &self,
        lat: f64,
        lon: f64,
        bounds: Option<(f64, f64, f64, f64)>,
    ) -> Result<Vec<MosqueHit>, Error> {
        let cc = self
            .country_code_at(lat, lon)
            .unwrap_or_else(|_| guess_country_code(lat, lon));
        let country = self.mosques_by_country(&cc)?;
        if let Some((north, south, east, west)) = bounds {
            let mut in_view: Vec<MosqueHit> = country
                .iter()
                .filter(|h| match (h.lat, h.lon) {
                    (Some(la), Some(lo)) => {
                        la <= north && la >= south && lo <= east && lo >= west
                    }
                    _ => false,
                })
                .cloned()
                .collect();
            if !in_view.is_empty() {
                in_view.sort_by(|a, b| {
                    let da = match (a.lat, a.lon) {
                        (Some(la), Some(lo)) => haversine_km(lat, lon, la, lo),
                        _ => f64::MAX,
                    };
                    let db = match (b.lat, b.lon) {
                        (Some(la), Some(lo)) => haversine_km(lat, lon, la, lo),
                        _ => f64::MAX,
                    };
                    da.partial_cmp(&db).unwrap_or(std::cmp::Ordering::Equal)
                });
                in_view.truncate(60);
                return Ok(in_view);
            }
        }
        Ok(nearest_mosques(&country, lat, lon, 40))
    }

    /// All mosques registered for a country (ISO 3166-1 alpha-2).
    pub fn mosques_by_country(&self, country_code: &str) -> Result<Vec<MosqueHit>, Error> {
        let code = country_code.trim().to_ascii_uppercase();
        if code.len() != 2 {
            return Err(Error::msg("country code must be 2 letters"));
        }
        if let Ok(cache) = self.country_cache.lock() {
            if let Some(hits) = cache.get(&code) {
                return Ok(hits.clone());
            }
        }
        let url = format!("https://mawaqit.net/api/2.0/mosque/map/{code}");
        let resp = self
            .http
            .get(&url)
            .header("Accept", "application/json")
            .header("Referer", "https://mawaqit.net/")
            .header("Origin", "https://mawaqit.net")
            .send()?
            .error_for_status()?;
        let items: Vec<MapItem> = resp.json()?;
        let mut hits = Vec::with_capacity(items.len());
        for item in items {
            let slug = item.slug.unwrap_or_default();
            if slug.is_empty() {
                continue;
            }
            let name = item.name.unwrap_or_else(|| slug.clone());
            let loc = [item.city.clone(), item.zipcode.clone()]
                .into_iter()
                .flatten()
                .filter(|s| !s.is_empty())
                .collect::<Vec<_>>()
                .join(", ");
            let label = if loc.is_empty() {
                name
            } else {
                format!("{name} — {loc}")
            };
            hits.push(MosqueHit {
                slug,
                label,
                lat: item.lat,
                lon: item.lng.or(item.lon),
            });
        }
        if let Ok(mut cache) = self.country_cache.lock() {
            cache.insert(code, hits.clone());
        }
        Ok(hits)
    }

    fn country_code_at(&self, lat: f64, lon: f64) -> Result<String, Error> {
        let url = format!(
            "https://nominatim.openstreetmap.org/reverse?lat={lat}&lon={lon}&format=json"
        );
        let resp = self
            .http
            .get(&url)
            .header("Accept", "application/json")
            .send()?
            .error_for_status()?;
        let body: Value = resp.json()?;
        let cc = body
            .pointer("/address/country_code")
            .and_then(|v| v.as_str())
            .ok_or_else(|| Error::msg("no country for coordinates"))?;
        Ok(cc.to_ascii_uppercase())
    }

    fn geocode_place(&self, word: &str) -> Result<Option<(f64, f64, String)>, Error> {
        let url = format!(
            "https://nominatim.openstreetmap.org/search?q={}&format=json&limit=1",
            urlencoding_simple(word)
        );
        let resp = self
            .http
            .get(&url)
            .header("Accept", "application/json")
            .send()?
            .error_for_status()?;
        let body: Value = resp.json()?;
        let first = match body.as_array().and_then(|a| a.first()) {
            Some(v) => v,
            None => return Ok(None),
        };
        let lat = first
            .get("lat")
            .and_then(|v| v.as_str())
            .and_then(|s| s.parse().ok());
        let lon = first
            .get("lon")
            .and_then(|v| v.as_str())
            .and_then(|s| s.parse().ok());
        let (Some(lat), Some(lon)) = (lat, lon) else {
            return Ok(None);
        };
        let cc = self
            .country_code_at(lat, lon)
            .unwrap_or_else(|_| guess_country_code(lat, lon));
        Ok(Some((lat, lon, cc)))
    }

    fn fetch_conf_data(&self, slug: &str) -> Result<Value, Error> {
        for lang in ["en", "fr", "ar"] {
            let url = format!("https://mawaqit.net/{lang}/{slug}");
            let resp = self
                .http
                .get(&url)
                .header("Accept", "text/html,application/xhtml+xml")
                .header("Accept-Language", "en-US,en;q=0.9,fr;q=0.8")
                .send()?;
            if resp.status().as_u16() == 404 {
                continue;
            }
            let resp = resp.error_for_status()?;
            let html = resp.text()?;
            if let Some(conf) = extract_conf_data(&html) {
                return Ok(conf);
            }
        }
        Err(Error::msg("page not found or confData missing"))
    }
}

fn times_from_conf(conf: &Value) -> Option<PrayerTimes> {
    let times = conf.get("times")?.as_array()?;
    if times.len() < 5 {
        return None;
    }
    let get = |i: usize| times.get(i)?.as_str().map(|s| s.to_string());
    Some(PrayerTimes {
        fajr: get(0)?,
        sunrise: conf
            .get("shuruq")
            .and_then(|v| v.as_str())
            .unwrap_or("00:00")
            .to_string(),
        dohr: get(1)?,
        asr: get(2)?,
        maghreb: get(3)?,
        icha: get(4)?,
    })
}

fn extract_conf_data(html: &str) -> Option<Value> {
    // var confData = {...};  or let confData = {...};
    let markers = ["var confData =", "let confData =", "confData ="];
    let start = markers.iter().find_map(|m| html.find(m).map(|i| i + m.len()))?;
    let slice = html.get(start..)?;
    let slice = slice.trim_start();
    if !slice.starts_with('{') {
        return None;
    }
    // Brace-match JSON object.
    let bytes = slice.as_bytes();
    let mut depth = 0i32;
    let mut end = None;
    for (i, b) in bytes.iter().enumerate() {
        match *b {
            b'{' => depth += 1,
            b'}' => {
                depth -= 1;
                if depth == 0 {
                    end = Some(i + 1);
                    break;
                }
            }
            _ => {}
        }
    }
    let end = end?;
    let json = slice.get(..end)?;
    serde_json::from_str(json).ok()
}

/// Normalize pasted URL or bare slug into a mosque id.
pub fn normalize_mosque_input(input: &str) -> Option<String> {
    let input = input.trim();
    if input.is_empty() {
        return None;
    }
    if let Some(slug) = extract_slug_from_url(input) {
        return Some(slug);
    }
    // bare slug
    if !input.contains(' ')
        && input.len() >= 3
        && input
            .chars()
            .all(|c| c.is_ascii_alphanumeric() || c == '-' || c == '_')
    {
        return Some(input.to_ascii_lowercase());
    }
    None
}

fn extract_slug_from_url(input: &str) -> Option<String> {
    let mut s = input.trim().trim_end_matches('/');
    // strip query/fragment
    if let Some(i) = s.find(['?', '#']) {
        s = &s[..i];
        s = s.trim_end_matches('/');
    }

    let lower = s.to_ascii_lowercase();
    if !(lower.contains("mawaqit.net/") || lower.starts_with("mawaqit.net/")) {
        return None;
    }

    let idx = lower.find("mawaqit.net/")?;
    let after = &s[idx + "mawaqit.net/".len()..];
    let parts: Vec<&str> = after.split('/').filter(|p| !p.is_empty()).collect();

    // Mobile site uses `/m/...`; also skip language codes. Mosque slug is the
    // last non-meta path segment (e.g. mawaqit.net/m/en/al-andalous-mtl).
    let slug = parts.into_iter().rev().find(|p| !is_url_meta_segment(p))?;
    let slug = slug.trim();
    if slug.is_empty() {
        return None;
    }
    Some(slug.to_string())
}

fn is_url_meta_segment(part: &str) -> bool {
    matches!(
        part.to_ascii_lowercase().as_str(),
        "m" | "mobile"
            | "www"
            | "api"
            | "static"
            | "en"
            | "fr"
            | "ar"
            | "es"
            | "de"
            | "nl"
            | "tr"
            | "id"
            | "ur"
            | "bn"
            | "ru"
            | "zh"
            | "it"
            | "pt"
    )
}

fn filter_mosques(all: &[MosqueHit], word: &str) -> Vec<MosqueHit> {
    let q = word.to_ascii_lowercase();
    let tokens: Vec<&str> = q.split_whitespace().filter(|t| t.len() >= 2).collect();
    let mut hits: Vec<MosqueHit> = all
        .iter()
        .filter(|h| {
            let hay = format!("{} {}", h.label, h.slug).to_ascii_lowercase();
            if hay.contains(&q) {
                return true;
            }
            !tokens.is_empty() && tokens.iter().all(|t| hay.contains(t))
        })
        .cloned()
        .collect();
    hits.truncate(40);
    hits
}

fn nearest_mosques(all: &[MosqueHit], lat: f64, lon: f64, limit: usize) -> Vec<MosqueHit> {
    let mut scored: Vec<(f64, &MosqueHit)> = all
        .iter()
        .filter_map(|h| match (h.lat, h.lon) {
            (Some(la), Some(lo)) => Some((haversine_km(lat, lon, la, lo), h)),
            _ => None,
        })
        .collect();
    scored.sort_by(|a, b| a.0.partial_cmp(&b.0).unwrap_or(std::cmp::Ordering::Equal));
    scored
        .into_iter()
        .take(limit)
        .map(|(_, h)| h.clone())
        .collect()
}

fn dedupe_hits(hits: &mut Vec<MosqueHit>) {
    let mut seen = std::collections::HashSet::new();
    hits.retain(|h| seen.insert(h.slug.clone()));
}

fn haversine_km(lat1: f64, lon1: f64, lat2: f64, lon2: f64) -> f64 {
    const R: f64 = 6371.0;
    let (lat1, lon1, lat2, lon2) = (
        lat1.to_radians(),
        lon1.to_radians(),
        lat2.to_radians(),
        lon2.to_radians(),
    );
    let dlat = lat2 - lat1;
    let dlon = lon2 - lon1;
    let a = (dlat / 2.0).sin().powi(2) + lat1.cos() * lat2.cos() * (dlon / 2.0).sin().powi(2);
    2.0 * R * a.sqrt().asin()
}

/// Rough fallback when Nominatim is unavailable.
fn guess_country_code(lat: f64, lon: f64) -> String {
    // Very coarse continental boxes — good enough to pick a map feed.
    if (41.0..=83.0).contains(&lat) && (-141.0..=-52.0).contains(&lon) {
        return "CA".into();
    }
    if (24.0..=50.0).contains(&lat) && (-125.0..=-66.0).contains(&lon) {
        return "US".into();
    }
    if (41.0..=51.5).contains(&lat) && (-5.5..=10.0).contains(&lon) {
        return "FR".into();
    }
    if (49.0..=61.0).contains(&lat) && (-8.5..=2.0).contains(&lon) {
        return "GB".into();
    }
    if (47.0..=55.5).contains(&lat) && (5.5..=15.5).contains(&lon) {
        return "DE".into();
    }
    "FR".into()
}

fn slug_candidates(word: &str) -> Vec<String> {
    let base = slugify(word);
    let mut out = vec![base.clone()];

    // Drop common words that often aren't in slugs.
    let cleaned = word
        .split_whitespace()
        .filter(|w| {
            !matches!(
                w.to_ascii_lowercase().as_str(),
                "mosque" | "mosquee" | "mosquée" | "masjid" | "the" | "of" | "de" | "la" | "le"
            )
        })
        .collect::<Vec<_>>()
        .join(" ");
    let cleaned_slug = slugify(&cleaned);
    if !cleaned_slug.is_empty() && cleaned_slug != base {
        out.push(cleaned_slug);
    }

    // First token only / last token only
    let tokens: Vec<&str> = cleaned.split_whitespace().collect();
    if tokens.len() >= 2 {
        out.push(slugify(tokens[0]));
        out.push(slugify(tokens[tokens.len() - 1]));
        out.push(slugify(&format!("{}-{}", tokens[0], tokens[tokens.len() - 1])));
    }

    out.retain(|s| s.len() >= 3);
    out
}

fn slugify(s: &str) -> String {
    let s = s.trim().to_lowercase();
    let mut out = String::new();
    let mut prev_dash = false;
    for c in s.chars() {
        let c = match c {
            'à' | 'á' | 'â' | 'ä' | 'ã' => 'a',
            'è' | 'é' | 'ê' | 'ë' => 'e',
            'ì' | 'í' | 'î' | 'ï' => 'i',
            'ò' | 'ó' | 'ô' | 'ö' | 'õ' => 'o',
            'ù' | 'ú' | 'û' | 'ü' => 'u',
            'ç' => 'c',
            'ñ' => 'n',
            other => other,
        };
        if c.is_ascii_alphanumeric() {
            out.push(c);
            prev_dash = false;
        } else if !prev_dash && !out.is_empty() {
            out.push('-');
            prev_dash = true;
        }
    }
    while out.ends_with('-') {
        out.pop();
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn extracts_slug_from_url() {
        assert_eq!(
            extract_slug_from_url("https://mawaqit.net/en/assalam-argenteuil").as_deref(),
            Some("assalam-argenteuil")
        );
        assert_eq!(
            extract_slug_from_url("https://www.mawaqit.net/fr/assalam-argenteuil?x=1").as_deref(),
            Some("assalam-argenteuil")
        );
        assert_eq!(
            extract_slug_from_url("mawaqit.net/en/foo-bar").as_deref(),
            Some("foo-bar")
        );
        // Mobile site inserts `/m` — must not become the slug.
        assert_eq!(
            extract_slug_from_url("https://mawaqit.net/m/en/al-andalous-mtl").as_deref(),
            Some("al-andalous-mtl")
        );
        assert_eq!(
            extract_slug_from_url("https://mawaqit.net/m/al-andalous-mtl").as_deref(),
            Some("al-andalous-mtl")
        );
    }

    #[test]
    fn slugify_basic() {
        assert_eq!(slugify("Grande Mosquée de Paris"), "grande-mosquee-de-paris");
    }

    #[test]
    #[ignore = "network"]
    fn live_resolve() {
        let client = MawaqitClient::new("http://127.0.0.1:8000").unwrap();
        let r = client
            .resolve_mosque("https://mawaqit.net/en/assalam-argenteuil")
            .unwrap();
        assert!(!r.times.fajr.is_empty());
        assert!(!r.name.is_empty());
    }
}

fn urlencoding_simple(s: &str) -> String {
    let mut out = String::with_capacity(s.len());
    for b in s.bytes() {
        match b {
            b'A'..=b'Z' | b'a'..=b'z' | b'0'..=b'9' | b'-' | b'_' | b'.' | b'~' => {
                out.push(b as char);
            }
            b' ' => out.push('+'),
            _ => {
                out.push('%');
                out.push(char::from(b"0123456789ABCDEF"[(b >> 4) as usize]));
                out.push(char::from(b"0123456789ABCDEF"[(b & 0xf) as usize]));
            }
        }
    }
    out
}

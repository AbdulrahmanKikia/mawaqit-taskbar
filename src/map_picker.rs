//! Chromium (WebView2) mosque map picker.
//!
//! Shows Mawaqit mosque **names** on a Leaflet map. Selecting a name returns
//! the Mawaqit slug so prayer times can be fetched.

use std::borrow::Cow;

use serde::Deserialize;
use serde_json::json;
use tao::{
    dpi::LogicalSize,
    event::{Event, WindowEvent},
    event_loop::{ControlFlow, EventLoopBuilder},
    window::WindowBuilder,
};
#[cfg(target_os = "windows")]
use tao::platform::run_return::EventLoopExtRunReturn;
#[cfg(target_os = "windows")]
use tao::platform::windows::EventLoopBuilderExtWindows;
use wry::{
    http::{header::CONTENT_TYPE, Request, Response, StatusCode},
    WebViewBuilder,
};

use crate::mawaqit::{MawaqitClient, MosqueHit};

const MAP_HTML: &str = include_str!("../assets/mosque_map.html");
const MAP_JS: &str = include_str!("../assets/mosque_map.js");
const LEAFLET_CSS: &str = include_str!("../assets/leaflet/leaflet.css");
const LEAFLET_JS: &str = include_str!("../assets/leaflet/leaflet.js");

#[derive(Debug, Deserialize)]
#[serde(tag = "type")]
enum IpcMsg {
    #[serde(rename = "ready")]
    Ready,
    #[serde(rename = "nearby")]
    Nearby {
        lat: f64,
        lon: f64,
        #[serde(default)]
        north: Option<f64>,
        #[serde(default)]
        south: Option<f64>,
        #[serde(default)]
        east: Option<f64>,
        #[serde(default)]
        west: Option<f64>,
    },
    #[serde(rename = "search")]
    Search { word: String },
    #[serde(rename = "select")]
    Select { slug: String, label: String },
}

enum UserEvent {
    Ipc(String),
}

/// Opens a WebView2 map window and invokes `on_done` with the selection (or `None` if closed).
///
/// Runs a nested GUI event loop on the calling thread — spawn a thread before calling.
pub fn run(on_done: impl FnOnce(Option<MosqueHit>) + 'static) {
    let mut builder = EventLoopBuilder::<UserEvent>::with_user_event();
    #[cfg(target_os = "windows")]
    builder.with_any_thread(true);
    // mut: run_return (not run — run() calls process::exit and kills the widget).
    let mut event_loop = builder.build();
    let proxy = event_loop.create_proxy();

    let window = match WindowBuilder::new()
        .with_title("Mawaqit — Find mosque")
        .with_inner_size(LogicalSize::new(1040.0, 700.0))
        .with_min_inner_size(LogicalSize::new(860.0, 560.0))
        .with_visible(true)
        .with_focused(true)
        .build(&event_loop)
    {
        Ok(w) => w,
        Err(e) => {
            eprintln!("map picker window failed: {e}");
            on_done(None);
            return;
        }
    };
    window.set_focus();

    let proxy_ipc = proxy.clone();
    let webview = match WebViewBuilder::new()
        .with_custom_protocol("mawaqit".into(), move |_id, request| {
            match protocol_response(request) {
                Ok(r) => r.map(Into::into),
                Err(e) => Response::builder()
                    .status(StatusCode::INTERNAL_SERVER_ERROR)
                    .header(CONTENT_TYPE, "text/plain")
                    .body(Cow::Owned(e.into_bytes()))
                    .unwrap()
                    .map(Into::into),
            }
        })
        .with_url("http://mawaqit.localhost/")
        .with_ipc_handler(move |req| {
            let body = req.into_body();
            let _ = proxy_ipc.send_event(UserEvent::Ipc(body));
        })
        .build(&window)
    {
        Ok(v) => v,
        Err(e) => {
            eprintln!("map picker webview failed: {e}");
            on_done(None);
            return;
        }
    };

    let client = match MawaqitClient::new("https://mawaqit.net") {
        Ok(c) => c,
        Err(e) => {
            eprintln!("map picker client failed: {e}");
            on_done(None);
            return;
        }
    };

    let mut callback = Some(on_done);

    let _ = event_loop.run_return(move |event, _elwt, control_flow| {
        *control_flow = ControlFlow::Wait;

        match event {
            Event::UserEvent(UserEvent::Ipc(raw)) => {
                let msg: IpcMsg = match serde_json::from_str(&raw) {
                    Ok(m) => m,
                    Err(_) => return,
                };
                match msg {
                    IpcMsg::Ready => {}
                    IpcMsg::Nearby {
                        lat,
                        lon,
                        north,
                        south,
                        east,
                        west,
                    } => {
                        let bounds = match (north, south, east, west) {
                            (Some(n), Some(s), Some(e), Some(w)) => Some((n, s, e, w)),
                            _ => None,
                        };
                        match client.search_in_view(lat, lon, bounds) {
                            Ok(hits) => push_hits(
                                &webview,
                                &hits,
                                &format!("{} mosque name(s) in view", hits.len()),
                            ),
                            Err(e) => push_error(&webview, &e.to_string()),
                        }
                    }
                    IpcMsg::Search { word } => match client.search_mosques(&word) {
                        Ok(hits) => push_hits(
                            &webview,
                            &hits,
                            &format!("{} match(es) for “{word}”", hits.len()),
                        ),
                        Err(e) => push_error(&webview, &e.to_string()),
                    },
                    IpcMsg::Select { slug, label } => {
                        if let Some(cb) = callback.take() {
                            cb(Some(MosqueHit {
                                slug,
                                label,
                                lat: None,
                                lon: None,
                            }));
                        }
                        *control_flow = ControlFlow::Exit;
                    }
                }
            }
            Event::WindowEvent {
                event: WindowEvent::CloseRequested,
                ..
            } => {
                if let Some(cb) = callback.take() {
                    cb(None);
                }
                *control_flow = ControlFlow::Exit;
            }
            Event::LoopDestroyed => {
                if let Some(cb) = callback.take() {
                    cb(None);
                }
            }
            _ => {}
        }
    });
}

fn protocol_response(
    request: Request<Vec<u8>>,
) -> Result<Response<Cow<'static, [u8]>>, String> {
    let path = request.uri().path();
    let (body, mime): (&'static [u8], &str) = match path {
        "/" | "/index.html" | "" => (MAP_HTML.as_bytes(), "text/html; charset=utf-8"),
        "/app.js" => (MAP_JS.as_bytes(), "text/javascript; charset=utf-8"),
        "/leaflet.css" => (LEAFLET_CSS.as_bytes(), "text/css; charset=utf-8"),
        "/leaflet.js" => (LEAFLET_JS.as_bytes(), "text/javascript; charset=utf-8"),
        _ => {
            return Ok(asset_response(
                StatusCode::NOT_FOUND,
                "text/plain",
                b"not found",
            ));
        }
    };
    Ok(asset_response(StatusCode::OK, mime, body))
}

fn asset_response(
    status: StatusCode,
    mime: &str,
    body: &'static [u8],
) -> Response<Cow<'static, [u8]>> {
    // WebView2 custom-protocol responses hang without Content-Length.
    Response::builder()
        .status(status)
        .header(CONTENT_TYPE, mime)
        .header("Content-Length", body.len().to_string())
        .body(Cow::Borrowed(body))
        .unwrap()
}

fn push_hits(webview: &wry::WebView, hits: &[MosqueHit], message: &str) {
    let list: Vec<serde_json::Value> = hits
        .iter()
        .map(|h| {
            json!({
                "slug": h.slug,
                "label": h.label,
                "lat": h.lat,
                "lon": h.lon,
            })
        })
        .collect();
    let payload = json!(list).to_string();
    let msg = serde_json::to_string(message).unwrap_or_else(|_| "\"\"".into());
    let script = format!("window.__setMosques({payload}, {msg});");
    let _ = webview.evaluate_script(&script);
}

fn push_error(webview: &wry::WebView, message: &str) {
    let msg = serde_json::to_string(message).unwrap_or_else(|_| "\"error\"".into());
    let script = format!("window.__setError({msg});");
    let _ = webview.evaluate_script(&script);
}

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
    WebContext, WebViewBuilder,
};

use crate::config;
use crate::mawaqit::{MawaqitClient, MosqueHit};
use crate::webview2_rt;

/// Result of picking a mosque on the map.
#[derive(Debug, Clone)]
pub struct MapPick {
    pub slug: String,
    pub label: String,
    pub favorite: bool,
}

#[cfg(target_os = "windows")]
fn init_com_sta() {
    use windows::Win32::System::Com::{CoInitializeEx, COINIT_APARTMENTTHREADED};
    unsafe {
        let _ = CoInitializeEx(None, COINIT_APARTMENTTHREADED);
    }
}

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
    Select {
        slug: String,
        label: String,
        #[serde(default)]
        favorite: bool,
    },
}

enum UserEvent {
    Ipc(String),
}

/// Opens a WebView2 map window and invokes `on_done` with the selection (or `None` if closed).
///
/// Runs a nested GUI event loop on the calling thread — spawn a thread before calling.
/// Returns `Err` if the window/WebView2 could not be created (caller should show a fallback UI).
pub fn run(on_done: impl FnOnce(Option<MapPick>) + 'static) -> Result<(), String> {
    #[cfg(target_os = "windows")]
    init_com_sta();
    webview2_rt::prepare();

    let mut builder = EventLoopBuilder::<UserEvent>::with_user_event();
    #[cfg(target_os = "windows")]
    builder.with_any_thread(true);
    // mut: run_return (not run — run() calls process::exit and kills the widget).
    let mut event_loop = builder.build();
    let proxy = event_loop.create_proxy();

    let window = WindowBuilder::new()
        .with_title("Mawaqit — Find mosque")
        .with_inner_size(LogicalSize::new(1040.0, 700.0))
        .with_min_inner_size(LogicalSize::new(860.0, 560.0))
        .with_visible(true)
        .with_focused(true)
        .with_always_on_top(true)
        .build(&event_loop)
        .map_err(|e| format!("Could not create map window: {e}"))?;
    window.set_focus();

    let mut web_context = WebContext::new(config::webview_data_dir());

    let proxy_ipc = proxy.clone();
    let build_wv = |ctx: Option<&mut WebContext>| {
        let mut b = match ctx {
            Some(c) => WebViewBuilder::with_web_context(c),
            None => WebViewBuilder::new(),
        };
        let proxy_ipc = proxy_ipc.clone();
        b = b
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
            });
        b.build(&window)
    };

    let webview = match build_wv(Some(&mut web_context)) {
        Ok(v) => v,
        Err(_first) => match build_wv(None) {
            Ok(v) => v,
            Err(_second) => {
                webview2_rt::wipe_user_data();
                let mut fresh = WebContext::new(config::webview_data_dir());
                build_wv(Some(&mut fresh)).or_else(|_| build_wv(None)).map_err(|e| {
                    format!("Could not start the mosque map (WebView2). Details: {e}")
                })?
            }
        },
    };
    let _ = window.set_always_on_top(false);

    let client = MawaqitClient::new("https://mawaqit.net")
        .map_err(|e| format!("Could not start HTTP client: {e}"))?;

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
                                false,
                            ),
                            Err(e) => push_error(&webview, &e.to_string()),
                        }
                    }
                    IpcMsg::Search { word } => match client.search_mosques(&word) {
                        Ok(hits) => push_hits(
                            &webview,
                            &hits,
                            &format!("{} match(es) for “{word}”", hits.len()),
                            true,
                        ),
                        Err(e) => push_error(&webview, &e.to_string()),
                    },
                    IpcMsg::Select {
                        slug,
                        label,
                        favorite,
                    } => {
                        if let Some(cb) = callback.take() {
                            cb(Some(MapPick {
                                slug,
                                label,
                                favorite,
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
    Ok(())
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

fn push_hits(webview: &wry::WebView, hits: &[MosqueHit], message: &str, fit: bool) {
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
    let script = format!(
        "window.__setMosques({payload}, {msg}, {});",
        if fit { "true" } else { "false" }
    );
    let _ = webview.evaluate_script(&script);
}

fn push_error(webview: &wry::WebView, message: &str) {
    let msg = serde_json::to_string(message).unwrap_or_else(|_| "\"error\"".into());
    let script = format!("window.__setError({msg});");
    let _ = webview.evaluate_script(&script);
}

/// Opens the same map in the default browser when WebView2 cannot start.
pub fn run_in_browser(on_done: impl FnOnce(Option<MapPick>) + 'static) -> Result<(), String> {
    use std::net::{TcpListener, TcpStream};
    use std::sync::atomic::{AtomicBool, Ordering};
    use std::sync::{mpsc, Arc, Mutex};
    use std::thread;
    use std::time::Duration;

    let listener = TcpListener::bind("127.0.0.1:0")
        .map_err(|e| format!("Could not start the map server: {e}"))?;
    let addr = listener
        .local_addr()
        .map_err(|e| format!("Could not start the map server: {e}"))?;
    let url = format!("http://{addr}/");

    let client = Arc::new(
        MawaqitClient::new("https://mawaqit.net")
            .map_err(|e| format!("Could not start HTTP client: {e}"))?,
    );
    let (tx, rx) = mpsc::sync_channel::<Option<MapPick>>(1);
    let tx = Arc::new(Mutex::new(Some(tx)));
    let stop = Arc::new(AtomicBool::new(false));

    let client_srv = Arc::clone(&client);
    let tx_srv = Arc::clone(&tx);
    let stop_srv = Arc::clone(&stop);
    let server = thread::spawn(move || {
        for stream in listener.incoming() {
            if stop_srv.load(Ordering::SeqCst) {
                break;
            }
            let Ok(stream) = stream else {
                continue;
            };
            let client = Arc::clone(&client_srv);
            let tx = Arc::clone(&tx_srv);
            let stop = Arc::clone(&stop_srv);
            thread::spawn(move || handle_http(stream, &client, &tx, &stop));
        }
    });

    open_browser(&url)?;

    #[cfg(windows)]
    unsafe {
        use windows::core::w;
        use windows::Win32::Foundation::HWND;
        use windows::Win32::UI::WindowsAndMessaging::{
            MessageBoxW, MB_ICONINFORMATION, MB_OK,
        };
        let _ = MessageBoxW(
            HWND::default(),
            w!("The mosque map opened in your browser.\n\nPick a mosque, then click Use selected or Set as favorite. Click OK here when you’re done."),
            w!("Mawaqit map"),
            MB_OK | MB_ICONINFORMATION,
        );
    }

    finish_browser_pick(&tx, None);
    stop.store(true, Ordering::SeqCst);
    let _ = TcpStream::connect_timeout(&addr, Duration::from_millis(400));
    let _ = server.join();
    let hit = rx.try_recv().ok().flatten();
    on_done(hit);
    Ok(())
}

fn hits_payload(hits: &[MosqueHit], message: &str, fit: bool) -> serde_json::Value {
    json!({
        "mosques": hits.iter().map(|h| json!({
            "slug": h.slug,
            "label": h.label,
            "lat": h.lat,
            "lon": h.lon,
        })).collect::<Vec<_>>(),
        "message": message,
        "fit": fit,
    })
}

fn handle_browser_ipc(
    client: &MawaqitClient,
    raw: &str,
) -> (serde_json::Value, Option<Option<MapPick>>) {
    let msg: IpcMsg = match serde_json::from_str(raw) {
        Ok(m) => m,
        Err(_) => return (json!({ "error": "bad request" }), None),
    };
    match msg {
        IpcMsg::Ready => (json!({ "ok": true }), None),
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
                Ok(hits) => (
                    hits_payload(&hits, &format!("{} mosque name(s) in view", hits.len()), false),
                    None,
                ),
                Err(e) => (json!({ "error": e.to_string() }), None),
            }
        }
        IpcMsg::Search { word } => match client.search_mosques(&word) {
            Ok(hits) => (
                hits_payload(&hits, &format!("{} match(es) for “{word}”", hits.len()), true),
                None,
            ),
            Err(e) => (json!({ "error": e.to_string() }), None),
        },
        IpcMsg::Select {
            slug,
            label,
            favorite,
        } => (
            json!({ "ok": "selected" }),
            Some(Some(MapPick {
                slug,
                label,
                favorite,
            })),
        ),
    }
}

fn handle_http(
    mut stream: std::net::TcpStream,
    client: &MawaqitClient,
    tx: &std::sync::Mutex<Option<std::sync::mpsc::SyncSender<Option<MapPick>>>>,
    stop: &std::sync::atomic::AtomicBool,
) {
    use std::io::{Read, Write};
    use std::sync::atomic::Ordering;
    use std::time::Duration;

    let _ = stream.set_read_timeout(Some(Duration::from_secs(20)));
    let mut buf = Vec::new();
    let mut tmp = [0u8; 4096];
    let header_end = loop {
        match stream.read(&mut tmp) {
            Ok(0) => return,
            Ok(n) => buf.extend_from_slice(&tmp[..n]),
            Err(_) => return,
        }
        if let Some(pos) = buf.windows(4).position(|w| w == b"\r\n\r\n") {
            break pos + 4;
        }
        if buf.len() > 64 * 1024 {
            return;
        }
    };
    let headers = String::from_utf8_lossy(&buf[..header_end]).into_owned();
    let mut lines = headers.split("\r\n");
    let req = lines.next().unwrap_or("");
    let mut parts = req.split_whitespace();
    let method = parts.next().unwrap_or("").to_ascii_uppercase();
    let path = parts.next().unwrap_or("/");
    let mut content_len = 0usize;
    for line in lines {
        if let Some(v) = line.to_ascii_lowercase().strip_prefix("content-length:") {
            content_len = v.trim().parse().unwrap_or(0);
        }
    }
    let mut body = buf[header_end..].to_vec();
    while body.len() < content_len {
        match stream.read(&mut tmp) {
            Ok(0) => break,
            Ok(n) => body.extend_from_slice(&tmp[..n]),
            Err(_) => break,
        }
    }
    body.truncate(content_len);

    let status_line = |code: u16, reason: &str, mime: &str, body: &[u8]| {
        format!(
            "HTTP/1.1 {code} {reason}\r\nContent-Type: {mime}\r\nContent-Length: {}\r\nConnection: close\r\n\r\n",
            body.len()
        )
    };
    let mut send = |code: u16, reason: &str, mime: &str, body: &[u8]| {
        let head = status_line(code, reason, mime, body);
        let _ = stream.write_all(head.as_bytes());
        if !body.is_empty() {
            let _ = stream.write_all(body);
        }
        let _ = stream.flush();
    };

    if method == "POST" && path == "/ipc" {
        let raw = String::from_utf8_lossy(&body);
        let (val, done) = handle_browser_ipc(client, &raw);
        if let Some(hit) = done {
            finish_browser_pick(tx, hit);
            stop.store(true, Ordering::SeqCst);
        }
        let bytes = val.to_string().into_bytes();
        send(200, "OK", "application/json; charset=utf-8", &bytes);
        return;
    }

    if method == "GET" || method == "HEAD" {
        let (body, mime): (&[u8], &str) = match path {
            "/" | "/index.html" => (MAP_HTML.as_bytes(), "text/html; charset=utf-8"),
            "/app.js" => (MAP_JS.as_bytes(), "text/javascript; charset=utf-8"),
            "/leaflet.css" => (LEAFLET_CSS.as_bytes(), "text/css; charset=utf-8"),
            "/leaflet.js" => (LEAFLET_JS.as_bytes(), "text/javascript; charset=utf-8"),
            _ => {
                send(404, "Not Found", "text/plain", b"not found");
                return;
            }
        };
        if method == "HEAD" {
            send(200, "OK", mime, b"");
        } else {
            send(200, "OK", mime, body);
        }
        return;
    }

    send(405, "Method Not Allowed", "text/plain", b"method");
}

fn finish_browser_pick(
    tx: &std::sync::Mutex<Option<std::sync::mpsc::SyncSender<Option<MapPick>>>>,
    hit: Option<MapPick>,
) {
    if let Ok(mut slot) = tx.lock() {
        if let Some(sender) = slot.take() {
            let _ = sender.send(hit);
        }
    }
}

fn open_browser(url: &str) -> Result<(), String> {
    #[cfg(windows)]
    {
        use windows::core::{w, HSTRING, PCWSTR};
        use windows::Win32::Foundation::HWND;
        use windows::Win32::UI::Shell::ShellExecuteW;
        use windows::Win32::UI::WindowsAndMessaging::SW_SHOWNORMAL;
        let wide = HSTRING::from(url);
        let rc = unsafe {
            ShellExecuteW(
                HWND::default(),
                w!("open"),
                PCWSTR(wide.as_ptr()),
                PCWSTR::null(),
                PCWSTR::null(),
                SW_SHOWNORMAL,
            )
        };
        if (rc.0 as isize) <= 32 {
            return Err(format!("Could not open the browser (code {}).", rc.0 as isize));
        }
        Ok(())
    }
    #[cfg(not(windows))]
    {
        let _ = url;
        Err("browser map is only available on Windows".into())
    }
}

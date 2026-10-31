const REPO = "AbdulrahmanKikia/mawaqit-taskbar";
const FALLBACK = `https://github.com/${REPO}/releases/latest`;

function setHref(id, url) {
  const el = document.getElementById(id);
  if (el && url) el.setAttribute("href", url);
}

function mb(bytes) {
  if (!bytes) return "";
  return `${(bytes / 1048576).toFixed(1)} MB`;
}

async function bindRelease() {
  const meta = document.getElementById("releaseMeta");
  try {
    const res = await fetch(`https://api.github.com/repos/${REPO}/releases/latest`, {
      headers: { Accept: "application/vnd.github+json" }
    });
    if (!res.ok) throw new Error(String(res.status));
    const rel = await res.json();
    const setup = rel.assets?.find((a) => /Setup-.*\.exe$/i.test(a.name));
    const zip = rel.assets?.find((a) => /Portable-.*\.zip$/i.test(a.name));
    const setupUrl = setup?.browser_download_url || FALLBACK;
    const zipUrl = zip?.browser_download_url || FALLBACK;
    ["btnSetup", "btnSetup2"].forEach((id) => setHref(id, setupUrl));
    ["btnZip", "btnZip2"].forEach((id) => setHref(id, zipUrl));
    const tag = rel.tag_name || "latest";
    const name = setup?.name || "MawaqitWidget-Setup.exe";
    const size = mb(setup?.size);
    if (meta) meta.textContent = size ? `${tag}  ·  ${name}  ·  ${size}` : `${tag}  ·  ${name}`;
  } catch {
    if (meta) meta.textContent = "Open GitHub Releases if the file does not start.";
  }
}

function pad(n) {
  return String(n).padStart(2, "0");
}

function nextDemo() {
  const now = new Date();
  const target = new Date(now);
  target.setHours(5, 12, 0, 0);
  if (target <= now) target.setDate(target.getDate() + 1);
  return target;
}

function formatRemain(ms) {
  const total = Math.max(0, Math.floor(ms / 1000));
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  return h > 0 ? `${h}:${pad(m)}:${pad(s)}` : `${pad(m)}:${pad(s)}`;
}

function tick() {
  const remain = formatRemain(nextDemo() - Date.now());
  document.querySelectorAll(".tick").forEach((el) => {
    el.textContent = remain;
  });
}

function setMode(mode) {
  const desktop = document.getElementById("desktop");
  const note = document.getElementById("stageNote");
  desktop.dataset.mode = mode;
  document.getElementById("tabInject").setAttribute("aria-selected", String(mode === "inject"));
  document.getElementById("tabStrip").setAttribute("aria-selected", String(mode === "strip"));
  const strip = document.getElementById("mockStrip");
  if (strip) strip.hidden = mode !== "strip";
  if (note) {
    note.textContent = mode === "strip"
      ? "The strip reserves desktop space. Mosque name sits on the right. Click the chip for the schedule."
      : "Expanded chip pins to the left of the taskbar. Click it for today’s adhan and iqama.";
  }
}

function toggleFlyout(force) {
  const flyout = document.getElementById("flyout");
  if (!flyout) return;
  const open = force === undefined ? flyout.hidden : force;
  flyout.hidden = !open;
}

document.querySelectorAll(".switch button").forEach((btn) => {
  btn.addEventListener("click", () => setMode(btn.dataset.mode));
});

["barChip", "stripChip"].forEach((id) => {
  const el = document.getElementById(id);
  if (el) el.addEventListener("click", (e) => {
    e.stopPropagation();
    toggleFlyout();
  });
});

document.getElementById("desktop")?.addEventListener("click", () => toggleFlyout(false));
document.getElementById("flyout")?.addEventListener("click", (e) => e.stopPropagation());

tick();
setInterval(tick, 1000);
bindRelease();

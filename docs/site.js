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
    const notes = document.getElementById("releaseNotesLink");
    if (notes && rel.html_url) notes.setAttribute("href", rel.html_url);
    const ver = document.getElementById("reportVersion");
    if (ver && !ver.value && tag.startsWith("v")) ver.placeholder = `e.g. ${tag.slice(1)}`;
  } catch {
    if (meta) meta.textContent = "Open GitHub Releases if the file does not start.";
  }
}

const KIND_LABEL = {
  bug: "Bug / crash",
  "wrong-times": "Wrong prayer times",
  install: "Install / update / SmartScreen",
  ui: "Chip, flyout, or map UI",
  idea: "Idea / feedback",
};

function bindReportForm() {
  const form = document.getElementById("reportForm");
  const note = document.getElementById("reportNote");
  if (!form) return;
  form.addEventListener("submit", (e) => {
    e.preventDefault();
    const kind = document.getElementById("reportKind")?.value || "bug";
    const version = (document.getElementById("reportVersion")?.value || "").trim();
    const summary = (document.getElementById("reportSummary")?.value || "").trim();
    const details = (document.getElementById("reportDetails")?.value || "").trim();
    if (!summary || !details) {
      if (note) {
        note.hidden = false;
        note.textContent = "Add a short summary and a few details before opening GitHub.";
      }
      return;
    }
    if (note) note.hidden = true;
    const title = `[${KIND_LABEL[kind] || kind}] ${summary}`.slice(0, 200);
    const body = [
      `**Kind:** ${KIND_LABEL[kind] || kind}`,
      `**App version:** ${version || "(not provided)"}`,
      "",
      "## What happened",
      details,
      "",
      "---",
      "_Submitted from the product site report form._",
    ].join("\n");
    const url =
      `https://github.com/${REPO}/issues/new` +
      `?title=${encodeURIComponent(title)}` +
      `&body=${encodeURIComponent(body)}`;
    window.open(url, "_blank", "noopener,noreferrer");
  });
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
bindReportForm();

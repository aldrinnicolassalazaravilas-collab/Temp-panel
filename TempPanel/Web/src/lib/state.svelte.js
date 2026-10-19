// Estado de la interfaz + protocolo con la app WPF (Svelte 5 runes).
// Un único objeto reactivo `app`: cualquier componente lo lee y se refresca solo.
import { post, onMessage } from "./bridge.js";
import { clamp, clamp01, mixHex, num } from "./format.js";

export const TITLES = {
  resumen: "Resumen", cpu: "CPU", gpu: "GPU", ram: "Memoria RAM", bateria: "Batería",
  refrigeracion: "Refrigeración", sensores: "Todos los sensores", historial: "Historial",
  ayuda: "Ayuda", config: "Configuración"
};

const MAXH = 900;                       // 30 min a 2 s
const TEMP_HYST = 3;                    // °C por debajo del umbral para dar por superado el aviso
const TEMP_COOLDOWN_MS = 10 * 60 * 1000; // 10 min entre avisos térmicos

export const app = $state({
  cfg: {
    refreshMs: 2000, cpuWarnTemp: 85, cpuCritTemp: 95, gpuWarnTemp: 80, gpuCritTemp: 90,
    preferredCpuSensor: "auto", unit: "C", startMinimized: false, startWithWindows: false,
    glass: false, batteryEnabled: true, batteryThreshold: 80, batteryRearm: 75, batterySound: true,
    batteryMode: "popup", batteryFull: true, version: "3.0.0", autoCheckUpdates: false, firstRun: false,
    miniSeries: "cpu,temp,bat,ram,gpu", miniExpanded: false,
    // v3.0.0 — integración con Windows y rendimiento adaptativo.
    adaptivePerf: true, hotkeyEnabled: true, taskbarBadge: true,
    effectiveMs: 2000, perf: "normal",
    os: {
      name: "Windows", build: 0, win11: false, accent: "#4f8cff", theme: "dark",
      highContrast: false, powerSaver: false, hotkey: false, glass: false
    }
  },
  snap: null,
  page: "resumen",
  paused: false,
  immersive: false,
  osHover: "",
  conn: { cls: "waiting", text: "Conectando…" },
  lastRead: "Última lectura: —",
  alertCls: "", alertText: "", health: "ok", warns: [],
  update: { text: "", latest: null, url: null, showDl: false, showRetry: false },
  defender: null, hvciText: "",
  filter: "", sortKey: "hardware", sortDir: 1,
  history: [], toasts: [],
  uiAlpha: 0.26,
  aurora: { a: "#4f8cff", b: "#22d3ee", c: "#a78bfa" }
});

// ---------- Comandos hacia la app nativa (único lugar con el protocolo) ----------
export const cmd = {
  view: (page) => post({ cmd: "view", page }),
  winMin: () => post({ cmd: "winMin" }),
  winMax: () => post({ cmd: "winMax" }),
  winClose: () => post({ cmd: "winClose" }),
  getSnapshot: () => post({ cmd: "getSnapshot" }),
  openMini: () => post({ cmd: "openMini" }),
  resetMiniPos: () => post({ cmd: "resetMiniPos" }),
  setFirstRunDone: () => post({ cmd: "setFirstRunDone" }),
  setRefresh: (ms) => post({ cmd: "setRefresh", ms }),
  setUnit: (unit) => post({ cmd: "setUnit", unit }),
  setCpuSensor: (id) => post({ cmd: "setCpuSensor", id }),
  setThresholds: (t) => post({ cmd: "setThresholds", cpuWarn: t.cpuWarn, cpuCrit: t.cpuCrit, gpuWarn: t.gpuWarn, gpuCrit: t.gpuCrit }),
  setMiniOptions: (series, expanded) => post({ cmd: "setMiniOptions", series, expanded }),
  setBatteryAlert: (b) => post({
    cmd: "setBatteryAlert", enabled: b.enabled, threshold: b.threshold, rearm: b.rearm,
    sound: b.sound, mode: b.mode, full: b.full
  }),
  testBatteryAlert: () => post({ cmd: "testBatteryAlert" }),
  setStartMinimized: (value) => post({ cmd: "setStartMinimized", value }),
  setStartWithWindows: (value) => post({ cmd: "setStartWithWindows", value }),
  setAutoUpdates: (value) => post({ cmd: "setAutoUpdates", value }),
  // v3.0.0 — integración con Windows
  setHotkey: (value) => post({ cmd: "setHotkey", value }),
  setTaskbarBadge: (value) => post({ cmd: "setTaskbarBadge", value }),
  setAdaptivePerf: (value) => post({ cmd: "setAdaptivePerf", value }),
  health: (cls) => post({ cmd: "health", cls }),
  openWindowsSettings: (key) => post({ cmd: "openWindowsSettings", key }),
  resetSettings: () => post({ cmd: "resetSettings" }),
  openLogs: () => post({ cmd: "openLogs" }),
  restartAdmin: () => post({ cmd: "restartAdmin" }),
  checkUpdates: () => post({ cmd: "checkUpdates" }),
  downloadUpdate: (url, tag) => post({ cmd: "downloadUpdate", url, tag }),
  retryLaunch: () => post({ cmd: "retryLaunch" }),
  setHvci: (enabled) => post({ cmd: "setHvci", enabled }),
  rebootNow: () => post({ cmd: "rebootNow" }),
  defenderStatus: () => post({ cmd: "defenderStatus" }),
  defenderAdd: () => post({ cmd: "defenderAdd" }),
  defenderRemove: () => post({ cmd: "defenderRemove" })
};

// ---------- Avisos flotantes ----------
let toastSeq = 0;
export function toast(text, kind = "") {
  const id = ++toastSeq;
  app.toasts.push({ id, text, kind });
  setTimeout(() => {
    const i = app.toasts.findIndex((t) => t.id === id);
    if (i >= 0) app.toasts.splice(i, 1);
  }, 3400);
}

// ---------- Navegación ----------
export function setPage(page) {
  app.page = page;
  // La app nativa solo construye la tabla completa de sensores cuando esta página está visible.
  cmd.view(page);
  if (page === "ayuda") cmd.defenderStatus();
}

export function togglePause() {
  app.paused = !app.paused;
}

export function toggleImmersive() {
  app.immersive = !app.immersive;
  saveUiPrefs();
}

// ---------- Historial (solo en memoria) ----------
export function pushHist(s) {
  app.history.push({
    t: new Date(s.timestampLocal && !isNaN(Date.parse(s.timestampLocal)) ? s.timestampLocal : (s.timestamp || Date.now())),
    cpuTemp: s.cpu?.packageTemp ?? null,
    cpuLoad: s.cpu?.totalLoad ?? null,
    ramUsed: s.memory?.usedMb ?? null,
    gpuTemp: s.gpu?.temp ?? null,
    batPct: (s.battery?.present ? s.battery?.percent : null) ?? null
  });
  if (app.history.length > MAXH) app.history.splice(0, app.history.length - MAXH);
}

export function seriesOf(key, rangeS) {
  const cut = Date.now() - rangeS * 1000;
  return app.history.filter((p) => p.t.getTime() >= cut).map((p) => p[key]);
}
export function timesOf(rangeS) {
  const cut = Date.now() - rangeS * 1000;
  return app.history.filter((p) => p.t.getTime() >= cut).map((p) => p.t);
}
export function clearHistory() {
  app.history.length = 0;
}

/** Tendencia medida (°/min) sobre el historial real; null si aún no hay datos. */
export function slopePerMin(key, rangeS, convert) {
  const cut = Date.now() - rangeS * 1000;
  const pts = app.history.filter((p) => p.t.getTime() >= cut && p[key] != null);
  if (pts.length < 3) return null;
  const t0 = pts[0].t.getTime(), t1 = pts[pts.length - 1].t.getTime();
  if (t1 - t0 < 15000) return null;
  const v0 = convert ? convert(pts[0][key]) : pts[0][key];
  const v1 = convert ? convert(pts[pts.length - 1][key]) : pts[pts.length - 1][key];
  return (v1 - v0) / ((t1 - t0) / 60000);
}

// ---------- Alertas térmicas (informativas, con histéresis y enfriamiento) ----------
let tempAlertState = { cpuWarn: false, cpuCrit: false, gpuWarn: false, gpuCrit: false };
let lastTempAlertAt = 0;
// Se avisa al nativo solo cuando cambia (distintivo de la barra de tareas).
let lastHealthSent = "ok";

function updateAlerts(s) {
  const nowMs = Date.now();
  const cpuT = s.cpu?.packageTemp ?? null, gpuT = s.gpu?.temp ?? null;
  const chk = (key, val, th) => {
    if (val === null || val === undefined) { tempAlertState[key] = false; return false; }
    if (val >= th) {
      if (!tempAlertState[key] && nowMs - lastTempAlertAt >= TEMP_COOLDOWN_MS) {
        tempAlertState[key] = true; lastTempAlertAt = nowMs;
      }
      return tempAlertState[key];
    }
    if (val < th - TEMP_HYST) tempAlertState[key] = false;
    return tempAlertState[key];
  };
  const unit = app.cfg.unit;
  const d = (v) => (v == null ? "—" : (unit === "F" ? v * 9 / 5 + 32 : v).toFixed(1) + (unit === "F" ? " °F" : " °C"));
  const cc = chk("cpuCrit", cpuT, app.cfg.cpuCritTemp);
  const cw = !cc && chk("cpuWarn", cpuT, app.cfg.cpuWarnTemp);
  const gc = chk("gpuCrit", gpuT, app.cfg.gpuCritTemp);
  const gw = !gc && chk("gpuWarn", gpuT, app.cfg.gpuWarnTemp);
  let text = "", cls = "";
  if (cc) { text = `⚠ CPU a ${d(cpuT)} (umbral crítico ${d(app.cfg.cpuCritTemp)})`; cls = "crit"; }
  else if (gc) { text = `⚠ GPU a ${d(gpuT)} (umbral crítico ${d(app.cfg.gpuCritTemp)})`; cls = "crit"; }
  else if (cw) { text = `⚠ CPU a ${d(cpuT)} (aviso ${d(app.cfg.cpuWarnTemp)})`; cls = "warn"; }
  else if (gw) { text = `⚠ GPU a ${d(gpuT)} (aviso ${d(app.cfg.gpuWarnTemp)})`; cls = "warn"; }
  app.alertCls = cls; app.alertText = text;
  app.health = cls === "crit" ? "crit" : cls === "warn" ? "warn" : "ok";
  if (app.health !== lastHealthSent) { lastHealthSent = app.health; cmd.health(app.health); }

  const warns = (s.warnings || []).filter(Boolean);
  if (s.meta && !s.meta.isAdmin) warns.unshift("La app no se ejecuta como administrador: algunos sensores pueden no estar disponibles.");
  app.warns = warns;
}

// ---------- Fondo reactivo (decorativo, no son datos) ----------
function updateAurora(s) {
  const t = s.cpu?.packageTemp ?? null;
  const heat = t == null ? 0 : clamp01((t - 62) / Math.max(1, app.cfg.cpuCritTemp - 62));
  const charging = s.battery?.charging === true;
  app.aurora = {
    a: mixHex("#4f8cff", "#f87171", heat * 0.6),
    b: mixHex("#22d3ee", "#fbbf24", heat * 0.55),
    c: charging ? mixHex("#a78bfa", "#34d399", 0.45) : mixHex("#a78bfa", "#f472b6", heat * 0.35)
  };
}

// ---------- Recepción de mensajes ----------
let msgTimer = null;

export function onSnapshot(s) {
  clearTimeout(msgTimer);
  app.snap = s;
  // Intervalo REAL (el gobernador puede ampliarlo en segundo plano/ahorro).
  const millis = app.cfg.effectiveMs || s.meta?.refreshMs || app.cfg.refreshMs;
  const secs = millis / 1000;
  app.conn = { cls: "ok", text: `Conectado · cada ${secs} s` };
  app.lastRead = "Última lectura: " + (s.meta?.lastReadLocal || s.timestampLocal || "—");
  if (app.paused) return; // pausa: congela gráficas e historial, la lectura sigue
  pushHist(s);
  updateAlerts(s);
  updateAurora(s);
}

function onUpdate(d) {
  const st = app.update;
  st.latest = null; st.url = null; st.showDl = false; st.showRetry = false;
  if (d.state === "checking") st.text = "Comprobando…";
  else if (d.state === "uptodate") st.text = `Estás al día (v${d.current}).`;
  else if (d.state === "noreleases") st.text = "No se encontraron versiones (¿repo privado o aún sin publicar?).";
  else if (d.state === "available") {
    st.latest = d.latest; st.url = d.url; st.showDl = true;
    st.text = `Nueva versión ${d.latest} disponible (tienes v${d.current}).`;
  }
  else if (d.state === "downloading") st.text = "Descargando instalador… se abrirá solo; la app se cerrará.";
  else if (d.state === "dlerror") {
    st.text = "Se descargó pero no se pudo abrir el instalador (quizá el antivirus lo está revisando). Espera unos segundos y pulsa Reintentar, o ábrelo a mano desde tu carpeta temporal.";
    st.showRetry = true;
  }
  else if (d.state === "error") st.text = "No se pudo comprobar (¿sin Internet o sin releases?).";
}

export function onMessageDispatch(m) {
  if (!m || !m.type) return;
  if (m.type === "config") Object.assign(app.cfg, m.data || {});
  else if (m.type === "snapshot") onSnapshot(m.data);
  else if (m.type === "hover") app.osHover = m.data?.id || "";
  else if (m.type === "navigate" && m.data?.page) setPage(m.data.page);
  else if (m.type === "updateStatus") onUpdate(m.data);
  else if (m.type === "defenderStatus") app.defender = m.data || { ok: false, excluded: false, message: "" };
  else if (m.type === "hvciStatus") {
    const d = m.data || {};
    app.hvciText = d.result === "Ok"
      ? `Integridad de memoria: ${d.enabled ? "ACTIVADA" : "DESACTIVADA"}. Reinicia el equipo para que surta efecto.`
      : d.result === "NoAdmin" ? "Sin permisos de administrador: reabre la app como admin."
        : "No se pudo cambiar. Mira el registro de errores.";
  }
}

export function initBridge() {
  const ok = onMessage(onMessageDispatch);
  if (!ok) {
    app.conn = { cls: "err", text: "Sin puente nativo (vista previa). Ejecuta la app instalada." };
  } else {
    msgTimer = setTimeout(() => {
      app.conn = { cls: "err", text: "Sin datos: comprueba que la app esté ejecutándose con permisos suficientes." };
    }, 8000);
  }
  return ok;
}

// ---------- Preferencias solo de la interfaz (no tocan config.json) ----------
export const PERF_LABELS = {
  normal: "",
  background: "Segundo plano",
  saver: "Ahorro de energía",
  fullscreen: "Pantalla completa",
  hidden: "Oculto"
};

/**
 * v3.0.0 — Aplica a la página el alto contraste y el color de acento de
 * Windows. El diseño sigue siendo el mismo; solo cambian los acentos y los
 * bordes cuando el sistema lo pide (accesibilidad).
 */
export function syncOsTheme() {
  const os = app.cfg.os || {};
  const hc = !!os.highContrast;
  document.body.classList.toggle("hc", hc);
  const accent = /^#[0-9a-f]{6}$/i.test(os.accent || "") ? os.accent : "#4f8cff";
  document.documentElement.style.setProperty("--os-accent", accent);
  document.body.dataset.perf = os.perf || app.cfg.perf || "normal";
}

// ---------- Preferencias solo de la interfaz (no tocan config.json) ----------
const UI_KEY = "temppanel.ui";
export function loadUiPrefs() {
  try {
    const raw = localStorage.getItem(UI_KEY);
    if (!raw) return;
    const o = JSON.parse(raw);
    if (typeof o.alpha === "number") app.uiAlpha = clamp(o.alpha, 0.10, 0.6);
    if (typeof o.immersive === "boolean") app.immersive = o.immersive;
  } catch { /* sin preferencias guardadas */ }
}
export function saveUiPrefs() {
  try {
    localStorage.setItem(UI_KEY, JSON.stringify({ alpha: app.uiAlpha, immersive: app.immersive }));
  } catch { /* modo privado o sin permiso */ }
}

// ---------- Resumen / exportaciones ----------
export async function copySummary() {
  const s = app.snap;
  if (!s) return;
  const d = (v, dec, u) => (v === null || v === undefined ? "no disponible" : Number(v).toFixed(dec) + " " + u);
  const L = [
    `TempPanel — ${s.meta?.lastReadLocal || ""}`,
    `CPU: ${s.cpu?.processorName || "?"} | Temp: ${d(s.cpu?.packageTemp, 1, "°C")} | Carga: ${d(s.cpu?.totalLoad, 1, "%")}`,
    `RAM: ${s.memory?.usedMb != null ? Math.round(s.memory.usedMb) + "/" + Math.round(s.memory.totalMb) + " MB" : "—"} (${d(s.memory?.percentUsed, 1, "%")})`,
    `GPU: ${s.gpu?.name || "?"} | Temp: ${d(s.gpu?.temp, 1, "°C")} | Carga: ${d(s.gpu?.load, 1, "%")}`,
    `Batería: ${s.battery?.present && s.battery?.percent != null ? s.battery.percent.toFixed(1) + " % (" + s.battery.status + ")" : "—"}`
  ].join("\n");
  try {
    await navigator.clipboard.writeText(L);
    toast("Resumen copiado", "ok");
  } catch {
    try {
      const ta = document.createElement("textarea");
      ta.value = L; document.body.appendChild(ta); ta.select();
      document.execCommand("copy"); ta.remove();
      toast("Resumen copiado", "ok");
    } catch { toast("No se pudo copiar", "err"); }
  }
}

function download(name, text, mime) {
  const a = document.createElement("a");
  a.href = URL.createObjectURL(new Blob([text], { type: mime || "text/plain" }));
  a.download = name; a.click();
  setTimeout(() => URL.revokeObjectURL(a.href), 5000);
}

export function exportCsv() {
  if (!app.history.length) { toast("Aún no hay datos para exportar", "err"); return; }
  const rows = [["timestamp", "cpu_temp_c", "cpu_load_pct", "ram_used_mb", "gpu_temp_c", "bat_pct"]];
  app.history.forEach((p) => rows.push([p.t.toISOString(), p.cpuTemp ?? "", p.cpuLoad ?? "", p.ramUsed ?? "", p.gpuTemp ?? "", p.batPct ?? ""]));
  download(`temppanel-${new Date().toISOString().slice(0, 19).replace(/[:T]/g, "-")}.csv`, rows.map((r) => r.join(",")).join("\n"), "text/csv");
  toast("CSV exportado", "ok");
}

export function exportSession() {
  if (!app.snap) { toast("Aún no hay lectura", "err"); return; }
  download(`temppanel-sesion-${Date.now()}.json`,
    JSON.stringify({ exportedAt: new Date().toISOString(), config: app.cfg, snapshot: app.snap, history: app.history }, null, 2),
    "application/json");
  toast("Registro de sesión guardado", "ok");
}

export { num };

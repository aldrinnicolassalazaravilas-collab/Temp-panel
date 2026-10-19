// Genera TempPanel/Frontend/_preview.html: la interfaz real ya compilada
// (index.html + assets/app.js + assets/style.css) con:
//   - el puente WebView2 emulado (chrome.webview) ANTES de cargar el bundle,
//   - un fondo tipo escritorio para ver el cristal (lo dibuja Windows de verdad),
//   - datos REALES del último --selftest (o de una serie guardada).
// Nunca se publica (excluido del .csproj) ni forma parte de la app instalada.
//
// Uso:
//   node C:\tempanel\uitest\preview.js                (datos reales del selftest)
//   node C:\tempanel\uitest\preview.js --demo-series  (añade una serie de ejemplo
//                                                      para revisar las gráficas;
//                                                      queda avisado en pantalla)
const fs = require("fs");
const path = require("path");

const feDir = "C:/tempanel/TempPanel/Frontend";
const html = fs.readFileSync(path.join(feDir, "index.html"), "utf8");
const samplesPath = path.join(process.env.TEMP, "temppanel-preview-samples.json");
const selftestPath = path.join(process.env.TEMP, "temppanel-selftest.json");
const demo = process.argv.includes("--demo-series");

function camel(o) {
  if (Array.isArray(o)) return o.map(camel);
  if (o && typeof o === "object") {
    const out = {};
    for (const [k, v] of Object.entries(o)) out[k.charAt(0).toLowerCase() + k.slice(1)] = camel(v);
    return out;
  }
  return o;
}

// ---- Muestras: 1) serie guardada, 2) selftest único (real). ----
let samples = [];
let source = "sin datos";
if (fs.existsSync(samplesPath)) {
  samples = JSON.parse(fs.readFileSync(samplesPath, "utf8")).map(camel);
  source = `serie guardada (${samples.length} muestras reales)`;
} else if (fs.existsSync(selftestPath)) {
  samples = [camel(JSON.parse(fs.readFileSync(selftestPath, "utf8")))];
  source = "último --selftest (1 muestra real)";
}

// ---- Opcional: serie de ejemplo SOLO para revisar el dibujo de las gráficas. ----
function demoSeries(base) {
  const out = [];
  const n = 90;
  const cpu0 = base.cpu?.packageTemp ?? 55;
  const load0 = base.cpu?.totalLoad ?? 25;
  const ram0 = base.memory?.usedMb ?? 6000;
  const gpu0 = base.gpu?.temp ?? 45;
  const bat0 = base.battery?.percent ?? 80;
  for (let i = 0; i < n; i++) {
    const k = i / (n - 1);
    const s = JSON.parse(JSON.stringify(base));
    const wave = Math.sin(k * Math.PI * 2.2) * 6 + Math.sin(k * Math.PI * 6) * 2;
    s.cpu.packageTemp = Math.max(35, cpu0 - 8 + wave);
    s.cpu.totalLoad = Math.max(3, Math.min(99, load0 - 10 + wave * 2.4));
    if (s.memory) s.memory.usedMb = Math.max(1200, ram0 - 300 + wave * 60);
    if (s.gpu) s.gpu.temp = Math.max(30, gpu0 - 6 + wave * 0.7);
    if (s.battery && s.battery.percent != null) s.battery.percent = Math.min(100, Math.max(5, bat0 - 3 + k * 6));
    const ts = new Date(Date.now() - (n - i) * 1000);
    s.timestampLocal = ts.toISOString();
    s.timestamp = ts.toISOString();
    s.meta = Object.assign({}, s.meta, { lastReadLocal: ts.toLocaleTimeString("es-ES") });
    out.push(s);
  }
  return out;
}
if (demo && samples.length) {
  samples = demoSeries(samples[0]);
  source = "serie de EJEMPLO para revisar las gráficas (banner visible)";
}

// ---- 1. Puente emulado: debe existir antes de que arranque el bundle. ----
const bridge = `<script>
(function () {
  var handlers = [], queue = [];
  window.chrome = { webview: {
    postMessage: function (m) { (window.__previewSent = window.__previewSent || []).push(m); },
    addEventListener: function (ev, cb) {
      if (ev !== "message") return;
      handlers.push(cb);
      while (queue.length) cb({ data: queue.shift() });
    },
    removeEventListener: function (ev, cb) { handlers = handlers.filter(function (h) { return h !== cb; }); }
  } };
  window.__previewSend = function (type, data) {
    var msg = { type: type, data: data };
    if (!handlers.length) { queue.push(msg); return; }
    handlers.forEach(function (h) { h({ data: msg }); });
  };
})();
</script>`;

// ---- 2. Disparo de mensajes después del bundle (config + muestras). ----
const runtime = `<script>
(function () {
  // Escritorio emulado (el cristal real lo dibuja Windows detrás de la ventana).
  var wall = document.createElement("style");
  wall.textContent = "html{background:radial-gradient(1200px 800px at 15% 10%, #2b6cb0 0%, transparent 55%)," +
    "radial-gradient(1000px 700px at 85% 20%, #7c3aed 0%, transparent 55%)," +
    "radial-gradient(900px 900px at 60% 100%, #0ea5e9 0%, transparent 50%)," +
    "linear-gradient(135deg, #101828 0%, #1b2437 40%, #0b1220 100%) fixed}";
  document.head.appendChild(wall);

  var cfg = { refreshMs: 1000, glass: true, version: "3.0.0", firstRun: false, unit: "C",
    cpuWarnTemp: 85, cpuCritTemp: 95, gpuWarnTemp: 80, gpuCritTemp: 90,
    miniSeries: "cpu,temp,bat,ram,gpu", miniExpanded: true, preferredCpuSensor: "auto",
    batteryEnabled: true, batteryThreshold: 80, batteryRearm: 75, batterySound: true,
    batteryMode: "popup", batteryFull: true, startMinimized: false, startWithWindows: false,
    adaptivePerf: true, hotkeyEnabled: true, taskbarBadge: true, effectiveMs: 1000, perf: "normal",
    os: { name: "Windows 11 25H2", build: 26200, win11: true, accent: "#4f8cff", theme: "dark",
          highContrast: false, powerSaver: false, hotkey: true, glass: true } };
  var samples = ${JSON.stringify(samples)};
  var i = 0;
  function tick() {
    window.__previewSend("config", cfg);
    if (samples.length) {
      window.__previewSend("snapshot", samples[Math.min(i, samples.length - 1)]);
      i++;
      if (i <= samples.length) { setTimeout(tick, 900); return; }
    }
    setTimeout(function () { if (samples.length) window.__previewSend("snapshot", samples[samples.length - 1]); }, 2000);
  }
  setTimeout(tick, 60);

  var note = document.createElement("div");
  note.style.cssText = "position:fixed;left:14px;bottom:12px;z-index:200;font:12px 'Segoe UI',sans-serif;" +
    "color:#cfe3ff;background:rgba(8,13,24,.86);border:1px dashed rgba(34,211,238,.55);border-radius:10px;" +
    "padding:7px 12px;max-width:62ch;line-height:1.45";
  note.innerHTML = "<b>Vista previa</b> (no es la app instalada) · " +
    "fuente de datos: ${source}" + ${demo ? ' + "<b>serie de ejemplo para las gráficas</b>"' : '""'};
  document.body.appendChild(note);
})();
</script>`;

const withBridge = html.replace('<script src="./assets/app.js"></script>', bridge + "\n" + '<script src="./assets/app.js"></script>');
const out = withBridge.replace("</body>", runtime + "</body>");
fs.writeFileSync(path.join(feDir, "_preview.html"), out, "utf8");
console.log("OK: _preview.html generado · " + source + (demo ? " + demo-series" : ""));

"use strict";
/* TempPanel frontend — sin dependencias externas, 100% local. */

const $ = (id) => document.getElementById(id);
const fmt = (v, d = 1) => (v === null || v === undefined) ? '<span class="nd">No disponible</span>' : Number(v).toFixed(d);
const txt = (v) => (v === null || v === undefined || v === "") ? "No disponible" : String(v);

let lastSnap = null;
let cfg = { refreshMs: 2000, cpuWarnTemp: 85, cpuCritTemp: 95, gpuWarnTemp: 80, gpuCritTemp: 90, preferredCpuSensor: "auto",
  unit: "C", startMinimized: false, startWithWindows: false,
  batteryEnabled: true, batteryThreshold: 80, batteryRearm: 75, batterySound: true, batteryMode: "popup", batteryFull: true, version: "1.1.4", autoCheckUpdates: false };
let paused = false;
let history = []; // {t, cpuTemp(C), cpuLoad, ramUsed, gpuTemp(C), batPct}
const MAXH = 900; // 30 min a 2 s
let sensorTempIds = []; // identificadores de sensores de temp CPU para el selector

// Unidad de temperatura: los datos internos siempre están en °C; solo cambia la vista.
const unitSym = () => cfg.unit === "F" ? "°F" : "°C";
const toUnit = (c) => cfg.unit === "F" ? c * 9 / 5 + 32 : c;
const deg = (c) => (c === null || c === undefined) ? '<span class="nd">No disponible</span>' : toUnit(c).toFixed(1) + " " + unitSym();
const degT = (c) => (c === null || c === undefined) ? "No disponible" : toUnit(c).toFixed(1) + " " + unitSym();

// Histéresis + enfriamiento para no repetir avisos térmicos.
let tempAlertState = { cpuWarn: false, cpuCrit: false, gpuWarn: false, gpuCrit: false };
let lastTempAlertAt = 0;
const TEMP_HYST = 3;               // °C por debajo del umbral para dar por superado el aviso
const TEMP_COOLDOWN_MS = 10 * 60 * 1000; // 10 min entre avisos

function post(msg) {
  if (window.chrome && chrome.webview) chrome.webview.postMessage(msg);
}

// ---------- Navegación ----------
const titles = { resumen: "Resumen", cpu: "CPU", gpu: "GPU", ram: "Memoria RAM", bateria: "Batería", refrigeracion: "Refrigeración", sensores: "Todos los sensores", historial: "Historial", ayuda: "Ayuda", config: "Configuración" };
document.querySelectorAll(".nav-btn").forEach(b => b.addEventListener("click", () => {
  document.querySelectorAll(".nav-btn").forEach(x => x.classList.remove("active"));
  b.classList.add("active");
  document.querySelectorAll(".page").forEach(p => p.classList.remove("active"));
  $("page-" + b.dataset.page).classList.add("active");
  $("page-title").textContent = titles[b.dataset.page] || "";
  if (b.dataset.page === "historial") drawHist();
}));

$("btn-pause").addEventListener("click", () => {
  paused = !paused;
  $("btn-pause").textContent = paused ? "▶ Reanudar" : "⏸ Pausar";
});
$("btn-snapshot").addEventListener("click", () => post({ cmd: "getSnapshot" }));
$("btn-mini").addEventListener("click", () => post({ cmd: "openMini" }));

// ---------- Recepción ----------
if (window.chrome && chrome.webview) {
  chrome.webview.addEventListener("message", (e) => onMsg(e.data));
} else {
  setConn("err", "Sin puente nativo (vista previa). Ejecuta la app instalada.");
}
function onMsg(m) {
  if (!m || !m.type) return;
  if (m.type === "config") { cfg = Object.assign(cfg, m.data || {}); applyCfg(); }
  if (m.type === "snapshot") onSnapshot(m.data);
  if (m.type === "updateStatus") onUpdate(m.data);
}
let pendingUpdate = null;
function onUpdate(d) {
  const st = $("upd-status"), dl = $("btn-dl-upd");
  pendingUpdate = null; dl.style.display = "none";
  if (d.state === "checking") st.textContent = "Comprobando…";
  else if (d.state === "uptodate") st.textContent = `Estás al día (v${d.current}).`;
  else if (d.state === "noreleases") st.textContent = "No se encontraron versiones (¿repo privado o aún sin publicar?).";
  else if (d.state === "available") {
    pendingUpdate = d;
    st.textContent = `Nueva versión ${d.latest} disponible (tienes v${d.current}).`;
    dl.style.display = "";
  }
  else if (d.state === "downloading") st.textContent = "Descargando instalador… se abrirá solo; la app se cerrará.";
  else if (d.state === "dlerror") st.textContent = "Se descargó pero no se pudo abrir el instalador (quizá el antivirus lo está revisando). Busca TempPanel-Setup-*.exe en tu carpeta temporal y ábrelo a mano pasados unos segundos.";
  else if (d.state === "error") st.textContent = "No se pudo comprobar (¿sin Internet o sin releases?).";
}
function setConn(cls, text) {
  $("conn-dot").className = "conn " + cls;
  $("conn-text").textContent = text;
}

let msgTimer = setTimeout(() => setConn("err", "Sin datos: comprueba que la app esté ejecutándose con permisos suficientes."), 8000);

function onSnapshot(s) {
  clearTimeout(msgTimer);
  lastSnap = s;
  setConn("ok", "Conectado · cada " + (s.meta.refreshMs / 1000) + " s");
  $("last-read").textContent = "Última lectura: " + (s.meta.lastReadLocal || s.timestampLocal || "—");
  if (paused) return; // pausa: congela gráficas e historial, la lectura sigue
  pushHist(s);
  renderAll(s);
}

function pushHist(s) {
  history.push({
    t: new Date(s.timestampLocal || s.timestamp || Date.now()),
    cpuTemp: s.cpu.packageTemp ?? null,
    cpuLoad: s.cpu.totalLoad ?? null,
    ramUsed: s.memory.usedMb ?? null,
    gpuTemp: s.gpu.temp ?? null,
    batPct: (s.battery.present ? s.battery.percent : null) ?? null
  });
  if (history.length > MAXH) history.splice(0, history.length - MAXH);
}

// ---------- Render ----------
function card(title, valueHtml, sub) {
  return `<div class="card"><h3>${title}</h3><div class="big">${valueHtml}</div><div class="muted small">${sub || ""}</div></div>`;
}
const nd = '<span class="nd">No disponible</span>';

function tempClass(v, warn, crit) {
  if (v === null || v === undefined) return "";
  if (v >= crit) return "crit-t";
  if (v >= warn) return "warn-t";
  return "ok-t";
}

function renderAll(s) {
  // Alertas térmicas informativas con histéresis y enfriamiento (10 min entre avisos).
  const ab = $("alert-banner");
  const nowMs = Date.now();
  const cpuT = s.cpu.packageTemp, gpuT = s.gpu.temp;
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
  const cc = chk("cpuCrit", cpuT, cfg.cpuCritTemp);
  const cw = !cc && chk("cpuWarn", cpuT, cfg.cpuWarnTemp);
  const gc = chk("gpuCrit", gpuT, cfg.gpuCritTemp);
  const gw = !gc && chk("gpuWarn", gpuT, cfg.gpuWarnTemp);
  let alertTxt = "", alertCls = "alert hidden";
  if (cc) { alertTxt = `⚠ CPU a ${deg(cpuT)} (umbral crítico ${deg(cfg.cpuCritTemp)})`; alertCls = "alert crit"; }
  else if (gc) { alertTxt = `⚠ GPU a ${deg(gpuT)} (umbral crítico ${deg(cfg.gpuCritTemp)})`; alertCls = "alert crit"; }
  else if (cw) { alertTxt = `⚠ CPU a ${deg(cpuT)} (aviso ${deg(cfg.cpuWarnTemp)})`; alertCls = "alert warn"; }
  else if (gw) { alertTxt = `⚠ GPU a ${deg(gpuT)} (aviso ${deg(cfg.gpuWarnTemp)})`; alertCls = "alert warn"; }
  ab.className = alertCls; ab.textContent = alertTxt;
  const hd = $("health-dot");
  if (hd) hd.className = "health " + (alertCls.includes("crit") ? "crit" : alertCls.includes("warn") ? "warn" : "ok");

  // Avisos de disponibilidad
  const wb = $("warn-bar");
  const warns = (s.warnings || []).filter(Boolean);
  if (s.meta && !s.meta.isAdmin) warns.unshift("La app no se ejecuta como administrador: algunos sensores pueden no estar disponibles.");
  if (warns.length) { wb.classList.remove("hidden"); wb.textContent = warns.join(" · "); }
  else wb.classList.add("hidden");

  renderSummary(s); renderCpu(s); renderGpu(s); renderRam(s);
  renderBat(s); renderCooling(s); renderSensors(s); renderSys(s);
  const dg = $("diag-info"), da = $("diag-advice");
  if (dg && s.diagnostics) {
    const d = s.diagnostics;
    const drv = d.driverBlocked === true ? "Sí, Windows lo bloqueó"
      : d.driverBlocked === false ? "No se encontraron bloqueos"
      : "No comprobable sin administrador";
    dg.innerHTML =
      `<div>Permisos</div><div>${d.isAdmin ? "Administrador" : "Usuario estándar"}</div>` +
      `<div>Integridad de memoria</div><div>${d.hvciEnabled ? "Activada (bloquea el driver de temperatura)" : "Desactivada"}</div>` +
      `<div>Driver bloqueado</div><div>${drv}</div>` +
      `<div>Temps. CPU con valor</div><div>${d.cpuTempWithValue} de ${d.cpuTempSensors}</div>` +
      `<div>Sensores con valor</div><div>${d.sensorsWithValue} de ${d.totalSensors}</div>`;
    if (da) {
      if (d.cpuTempWithValue > 0) da.textContent = "El equipo publica temperaturas de CPU: las verás en la sección CPU.";
      else if (d.driverBlocked === true) da.textContent = "Confirmado: Windows bloqueó el controlador de lectura. Para ver temperaturas, desactiva la integridad de memoria y reinicia (o mantenla y usa el resto de sensores).";
      else if (d.hvciEnabled) da.textContent = "Sin valores de temperatura y con integridad activada: es el bloqueo probable. Desactívala y reinicia para comprobarlo.";
      else if (!d.isAdmin) da.textContent = "Prueba «Reiniciar como administrador» en Configuración → Permisos.";
      else da.textContent = "El firmware no entrega temperaturas al sistema. Se muestran el resto de sensores reales.";
    }
  }
  drawMini(); drawHist();
}

function renderSummary(s) {
  const cpuV = s.cpu.packageTemp != null
    ? `<span class="${tempClass(s.cpu.packageTemp, cfg.cpuWarnTemp, cfg.cpuCritTemp)}">${degT(s.cpu.packageTemp)}</span>` : nd;
  const loadV = s.cpu.totalLoad != null ? `${s.cpu.totalLoad.toFixed(1)} <span class="unit">%</span>` : nd;
  const ramV = s.memory.percentUsed != null ? `${s.memory.percentUsed.toFixed(1)} <span class="unit">%</span>` : nd;
  const batV = s.battery.present && s.battery.percent != null ? `${s.battery.percent.toFixed(0)} <span class="unit">%</span>` : nd;
  const cpuGauge = s.cpu.packageTemp != null
    ? gaugeSVG(Math.min(1, s.cpu.packageTemp / cfg.cpuCritTemp),
        tempClass(s.cpu.packageTemp, cfg.cpuWarnTemp, cfg.cpuCritTemp) === "crit-t" ? "#f87171"
        : tempClass(s.cpu.packageTemp, cfg.cpuWarnTemp, cfg.cpuCritTemp) === "warn-t" ? "#fbbf24" : "#22d3ee",
        degT(s.cpu.packageTemp))
    : "";
  const batRing = s.battery.present && s.battery.percent != null ? ringSVG(s.battery.percent / 100, s.battery.percent.toFixed(0) + " %") : "";
  const spCpu = sparkline(convT(seriesOf("cpuTemp", 300)), "#22d3ee");
  const spLoad = sparkline(seriesOf("cpuLoad", 300), "#2f7bff");
  const spRam = sparkline(history.filter(p => p.t >= new Date(Date.now() - 300000)).map(p => p.ramUsed), "#a78bfa");
  const spBat = sparkline(seriesOf("batPct", 300), "#34d399");
  $("summary-cards").innerHTML =
    `<div class="card"><h3>CPU · ${esc(txt(s.cpu.packageTempSource || "temperatura"))}</h3><div class="gaugewrap">${cpuGauge}<div class="big">${cpuV}</div></div>${spCpu}<div class="muted small">${esc(txt(s.cpu.processorName))}</div></div>` +
    `<div class="card"><h3>Carga CPU</h3><div class="big">${loadV}</div>${spLoad}<div class="muted small">${s.cpu.throttling ? "⚠ posible limitación térmica" : "Carga total"}</div></div>` +
    `<div class="card"><h3>RAM en uso</h3><div class="big">${ramV}</div>${spRam}<div class="muted small">${s.memory.usedMb != null ? `${Math.round(s.memory.usedMb)} / ${Math.round(s.memory.totalMb)} MB` : "Sin datos de memoria"}</div></div>` +
    `<div class="card"><h3>Batería</h3><div class="gaugewrap">${batRing}<div class="big">${batV}</div></div>${spBat}<div class="muted small">${esc(txt(s.battery.status))}</div></div>`;
}
function sparkline(vals, color) {
  const pts = (vals || []).filter(v => v !== null && v !== undefined);
  if (pts.length < 2) return "";
  const w = 220, h = 44;
  const min = Math.min(...pts), max = Math.max(...pts);
  const rg = (max - min) < 1e-6 ? 1 : (max - min);
  const X = (i) => (w * i / (vals.length - 1)).toFixed(1);
  const Y = (v) => (h - 4 - (h - 8) * ((v - min) / rg)).toFixed(1);
  let d = "";
  vals.forEach((v, i) => { if (v === null || v === undefined) return; d += (d ? " L" : "M") + X(i) + " " + Y(v); });
  return `<svg viewBox="0 0 ${w} ${h}" class="spark" preserveAspectRatio="none"><path d="${d}" fill="none" stroke="${color}" stroke-width="2"/></svg>`;
}
function gaugeSVG(frac, color, label) {
  const f = Math.max(0, Math.min(1, frac || 0));
  const len = 251.3; // semicírculo r=80
  return `<svg viewBox="0 0 200 112" class="gauge"><path d="M 10 100 A 80 80 0 0 1 190 100" fill="none" stroke="#0b1220" stroke-width="14" stroke-linecap="round"/>` +
    `<path d="M 10 100 A 80 80 0 0 1 190 100" fill="none" stroke="${color}" stroke-width="14" stroke-linecap="round" stroke-dasharray="${(f * len).toFixed(1)} ${len.toFixed(1)}">` +
    `<animate attributeName="stroke-dasharray" from="0 ${len.toFixed(1)}" to="${(f * len).toFixed(1)} ${len.toFixed(1)}" dur="0.4s" fill="freeze"/></path>` +
    `<text x="100" y="92" text-anchor="middle" fill="#e6edf6" font-size="17" font-weight="bold">${esc(label)}</text></svg>`;
}
function ringSVG(frac, label) {
  const f = Math.max(0, Math.min(1, frac || 0));
  const c = 2 * Math.PI * 54;
  return `<svg viewBox="0 0 130 130" class="ring"><circle cx="65" cy="65" r="54" fill="none" stroke="#0b1220" stroke-width="12"/>` +
    `<circle cx="65" cy="65" r="54" fill="none" stroke="#22d3ee" stroke-width="12" stroke-linecap="round" transform="rotate(-90 65 65)" stroke-dasharray="${(f * c).toFixed(1)} ${c.toFixed(1)}"/>` +
    `<text x="65" y="71" text-anchor="middle" fill="#e6edf6" font-size="19" font-weight="bold">${esc(label)}</text></svg>`;
}

function renderCpu(s) {
  $("cpu-head").innerHTML =
    `<div>Procesador</div><div>${txt(s.cpu.processorName)}</div>` +
    `<div>Núcleos</div><div>${s.cpu.coresPhysical != null ? s.cpu.coresPhysical + " físicos / " + (s.cpu.coresLogical ?? "?") + " lógicos" : "No disponible"}</div>` +
    `<div>Temperatura paquete</div><div>${degT(s.cpu.packageTemp)}</div>` +
    `<div>Carga total</div><div>${s.cpu.totalLoad != null ? s.cpu.totalLoad.toFixed(1) + " %" : "No disponible"}</div>` +
    `<div>Potencia paquete</div><div>${s.cpu.power != null ? s.cpu.power.toFixed(2) + " W" : "No disponible (sensor no expuesto)"}</div>` +
    `<div>Limitación térmica</div><div>${s.cpu.throttling ? "⚠ Detectada" : "No detectada"}</div>`;
  $("cpu-source-note").textContent = s.cpu.packageTempSource ? "Sensor seleccionado: " + s.cpu.packageTempSource : "Sin sensor de temperatura de CPU disponible.";
  buildCpuSensorSelect(s);
  const sess = s.session || {};
  $("cpu-session").innerHTML =
    `<div>Temp. mínima (sesión)</div><div>${degT(sess.cpuTempMin)}</div>` +
    `<div>Temp. máxima (sesión)</div><div>${degT(sess.cpuTempMax)}</div>` +
    `<div>Temp. promedio (sesión)</div><div>${degT(sess.cpuTempAvg)}</div>` +
    `<div>Carga máx. (sesión)</div><div>${sess.cpuLoadMax != null ? sess.cpuLoadMax.toFixed(1) + " %" : "No disponible"}</div>` +
    `<div>Muestras</div><div>${sess.samples != null ? sess.samples : "—"}</div>`;
  $("cpu-cards").innerHTML =
    card("Temperatura", s.cpu.packageTemp != null ? degT(s.cpu.packageTemp) : nd, "Paquete") +
    card("Carga", s.cpu.totalLoad != null ? `${s.cpu.totalLoad.toFixed(1)} <span class="unit">%</span>` : nd, "Total") +
    card("Frecuencia media", avg(s.cpu.coreClocks) != null ? `${Math.round(avg(s.cpu.coreClocks))} <span class="unit">MHz</span>` : nd, "Promedio de núcleos");
  const ct = (s.cpu.coreTemps || []).map(v => v === null || v === undefined ? null : toUnit(v));
  bars($("core-temps"), ct, unitSym(), toUnit(cfg.cpuWarnTemp), toUnit(cfg.cpuCritTemp), s.cpu.coreTempNames, "Núcleo");
  bars($("core-loads"), s.cpu.coreLoads, "%", 80, 95, s.cpu.coreLoadNames, "Núcleo");
  const tj = $("tjmax-info");
  if (s.cpu.tjMaxDistances && s.cpu.tjMaxDistances.length)
    tj.innerHTML = s.cpu.tjMaxNames.map((n, i) => {
      const v = s.cpu.tjMaxDistances[i];
      return `<div>${esc(n)}</div><div>${v != null ? degT(v) + " de margen" : "No disponible"}</div>`;
    }).join("");
  else tj.innerHTML = "<div>Margen térmico</div><div>No disponible (el firmware no lo expone en este modo).</div>";
}

function buildCpuSensorSelect(s) {
  const sel = $("cpu-sensor-select");
  const seen = new Set(["auto"]);
  (s.allSensors || []).filter(x => x.hardwareType === "Cpu" && x.type === "Temperature" && x.value != null)
    .forEach(x => {
      const id = x.identifier || x.name;
      if (!seen.has(id)) { seen.add(id); sensorTempIds.push(id); }
    });
  const cur = Array.from(sel.options).map(o => o.value);
  (s.allSensors || []).filter(x => x.hardwareType === "Cpu" && x.type === "Temperature").forEach(x => {
    const id = x.identifier || x.name;
    if (!cur.includes(id)) {
      const o = document.createElement("option");
      o.value = id; o.textContent = `${x.name} — ${x.value != null ? degT(x.value) + " (sensor)" : "sin lectura"}`;
      sel.appendChild(o);
    }
  });
  if (sel.value !== (s.meta.preferredCpuSensor || "auto")) sel.value = s.meta.preferredCpuSensor || "auto";
  sel.onchange = () => post({ cmd: "setCpuSensor", id: sel.value });
}

function renderGpu(s) {
  const g = s.gpu;
  $("gpu-cards").innerHTML =
    card("GPU", `<span class="accent">${txt(g.name)}</span>`, g.available ? "Detectada" : "No detectada") +
    card("Temperatura", g.temp != null ? `<span class="${tempClass(g.temp, cfg.gpuWarnTemp, cfg.gpuCritTemp)}">${degT(g.temp)}</span>` : nd, txt(g.tempSource)) +
    card("Carga", g.load != null ? `${g.load.toFixed(1)} <span class="unit">%</span>` : nd, "Núcleo GPU") +
    card("Frecuencia", g.clock != null ? `${Math.round(g.clock)} <span class="unit">MHz</span>` : nd, g.memoryUsedMb != null ? `Memoria: ${Math.round(g.memoryUsedMb)} MB` : "");
  $("gpu-note").innerHTML = `<div>Estado</div><div>${txt(g.note || (g.available ? "Sensores GPU disponibles arriba." : "Sin GPU detectable."))}</div>`;
  const gs = s.session || {};
  $("gpu-session").innerHTML =
    `<div>Temp. mínima (sesión)</div><div>${degT(gs.gpuTempMin)}</div>` +
    `<div>Temp. máxima (sesión)</div><div>${degT(gs.gpuTempMax)}</div>`;
}

function renderRam(s) {
  const m = s.memory;
  $("ram-cards").innerHTML =
    card("RAM total", m.totalMb != null ? `${Math.round(m.totalMb).toLocaleString("es")} <span class="unit">MB</span>` : nd, "") +
    card("RAM usada", m.usedMb != null ? `${Math.round(m.usedMb).toLocaleString("es")} <span class="unit">MB</span>` : nd, "") +
    card("RAM disponible", m.availableMb != null ? `${Math.round(m.availableMb).toLocaleString("es")} <span class="unit">MB</span>` : nd, "") +
    card("Uso", m.percentUsed != null ? `${m.percentUsed.toFixed(1)} <span class="unit">%</span>` : nd, txt(m.source));
}

function renderBat(s) {
  const b = s.battery;
  $("bat-cards").innerHTML =
    card("Carga", b.present && b.percent != null ? `${b.percent.toFixed(0)} <span class="unit">%</span>` : nd, txt(b.status)) +
    card("Estado", `<span class="accent">${txt(b.status)}</span>`, b.present ? "Batería detectada" : "Sin batería") +
    card("Capacidad restante", b.remainingCapacityMwh != null ? `${Math.round(b.remainingCapacityMwh).toLocaleString("es")} <span class="unit">mWh</span>` : nd, "") +
    card("Temperatura", nd, "Windows no expone temperatura de batería");
  $("bat-info").innerHTML =
    `<div>Alimentación externa</div><div>${txt(b.powerLine)}</div>` +
    `<div>Nivel preciso</div><div>${b.chargeLevelPct != null ? b.chargeLevelPct.toFixed(1) + " %" : "No disponible"}</div>` +
    `<div>Capacidad de diseño</div><div>${b.designCapacityMwh != null ? Math.round(b.designCapacityMwh).toLocaleString("es") + " mWh" : "No expuesta"}</div>` +
    `<div>Capacidad de carga total</div><div>${b.fullChargeCapacityMwh != null ? Math.round(b.fullChargeCapacityMwh).toLocaleString("es") + " mWh" : "No expuesta"}</div>` +
    `<div>Tasa de descarga</div><div>${b.dischargeRateW != null ? b.dischargeRateW.toFixed(1) + " W" : "No disponible"}</div>` +
    `<div>Autonomía estimada</div><div>${b.remainingMinutes != null ? Math.round(b.remainingMinutes) + " min" : "No disponible"}</div>` +
    `<div>Tensión</div><div>${b.voltageV != null ? b.voltageV.toFixed(2) + " V" : "No disponible"}</div>` +
    `<div>Nota</div><div>${txt(b.note)}</div>`;
}

function renderCooling(s) {
  const fans = s.fans || [];
  $("fan-cards").innerHTML = fans.length
    ? fans.map(f => card(`Ventilador · ${txt(f.name)}`, f.rpm != null ? `${Math.round(f.rpm)} <span class="unit">RPM</span>` : nd, txt(f.hardware))).join("")
    : card("Ventiladores", nd, "El EC del portátil no expone RPM al sistema. Es habitual en IdeaPad Slim.");
  const et = (s.extraTempNames || []).map((n, i) => {
    const v = (s.extraTemps || [])[i];
    return `<div>${txt(n)}</div><div>${v != null ? degT(v) : "No disponible"}</div>`;
  }).join("");
  $("extra-temps").innerHTML = et || "<div>No hay sensores de temperatura auxiliares accesibles.</div><div></div>";
  const st = (s.storage || []).map(d =>
    `<div>${esc(d.name)}</div><div>${d.tempC != null ? degT(d.tempC) : "No disponible (sensor no expuesto)"}</div>`
  ).join("");
  $("storage-info").innerHTML = st || "<div>Unidades</div><div>No se detectaron unidades con sensor accesible.</div>";
}

function renderSensors(s) {
  const q = ($("sensor-filter").value || "").toLowerCase();
  const rows = (s.allSensors || []).filter(x => !q ||
    (x.hardware + " " + x.hardwareType + " " + x.type + " " + x.name).toLowerCase().includes(q));
  $("sensor-count").textContent = `${rows.length} / ${(s.allSensors || []).length} sensores`;
  $("sensor-table").querySelector("tbody").innerHTML = rows.map(x =>
    `<tr><td>${esc(x.hardware)}</td><td>${esc(x.hardwareType)}</td><td>${esc(x.type)}</td><td>${esc(x.name)}</td>` +
    `<td>${x.value != null ? x.value + " " + esc(x.unit) : '<span class="nd">—</span>'}</td>` +
    `<td>${x.min != null ? x.min : "—"}</td><td>${x.max != null ? x.max : "—"}</td></tr>`).join("");
}
$("sensor-filter").addEventListener("input", () => lastSnap && renderSensors(lastSnap));

function renderSys(s) {
  const m = s.meta;
  $("sys-info").innerHTML =
    `<div>Procesador</div><div>${txt(m.cpuName)}</div>` +
    `<div>Memoria instalada</div><div>${m.installedRamMb != null ? Math.round(m.installedRamMb).toLocaleString("es") + " MB" : "No disponible"}</div>` +
    `<div>Tiempo de app</div><div>${txt(m.appUptime)}</div>` +
    `<div>Equipo encendido</div><div>${m.bootTimeLocal ? txt(m.bootTimeLocal) : "No disponible"}</div>` +
    `<div>Frecuencia</div><div>cada ${(m.refreshMs / 1000)} s</div>` +
    `<div>Última lectura</div><div>${txt(m.lastReadLocal)}</div>` +
    `<div>Permisos</div><div>${m.isAdmin ? "Administrador" : "Usuario estándar"}</div>`;
  $("admin-info").innerHTML = `<div>Modo actual</div><div>${m.isAdmin ? "Administrador (elevado)" : "Usuario estándar"}</div>`;
}

// ---------- Barras ----------
function bars(el, arr, unit, warn, crit, names, fallbackPrefix) {
  if (!arr || !arr.length) { el.innerHTML = '<span class="nd">Sin lecturas por núcleo disponibles.</span>'; return; }
  const vals = arr.filter(v => v != null);
  const max = vals.length ? Math.max(...vals) : 1;
  el.innerHTML = arr.map((v, i) => {
    const label = (names && names[i]) ? shortName(names[i]) : `${fallbackPrefix || "Núcleo"} ${i}`;
    if (v == null) return `<div class="bar-row"><span>${esc(label)}</span><div class="bar"><div style="width:0%"></div></div><span class="nd">—</span></div>`;
    const pct = Math.min(100, v / (unit === "%" ? 100 : max) * 100);
    const col = v >= crit ? "linear-gradient(90deg,#f87171,#ef4444)" : v >= warn ? "linear-gradient(90deg,#fbbf24,#f59e0b)" : "linear-gradient(90deg,#2f7bff,#22d3ee)";
    return `<div class="bar-row"><span>${esc(label)}</span><div class="bar"><div style="width:${pct.toFixed(0)}%;background:${col}"></div></div><span>${v.toFixed(unit === "%" ? 1 : 1)} ${unit}</span></div>`;
  }).join("");
}
function shortName(n) {
  return String(n)
    .replace("CPU Core ", "N")
    .replace("P-Core #", "P")
    .replace("E-Core #", "E")
    .replace("Core #", "N")
    .replace("Core ", "N")
    .replace(" Thread ", "·H")
    .replace(/#/g, "");
}
function avg(a) {
  if (!a || !a.length) return null;
  const v = a.filter(x => x != null);
  return v.length ? v.reduce((x, y) => x + y, 0) / v.length : null;
}
function esc(s) { return String(s ?? "").replace(/[&<>"]/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c])); }

// Tooltip interactivo: hora + valor exacto al pasar el ratón.
const chartData = {};
function drawLine(canvasId, series, unit, warn, crit, times) {
  const c = $(canvasId); if (!c) return { min: null, max: null, avg: null };
  chartData[canvasId] = { vals: series.slice(), unit, times: (times || []).slice() };
  attachTooltip(c);
  const ctx = c.getContext("2d");
  const W = c.width, H = c.height;
  ctx.clearRect(0, 0, W, H);
  ctx.fillStyle = "#0b1220"; ctx.fillRect(0, 0, W, H);
  const pts = series.filter(v => v != null);
  if (pts.length < 2) {
    ctx.fillStyle = "#8b98ad"; ctx.font = "13px Segoe UI";
    ctx.fillText("Esperando datos… (sin lecturas ficticias)", 14, H / 2);
    return { min: null, max: null, avg: null };
  }
  let min = Math.min(...pts), max = Math.max(...pts);
  if (max - min < 1e-6) { max += 1; min -= 1; }
  const pad = (max - min) * 0.15; min -= pad; max += pad;
  const X = (i) => 8 + (W - 16) * (i / (series.length - 1));
  const Y = (v) => H - 10 - (H - 20) * ((v - min) / (max - min));
  // umbrales
  [[warn, "#fbbf24"], [crit, "#f87171"]].forEach(([t, col]) => {
    if (t != null && t > min && t < max) {
      ctx.strokeStyle = col; ctx.setLineDash([5, 4]); ctx.globalAlpha = 0.6;
      ctx.beginPath(); ctx.moveTo(0, Y(t)); ctx.lineTo(W, Y(t)); ctx.stroke();
      ctx.setLineDash([]); ctx.globalAlpha = 1;
    }
  });
  // área + línea
  const grad = ctx.createLinearGradient(0, 0, 0, H);
  grad.addColorStop(0, "rgba(47,123,255,.35)"); grad.addColorStop(1, "rgba(34,211,238,.05)");
  ctx.beginPath();
  let started = false;
  series.forEach((v, i) => { if (v == null) return; const x = X(i), y = Y(v); if (!started) { ctx.moveTo(x, y); started = true; } else ctx.lineTo(x, y); });
  ctx.strokeStyle = "#22d3ee"; ctx.lineWidth = 2; ctx.stroke();
  ctx.lineTo(X(series.length - 1), H); ctx.lineTo(X(0), H); ctx.closePath();
  ctx.fillStyle = grad; ctx.fill();
  const a = pts.reduce((x, y) => x + y, 0) / pts.length;
  return { min, max: Math.max(...pts), avg: a, rmin: Math.min(...pts) };
}
function statLine(elId, st, unit) {
  const el = $(elId); if (!el) return;
  el.innerHTML = st.min == null ? "Mín: — · Máx: — · Promedio: —"
    : `Mín: <b>${st.rmin.toFixed(1)} ${unit}</b> · Máx: <b>${st.max.toFixed(1)} ${unit}</b> · Promedio: <b>${st.avg.toFixed(1)} ${unit}</b>`;
}
let tipEl = null;
function attachTooltip(canvas) {
  if (canvas.dataset.tip === "1") return;
  canvas.dataset.tip = "1";
  canvas.addEventListener("mousemove", (ev) => {
    const d = chartData[canvas.id];
    if (!d || !d.vals.length) return;
    const r = canvas.getBoundingClientRect();
    const fx = (ev.clientX - r.left) / r.width;
    const idx = Math.max(0, Math.min(d.vals.length - 1, Math.round(fx * (d.vals.length - 1))));
    const v = d.vals[idx];
    if (v === null || v === undefined) return;
    if (!tipEl) {
      tipEl = document.createElement("div");
      tipEl.id = "chart-tip";
      document.body.appendChild(tipEl);
    }
    const t = d.times[idx];
    const hh = t ? new Date(t).toLocaleTimeString("es") : "";
    tipEl.textContent = `${hh} · ${Number(v).toFixed(1)} ${d.unit}`;
    tipEl.style.display = "block";
    tipEl.style.left = (ev.clientX + 14) + "px";
    tipEl.style.top = (ev.clientY + 10) + "px";
  });
  canvas.addEventListener("mouseleave", () => { if (tipEl) tipEl.style.display = "none"; });
}
function seriesOf(key, rangeS) {
  const now = history.length ? history[history.length - 1].t : new Date();
  const cut = new Date(now.getTime() - rangeS * 1000);
  return history.filter(p => p.t >= cut).map(p => p[key]);
}
function timesOf(key, rangeS) {
  const now = history.length ? history[history.length - 1].t : new Date();
  const cut = new Date(now.getTime() - rangeS * 1000);
  return history.filter(p => p.t >= cut).map(p => p.t);
}
function drawMini() {
  const rT = 120, rR = 300;
  statLine("stats-summary-cpu", drawLine("chart-summary-cpu", convT(seriesOf("cpuTemp", rT)), unitSym(), toUnit(cfg.cpuWarnTemp), toUnit(cfg.cpuCritTemp), timesOf("cpuTemp", rT)), unitSym());
  statLine("stats-summary-load", drawLine("chart-summary-load", seriesOf("cpuLoad", rT), "%", 80, 95, timesOf("cpuLoad", rT)), "%");
  statLine("stats-ram", drawLine("chart-ram", seriesOf("ramUsed", rR), "MB", null, null, timesOf("ramUsed", rR)), "MB");
}
const convT = (arr) => cfg.unit === "F" ? arr.map(v => (v === null || v === undefined) ? null : v * 9 / 5 + 32) : arr;
function drawHist() {
  const range = parseInt($("hist-range").value, 10);
  const metric = $("hist-metric").value;
  const map = { cpuTemp: [unitSym(), toUnit(cfg.cpuWarnTemp), toUnit(cfg.cpuCritTemp)], cpuLoad: ["%", 80, 95], ramUsed: ["MB", null, null], gpuTemp: [unitSym(), toUnit(cfg.gpuWarnTemp), toUnit(cfg.gpuCritTemp)], batPct: ["%", null, null] };
  const [unit, w, c] = map[metric];
  const names = { cpuTemp: "Temperatura CPU", cpuLoad: "Carga CPU", ramUsed: "RAM usada", gpuTemp: "Temperatura GPU", batPct: "Nivel de batería" };
  $("hist-title").textContent = `${names[metric]} — ${$("hist-range").selectedOptions[0].textContent}`;
  const raw = seriesOf(metric, range);
  const data = (metric === "cpuTemp" || metric === "gpuTemp") ? convT(raw) : raw;
  statLine("stats-hist", drawLine("chart-hist", data, unit, w, c, timesOf(metric, range)), unit);
}
$("hist-range").addEventListener("change", drawHist);
$("hist-metric").addEventListener("change", drawHist);
$("btn-clear-hist").addEventListener("click", () => { history = []; drawMini(); drawHist(); });

// ---------- CSV / registro ----------
function toCsv() {
  const rows = [["timestamp", "cpu_temp_c", "cpu_load_pct", "ram_used_mb", "gpu_temp_c", "bat_pct"]];
  history.forEach(p => rows.push([p.t.toISOString(), p.cpuTemp ?? "", p.cpuLoad ?? "", p.ramUsed ?? "", p.gpuTemp ?? "", p.batPct ?? ""]));
  return rows.map(r => r.join(",")).join("\n");
}
function download(name, text, mime) {
  const a = document.createElement("a");
  a.href = URL.createObjectURL(new Blob([text], { type: mime || "text/plain" }));
  a.download = name; a.click();
  setTimeout(() => URL.revokeObjectURL(a.href), 5000);
}
$("btn-csv").addEventListener("click", () => {
  if (!history.length) { alert("Aún no hay datos para exportar."); return; }
  download(`temppanel-${new Date().toISOString().slice(0, 19).replace(/[:T]/g, "-")}.csv`, toCsv(), "text/csv");
});
$("btn-log").addEventListener("click", () => {
  if (!lastSnap) { alert("Aún no hay lectura."); return; }
  download(`temppanel-sesion-${Date.now()}.json`, JSON.stringify({ exportedAt: new Date().toISOString(), config: cfg, snapshot: lastSnap, history }, null, 2), "application/json");
});

// ---------- Config ----------
function applyCfg() {
  $("cfg-refresh").value = Math.round(cfg.refreshMs / 1000);
  $("cfg-cpu-warn").value = cfg.cpuWarnTemp; $("cfg-cpu-crit").value = cfg.cpuCritTemp;
  $("cfg-gpu-warn").value = cfg.gpuWarnTemp; $("cfg-gpu-crit").value = cfg.gpuCritTemp;
  $("cfg-unit").value = cfg.unit || "C";
  $("cfg-min").checked = !!cfg.startMinimized;
  $("cfg-startup").checked = !!cfg.startWithWindows;
  $("cfg-bat-on").checked = cfg.batteryEnabled !== false;
  $("cfg-bat-full").checked = cfg.batteryFull !== false;
  $("cfg-bat-th").value = cfg.batteryThreshold ?? 80;
  $("cfg-bat-re").value = cfg.batteryRearm ?? 75;
  $("cfg-bat-sound").checked = cfg.batterySound !== false;
  $("cfg-bat-mode").value = cfg.batteryMode === "balloon" ? "balloon" : "popup";
  $("cfg-auto-upd").checked = !!cfg.autoCheckUpdates;
  $("ver-info").innerHTML =
    `<div>TempPanel</div><div>${txt(cfg.version)} (WPF + WebView2)</div>` +
    `<div>Sensores</div><div>LibreHardwareMonitorLib 0.9.5</div>` +
    `<div>Interfaz</div><div>Microsoft.Web.WebView2 1.0.2903.40 (runtime del sistema)</div>` +
    `<div>.NET</div><div>8.0 autocontenido x64</div>`;
}
$("btn-save-refresh").addEventListener("click", () => {
  const s = Math.min(10, Math.max(1, parseInt($("cfg-refresh").value, 10) || 2));
  post({ cmd: "setRefresh", ms: s * 1000 });
});
$("btn-save-th").addEventListener("click", () => post({
  cmd: "setThresholds",
  cpuWarn: parseFloat($("cfg-cpu-warn").value) || 80,
  cpuCrit: parseFloat($("cfg-cpu-crit").value) || 90,
  gpuWarn: parseFloat($("cfg-gpu-warn").value) || 80,
  gpuCrit: parseFloat($("cfg-gpu-crit").value) || 90
}));
$("btn-admin").addEventListener("click", () => {
  if (confirm("Se reiniciará TempPanel con permisos de administrador. ¿Continuar?")) post({ cmd: "restartAdmin" });
});
$("cfg-unit").addEventListener("change", () => post({ cmd: "setUnit", unit: $("cfg-unit").value }));
$("cfg-min").addEventListener("change", () => post({ cmd: "setStartMinimized", value: $("cfg-min").checked }));
$("cfg-startup").addEventListener("change", () => {
  if ($("cfg-startup").checked && !confirm("TempPanel se iniciará con Windows. Puedes desactivarlo aquí o en el Administrador de tareas > Aplicaciones de arranque. ¿Continuar?")) {
    $("cfg-startup").checked = false; return;
  }
  post({ cmd: "setStartWithWindows", value: $("cfg-startup").checked });
});
$("btn-reset").addEventListener("click", () => {
  if (confirm("¿Restablecer todos los ajustes a sus valores predeterminados?")) post({ cmd: "resetSettings" });
});
$("btn-logs").addEventListener("click", () => post({ cmd: "openLogs" }));
$("btn-save-bat").addEventListener("click", () => {
  let th = parseFloat($("cfg-bat-th").value) || 80;
  let re = parseFloat($("cfg-bat-re").value) || 75;
  th = Math.min(100, Math.max(50, th));
  re = Math.min(99, Math.max(10, re));
  if (re >= th) { alert("El segundo valor debe ser menor que el primero."); return; }
  post({ cmd: "setBatteryAlert", enabled: $("cfg-bat-on").checked, threshold: th, rearm: re, sound: $("cfg-bat-sound").checked, mode: $("cfg-bat-mode").value, full: $("cfg-bat-full").checked });
});
$("btn-test-bat").addEventListener("click", () => post({ cmd: "testBatteryAlert" }));
$("btn-check-upd").addEventListener("click", () => post({ cmd: "checkUpdates" }));
$("btn-dl-upd").addEventListener("click", () => {
  if (!pendingUpdate) return;
  if (confirm(`Se descargará TempPanel ${pendingUpdate.latest} y se abrirá su instalador. ¿Continuar?`))
    post({ cmd: "downloadUpdate", url: pendingUpdate.url, tag: pendingUpdate.latest });
});
$("cfg-auto-upd").addEventListener("change", () => post({ cmd: "setAutoUpdates", value: $("cfg-auto-upd").checked }));
applyCfg();

// Arnés DOM: carga el bundle REAL compilado (Frontend/index.html + assets/app.js)
// en un JSDOM, emula el puente de WebView2 e inyecta snapshots SINTÉTICOS
// (solo pruebas: esos valores jamás salen de aquí ni llegan a la app).
//
// Requisitos: haber compilado antes la interfaz:
//   cd C:\tempanel\TempPanel\Web ; npm run build
// Ejecutar:
//   node C:\tempanel\uitest\run.js
const fs = require('fs');
const path = require('path');
const { JSDOM, VirtualConsole } = require(path.join(__dirname, 'node_modules', 'jsdom'));

const feDir = 'C:/tempanel/TempPanel/Frontend';
const html = fs.readFileSync(path.join(feDir, 'index.html'), 'utf8');
const tag = html.match(/<script src="([^"]+)"><\/script>/);
if (!tag) {
  console.log('FALLO: index.html no referencia ningún bundle (¿compilaste la interfaz?)');
  process.exit(1);
}
const bundlePath = path.join(feDir, tag[1].replace(/^\.\//, ''));
if (!fs.existsSync(bundlePath)) {
  console.log('FALLO: falta ' + bundlePath + ' -> ejecuta: cd TempPanel\\Web ; npm run build');
  process.exit(1);
}
const js = fs.readFileSync(bundlePath, 'utf8');
const cssPath = path.join(feDir, 'assets', 'style.css');
const css = fs.existsSync(cssPath) ? fs.readFileSync(cssPath, 'utf8') : '';

function anyStub() {
  const f = function () {};
  return new Proxy(f, {
    get: (t, p) => {
      if (p === Symbol.toPrimitive) return () => 0;
      return (...a) => anyStub();
    },
    set: () => true,
    apply: () => anyStub()
  });
}

let msgHandler = null;
const sent = [];
const blobs = [];      // descargas capturadas (CSV, registro de sesión)
const jsErrors = [];   // errores de ejecución de la página (los "not implemented" de JSDOM no cuentan)
const vc = new VirtualConsole();
vc.on("jsdomError", (e) => {
  const m = String((e && e.message) || e);
  if (!/Not implemented/i.test(m)) jsErrors.push(m);
});
const dom = new JSDOM(html, {
  url: 'https://temppanel.local/index.html',
  runScripts: 'outside-only',
  pretendToBeVisual: true,
  virtualConsole: vc,
  beforeParse(window) {
    window.chrome = {
      webview: {
        postMessage: (m) => sent.push(m),
        addEventListener: (ev, cb) => { if (ev === 'message') msgHandler = cb; }
      }
    };
    // JSDOM no dibuja canvas ni tiene ResizeObserver: se emulan (no se comprueba el píxel, sí el DOM).
    window.HTMLCanvasElement.prototype.getContext = function () { return anyStub(); };
    window.ResizeObserver = class { observe() {} unobserve() {} disconnect() {} };
    window.URL.createObjectURL = (b) => { blobs.push(b); return 'blob:test'; };
    window.URL.revokeObjectURL = () => {};
  }
});
dom.window.eval(js);

const doc = dom.window.document;
const flush = (ms = 40) => new Promise((r) => setTimeout(r, ms));
const txtOf = (sel) => {
  const el = doc.querySelector(sel);
  return el ? el.textContent.replace(/\s+/g, ' ').trim() : 'FALTA:' + sel;
};
const has = (sel, needle) => txtOf(sel).includes(needle);
const send = (type, data) => { if (msgHandler) msgHandler({ data: { type, data } }); };
const clickPage = (p) => {
  const b = Array.from(doc.querySelectorAll('.nav-btn')).find((x) => x.dataset.page === p);
  if (b) b.click();
};

// Snapshots sintéticos con marcas de tiempo REALES (ahora) para que entren en la ventana.
function makeSnap(i) {
  const t = new Date(Date.now() - (5 - i) * 5000);
  return {
    timestamp: t.toISOString(), timestampLocal: t.toISOString(),
    cpu: {
      processorName: '13th Gen Intel Core i7-13620H', packageTemp: 54.3 + i * 1.1,
      packageTempSource: 'CPU Package (test)', coreTemps: [51.2 + i, 55.9 + i], coreTempNames: ['P-Core #1', 'E-Core #1'],
      coreTempMin: 51.2 + i, coreTempMax: 55.9 + i, coreTempAvg: 53.5 + i, clockAvgMhz: 2450,
      totalLoad: 21.4 + i, coreLoads: [20.1, 22.3], coreLoadNames: ['CPU Core #1', 'CPU Core #2'],
      coreClocks: [2400, 2500], power: 12.5, throttling: false,
      tjMaxNames: ['P-Core #1 Distance to TjMax'], tjMaxDistances: [45.7], tjMaxC: 100,
      coresPhysical: 10, coresLogical: 16
    },
    gpu: { name: 'Intel UHD', temp: i === 2 ? null : 48.0, tempSource: i === 2 ? null : 'GPU Temperature (test)', load: 6.5, clock: 1200, memoryUsedMb: null, available: true, note: null },
    memory: { totalMb: 24237, usedMb: 18916, availableMb: 5321, percentUsed: 78.0, source: 'test' },
    battery: {
      present: true, percent: 79.7, status: 'Cargando · retenida por el equipo', charging: true,
      percentSource: 'capacidades reales (root\\wmi)', designCapacityMwh: 50000, fullChargeCapacityMwh: 52800,
      remainingCapacityMwh: 42090, temperatureC: null, chargeLevelPct: 67.0, dischargeRateW: null, chargeRateW: null,
      remainingMinutes: 42, voltageV: 12.1, powerLine: 'Conectado a la red',
      trendPctPerHour: 0.42, held: false, heldMinutes: null, note: 'test'
    },
    fans: [], extraTempNames: [], extraTemps: [], storage: [],
    allSensors: [{ hardware: 'CPU', hardwareType: 'Cpu', type: 'Temperature', name: 'CPU Package', identifier: 'x', value: 54.3, min: 40, max: 70, unit: '°C' }],
    sensorOptions: [{ identifier: 'x', name: 'CPU Package', value: 54.3 }, { identifier: 'y', name: 'CPU Core #1', value: 51.2 }],
    session: { cpuTempMin: 49.1, cpuTempMax: 61.0, cpuTempAvg: 54.8, cpuLoadMax: 60.2, cpuLoadAvg: 20.1, gpuTempMin: 45.0, gpuTempMax: 50.2, samples: 120 },
    meta: {
      cpuName: '13th Gen Intel Core i7-13620H', installedRamMb: 24237, appUptime: '10min', refreshMs: 2000,
      lastReadIso: t.toISOString(), lastReadLocal: t.toISOString(), isAdmin: true, preferredCpuSensor: 'auto',
      bootTimeLocal: '2026-10-13 08:00:00', appMemoryMb: 168.4
    },
    warnings: [],
    diagnostics: { isAdmin: true, hvciEnabled: false, cpuTempSensors: 23, cpuTempWithValue: 3, totalSensors: 88, sensorsWithValue: 70, driverBlocked: false, driverState: 'ok' }
  };
}

let fail = 0;
function check(name, ok) {
  console.log((ok ? 'OK: ' : 'FALLO: ') + name);
  if (!ok) fail++;
}

(async () => {
  await flush(80);
  if (!msgHandler) { console.log('FALLO: sin handler de mensajes (¿el bundle no arrancó?)'); process.exit(1); }
  check('bundle montado (#app con contenido)', doc.getElementById('app').innerHTML.length > 500);

  send('config', {
    refreshMs: 2000, cpuWarnTemp: 85, cpuCritTemp: 95, gpuWarnTemp: 80, gpuCritTemp: 90,
    preferredCpuSensor: 'auto', unit: 'C', glass: true, miniSeries: 'cpu,temp,bat,ram,gpu',
    miniExpanded: false, firstRun: true, version: '3.0.0',
    adaptivePerf: true, hotkeyEnabled: true, taskbarBadge: true, effectiveMs: 2000, perf: 'normal',
    os: {
      name: 'Windows 11 24H2', build: 26200, win11: true, accent: '#f472b6', theme: 'light',
      highContrast: true, powerSaver: false, hotkey: true, glass: true
    }
  });
  for (let i = 0; i < 5; i++) send('snapshot', makeSnap(i));
  await flush(120);

  check('modo cristal activo', doc.body.className.includes('glass') && !doc.body.className.includes('no-glass'));
  check('v3: alto contraste aplicado (body.hc)', doc.body.classList.contains('hc'));
  check('v3: acento de Windows en variable CSS',
    doc.documentElement.style.getPropertyValue('--os-accent') === '#f472b6');
  check('v3: modo de rendimiento expuesto', doc.body.dataset.perf === 'normal');
  check('barra de título con la página', txtOf('#tb-page') === 'Resumen');
  check('botones de ventana', !!doc.getElementById('win-min') && !!doc.getElementById('win-max') && !!doc.getElementById('win-close'));
  send('hover', { id: 'win-max' });
  await flush(40);
  check('v3: hover nativo del maximizar', doc.getElementById('win-max').classList.contains('os-hover'));
  send('hover', { id: null });
  await flush(40);
  check('v3: hover nativo se retira', !doc.getElementById('win-max').classList.contains('os-hover'));
  check('navegación con 10 páginas', doc.querySelectorAll('.nav-btn').length === 10);
  check('resumen: temperatura CPU', has('#summary-cards', '58.7'));
  check('resumen: medidor SVG', !!doc.querySelector('#summary-cards svg.gauge path.arc'));
  check('resumen: anillo de batería 79.7 %', has('#summary-cards', '79.7'));
  check('resumen: minigráficos (>=2 puntos)', doc.querySelectorAll('#summary-cards svg.spark').length >= 2);
  check('resumen: gráficas canvas', doc.querySelectorAll('#summary-cards ~ .grid canvas, .page canvas').length >= 2);
  check('resumen: memoria de la app', has('#page-resumen', '168.4'));
  check('resumen: estadísticas reales', has('#page-resumen', 'lecturas') || has('#page-resumen', 'Mín:'));
  check('cristal: transparencia en variable CSS', css.includes('--panel-a'));

  // ---- v3.0.0: rendimiento adaptativo (gobernador) ----
  send('config', { perf: 'background', effectiveMs: 3000 });
  await flush(60);
  check('v3: chip de rendimiento (segundo plano)', has('#perf-chip', 'Segundo plano'));
  send('snapshot', makeSnap(4));
  await flush(60);
  check('v3: ritmo efectivo mostrado', has('#conn-text', 'cada 3 s'));
  send('config', { perf: 'normal', effectiveMs: 2000 });
  await flush(60);
  check('v3: chip desaparece en primer plano', !doc.getElementById('perf-chip'));

  // ---- v3.0.0: navegación desde la bandeja (mensaje nativo) ----
  send('navigate', { page: 'cpu' });
  await flush(60);
  check('v3: navegación desde la bandeja', txtOf('#tb-page') === 'CPU');

  // ---- CPU ----
  clickPage('cpu');
  await flush(80);
  check('cpu: ficha de sesión (mín. 49.1)', has('#page-cpu', '49.1'));
  check('cpu: media de núcleos', has('#page-cpu', '57.5'));
  check('cpu: núcleo más caliente', has('#page-cpu', 'E1'));
  check('cpu: tendencia calculada', has('#page-cpu', 'observado') || has('#page-cpu', 'Reuniendo datos'));
  check('cpu: barras por núcleo', doc.querySelectorAll('#page-cpu .bar-row').length >= 4);
  check('cpu: TjMax visible', has('#page-cpu', '100'));
  const sel = doc.getElementById('cpu-sensor-select');
  check('cpu: selector con sensorOptions', !!sel && sel.textContent.includes('CPU Core #1'));

  // ---- GPU ----
  clickPage('gpu');
  await flush(80);
  check('gpu: temperatura 48.0', has('#gpu-cards', '48.0'));
  check('gpu: sesión (mín. 45.0)', has('#page-gpu', '45.0'));

  // ---- RAM ----
  clickPage('ram');
  await flush(80);
  check('ram: uso total', has('#ram-cards', '18.916') || has('#ram-cards', '18.916'));
  check('ram: gráfica', !!doc.querySelector('#page-ram canvas'));

  // ---- Batería ----
  clickPage('bateria');
  await flush(80);
  check('batería: 1 decimal', has('#bat-cards', '79.7'));
  check('batería: fuente real', has('#page-bateria', 'capacidades reales'));
  check('batería: tendencia observada', has('#page-bateria', '0.42'));
  check('batería: estado real de carga', has('#page-bateria', 'Cargando por red'));
  check('batería: flujo de carga visible', !!doc.querySelector('#page-bateria .flow'));

  // ---- v3.0.0: equipo sin batería (sobremesa): se explica, no se inventa ----
  const desk = makeSnap(0);
  desk.battery = { present: false, percent: null, charging: null, status: 'No disponible', note: 'sin batería' };
  send('snapshot', desk);
  await flush(80);
  clickPage('bateria');
  await flush(60);
  check('v3: sin batería se explica', has('#page-bateria', 'no expone una batería'));
  clickPage('resumen');
  await flush(60);
  check('v3: resumen sin batería', has('#summary-cards', 'no expone batería'));

  // ---- Refrigeración ----
  clickPage('refrigeracion');
  await flush(80);
  check('refrigeración: aviso sin RPM', has('#page-refrigeracion', 'No disponible'));

  // ---- Sensores ----
  clickPage('sensores');
  await flush(80);
  check('sensores: tabla con filas', doc.querySelectorAll('#sensor-table tbody tr').length >= 1);
  check('sensores: fila CPU Package', has('#sensor-table', 'CPU Package'));
  const th = Array.from(doc.querySelectorAll('#sensor-table thead th')).find((t) => t.textContent.includes('Valor'));
  if (th) { th.click(); await flush(60); }
  check('sensores: orden por Valor', has('#sensor-table thead', '▲') || has('#sensor-table thead', '▼'));

  // ---- v3.0.0: virtualización con muchos sensores ----
  const big = makeSnap(4);
  big.allSensors = Array.from({ length: 400 }, (_, i) => ({
    hardware: 'CPU', hardwareType: 'Cpu', type: 'Temperature',
    name: 'Sensor ' + i, identifier: 'id' + i, value: i, min: 0, max: 100, unit: '°C'
  }));
  send('snapshot', big);
  await flush(80);
  check('v3: contador con 400 sensores', has('#sensor-count', '400 / 400'));
  check('v3: filas virtualizadas (<100)', doc.querySelectorAll('#sensor-table tbody tr.row').length < 100);
  const wrap = doc.getElementById('sensor-table').parentElement;
  wrap.scrollTop = 34 * 300;
  wrap.dispatchEvent(new dom.window.Event('scroll'));
  await flush(80);
  check('v3: desplazamiento virtual (fila 300)', has('#sensor-table', 'Sensor 300'));

  // ---- Historial ----
  clickPage('historial');
  await flush(80);
  check('historial: chips de rango y métrica', doc.querySelectorAll('#page-historial .chip').length >= 9);
  check('historial: gráfica grande', !!doc.querySelector('#page-historial canvas'));
  check('historial: estadísticas', has('#page-historial', 'Mín:'));
  const csvBtn = doc.getElementById('btn-csv');
  if (csvBtn) { csvBtn.click(); await flush(80); }
  let csv = '';
  if (blobs.length) {
    const b = blobs[blobs.length - 1];
    try { csv = b.text ? await b.text() : ''; } catch (e) { csv = ''; }
  }
  const csvLines = csv.split('\n');
  check('historial: CSV con cabecera y datos',
    csvLines.length >= 6 && csvLines[0] === 'timestamp,cpu_temp_c,cpu_load_pct,ram_used_mb,gpu_temp_c,bat_pct');
  check('historial: CSV deja vacíos los nulos', csv.includes(',,'));

  // ---- Ayuda ----
  clickPage('ayuda');
  await flush(80);
  check('ayuda: diagnóstico funcionando', has('#page-ayuda', 'Funcionando'));
  check('ayuda: fila de driver PawnIO', has('#page-ayuda', 'PawnIO'));
  send('defenderStatus', { ok: true, excluded: true, paths: ['C:\\x'], message: 'TempPanel está excluido de Defender.' });
  send('hvciStatus', { result: 'Ok', enabled: false });
  await flush(60);
  check('ayuda: defender activo', has('#page-ayuda', 'ACTIVA'));
  check('ayuda: estado de HVCI', has('#hvci-status', 'DESACTIVADA'));

  // ---- Configuración ----
  clickPage('config');
  await flush(80);
  check('config: chips de mini ventana', doc.querySelectorAll('#page-config .chip').length === 5);
  check('config: versión y dependencias', has('#page-config', 'Svelte'));
  check('config: transparencia ajustable', !!doc.getElementById('cfg-alpha'));
  check('config: integración con Windows', has('#page-config', 'Integración con Windows'));
  check('config: sistema real', has('#page-config', 'Windows 11 24H2'));
  check('config: atajo global', !!doc.getElementById('cfg-hotkey'));
  check('config: distintivo de barra de tareas', !!doc.getElementById('cfg-taskbar-badge'));
  check('config: rendimiento adaptativo', !!doc.getElementById('cfg-adaptive'));
  check('config: botones de Windows', !!doc.getElementById('btn-win-colors') && !!doc.getElementById('btn-win-battery'));
  const hk = doc.getElementById('cfg-hotkey');
  if (hk) { hk.click(); await flush(60); }
  check('config: comando de atajo enviado', sent.some((m) => m.cmd === 'setHotkey'));
  const wc = doc.getElementById('btn-win-colors');
  if (wc) { wc.click(); await flush(60); }
  check('config: abrir Colores de Windows', sent.some((m) => m.cmd === 'openWindowsSettings' && m.key === 'colors'));

  // ---- Comandos que la app nativa espera recibir ----
  clickPage('cpu');
  await flush(60);
  check('comando view enviado', sent.some((m) => m.cmd === 'view' && m.page === 'cpu'));

  const CMDS = ['view', 'winMin', 'winMax', 'winClose', 'getSnapshot', 'openMini', 'resetMiniPos',
    'setFirstRunDone', 'setRefresh', 'setUnit', 'setCpuSensor', 'setThresholds', 'setMiniOptions',
    'setBatteryAlert', 'testBatteryAlert', 'setStartMinimized', 'setStartWithWindows', 'setAutoUpdates',
    'resetSettings', 'openLogs', 'restartAdmin', 'checkUpdates', 'downloadUpdate', 'retryLaunch',
    'setHvci', 'rebootNow', 'defenderStatus', 'defenderAdd', 'defenderRemove',
    // v3.0.0
    'setHotkey', 'setTaskbarBadge', 'setAdaptivePerf', 'health', 'openWindowsSettings'];
  const missing = CMDS.filter((c) => !js.includes(c));
  check('protocolo completo (' + CMDS.length + ' comandos)', missing.length === 0);
  if (missing.length) console.log('   faltan: ' + missing.join(', '));

  // ---- Bienvenida (primera ejecución) ----
  check('bienvenida visible', !!doc.getElementById('welcome'));
  if (doc.getElementById('btn-welcome-ok')) {
    doc.getElementById('btn-welcome-ok').click();
    await flush(60);
    check('bienvenida cerrada + comando', !doc.getElementById('welcome') && sent.some((m) => m.cmd === 'setFirstRunDone'));
  }

  // ---- Alerta crítica con umbral bajo (histéresis) ----
  const hot = makeSnap(9);
  hot.cpu.packageTemp = 96.0;
  send('snapshot', hot);
  await flush(80);
  check('banner crítico a 96 °C', !!doc.getElementById('alert-banner') && doc.getElementById('alert-banner').className.includes('crit'));
  check('v3: salud crítica enviada a la barra de tareas',
    sent.some((m) => m.cmd === 'health' && m.cls === 'crit'));

  // ---- Pausa: congela historial ----
  clickPage('resumen');
  await flush(60);
  const pause = doc.getElementById('btn-pause');
  if (pause) { pause.click(); await flush(40); }
  check('pausa activa', (doc.getElementById('btn-pause') || {}).textContent.includes('Reanudar'));

  check('sin errores de ejecución en la página', jsErrors.length === 0);
  if (jsErrors.length) console.log('   ' + jsErrors.slice(0, 3).join('\n   '));

  console.log(fail ? '\n' + fail + ' comprobaciones FALLIDAS' : '\nTODO OK');
  process.exit(fail ? 1 : 0);
})();

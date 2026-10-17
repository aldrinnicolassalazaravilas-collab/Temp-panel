// Harness DOM: verifica el pipeline de visualización con un snapshot SINTÉTICO
// (solo pruebas: estos valores jamás salen de aquí ni se envían a la app real).
const fs = require('fs');
const path = require('path');
const { JSDOM } = require(path.join(__dirname, 'node_modules', 'jsdom'));

const feDir = 'C:/tempanel/TempPanel/Frontend';
const html = fs.readFileSync(path.join(feDir, 'index.html'), 'utf8');
const js = fs.readFileSync(path.join(feDir, 'app.js'), 'utf8');

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
const dom = new JSDOM(html, {
  url: 'https://temppanel.local/index.html',
  runScripts: 'outside-only',
  beforeParse(window) {
    window.chrome = {
      webview: {
        postMessage: () => {},
        addEventListener: (ev, cb) => { if (ev === 'message') msgHandler = cb; }
      }
    };
    window.HTMLCanvasElement.prototype.getContext = function () { return anyStub(); };
  }
});
dom.window.eval(js);
if (!msgHandler) { console.log('FALLO: sin handler de mensajes'); process.exit(1); }

const snap = {
  timestamp: '2026-10-13T12:00:00Z', timestampLocal: '2026-10-13 12:00:00',
  cpu: { processorName: '13th Gen Intel Core i7-13620H', packageTemp: 54.3,
    packageTempSource: 'CPU Package (test)', coreTemps: [51.2, 52.0], coreTempNames: ['P-Core #1', 'P-Core #2'],
    coreTempMin: 51.2, coreTempMax: 52.0, coreTempAvg: 51.6, clockAvgMhz: 2450,
    totalLoad: 21.4, coreLoads: [20.1, 22.3], coreLoadNames: ['CPU Core #1', 'CPU Core #2'],
    coreClocks: [2400, 2500], power: 12.5, throttling: false,
    tjMaxNames: ['P-Core #1 Distance to TjMax'], tjMaxDistances: [45.7], tjMaxC: 100, coresPhysical: 10, coresLogical: 16 },
  gpu: { name: 'Intel UHD', temp: 48.0, tempSource: 'GPU Temperature (test)', load: 6.5, clock: 1200, memoryUsedMb: null, available: true, note: null },
  memory: { totalMb: 24237, usedMb: 18916, availableMb: 5321, percentUsed: 78.0, source: 'test' },
  battery: { present: true, percent: 67.0, status: 'Cargando', designCapacityMwh: 50000, fullChargeCapacityMwh: 52550,
    remainingCapacityMwh: 36000, temperatureC: null, chargeLevelPct: 67.0, dischargeRateW: null,
    remainingMinutes: 42, voltageV: 12.1, powerLine: 'Conectado a la red', note: 'test' },
  fans: [], extraTempNames: [], extraTemps: [], storage: [],
  allSensors: [{ hardware: 'CPU', hardwareType: 'Cpu', type: 'Temperature', name: 'CPU Package', identifier: 'x', value: 54.3, min: 40, max: 70, unit: '°C' }],
  session: { cpuTempMin: 49.1, cpuTempMax: 61.0, cpuTempAvg: 54.8, cpuLoadMax: 60.2, cpuLoadAvg: 20.1, gpuTempMin: 45.0, gpuTempMax: 50.2, samples: 120 },
  meta: { cpuName: '13th Gen Intel Core i7-13620H', installedRamMb: 24237, appUptime: '10min', refreshMs: 2000,
    lastReadIso: '2026-10-13T12:00:00Z', lastReadLocal: '2026-10-13 12:00:00', isAdmin: true, preferredCpuSensor: 'auto', bootTimeLocal: '2026-10-13 08:00:00' },
  warnings: [],
  diagnostics: { isAdmin: true, hvciEnabled: false, cpuTempSensors: 23, cpuTempWithValue: 3, totalSensors: 88, sensorsWithValue: 70, driverBlocked: false, driverState: 'ok' }
};

msgHandler({ data: { type: 'config', data: { refreshMs: 2000, cpuWarnTemp: 85, cpuCritTemp: 95, gpuWarnTemp: 80, gpuCritTemp: 90, preferredCpuSensor: 'auto', unit: 'C', miniSeries: 'cpu,temp,bat,ram,gpu', miniExpanded: false } } });
msgHandler({ data: { type: 'snapshot', data: snap } });
// segunda lectura para que las gráficas tengan >= 2 puntos
snap.timestampLocal = '2026-10-13 12:00:02'; snap.cpu.packageTemp = 55.1; snap.cpu.totalLoad = 23.0;
msgHandler({ data: { type: 'snapshot', data: snap } });

const doc = dom.window.document;
const get = (id) => (doc.getElementById(id) ? doc.getElementById(id).textContent : 'FALTA-ID');
const checks = [
  ['resumen cpu', get('summary-cards').includes('54.3') || get('summary-cards').includes('55.1')],
  ['gauge cpu', doc.querySelector('#summary-cards svg') !== null],
  ['sesion cpu min', get('cpu-session').includes('49.1')],
  ['nucleos P1', get('core-temps').includes('P1')],
  ['media nucleos', get('cpu-head').includes('51.6')],
  ['frecuencia media', get('cpu-head').includes('2450')],
  ['gpu temp', get('gpu-cards').includes('48.0')],
  ['sesion gpu', get('gpu-session').includes('45.0')],
  ['diag funcionando', get('diag-info').includes('Funcionando')],
  ['tjmax visible', get('cpu-head').includes('100')],
  ['nucleo caliente', get('cpu-session').includes('P2')],
  ['nucleo frio', get('cpu-session').includes('P1')],
  ['bienvenida visible', !doc.getElementById('welcome').className.includes('hidden')],
  ['chips mini on', doc.getElementById('chip-mini-cpu').className.includes('on')],
  ['tabla sensor', get('sensor-table').includes('CPU Package')],
  ['banner oculto (54C<85)', doc.getElementById('alert-banner').className.includes('hidden')],
  ['stats historial', get('stats-hist').length > 0]
];
let fail = 0;
for (const [name, ok] of checks) { console.log((ok ? 'OK: ' : 'FALLO: ') + name); if (!ok) fail++; }
// alerta con umbral bajo: reinyecta snapshot caliente
snap.cpu.packageTemp = 96.0;
msgHandler({ data: { type: 'snapshot', data: snap } });
const crit = doc.getElementById('alert-banner').className.includes('crit');
console.log((crit ? 'OK: ' : 'FALLO: ') + 'banner critico a 96C');
if (!crit) fail++;
// Defender: el mensaje del backend se pinta en Ayuda
msgHandler({ data: { type: 'defenderStatus', data: { ok: true, excluded: true, paths: ['C:\\x'], message: 'TempPanel está excluido de Defender.' } } });
const defOk = get('def-advice').includes('ACTIVA');
console.log((defOk ? 'OK: ' : 'FALLO: ') + 'defender estado');
if (!defOk) fail++;
process.exit(fail ? 1 : 0);

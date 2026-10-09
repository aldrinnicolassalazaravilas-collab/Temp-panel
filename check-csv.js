// Verifica toCsv() con historial simulado
const fs = require('fs');
let src = fs.readFileSync('C:/tempanel/TempPanel/Frontend/app.js', 'utf8');
const m = src.match(/function toCsv\(\) \{[\s\S]*?\n\}/);
if (!m) { console.log('FALLO: toCsv no encontrada'); process.exit(1); }
let history = [
  { t: new Date('2026-10-09T17:00:00.000Z'), cpuTemp: 55.2, cpuLoad: 21.4, ramUsed: 18916, gpuTemp: null },
  { t: new Date('2026-10-09T17:00:02.000Z'), cpuTemp: null, cpuLoad: 12.7, ramUsed: 18974, gpuTemp: null }
];
const toCsv = new Function('history', m[0] + '\nreturn toCsv();');
const csv = toCsv(history);
console.log(csv);
const lines = csv.split('\n');
console.log('---');
console.log(lines[0] === 'timestamp,cpu_temp_c,cpu_load_pct,ram_used_mb,gpu_temp_c' ? 'OK: cabecera' : 'FALLO: cabecera');
console.log(lines.length === 3 ? 'OK: 2 filas + cabecera' : 'FALLO: filas=' + lines.length);
console.log(lines[1].includes('55.2') && lines[2].startsWith('2026-10-09T17:00:02') ? 'OK: valores y nulos' : 'FALLO: valores');

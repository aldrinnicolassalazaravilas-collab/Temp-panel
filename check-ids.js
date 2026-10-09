const fs = require('fs');
const h = fs.readFileSync('C:/tempanel/TempPanel/Frontend/index.html', 'utf8');
const j = fs.readFileSync('C:/tempanel/TempPanel/Frontend/app.js', 'utf8');
const ids = new Set([...h.matchAll(/id="([^"]+)"/g)].map(m => m[1]));
const refs = [...j.matchAll(/\$\("([^"]+)"\)/g)].map(m => m[1]);
const missing = [...new Set(refs)].filter(id => !ids.has(id));
console.log('IDs en HTML: ' + ids.size);
console.log('IDs referenciados en JS: ' + new Set(refs).size);
console.log(missing.length ? 'FALTAN: ' + missing.join(', ') : 'OK: todas las referencias existen');

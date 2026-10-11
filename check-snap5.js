const fs = require('fs');
const s = JSON.parse(fs.readFileSync('C:/Users/LENOVO/AppData/Local/Temp/temppanel-selftest.json', 'utf8'));
console.log('cores:', s.Cpu.CoresPhysical + 'P/' + s.Cpu.CoresLogical + 'L', '| boot:', s.Meta.BootTimeLocal);

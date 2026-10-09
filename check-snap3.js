const fs = require('fs');
const s = JSON.parse(fs.readFileSync('C:/Users/LENOVO/AppData/Local/Temp/temppanel-selftest.json', 'utf8'));
console.log('Hwinfo:', JSON.stringify(s.Hwinfo));
console.log('CoreLoadNames[' + s.Cpu.CoreLoadNames.length + ']:', JSON.stringify(s.Cpu.CoreLoadNames.slice(0, 4)));
console.log('CoreTempNames:', JSON.stringify(s.Cpu.CoreTempNames));
console.log('MemSource:', s.Memory.Source);
console.log('PowerLine:', s.Battery.PowerLine, '| Session.samples:', s.Session.Samples);

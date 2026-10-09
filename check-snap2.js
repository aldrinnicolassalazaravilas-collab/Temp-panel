const fs = require('fs');
const s = JSON.parse(fs.readFileSync('C:/Users/LENOVO/AppData/Local/Temp/temppanel-selftest.json', 'utf8'));
console.log('PowerLine:', s.Battery.PowerLine);
console.log('Session:', JSON.stringify(s.Session));
console.log('Storage:', JSON.stringify(s.Storage));
console.log('ChargeLevel:', s.Battery.ChargeLevelPct, 'DischargeRate:', s.Battery.DischargeRateW,
  'RemMin:', s.Battery.RemainingMinutes, 'Volt:', s.Battery.VoltageV);
console.log('TjMaxNames:', s.Cpu.TjMaxNames.length, 'TjMaxDist:', s.Cpu.TjMaxDistances.length);
const stor = s.AllSensors.filter(x => x.HardwareType === 'Storage');
console.log('Storage sensors en AllSensors:', stor.length);

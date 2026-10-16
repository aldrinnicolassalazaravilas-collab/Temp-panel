// Sonda mínima: MISMA librería (LHM 0.9.6), OTRO proceso (consola, host dotnet real).
// Si aquí salen los mismos nulos que en TempPanel, el límite es librería/entorno,
// no el código de la app.
using LibreHardwareMonitor.Hardware;

static string? V(float? v) => v.HasValue ? $"{v.Value:0.00}" : "null";

var computer = new Computer
{
    IsCpuEnabled = true,
    IsGpuEnabled = true,
    IsMemoryEnabled = true,
    IsMotherboardEnabled = true,
    IsBatteryEnabled = true
};
computer.Open();

for (int i = 0; i < 2; i++)
{
    foreach (var hw in computer.Hardware) hw.Update();
    System.Threading.Thread.Sleep(800);
}

int tTotal = 0, tVal = 0;
foreach (var hw in computer.Hardware)
{
    Console.WriteLine($"HW: {hw.Name} [{hw.HardwareType}]");
    foreach (var sub in hw.SubHardware)
    {
        Console.WriteLine($"  SUB: {sub.Name}");
        foreach (var s in sub.Sensors)
            Console.WriteLine($"    [{s.SensorType}] {s.Name} = {V(s.Value)}");
    }
    foreach (var s in hw.Sensors)
    {
        if (hw.HardwareType == HardwareType.Cpu && s.SensorType == SensorType.Temperature)
        {
            tTotal++;
            if (s.Value.HasValue) tVal++;
        }
        if (s.SensorType is SensorType.Temperature or SensorType.Load or SensorType.Clock or SensorType.Power)
            Console.WriteLine($"  [{s.SensorType}] {s.Name} = {V(s.Value)}");
    }
}
Console.WriteLine($"CPU temps con valor: {tVal}/{tTotal}");
computer.Close();

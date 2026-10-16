using System.Diagnostics;
using System.IO;
using System.Management;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;

namespace TempPanel.Sensors;

/// <summary>
/// Lee sensores vía LibreHardwareMonitor + APIs oficiales de Windows.
/// Nunca inventa valores: si un sensor no existe, el campo queda null.
/// </summary>
public sealed class HardwareMonitor : IDisposable
{
    private readonly Computer _computer;
    private readonly DateTime _startTime = DateTime.Now;
    private readonly object _lock = new();
    private bool _disposed;

    // RAM vía PerformanceCounter (más fiable que LHM en portátiles)
    private PerformanceCounter? _availMbCounter;

    // Datos del equipo que no cambian: se consultan una sola vez.
    private static int? _coresPhysical;
    private static int? _coresLogical;
    private static string _bootTimeLocal = "";
    private static bool _staticInfoLoaded;

    // Estadísticas de sesión (mín/máx/promedio desde el arranque de la app)
    private double _cpuTempSum;
    private double _cpuLoadSum;
    private int _sessionSamples;
    private int _cpuTempSamples;
    private int _cpuLoadSamples;
    private double? _gpuTempMin;
    private double? _gpuTempMax;
    private double? _cpuTempMin;
    private double? _cpuTempMax;
    private double? _cpuLoadMax;

    public int RefreshMs { get; set; } = 2000;
    public string PreferredCpuSensor { get; set; } = "auto";

    public HardwareMonitor()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true,
            IsNetworkEnabled = false,
            // Almacenamiento desactivado: en este equipo no publica ningún sensor
            // y su listener generaba excepciones de fondo que solo ensuciaban el log.
            IsStorageEnabled = false,
            IsBatteryEnabled = true,
            IsPsuEnabled = false
        };
        try { _computer.Open(); }
        catch (Exception ex) { AppLogger.Error("No se pudo abrir LibreHardwareMonitor: " + ex.Message); }

        try
        {
            _availMbCounter = new PerformanceCounter("Memory", "Available MBytes");
            _ = _availMbCounter.NextValue();
        }
        catch (Exception ex) { AppLogger.Error("PerformanceCounter memoria no disponible: " + ex.Message); }
    }

    public static bool IsRunningAsAdmin()
    {
        try
        {
            var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            var p = new System.Security.Principal.WindowsPrincipal(id);
            return p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private static void EnsureStaticInfo()
    {
        if (_staticInfoLoaded) return;
        _staticInfoLoaded = true;
        try
        {
            using var s = new ManagementObjectSearcher("SELECT NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
            foreach (ManagementObject mo in s.Get())
            {
                _coresPhysical = ToNullableInt(mo["NumberOfCores"]);
                _coresLogical = ToNullableInt(mo["NumberOfLogicalProcessors"]);
                break;
            }
        }
        catch (Exception ex) { AppLogger.Error("StaticInfo CPU: " + ex.Message); }
        try
        {
            using var s = new ManagementObjectSearcher("SELECT LastBootUpTime FROM Win32_OperatingSystem");
            foreach (ManagementObject mo in s.Get())
            {
                string? raw = mo["LastBootUpTime"]?.ToString();
                if (!string.IsNullOrEmpty(raw))
                    _bootTimeLocal = ManagementDateTimeConverter.ToDateTime(raw).ToString("yyyy-MM-dd HH:mm:ss");
                break;
            }
        }
        catch (Exception ex) { AppLogger.Error("StaticInfo boot: " + ex.Message); }
    }

    private static int? ToNullableInt(object? o)
    {
        if (o == null) return null;
        try
        {
            int v = Convert.ToInt32(o);
            return v > 0 ? v : null;
        }
        catch { return null; }
    }

    public Snapshot GetSnapshot()
    {
        lock (_lock)
        {
            EnsureStaticInfo();
            EnsurePawnIoStatus();
            var snap = new Snapshot();
            var warnings = new List<string>();
            try
            {
                foreach (var hw in _computer.Hardware)
                {
                    try { hw.Update(); } catch { }
                }
            }
            catch (Exception ex)
            {
                warnings.Add("Error actualizando sensores: " + ex.Message);
                AppLogger.Error("Update sensores: " + ex);
            }

            try { snap.Cpu = ReadCpu(warnings); } catch (Exception ex) { warnings.Add("Error CPU: " + ex.Message); AppLogger.Error("ReadCpu: " + ex); }
            try { snap.Gpu = ReadGpu(warnings); } catch (Exception ex) { warnings.Add("Error GPU: " + ex.Message); AppLogger.Error("ReadGpu: " + ex); }
            try { snap.Memory = ReadMemory(); } catch (Exception ex) { warnings.Add("Error RAM: " + ex.Message); AppLogger.Error("ReadMemory: " + ex); }
            try { snap.Battery = ReadBattery(); } catch (Exception ex) { warnings.Add("Error batería: " + ex.Message); AppLogger.Error("ReadBattery: " + ex); }
            try { snap.Fans = ReadFans(warnings); } catch (Exception ex) { warnings.Add("Error ventiladores: " + ex.Message); AppLogger.Error("ReadFans: " + ex); }
            try { snap.Storage = ReadStorage(); } catch (Exception ex) { AppLogger.Error("ReadStorage: " + ex); }
            try
            {
                var (names, vals) = ReadExtraTemps();
                snap.ExtraTempNames = names;
                snap.ExtraTemps = vals;
            }
            catch (Exception ex) { AppLogger.Error("ReadExtraTemps: " + ex); }
            try { snap.AllSensors = ReadAllSensors(); } catch (Exception ex) { AppLogger.Error("ReadAllSensors: " + ex); }
            try { snap.Diagnostics = BuildDiagnostics(snap); } catch (Exception ex) { AppLogger.Error("Diagnostics: " + ex); }

            var now = DateTime.Now;
            snap.Timestamp = DateTime.UtcNow.ToString("o");
            snap.TimestampLocal = now.ToString("yyyy-MM-dd HH:mm:ss");
            UpdateSessionStats(snap);
            snap.Meta = new SystemMeta
            {
                CpuName = snap.Cpu.ProcessorName ?? GetCpuNameWmi(),
                InstalledRamMb = GetTotalRamMb(),
                AppUptime = FormatUptime(DateTime.Now - _startTime),
                RefreshMs = RefreshMs,
                LastReadIso = DateTime.UtcNow.ToString("o"),
                LastReadLocal = now.ToString("yyyy-MM-dd HH:mm:ss"),
                IsAdmin = IsRunningAsAdmin(),
                PreferredCpuSensor = PreferredCpuSensor,
                BootTimeLocal = _bootTimeLocal
            };
            snap.Cpu.CoresPhysical = _coresPhysical;
            snap.Cpu.CoresLogical = _coresLogical;
            snap.Warnings = warnings;
            return snap;
        }
    }

    // ---------- CPU ----------
    private CpuData ReadCpu(List<string> warnings)
    {
        var cpu = new CpuData();
        var cpuHw = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        if (cpuHw == null)
        {
            warnings.Add("No se detectó CPU vía LibreHardwareMonitor.");
            cpu.ProcessorName = GetCpuNameWmi();
            return cpu;
        }
        cpu.ProcessorName = string.IsNullOrWhiteSpace(cpuHw.Name) ? GetCpuNameWmi() : cpuHw.Name;

        var temps = new List<(string id, string name, float v)>();
        var loads = new List<(string name, float v)>();
        var clocks = new List<float>();
        double? totalLoad = null;
        double? power = null;

        foreach (var s in cpuHw.Sensors)
        {
            if (!s.Value.HasValue) continue;
            float v = s.Value.Value;
            switch (s.SensorType)
            {
                case SensorType.Temperature:
                    temps.Add((s.Identifier.ToString(), s.Name, v));
                    break;
                case SensorType.Load:
                    if (s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("CPU Total", StringComparison.OrdinalIgnoreCase))
                        totalLoad = v;
                    else
                        loads.Add((s.Name, v));
                    break;
                case SensorType.Clock:
                    clocks.Add(v);
                    break;
                case SensorType.Power:
                    if (s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))
                        power = v;
                    break;
            }
        }

        // Selección de temperatura del paquete: preferencia del usuario o "Package"
        if (temps.Count > 0)
        {
            (string id, string name, float v)? chosen = null;
            if (!string.IsNullOrEmpty(PreferredCpuSensor) && PreferredCpuSensor != "auto")
            {
                var match = temps.FirstOrDefault(t => t.id == PreferredCpuSensor || t.name == PreferredCpuSensor);
                if (!string.IsNullOrEmpty(match.id)) chosen = match;
            }
            if (chosen == null)
            {
                var pkg = temps.FirstOrDefault(t =>
                    t.name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                    t.name.Contains("Paquete", StringComparison.OrdinalIgnoreCase));
                chosen = string.IsNullOrEmpty(pkg.id) ? temps.OrderByDescending(t => t.v).First() : pkg;
            }
            cpu.PackageTemp = Math.Round(chosen.Value.v, 1);
            cpu.PackageTempSource = chosen.Value.name + " (" + cpuHw.Name + ")";
        }

        // Núcleos: "Core #N", "P-Core #N", "E-Core #N" — ordenados por número
        var coreTempList = temps
            .Where(t => System.Text.RegularExpressions.Regex.IsMatch(t.name, @"Core\s*#?\s*\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            .OrderBy(t => t.name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        cpu.CoreTemps = coreTempList.Select(t => (double?)Math.Round(t.v, 1)).ToList();
        cpu.CoreTempNames = coreTempList.Select(t => t.name).ToList();

        // Carga por núcleo/hilo: "CPU Core #N" (+ "Thread #M" en núcleos P)
        var coreLoadList = loads
            .Where(l => System.Text.RegularExpressions.Regex.IsMatch(l.name, @"Core\s*#?\s*\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            .OrderBy(l => l.name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        cpu.CoreLoads = coreLoadList
            .Select(l => (double?)Math.Round(l.v, 1)).ToList();
        cpu.CoreLoadNames = coreLoadList.Select(l => l.name).ToList();

        if (totalLoad.HasValue) cpu.TotalLoad = Math.Round(totalLoad.Value, 1);
        else
        {
            // Fallback: promedio de cargas de núcleo si existe
            var vals = loads.Select(l => (double)l.v).ToList();
            if (vals.Count > 0) cpu.TotalLoad = Math.Round(vals.Average(), 1);
        }

        cpu.CoreClocks = clocks.Select(c => (double?)Math.Round(c, 0)).ToList();
        // Potencia 0 o negativa = contador no legible (p. ej. sin admin), no un consumo real.
        if (power.HasValue && power.Value > 0) cpu.Power = Math.Round(power.Value, 2);

        // Margen térmico por núcleo (fuente fiable: DTS del propio procesador vía LHM)
        foreach (var hw2 in _computer.Hardware.Where(h => h.HardwareType == HardwareType.Cpu))
        {
            foreach (var s in hw2.Sensors)
            {
                if (s.SensorType != SensorType.Temperature) continue;
                if (!s.Name.Contains("Distance to TjMax", StringComparison.OrdinalIgnoreCase)) continue;
                if (!s.Value.HasValue) continue;
                cpu.TjMaxNames.Add(s.Name);
                cpu.TjMaxDistances.Add(Math.Round(s.Value.Value, 1));
            }
        }

        // Throttling: buscar sensor "Thermal Throttling" si existe
        foreach (var s in cpuHw.Sensors)
        {
            if (s.SensorType == SensorType.Load && s.Name.Contains("Throttling", StringComparison.OrdinalIgnoreCase) && s.Value.HasValue && s.Value.Value > 0)
            { cpu.Throttling = true; break; }
        }
        return cpu;
    }

    // ---------- GPU ----------
    private GpuData ReadGpu(List<string> warnings)
    {
        var gpu = new GpuData();
        var gpuHws = _computer.Hardware.Where(h => h.HardwareType == HardwareType.GpuIntel ||
                                                   h.HardwareType == HardwareType.GpuAmd ||
                                                   h.HardwareType == HardwareType.GpuNvidia).ToList();
        if (gpuHws.Count == 0)
        {
            gpu.Available = false;
            gpu.Note = "No se detectó GPU vía LibreHardwareMonitor. La GPU integrada Intel UHD no siempre expone temperatura independiente.";
            return gpu;
        }
        // Preferir Intel (equipo sin dedicada)
        var hw = gpuHws.FirstOrDefault(h => h.HardwareType == HardwareType.GpuIntel) ?? gpuHws[0];
        gpu.Name = hw.Name;
        gpu.Available = true;

        double? temp = null; string? tempSrc = null;
        double? load = null; double? clock = null; double? memUsed = null;

        foreach (var s in hw.Sensors)
        {
            if (!s.Value.HasValue) continue;
            float v = s.Value.Value;
            if (s.SensorType == SensorType.Temperature && temp == null)
            { temp = Math.Round(v, 1); tempSrc = s.Name + " (" + hw.Name + ")"; }
            else if (s.SensorType == SensorType.Load && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) && load == null)
                load = Math.Round(v, 1);
            else if (s.SensorType == SensorType.Clock && clock == null)
                clock = Math.Round(v, 0);
            else if (s.SensorType == SensorType.SmallData && s.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase) && memUsed == null)
                memUsed = Math.Round(v, 0);
        }
        // Carga total GPU como fallback: primer Load
        if (load == null)
        {
            var anyLoad = hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Value.HasValue);
            if (anyLoad != null) load = Math.Round(anyLoad.Value!.Value, 1);
        }
        gpu.Temp = temp;
        gpu.TempSource = tempSrc;
        gpu.Load = load;
        gpu.Clock = clock;
        gpu.MemoryUsedMb = memUsed;
        if (temp == null)
            gpu.Note = "La GPU (" + hw.Name + ") no expone temperatura independiente. Se muestran el resto de sensores disponibles.";
        return gpu;
    }

    // ---------- Memoria ----------
    private MemoryData ReadMemory()
    {
        var mem = new MemoryData();
        double? totalMb = GetTotalRamMb();
        double? availMb = null;
        bool viaPerf = false;
        try
        {
            if (_availMbCounter != null)
            {
                availMb = (double)_availMbCounter.NextValue();
                viaPerf = true;
            }
        }
        catch { viaPerf = false; }

        // Fallback LHM
        if (availMb == null)
        {
            var memHw = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Memory);
            if (memHw != null)
            {
                foreach (var s in memHw.Sensors)
                {
                    if (!s.Value.HasValue) continue;
                    if (s.SensorType == SensorType.Data && s.Name.Contains("Available", StringComparison.OrdinalIgnoreCase))
                        availMb = s.Value.Value * 1024; // GB -> MB
                }
            }
        }

        mem.TotalMb = totalMb.HasValue ? Math.Round(totalMb.Value, 0) : null;
        if (totalMb.HasValue && availMb.HasValue)
        {
            double avail = Math.Max(0, availMb.Value);
            double used = Math.Max(0, totalMb.Value - avail);
            mem.AvailableMb = Math.Round(avail, 0);
            mem.UsedMb = Math.Round(used, 0);
            mem.PercentUsed = Math.Round(used / totalMb.Value * 100, 1);
        }
        // Fuente real + comprobación cruzada con LHM cuando existe
        string source = viaPerf
            ? "PerformanceCounter (disponible) + WMI (total)"
            : "LibreHardwareMonitor + WMI";
        try
        {
            var memHw = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Memory);
            var usedPct = memHw?.Sensors.FirstOrDefault(s =>
                s.SensorType == SensorType.Load && s.Name.Contains("Used", StringComparison.OrdinalIgnoreCase));
            if (usedPct?.Value.HasValue == true && mem.PercentUsed.HasValue &&
                Math.Abs(usedPct.Value.Value - mem.PercentUsed.Value) < 5)
                source += " ✓ coincide con LHM";
        }
        catch { }
        mem.Source = source;
        return mem;
    }

    // ---------- Batería ----------
    private BatteryData ReadBattery()
    {
        var bat = new BatteryData();
        try
        {
            var ps = System.Windows.Forms.SystemInformation.PowerStatus;
            // BatteryLifePercent: 0..1 ; 255/1.0 puede indicar sin batería -> validar con flags
            bool noneFlag = (ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) != 0
                         || ps.BatteryChargeStatus == BatteryChargeStatus.NoSystemBattery;
            if (noneFlag)
            {
                bat.Present = false;
                bat.Status = "Sin batería";
                bat.Note = "No se detectó batería en este equipo.";
                return bat;
            }
            bat.Present = true;
            double pct = Math.Round(ps.BatteryLifePercent * 100, 0);
            if (pct >= 0 && pct <= 100) bat.Percent = pct;

            bool charging = (ps.BatteryChargeStatus & BatteryChargeStatus.Charging) != 0;
            if (charging) bat.Status = "Cargando";
            else if (pct >= 99) bat.Status = "Cargada";
            else bat.Status = "Descargando";

            try
            {
                bat.PowerLine = ps.PowerLineStatus switch
                {
                    PowerLineStatus.Online => "Conectado a la red",
                    PowerLineStatus.Offline => "Con batería",
                    _ => "Desconocido"
                };
            }
            catch { bat.PowerLine = "Desconocido"; }
        }
        catch (Exception ex)
        {
            bat.Present = false;
            bat.Status = "Desconocido";
            bat.Note = "No se pudo leer la batería: " + ex.Message;
            AppLogger.Error("PowerStatus: " + ex.Message);
        }

        // Capacidades vía WMI (pueden no existir -> null, nunca inventar)
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT DesignCapacity, FullChargeCapacity, RemainingCapacity FROM BatteryFullChargedCapacity");
            foreach (ManagementObject mo in searcher.Get())
            {
                bat.FullChargeCapacityMwh = ToNullableDouble(mo["FullChargeCapacity"]);
                bat.RemainingCapacityMwh = ToNullableDouble(mo["RemainingCapacity"]);
            }
        }
        catch { }try
        {
            using var s2 = new ManagementObjectSearcher("SELECT DesignCapacity FROM BatteryStaticData");
            foreach (ManagementObject mo in s2.Get())
            {
                var d = ToNullableDouble(mo["DesignCapacity"]);
                if (d.HasValue && d.Value > 0) { bat.DesignCapacityMwh = d; break; }
            }
        }
        catch { }

        // Enriquecer con sensores LHM de batería (Energy/Level/Power/TimeSpan/Voltage).
        // Si no existen, los campos quedan null: nunca se inventan.
        try
        {
            var batHw = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Battery);
            if (batHw != null)
            {
                foreach (var s in batHw.Sensors)
                {
                    if (!s.Value.HasValue) continue;
                    double v = s.Value.Value;
                    if (s.SensorType == SensorType.Energy && s.Name.Contains("Designed", StringComparison.OrdinalIgnoreCase))
                        bat.DesignCapacityMwh ??= v;
                    else if (s.SensorType == SensorType.Energy && s.Name.Contains("Fully-Charged", StringComparison.OrdinalIgnoreCase))
                        bat.FullChargeCapacityMwh ??= v;
                    else if (s.SensorType == SensorType.Energy && s.Name.Contains("Remaining Capacity", StringComparison.OrdinalIgnoreCase))
                        bat.RemainingCapacityMwh ??= v;
                    else if (s.SensorType == SensorType.Level && s.Name.Contains("Charge Level", StringComparison.OrdinalIgnoreCase))
                        bat.ChargeLevelPct = Math.Round(v, 1);
                    else if (s.SensorType == SensorType.Power && s.Name.Contains("Discharge Rate", StringComparison.OrdinalIgnoreCase) && v > 0)
                        bat.DischargeRateW = Math.Round(v, 1);
                    else if (s.SensorType == SensorType.TimeSpan && s.Name.Contains("Remaining Time", StringComparison.OrdinalIgnoreCase) && v > 0)
                        bat.RemainingMinutes = Math.Round(v / 60.0, 0);
                    else if (s.SensorType == SensorType.Voltage && bat.VoltageV == null)
                        bat.VoltageV = Math.Round(v, 2);
                }
                // Nivel preciso de LHM prevalece sobre el entero de PowerStatus
                if (bat.ChargeLevelPct.HasValue && bat.ChargeLevelPct.Value is >= 0 and <= 100)
                    bat.Percent = bat.ChargeLevelPct;
            }
        }
        catch { }

        // Temperatura de batería: Windows no la expone de forma estándar -> null + nota
        bat.TemperatureC = null;
        if (bat.Present)
            bat.Note = "La temperatura y los ciclos de batería no están expuestos por Windows; no se muestran para evitar datos ficticios.";
        return bat;
    }

    private static double? ToNullableDouble(object? o)
    {
        if (o == null) return null;
        try
        {
            double d = Convert.ToDouble(o);
            return double.IsNaN(d) || double.IsInfinity(d) ? null : d;
        }
        catch { return null; }
    }

    // ---------- Ventiladores ----------
    private List<FanData> ReadFans(List<string> warnings)
    {
        var fans = new List<FanData>();
        foreach (var hw in _computer.Hardware)
        {
            if (hw.HardwareType != HardwareType.Motherboard &&
                hw.HardwareType != HardwareType.SuperIO &&
                hw.HardwareType != HardwareType.EmbeddedController &&
                hw.HardwareType != HardwareType.Cpu) continue;
            foreach (var sub in hw.SubHardware)
            {
                try { sub.Update(); } catch { }
                CollectFans(sub, fans);
            }
            CollectFans(hw, fans);
        }
        if (fans.Count == 0)
            warnings.Add("No se detectaron ventiladores con lectura RPM. Es habitual en portátiles: el EC no expone RPM al SO.");
        return fans;
    }

    private static void CollectFans(IHardware hw, List<FanData> fans)
    {
        foreach (var s in hw.Sensors)
        {
            if (s.SensorType != SensorType.Fan) continue;
            fans.Add(new FanData
            {
                Name = s.Name,
                Hardware = hw.Name,
                Rpm = s.Value.HasValue ? Math.Round(s.Value.Value, 0) : null,
                Available = s.Value.HasValue
            });
        }
    }

    // ---------- Temperaturas extra (placa, EC) ----------
    private (List<string> names, List<double?> vals) ReadExtraTemps()
    {
        var names = new List<string>();
        var vals = new List<double?>();
        foreach (var hw in _computer.Hardware)
        {
            if (hw.HardwareType != HardwareType.Motherboard &&
                hw.HardwareType != HardwareType.SuperIO &&
                hw.HardwareType != HardwareType.EmbeddedController) continue;
            foreach (var s in hw.Sensors)
            {
                if (s.SensorType != SensorType.Temperature || !s.Value.HasValue) continue;
                names.Add(s.Name + " (" + hw.Name + ")");
                vals.Add(Math.Round(s.Value.Value, 1));
                if (names.Count >= 12) break;
            }
            foreach (var sub in hw.SubHardware)
            {
                foreach (var s in sub.Sensors)
                {
                    if (s.SensorType != SensorType.Temperature || !s.Value.HasValue) continue;
                    names.Add(s.Name + " (" + sub.Name + ")");
                    vals.Add(Math.Round(s.Value.Value, 1));
                    if (names.Count >= 12) break;
                }
            }
        }
        return (names, vals);
    }

    // ---------- Diagnóstico de detección ----------
    private DetectionDiagnostics BuildDiagnostics(Snapshot snap)
    {
        int cpuTotal = 0, cpuWith = 0;
        foreach (var s in snap.AllSensors)
        {
            if (s.HardwareType == "Cpu" && s.Type == "Temperature")
            {
                cpuTotal++;
                if (s.Value.HasValue) cpuWith++;
            }
        }
        var diag = new DetectionDiagnostics
        {
            IsAdmin = IsRunningAsAdmin(),
            HvciEnabled = IsHvciEnabled(),
            CpuTempSensors = cpuTotal,
            CpuTempWithValue = cpuWith,
            TotalSensors = snap.AllSensors.Count,
            SensorsWithValue = snap.AllSensors.Count(s => s.Value.HasValue),
            DriverBlocked = CheckDriverBlocked(),
            PawnIoInstalled = _pawnIoInstalled.GetValueOrDefault(),
            PawnIoVersion = _pawnIoVersion
        };
        diag.DriverState = ResolveDriverState(diag);
        return diag;
    }

    private static bool IsHvciEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", false);
            var v = key?.GetValue("Enabled");
            if (v is int i) return i != 0;
            if (v is long l) return l != 0;
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Busca en el registro de integridad de código si Windows bloqueó el
    /// driver de lectura de sensores (WinRing0). Solo legible como admin:
    /// sin permisos devuelve null (desconocido), nunca un falso negativo
    /// presentado como certeza. Se cachea 10 minutos (recorrer el registro
    /// en cada lectura sería un gasto inútil).
    /// </summary>
    private bool? _driverBlockedCache;
    private DateTime _driverBlockedAt;
    private string _driverStateCache = "unknown";
    private static bool? _pawnIoInstalled;
    private static string? _pawnIoVersion;

    /// <summary>
    /// Detecta el driver PawnIO (el que LHM 0.9.x usa para leer MSR) igual que
    /// la propia librería: clave de desinstalación en ambas vistas del registro.
    /// Sin este driver instalado no hay temperaturas, con o sin admin.
    /// </summary>
    private static void EnsurePawnIoStatus()
    {
        if (_pawnIoInstalled.HasValue) return;
        _pawnIoInstalled = false;
        try
        {
            using var k1 = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO", false);
            string? v = k1?.GetValue("DisplayVersion") as string;
            if (string.IsNullOrWhiteSpace(v))
            {
                using var b64 = Microsoft.Win32.RegistryKey.OpenBaseKey(
                    Microsoft.Win32.RegistryHive.LocalMachine,
                    Microsoft.Win32.RegistryView.Registry64);
                using var k2 = b64.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO", false);
                v = k2?.GetValue("DisplayVersion") as string;
            }
            if (!string.IsNullOrWhiteSpace(v))
            {
                _pawnIoInstalled = true;
                _pawnIoVersion = v!.Trim();
            }
        }
        catch (Exception ex) { AppLogger.Error("PawnIO detect: " + ex.Message); }
    }

    /// <summary>
    /// Clasifica el acceso al driver: ok (hay lecturas), noadmin (sin intentar),
    /// no-pawnio (falta instalar el driver PawnIO), blocked (Windows lo frenó),
    /// loaded-noread (cargado pero sin valores), not-deployed, unknown.
    /// Con caché de 10 minutos.
    /// </summary>
    private string ResolveDriverState(DetectionDiagnostics diag)
    {
        try
        {
            if (diag.CpuTempWithValue > 0) return "ok";
            if (!diag.IsAdmin) return "noadmin";
            if (!_pawnIoInstalled.GetValueOrDefault())
                return "no-pawnio";
            if (DateTime.Now - _driverBlockedAt < TimeSpan.FromMinutes(10) && _driverStateCache != "unknown")
                return _driverStateCache;
            string state;
            if (diag.DriverBlocked == true) state = "blocked";
            else if (IsRing0Deployed()) state = "loaded-noread";
            else state = diag.DriverBlocked == false ? "not-deployed" : "unknown";
            _driverStateCache = state;
            return state;
        }
        catch { return "unknown"; }
    }

    /// <returns>true si el servicio o el .sys de WinRing0 existen ahora mismo.</returns>
    private static bool IsRing0Deployed()
    {
        try
        {
            using var s = new ManagementObjectSearcher(
                "SELECT Name FROM Win32_Service WHERE Name LIKE '%Ring0%'");
            foreach (var _ in s.Get()) return true;
        }
        catch { }
        try
        {
            string[] dirs =
            {
                Path.GetTempPath(),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers")
            };
            foreach (var d in dirs)
            {
                try
                {
                    foreach (var f in Directory.GetFiles(d, "*Ring0*.sys"))
                        return true;
                }
                catch { }
            }
        }
        catch { }
        return false;
    }

    private bool? CheckDriverBlocked()
    {
        if (!IsRunningAsAdmin()) return null;
        if (_driverBlockedCache.HasValue && DateTime.Now - _driverBlockedAt < TimeSpan.FromMinutes(10))
            return _driverBlockedCache;
        bool? result = QueryDriverBlocked();
        _driverBlockedCache = result;
        _driverBlockedAt = DateTime.Now;
        return result;
    }

    private static bool? QueryDriverBlocked()
    {
        if (!IsRunningAsAdmin()) return null;
        try
        {
            var log = new System.Diagnostics.EventLog("System");
            int checkedEvents = 0;
            // Recorre de lo más reciente hacia atrás, acotado para no tardar.
            for (int i = log.Entries.Count - 1; i >= 0 && checkedEvents < 4000; i--)
            {
                System.Diagnostics.EventLogEntry entry;
                try { entry = log.Entries[i]; }
                catch { continue; }
                checkedEvents++;
                if (entry.TimeGenerated < DateTime.Now.AddDays(-14)) break;
                string src = entry.Source ?? "";
                if (!src.Contains("CodeIntegrity", StringComparison.OrdinalIgnoreCase) &&
                    !src.Contains("Kernel-PnP", StringComparison.OrdinalIgnoreCase)) continue;
                string msg = entry.Message ?? "";
                if (msg.Contains("WinRing0", StringComparison.OrdinalIgnoreCase) &&
                    (msg.Contains("bloque", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("block", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("vulnerab", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("HVCI", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("hypervisor", StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
            return false;
        }
        catch { return null; }
    }

    // ---------- Almacenamiento ----------
    private List<StorageData> ReadStorage()
    {
        var list = new List<StorageData>();
        foreach (var hw in _computer.Hardware.Where(h => h.HardwareType == HardwareType.Storage))
        {
            double? temp = null;
            foreach (var s in hw.Sensors)
            {
                if (s.SensorType == SensorType.Temperature && s.Value.HasValue && temp == null)
                    temp = Math.Round(s.Value.Value, 1);
            }
            list.Add(new StorageData
            {
                Name = hw.Name,
                TempC = temp,
                Available = temp.HasValue
            });
        }
        return list;
    }

    // ---------- Estadísticas de sesión ----------
    private void UpdateSessionStats(Snapshot snap)
    {
        if (snap.Cpu.PackageTemp.HasValue)
        {
            double t = snap.Cpu.PackageTemp.Value;
            _cpuTempMin = _cpuTempMin.HasValue ? Math.Min(_cpuTempMin.Value, t) : t;
            _cpuTempMax = _cpuTempMax.HasValue ? Math.Max(_cpuTempMax.Value, t) : t;
            _cpuTempSum += t;
            _cpuTempSamples++;
        }
        if (snap.Cpu.TotalLoad.HasValue)
        {
            double l = snap.Cpu.TotalLoad.Value;
            _cpuLoadMax = _cpuLoadMax.HasValue ? Math.Max(_cpuLoadMax.Value, l) : l;
            _cpuLoadSum += l;
            _cpuLoadSamples++;
        }
        if (snap.Gpu.Temp.HasValue)
        {
            double g = snap.Gpu.Temp.Value;
            _gpuTempMin = _gpuTempMin.HasValue ? Math.Min(_gpuTempMin.Value, g) : g;
            _gpuTempMax = _gpuTempMax.HasValue ? Math.Max(_gpuTempMax.Value, g) : g;
        }
        _sessionSamples++;
        snap.Session = new SessionStats
        {
            CpuTempMin = _cpuTempMin.HasValue ? Math.Round(_cpuTempMin.Value, 1) : null,
            CpuTempMax = _cpuTempMax.HasValue ? Math.Round(_cpuTempMax.Value, 1) : null,
            CpuTempAvg = _cpuTempSamples > 0
                ? Math.Round(_cpuTempSum / _cpuTempSamples, 1) : null,
            CpuLoadMax = _cpuLoadMax.HasValue ? Math.Round(_cpuLoadMax.Value, 1) : null,
            CpuLoadAvg = _cpuLoadSamples > 0
                ? Math.Round(_cpuLoadSum / _cpuLoadSamples, 1) : null,
            GpuTempMin = _gpuTempMin.HasValue ? Math.Round(_gpuTempMin.Value, 1) : null,
            GpuTempMax = _gpuTempMax.HasValue ? Math.Round(_gpuTempMax.Value, 1) : null,
            Samples = _sessionSamples
        };
    }

    // ---------- Todos los sensores ----------
    private List<SensorReading> ReadAllSensors()
    {
        var list = new List<SensorReading>();
        foreach (var hw in _computer.Hardware)
        {
            AddSensors(hw, list);
            foreach (var sub in hw.SubHardware)
                AddSensors(sub, list);
        }
        return list.OrderBy(s => s.Hardware).ThenBy(s => s.Type).ThenBy(s => s.Name).ToList();
    }

    private static void AddSensors(IHardware hw, List<SensorReading> list)
    {
        foreach (var s in hw.Sensors)
        {
            list.Add(new SensorReading
            {
                Hardware = hw.Name,
                HardwareType = hw.HardwareType.ToString(),
                Type = s.SensorType.ToString(),
                Name = s.Name,
                Identifier = s.Identifier.ToString(),
                Value = s.Value.HasValue ? Math.Round(s.Value.Value, 2) : null,
                Min = s.Min.HasValue ? Math.Round(s.Min.Value, 2) : null,
                Max = s.Max.HasValue ? Math.Round(s.Max.Value, 2) : null,
                Unit = UnitFor(s.SensorType)
            });
        }
    }

    private static string UnitFor(SensorType t) => t switch
    {
        SensorType.Temperature => "°C",
        SensorType.Load => "%",
        SensorType.Clock => "MHz",
        SensorType.Fan => "RPM",
        SensorType.Power => "W",
        SensorType.Data => "GB",
        SensorType.SmallData => "MB",
        SensorType.Voltage => "V",
        SensorType.Current => "A",
        SensorType.Energy => "mWh",
        SensorType.Throughput => "",
        SensorType.Timing => "",
        SensorType.Control => "%",
        SensorType.Level => "%",
        SensorType.Frequency => "Hz",
        _ => ""
    };

    // ---------- Utilidades WMI ----------
    private static string? GetCpuNameWmi()
    {
        try
        {
            using var s = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
            foreach (ManagementObject mo in s.Get())
            {
                var n = mo["Name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(n)) return n.Trim();
            }
        }
        catch { }
        return null;
    }

    private static double? GetTotalRamMb()
    {
        try
        {
            ulong totalBytes = 0;
            using var s = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            foreach (ManagementObject mo in s.Get())
            {
                if (mo["TotalPhysicalMemory"] != null)
                    totalBytes = Convert.ToUInt64(mo["TotalPhysicalMemory"]);
            }
            if (totalBytes > 0) return Math.Round(totalBytes / 1024.0 / 1024.0, 0);
        }
        catch { }
        try
        {
            // Fallback: GC / Environment
            var ci = GC.GetGCMemoryInfo();
            if (ci.TotalAvailableMemoryBytes > 0)
                return Math.Round(ci.TotalAvailableMemoryBytes / 1024.0 / 1024.0, 0);
        }
        catch { }
        return null;
    }

    private static string FormatUptime(TimeSpan ts)
    {
        if (ts.TotalSeconds < 60) return $"{(int)ts.TotalSeconds}s";
        if (ts.TotalMinutes < 60) return $"{(int)ts.TotalMinutes}min {(int)(ts.Seconds)}s";
        if (ts.TotalHours < 24) return $"{(int)ts.TotalHours}h {(int)ts.Minutes}min";
        return $"{(int)ts.TotalDays}d {(int)ts.Hours}h";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _computer.Close(); } catch { }
        try { _availMbCounter?.Dispose(); } catch { }
    }
}

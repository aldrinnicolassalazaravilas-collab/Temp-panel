using System.Diagnostics;
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

    // Estadísticas de sesión (mín/máx/promedio desde el arranque de la app)
    private double _cpuTempSum;
    private double _cpuLoadSum;
    private int _sessionSamples;
    private int _cpuTempSamples;
    private int _cpuLoadSamples;
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
            IsStorageEnabled = true,
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

    public Snapshot GetSnapshot()
    {
        lock (_lock)
        {
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
            try { MergeHwinfo(snap); } catch (Exception ex) { AppLogger.Error("MergeHwinfo: " + ex); }

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
                PreferredCpuSensor = PreferredCpuSensor
            };
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

    // ---------- Fusión opcional con HWiNFO64 ----------
    // Solo rellena lo que LibreHardwareMonitor no pudo leer (nunca sobrescribe
    // una lectura real). Cada valor conserva su fuente en los campos *Source.
    private void MergeHwinfo(Snapshot snap)
    {
        if (!HwinfoMonitor.TryRead(out var rs, out int nSensors, out string? note))
        {
            snap.Hwinfo = new HwinfoStatus { Available = false, Note = note };
            return;
        }
        snap.Hwinfo = new HwinfoStatus
        {
            Available = true,
            Sensors = nSensors,
            Readings = rs.Count,
            Note = note
        };

        // --- CPU: paquete ---
        if (!snap.Cpu.PackageTemp.HasValue)
        {
            var pkg = PickTemp(rs, IsCpuSensor, s =>
                s.Contains("Package", StringComparison.OrdinalIgnoreCase));
            pkg ??= PickTemp(rs, IsCpuSensor, s =>
                s.Contains("Average", StringComparison.OrdinalIgnoreCase));
            if (pkg != null)
            {
                snap.Cpu.PackageTemp = Math.Round(pkg.Value, 1);
                snap.Cpu.PackageTempSource = pkg.Label + " (HWiNFO64)";
            }
            else
            {
                var cores = CpuCoreTemps(rs);
                if (cores.Count > 0)
                {
                    snap.Cpu.PackageTemp = Math.Round(cores.Max(c => c.v), 1);
                    snap.Cpu.PackageTempSource = "Máx. de núcleos (HWiNFO64)";
                }
            }
        }
        // --- CPU: núcleos ---
        if (snap.Cpu.CoreTemps.Count == 0)
        {
            var cores = CpuCoreTemps(rs);
            foreach (var (name, v) in cores)
            {
                snap.Cpu.CoreTempNames.Add(name);
                snap.Cpu.CoreTemps.Add(Math.Round(v, 1));
            }
        }
        // --- GPU: temperatura ---
        if (!snap.Gpu.Temp.HasValue)
        {
            var gt = PickTemp(rs, IsGpuSensor, s =>
                s.Contains("Temperature", StringComparison.OrdinalIgnoreCase));
            if (gt != null && !string.IsNullOrEmpty(snap.Gpu.Name))
            {
                snap.Gpu.Temp = Math.Round(gt.Value, 1);
                snap.Gpu.TempSource = gt.Label + " (HWiNFO64)";
                snap.Gpu.Note = null;
            }
        }
        // --- Ventiladores que LHM no vio ---
        var knownFans = new HashSet<string>(snap.Fans.Select(f => f.Name),
            StringComparer.OrdinalIgnoreCase);
        foreach (var r in rs)
        {
            if (!IsRpm(r)) continue;
            if (r.Value < 0 || r.Value > 20000) continue;
            if (!knownFans.Add(r.Label)) continue;
            snap.Fans.Add(new FanData
            {
                Name = r.Label,
                Hardware = string.IsNullOrWhiteSpace(r.SensorName) ? "HWiNFO64" : r.SensorName,
                Rpm = Math.Round(r.Value, 0),
                Available = true
            });
        }
    }

    private static bool IsCpuSensor(HwinfoReading r) =>
        r.SensorType == HwinfoParser.SensorCpu ||
        r.SensorName.Contains("CPU", StringComparison.OrdinalIgnoreCase);

    private static bool IsGpuSensor(HwinfoReading r) =>
        r.SensorType == HwinfoParser.SensorGpu ||
        r.SensorName.Contains("GPU", StringComparison.OrdinalIgnoreCase);

    private static bool IsTemp(HwinfoReading r) =>
        r.Unit == "°C" || r.Unit == "C" ||
        r.ReadingType == HwinfoParser.ReadingTemp;

    private static bool IsRpm(HwinfoReading r) =>
        r.Unit.Equals("RPM", StringComparison.OrdinalIgnoreCase) ||
        r.ReadingType == HwinfoParser.ReadingFan;

    private static HwinfoReading? PickTemp(List<HwinfoReading> rs,
        Func<HwinfoReading, bool> sensor, Func<string, bool> label)
    {
        foreach (var r in rs)
        {
            if (!sensor(r) || !IsTemp(r)) continue;
            if (!label(r.Label)) continue;
            if (r.Value < -40 || r.Value > 130) continue; // fuera de rango físico
            return r;
        }
        return null;
    }

    private static List<(string name, double v)> CpuCoreTemps(List<HwinfoReading> rs)
    {
        var list = new List<(int n, string name, double v)>();
        foreach (var r in rs)
        {
            if (!IsCpuSensor(r) || !IsTemp(r)) continue;
            var m = System.Text.RegularExpressions.Regex.Match(
                r.Label, @"Core\s*#?\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) continue;
            if (!int.TryParse(m.Groups[1].Value, out int n)) continue;
            if (r.Value < -40 || r.Value > 130) continue;
            if (list.Any(x => x.n == n)) continue;
            list.Add((n, r.Label.Trim(), r.Value));
        }
        return list.OrderBy(x => x.n).Select(x => (x.name, x.v)).ToList();
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

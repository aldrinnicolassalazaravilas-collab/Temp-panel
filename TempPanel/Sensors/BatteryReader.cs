using System.Management;

namespace TempPanel.Sensors;

/// <summary>
/// Muestra de batería leída de fuentes reales del sistema (WMI root\wmi,
/// Win32_Battery, PowerStatus). Nunca inventa: cada campo queda null si
/// ninguna fuente lo expone, y PercentSource dice de dónde salió el nivel.
/// </summary>
public sealed class BatterySample
{
    public bool Present { get; set; }
    public double? Percent { get; set; }
    public string PercentSource { get; set; } = "";
    public bool? Charging { get; set; }
    public bool? PowerOnline { get; set; }
    public double? FullChargeMwh { get; set; }
    public double? DesignMwh { get; set; }
    public double? RemainingMwh { get; set; }
    public double? DischargeRateW { get; set; }
    public double? ChargeRateW { get; set; }
    public double? VoltageV { get; set; }
    public double? RuntimeMinutes { get; set; }
}

/// <summary>
/// Lectura precisa de batería (v2.5.0). Mejoras frente a la lectura antigua:
///  - Nivel con 1 decimal desde capacidades reales: RemainingCapacity /
///    FullChargedCapacity de root\wmi (79.7 % en vez del entero 79 %).
///  - Estado real de carga desde root\wmi BatteryStatus (Charging/PowerOnline),
///    que sí refleja "cargando ahora" aunque Win32_Battery diga "sin cargar" (2),
///    típico cuando el portátil retiene la carga (modo conservación ~80 %).
///  - Los centinelas de Windows (EstimatedRunTime 71582788 = desconocido) se
///    filtran y quedan null en vez de mostrarse como autonomía falsa.
/// </summary>
public static class BatteryReader
{
    /// <summary>% con un decimal a partir de capacidades reales (mWh); null si faltan.</summary>
    public static double? PercentFrom(double? remainingMwh, double? fullMwh)
    {
        if (remainingMwh is not double r || fullMwh is not double f) return null;
        if (f <= 0 || r < 0 || double.IsNaN(r) || double.IsNaN(f)) return null;
        double pct = r / f * 100.0;
        if (double.IsNaN(pct) || double.IsInfinity(pct)) return null;
        return Math.Round(Math.Min(100.0, pct), 1);
    }

    /// <summary>Descarta centinelas de Windows ("desconocido"): p. ej. 71582788 minutos.</summary>
    public static double? SanitizeRuntime(double? minutes)
    {
        if (minutes is not double m || double.IsNaN(m) || double.IsInfinity(m)) return null;
        if (m <= 0 || m > 60.0 * 24 * 7) return null; // más de una semana = sin dato real
        return Math.Round(m, 0);
    }

    /// <summary>Estado textual a partir de los datos medidos (sin suposiciones).</summary>
    public static string StatusFor(bool? powerOnline, bool? charging, double? percent, bool held, bool present)
    {
        if (!present) return "Sin batería";
        if (percent is null) return "Desconocido";
        if (powerOnline != true) return "Descargando";
        if (percent >= 99.5) return "Cargada (en red)";
        if (held && charging == true) return "Cargando · retenida por el equipo";
        if (charging == true) return "Cargando";
        return "Conectada · sin cargar";
    }

    /// <summary>
    /// Deja solo la potencia coherente con el estado medido: varios controladores
    /// ACPI publican la MISMA cifra en ChargeRate y DischargeRate (se ve en el
    /// IdeaPad: 32.6 W en ambas mientras descargaba). Mostrar "cargando a X W"
    /// mientras la batería baja sería un dato falso, así que la dirección la
    /// decide el firmware (Charging) y la otra columna queda en "No disponible".
    /// </summary>
    public static (double? ChargeW, double? DischargeW) CoherentRates(bool? charging, double? chargeW, double? dischargeW)
    {
        if (charging == true) return (chargeW, null);
        if (charging == false) return (null, dischargeW);
        return (chargeW, dischargeW); // sin estado confirmado: se muestra lo medido
    }

    public static BatterySample Read()
    {
        var s = new BatterySample();

        // --- 1) root\wmi: estado real de carga y medidas del firmware ---
        // Se usa SELECT * a propósito: cada controlador ACPI publica un juego de
        // columnas distinto y una lista explícita falla entera ("consulta no
        // válida") en equipos donde falta una sola columna. Así se lee lo que
        // exista, y lo que no exista queda null (honesto).
        ReadFirst(@"root\wmi", "SELECT * FROM BatteryStatus", "BatteryStatus", mo =>
        {
            s.Present = true;
            var charging = ToBool(mo["Charging"]);
            var discharging = ToBool(mo["Discharging"]);
            if (charging.HasValue) s.Charging = charging;
            else if (discharging == true) s.Charging = false;
            s.PowerOnline = ToBool(mo["PowerOnline"]);
            s.RemainingMwh = ToMilliRounded(mo["RemainingCapacity"]);
            var v = ToDouble(mo["Voltage"]);
            if (v.HasValue && v.Value > 0) s.VoltageV = Math.Round(v.Value / 1000.0, 2);
            var cr = ToDouble(mo["ChargeRate"]);
            if (cr.HasValue && cr.Value > 0) s.ChargeRateW = Math.Round(cr.Value / 1000.0, 1);
            var dr = ToDouble(mo["DischargeRate"]);
            if (dr.HasValue && dr.Value > 0) s.DischargeRateW = Math.Round(dr.Value / 1000.0, 1);
        });

        ReadFirst(@"root\wmi", "SELECT * FROM BatteryFullChargedCapacity", "BatteryFullChargedCapacity", mo =>
        {
            var full = ToDouble(mo["FullChargedCapacity"]);
            if (full.HasValue && full.Value > 0) s.FullChargeMwh = Math.Round(full.Value, 0);
        });

        ReadFirst(@"root\wmi", "SELECT * FROM BatteryStaticData", "BatteryStaticData", mo =>
        {
            var d = ToDouble(mo["DesignedCapacity"]);
            if (d.HasValue && d.Value > 0) s.DesignMwh = Math.Round(d.Value, 0);
        });

        // --- 2) win32_battery: respaldo + autonomía (con filtro de centinelas) ---
        try
        {
            using var q = new ManagementObjectSearcher(
                "SELECT BatteryStatus, EstimatedChargeRemaining, EstimatedRunTime FROM Win32_Battery");
            foreach (ManagementObject mo in q.Get())
            {
                s.Present = true;
                var rt = ToDouble(mo["EstimatedRunTime"]);
                s.RuntimeMinutes ??= SanitizeRuntime(rt);
                int? bs = ToInt(mo["BatteryStatus"]);
                if (bs.HasValue)
                {
                    // 2 = red sin cargar, 3 = cargada, 6..9 = cargando*; == 1 descargando
                    if (s.PowerOnline is null) s.PowerOnline = bs is 2 or 3 or 6 or 7 or 8 or 9;
                    if (s.Charging is null) s.Charging = bs is 6 or 7 or 8 or 9 or 3;
                }
                double? est = ToDouble(mo["EstimatedChargeRemaining"]);
                if (est.HasValue && s.Percent is null)
                {
                    s.Percent = Math.Round(est.Value, 1);
                    s.PercentSource = "Win32_Battery (entero)";
                }
                break;
            }
        }
        catch (Exception ex) { AppLogger.Error("BatteryReader Win32_Battery: " + ex.Message); }

        // --- 3) PowerStatus de Windows: último respaldo (siempre existe) ---
        try
        {
            var ps = System.Windows.Forms.SystemInformation.PowerStatus;
            bool none = (ps.BatteryChargeStatus & System.Windows.Forms.BatteryChargeStatus.NoSystemBattery) != 0;
            if (!none)
            {
                s.Present = true;
                double p = ps.BatteryLifePercent * 100.0;
                if (s.Percent is null && p >= 0 && p <= 100)
                {
                    s.Percent = Math.Round(p, 1);
                    s.PercentSource = "PowerStatus (Windows)";
                }
                s.Charging ??= (ps.BatteryChargeStatus & System.Windows.Forms.BatteryChargeStatus.Charging) != 0;
                s.PowerOnline ??= ps.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
                if (s.RuntimeMinutes is null && ps.BatteryLifeRemaining > 0)
                    s.RuntimeMinutes = SanitizeRuntime(ps.BatteryLifeRemaining / 60.0);
            }
            else if (!s.Present)
            {
                return s; // sin batería: no se fuerza ninguna fuente
            }
        }
        catch (Exception ex) { AppLogger.Error("BatteryReader PowerStatus: " + ex.Message); }

        // --- 4) Nivel preciso = capacidades reales (prioridad máxima) ---
        var precise = PercentFrom(s.RemainingMwh, s.FullChargeMwh);
        if (precise is not null)
        {
            s.Percent = precise;
            s.PercentSource = "capacidades reales del firmware (root\\wmi)";
        }

        return s;
    }

    /// <summary>
    /// Lee la primera instancia de una clase WMI (los portátiles traen una sola
    /// batería). Cualquier error se registra y se sigue: nunca se inventa un dato.
    /// </summary>
    private static void ReadFirst(string scope, string query, string label, Action<ManagementBaseObject> handle)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, query);
            foreach (ManagementBaseObject mo in searcher.Get())
            {
                handle(mo);
                return;
            }
        }
        catch (Exception ex) { AppLogger.Error("BatteryReader " + label + ": " + ex.Message); }
    }

    private static double? ToDouble(object? o)
    {
        if (o == null) return null;
        try
        {
            double d = Convert.ToDouble(o);
            return double.IsNaN(d) || double.IsInfinity(d) ? null : d;
        }
        catch { return null; }
    }

    private static int? ToInt(object? o)
    {
        if (o == null) return null;
        try { return Convert.ToInt32(o); } catch { return null; }
    }

    private static bool? ToBool(object? o)
    {
        if (o == null) return null;
        try { return Convert.ToBoolean(o); } catch { return null; }
    }

    /// <summary>mWh con redondeo a 0 decimales (capacidades del firmware).</summary>
    private static double? ToMilliRounded(object? o)
    {
        var v = ToDouble(o);
        return v.HasValue && v.Value >= 0 ? Math.Round(v.Value, 0) : null;
    }
}

/// <summary>
/// Tendencia de carga medida sobre datos reales (no predice, solo observa):
/// %/hora desde que hay muestras y detección de "nivel retenido" cuando el
/// equipo está enchufado pero el nivel no cambia (modo conservación típico
/// de portátiles como IdeaPad: mantiene ~80 %).
/// </summary>
public sealed class BatteryTrend
{
    private readonly List<(DateTime T, double Pct)> _pts = new();
    public double? PctPerHour { get; private set; }
    public bool Held { get; private set; }
    public double? HeldMinutes { get; private set; }

    /// <summary>Ventana máxima de observación (30 min).</summary>
    public const double WindowMinutes = 30.0;

    public void Add(DateTime t, double? percent, bool plugged)
    {
        if (percent.HasValue) _pts.Add((t, percent.Value));
        var cutoff = t.AddMinutes(-WindowMinutes);
        _pts.RemoveAll(p => p.T < cutoff);

        PctPerHour = null;
        Held = false;
        HeldMinutes = null;
        if (_pts.Count < 2) return;

        var first = _pts[0];
        var last = _pts[^1];
        double spanMin = (last.T - first.T).TotalMinutes;
        if (spanMin < 5) return; // ventana mínima para un dato honesto

        PctPerHour = Math.Round((last.Pct - first.Pct) / (spanMin / 60.0), 2);
        if (plugged && spanMin >= 10 && Math.Abs(last.Pct - first.Pct) < 0.1)
        {
            Held = true;
            HeldMinutes = Math.Round(spanMin, 0);
        }
    }
}

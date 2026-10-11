using System.IO;

namespace TempPanel.Sensors;

/// <summary>
/// Registro local de errores en %AppData%\TempPanel\logs.
/// Sin telemetría: solo escribe en disco local.
/// </summary>
public static class AppLogger
{
    private static readonly object _lock = new();
    private static string? _logFile;

    public static string LogDirectory
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TempPanel", "logs");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string ConfigDirectory
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TempPanel");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        try
        {
            // En pruebas automatizadas no se ensucia el log del usuario.
            if (Environment.GetEnvironmentVariable("TEMPANEL_QUIET_LOG") == "1") return;
            lock (_lock)
            {
                _logFile ??= Path.Combine(LogDirectory, $"temppanel-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(_logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}");
            }
        }
        catch { /* el registro nunca debe romper la app */ }
    }
}

/// <summary>
/// Configuración persistente local (umbrales, intervalo, sensor preferido).
/// </summary>
public sealed class AppConfig
{
    public int RefreshMs { get; set; } = 2000;
    public double CpuWarnTemp { get; set; } = 85;
    public double CpuCritTemp { get; set; } = 95;
    public double GpuWarnTemp { get; set; } = 80;
    public double GpuCritTemp { get; set; } = 90;
    public string PreferredCpuSensor { get; set; } = "auto";
    public bool StartMinimized { get; set; } = false;
    // Batería (§5): umbrales, activación, sonido, modo (popup|balloon)
    public bool BatteryAlertsEnabled { get; set; } = true;
    public double BatteryThreshold { get; set; } = 80;
    public double BatteryRearm { get; set; } = 75;
    public bool BatterySound { get; set; } = true;
    public string BatteryMode { get; set; } = "popup";
    // Aviso de carga completa (99 % cargando, rearma bajo 95 %).
    public bool BatteryFullEnabled { get; set; } = true;
    // Unidad de temperatura: "C" o "F"
    public string Unit { get; set; } = "C";
    public bool StartWithWindows { get; set; } = false;
    // Actualizaciones: comprobación automática desactivada por defecto (sin red salvo permiso).
    public bool AutoCheckUpdates { get; set; } = false;

    /// <summary>Garantiza reactivación &lt; alerta y rangos válidos.</summary>
    public void Normalize()
    {
        RefreshMs = Math.Clamp(RefreshMs, 1000, 10000);
        BatteryThreshold = Math.Clamp(BatteryThreshold, 50, 100);
        BatteryRearm = Math.Clamp(BatteryRearm, 10, 99);
        if (BatteryRearm >= BatteryThreshold) BatteryRearm = BatteryThreshold - 5;
        if (BatteryMode != "balloon") BatteryMode = "popup";
        if (Unit != "F") Unit = "C";
    }

    public void ResetToDefaults()
    {
        var fresh = new AppConfig();
        RefreshMs = fresh.RefreshMs;
        CpuWarnTemp = fresh.CpuWarnTemp;
        CpuCritTemp = fresh.CpuCritTemp;
        GpuWarnTemp = fresh.GpuWarnTemp;
        GpuCritTemp = fresh.GpuCritTemp;
        PreferredCpuSensor = fresh.PreferredCpuSensor;
        StartMinimized = fresh.StartMinimized;
        BatteryAlertsEnabled = fresh.BatteryAlertsEnabled;
        BatteryThreshold = fresh.BatteryThreshold;
        BatteryRearm = fresh.BatteryRearm;
        BatterySound = fresh.BatterySound;
        BatteryMode = fresh.BatteryMode;
        BatteryFullEnabled = fresh.BatteryFullEnabled;
        Unit = fresh.Unit;
        StartWithWindows = fresh.StartWithWindows;
        AutoCheckUpdates = fresh.AutoCheckUpdates;
    }

    public static string ConfigPath
    {
        get => System.IO.Path.Combine(AppLogger.ConfigDirectory, "config.json");
    }

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var cfg = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json);
                if (cfg != null)
                {
                    cfg.Normalize();
                    return cfg;
                }
            }
        }
        catch (Exception ex) { AppLogger.Error("Cargar config: " + ex.Message); }
        return new AppConfig();
    }

    public void Save()
    {
        try
        {
            Normalize();
            var json = System.Text.Json.JsonSerializer.Serialize(this, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex) { AppLogger.Error("Guardar config: " + ex.Message); }
    }
}

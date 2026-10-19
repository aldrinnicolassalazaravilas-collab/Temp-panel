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
    // Bienvenida inicial: true hasta que el usuario la confirma.
    public bool FirstRun { get; set; } = true;
    // Mini ventana: series visibles en el minigráfico ("cpu,temp,bat,ram,gpu"),
    // tamaño ampliado y última posición en pantalla (se guarda al moverla).
    public string MiniSeries { get; set; } = "cpu,temp,bat,ram,gpu";
    public bool MiniExpanded { get; set; } = false;
    public double? MiniLeft { get; set; }
    public double? MiniTop { get; set; }
    // v3.0.0 — integración con Windows:
    // atajo global Ctrl+Alt+T, distintivo en la barra de tareas y reducción
    // adaptativa de actividad (segundo plano / pantalla completa / ahorro).
    public bool HotkeyEnabled { get; set; } = true;
    public bool TaskbarBadge { get; set; } = true;
    public bool AdaptivePerf { get; set; } = true;

    public static readonly string[] MiniSeriesKeys = { "cpu", "temp", "bat", "ram", "gpu" };

    public bool IsMiniSeriesEnabled(string key) =>
        MiniSeries.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .Contains(key, StringComparer.OrdinalIgnoreCase);

    public void ToggleMiniSeries(string key)
    {
        var set = MiniSeries.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!set.Add(key)) set.Remove(key);
        MiniSeries = string.Join(',', MiniSeriesKeys.Where(set.Contains));
    }

    /// <summary>Garantiza reactivación &lt; alerta y rangos válidos.</summary>
    public void Normalize()
    {
        RefreshMs = Math.Clamp(RefreshMs, 1000, 10000);
        BatteryThreshold = Math.Clamp(BatteryThreshold, 50, 100);
        BatteryRearm = Math.Clamp(BatteryRearm, 10, 99);
        if (BatteryRearm >= BatteryThreshold) BatteryRearm = BatteryThreshold - 5;
        if (BatteryMode != "balloon") BatteryMode = "popup";
        if (Unit != "F") Unit = "C";
        // Mini: solo claves conocidas, en orden canónico; nunca vacío (una serie mínima).
        var miniSet = MiniSeries.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ordered = MiniSeriesKeys.Where(miniSet.Contains).ToArray();
        MiniSeries = ordered.Length > 0 ? string.Join(',', ordered) : "cpu";
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
        MiniSeries = fresh.MiniSeries;
        MiniExpanded = fresh.MiniExpanded;
        MiniLeft = null;
        MiniTop = null;
        HotkeyEnabled = fresh.HotkeyEnabled;
        TaskbarBadge = fresh.TaskbarBadge;
        AdaptivePerf = fresh.AdaptivePerf;
        // FirstRun NO se restablece: la bienvenida solo sale una vez.
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

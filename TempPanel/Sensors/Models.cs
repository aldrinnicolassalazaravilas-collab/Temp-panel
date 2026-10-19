using System.Text.Json.Serialization;

namespace TempPanel.Sensors;

// Todos los valores de sensor son nullables: null = no disponible (NO es cero).
// Esto distingue 0 real de "sin lectura" y de error.

public sealed class SensorReading
{
    public string Hardware { get; set; } = "";
    public string HardwareType { get; set; } = "";
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public string Identifier { get; set; } = "";
    public double? Value { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public string Unit { get; set; } = "";
}

public sealed class CpuData
{
    public string? ProcessorName { get; set; }
    public double? PackageTemp { get; set; }
    public string? PackageTempSource { get; set; }
    public List<double?> CoreTemps { get; set; } = new();
    public List<string> CoreTempNames { get; set; } = new();
    // Resumen calculado de los núcleos en esta misma lectura (null si no hay lecturas).
    // Son cálculos, no sensores: sirven para ver dispersión térmica entre núcleos.
    public double? CoreTempMin { get; set; }
    public double? CoreTempMax { get; set; }
    public double? CoreTempAvg { get; set; }
    public double? TotalLoad { get; set; }
    public List<double?> CoreLoads { get; set; } = new();
    public List<string> CoreLoadNames { get; set; } = new();
    // Solo relojes de núcleo (excluye Bus Speed y similares cuando existen).
    public List<double?> CoreClocks { get; set; } = new();
    public double? ClockAvgMhz { get; set; }
    public double? Power { get; set; }
    public bool Throttling { get; set; }
    public int? CoresPhysical { get; set; }
    public int? CoresLogical { get; set; }
    // Distancia a TjMax por núcleo ("Distance to TjMax"): indicador de margen térmico.
    // Solo se informa si el firmware lo expone; null/vacío = no disponible.
    public List<string> TjMaxNames { get; set; } = new();
    public List<double?> TjMaxDistances { get; set; } = new();
    // TjMax del procesador, calculado como temp + distancia cuando ambas
    // lecturas existen para el mismo núcleo (DTS: temp = TjMax - delta).
    public double? TjMaxC { get; set; }
}

public sealed class GpuData
{
    public string? Name { get; set; }
    public double? Temp { get; set; }
    public string? TempSource { get; set; }
    public double? Load { get; set; }
    public double? Clock { get; set; }
    public double? MemoryUsedMb { get; set; }
    public bool Available { get; set; }
    public string? Note { get; set; }
}

public sealed class MemoryData
{
    public double? TotalMb { get; set; }
    public double? UsedMb { get; set; }
    public double? AvailableMb { get; set; }
    public double? PercentUsed { get; set; }
    // Fuente real usada, p. ej. "PerformanceCounter + WMI".
    public string? Source { get; set; }
}

/// <summary>
/// Estado del acceso a sensores para la página de Ayuda: cuántos sensores de
/// temperatura de CPU publica el equipo y cuántos traen valor, modo admin,
/// HVCI y si Windows bloqueó el driver de bajo nivel.
/// </summary>
public sealed class DetectionDiagnostics
{
    public bool IsAdmin { get; set; }
    public bool HvciEnabled { get; set; }
    public int CpuTempSensors { get; set; }
    public int CpuTempWithValue { get; set; }
    public int TotalSensors { get; set; }
    public int SensorsWithValue { get; set; }
    // true = Windows bloqueó el driver (verificado en el registro de eventos);
    // false = sin bloqueos; null = no se pudo comprobar (p. ej. sin admin).
    public bool? DriverBlocked { get; set; }
    // Estado del driver de bajo nivel: ok | noadmin | no-pawnio | blocked |
    // loaded-noread | not-deployed | unknown. Ver BuildDiagnostics.
    public string DriverState { get; set; } = "unknown";
    // Driver PawnIO (el que usa LHM 0.9.x para leer MSR): instalado o no.
    public bool PawnIoInstalled { get; set; }
    public string? PawnIoVersion { get; set; }
}

public sealed class BatteryData
{
    public bool Present { get; set; }
    public double? Percent { get; set; }
    // Estado textual: "Cargando", "Cargando · retenida por el equipo",
    // "Conectada · sin cargar", "Cargada (en red)", "Descargando", "Sin batería"
    public string Status { get; set; } = "Desconocido";
    // Estado real de carga (root\wmi BatteryStatus): null = no expuesto.
    public bool? Charging { get; set; }
    // Fuente del nivel mostrado: capacidades reales, Win32_Battery, PowerStatus o LHM.
    public string PercentSource { get; set; } = "";
    public double? DesignCapacityMwh { get; set; }
    public double? FullChargeCapacityMwh { get; set; }
    public double? RemainingCapacityMwh { get; set; }
    // Nivel preciso y descarga, solo si LibreHardwareMonitor los expone (pueden faltar).
    public double? ChargeLevelPct { get; set; }
    public double? DischargeRateW { get; set; }
    public double? ChargeRateW { get; set; }
    public double? RemainingMinutes { get; set; }
    public double? VoltageV { get; set; }
    // Tendencia medida (no predicha): %/hora observado y "nivel retenido".
    public double? TrendPctPerHour { get; set; }
    public bool Held { get; set; }
    public double? HeldMinutes { get; set; }
    // Alimentación externa: "Conectado a la red", "Con batería" o "Desconocido"
    public string PowerLine { get; set; } = "Desconocido";
    // Temperatura SOLO si existe lectura real; en la práctica Windows no la expone -> null
    public double? TemperatureC { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// Entrada ligera para el selector de sensor de temperatura de CPU en
/// Configuración: se envía siempre (23 entradas cortas) aunque la lista
/// completa de sensores no viaje en esta lectura (ahorro de RAM/CPU).
/// </summary>
public sealed class SensorOption
{
    public string Identifier { get; set; } = "";
    public string Name { get; set; } = "";
    public double? Value { get; set; }
}

public sealed class FanData
{
    public string Name { get; set; } = "";
    public string Hardware { get; set; } = "";
    public double? Rpm { get; set; }
    public bool Available { get; set; }
}

public sealed class StorageData
{
    public string Name { get; set; } = "";
    public double? TempC { get; set; }
    public bool Available { get; set; }
}

/// <summary>
/// Mínimo, máximo y promedio de la sesión (desde que se abrió la app).
/// No confundir con las lecturas actuales.
/// </summary>
public sealed class SessionStats
{
    public double? CpuTempMin { get; set; }
    public double? CpuTempMax { get; set; }
    public double? CpuTempAvg { get; set; }
    public double? CpuLoadMax { get; set; }
    public double? CpuLoadAvg { get; set; }
    public double? GpuTempMin { get; set; }
    public double? GpuTempMax { get; set; }
    public int Samples { get; set; }
}

public sealed class SystemMeta
{
    public string? CpuName { get; set; }
    public double? InstalledRamMb { get; set; }
    public string AppUptime { get; set; } = "";
    public int RefreshMs { get; set; }
    public string LastReadIso { get; set; } = "";
    public string LastReadLocal { get; set; } = "";
    public bool IsAdmin { get; set; }
    public string PreferredCpuSensor { get; set; } = "auto";
    // Arranque del sistema (hora local legible).
    public string BootTimeLocal { get; set; } = "";
    // Memoria de trabajo del propio proceso (MB) — visible en Ayuda.
    public double? AppMemoryMb { get; set; }
}

public sealed class Snapshot
{
    public string Timestamp { get; set; } = "";
    public string TimestampLocal { get; set; } = "";
    public CpuData Cpu { get; set; } = new();
    public GpuData Gpu { get; set; } = new();
    public MemoryData Memory { get; set; } = new();
    public BatteryData Battery { get; set; } = new();
    public List<FanData> Fans { get; set; } = new();
    public List<StorageData> Storage { get; set; } = new();
    public SessionStats Session { get; set; } = new();
    public DetectionDiagnostics Diagnostics { get; set; } = new();
    public List<double?> ExtraTemps { get; set; } = new();
    public List<string> ExtraTempNames { get; set; } = new();
    public List<SensorReading> AllSensors { get; set; } = new();
    // Lista ligera (id + nombre + valor) para el selector de sensor de CPU:
    // viaja en cada lectura aunque la tabla completa no se envíe.
    public List<SensorOption> SensorOptions { get; set; } = new();
    public SystemMeta Meta { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

using TempPanel.Sensors;

namespace TempPanel.Services;

/// <summary>
/// Modo de aviso de batería: ventana modal o globo de la bandeja.
/// </summary>
public enum BatteryAlertMode { Popup, Balloon }

/// <summary>
/// Opciones de la alerta de batería. Valida que reactivación &lt; alerta.
/// </summary>
public sealed class BatteryAlertOptions
{
    public bool Enabled { get; set; } = true;
    public double ThresholdPct { get; set; } = 80;
    public double RearmPct { get; set; } = 75;
    public bool Sound { get; set; } = true;
    public BatteryAlertMode Mode { get; set; } = BatteryAlertMode.Popup;

    public void Normalize()
    {
        ThresholdPct = Math.Clamp(ThresholdPct, 50, 100);
        RearmPct = Math.Clamp(RearmPct, 10, 99);
        if (RearmPct >= ThresholdPct) RearmPct = ThresholdPct - 5;
    }

    public static BatteryAlertOptions FromConfig(AppConfig cfg) => new()
    {
        Enabled = cfg.BatteryAlertsEnabled,
        ThresholdPct = cfg.BatteryThreshold,
        RearmPct = cfg.BatteryRearm,
        Sound = cfg.BatterySound,
        Mode = cfg.BatteryMode == "balloon" ? BatteryAlertMode.Balloon : BatteryAlertMode.Popup
    };
}

/// <summary>
/// Lógica pura de la alerta de batería (sin UI: testeable).
/// - Dispara una sola vez por ciclo de carga.
/// - Solo dispara cargando (nunca descargando por encima del umbral).
/// - Se rearma al bajar del umbral de reactivación.
/// - Sin batería/nivel nulo: jamás dispara.
/// </summary>
public sealed class BatteryAlertService
{
    private BatteryAlertOptions _options = new();
    private bool _armed = true;

    public event EventHandler<double>? AlertTriggered;

    public BatteryAlertService() { }
    public BatteryAlertService(BatteryAlertOptions options) => ApplyOptions(options);

    public static BatteryAlertService FromConfig(AppConfig cfg) =>
        new(BatteryAlertOptions.FromConfig(cfg));

    public void ApplyOptions(BatteryAlertOptions options)
    {
        options.Normalize();
        _options = options;
        _armed = true; // al cambiar ajustes, rearmar para no perder avisos
    }

    public bool IsArmed => _armed;

    /// <returns>true si se disparó la alerta en esta evaluación.</returns>
    public bool Evaluate(double? percent, bool charging)
    {
        var o = _options;
        if (!o.Enabled) return false;
        if (!percent.HasValue) return false; // sin lectura: jamás alertar
        double pct = percent.Value;
        if (double.IsNaN(pct) || pct < 0 || pct > 100) return false;

        if (pct < o.RearmPct) _armed = true; // rearma al bajar del umbral

        if (_armed && charging && pct >= o.ThresholdPct)
        {
            _armed = false; // una sola vez por ciclo
            AlertTriggered?.Invoke(this, pct);
            return true;
        }
        return false;
    }
}

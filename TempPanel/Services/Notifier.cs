using TempPanel.Sensors;

namespace TempPanel.Services;

/// <summary>
/// Emite el aviso de batería según el modo configurado.
/// IMPORTANTE: notificar no detiene la carga física; ningún método
/// compatible y seguro para este modelo, así que solo se notifica.
/// </summary>
public sealed class Notifier
{
    public const string Title = "TempPanel — Batería al 80 %";

    private readonly Action<string, string, bool> _showPopup;
    private readonly Action<string, string, bool> _showBalloon;

    public Notifier(Action<string, string, bool> showPopup, Action<string, string, bool> showBalloon)
    {
        _showPopup = showPopup;
        _showBalloon = showBalloon;
    }

    public void NotifyBattery(AppConfig cfg, double pct)
    {
        string title = $"TempPanel — Batería al {cfg.BatteryThreshold:0} %";
        string message = $"La batería de tu portátil ha llegado al {cfg.BatteryThreshold:0} %. " +
            "Si no necesitas toda la autonomía, puedes desconectar el cargador para ayudar a reducir el desgaste de la batería.";
        if (cfg.BatteryMode == "balloon") _showBalloon(title, message, cfg.BatterySound);
        else _showPopup(title, message, cfg.BatterySound);
    }

    public void NotifyBatteryTest(AppConfig cfg)
    {
        // Prueba de apariencia: deja claro que NO es un nivel real.
        const string prefix = "[PRUEBA] Así se verá el aviso. No significa que la batería haya llegado al umbral. ";
        string title = "TempPanel — Prueba de notificación";
        string message = prefix + "La batería de tu portátil ha llegado al 80 %. Si no necesitas toda la autonomía, " +
            "puedes desconectar el cargador para ayudar a reducir el desgaste de la batería.";
        if (cfg.BatteryMode == "balloon") _showBalloon(title, message, cfg.BatterySound);
        else _showPopup(title, message, cfg.BatterySound);
    }
}

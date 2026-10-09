using TempPanel.Sensors;
using TempPanel.Services;
using Xunit;

namespace TempPanel.Tests;

/// <summary>
/// Pruebas automatizadas de la lógica de alerta de batería (§12.7).
/// Usan niveles SIMULADOS solo en el entorno de pruebas: jamás se
/// finge un nivel real en producción (MainWindow usa PowerStatus real).
/// </summary>
public sealed class BatteryAlertTests
{
    private static BatteryAlertService Default() =>
        new(new BatteryAlertOptions { Enabled = true, ThresholdPct = 80, RearmPct = 75 });

    [Fact]
    public void Fires_When_Charging_Reaches_Threshold()
    {
        var svc = Default();
        double? firedAt = null;
        svc.AlertTriggered += (_, pct) => firedAt = pct;
        Assert.True(svc.Evaluate(80.0, charging: true));
        Assert.Equal(80.0, firedAt);
    }

    [Fact]
    public void No_Fire_When_Discharging_Above_Threshold()
    {
        var svc = Default();
        // Mero hecho de estar descargando por encima del 80 %: sin alerta.
        Assert.False(svc.Evaluate(85.0, charging: false));
        Assert.False(svc.Evaluate(100.0, charging: false));
        Assert.True(svc.IsArmed); // sigue armada
    }

    [Fact]
    public void Fires_Once_Per_Charge_Cycle()
    {
        var svc = Default();
        int count = 0;
        svc.AlertTriggered += (_, _) => count++;
        Assert.True(svc.Evaluate(82.0, charging: true));
        Assert.False(svc.Evaluate(85.0, charging: true)); // repetidas: no
        Assert.False(svc.Evaluate(90.0, charging: true));
        Assert.Equal(1, count);
    }

    [Fact]
    public void Rearms_When_Below_Rearm_Threshold()
    {
        var svc = Default();
        int count = 0;
        svc.AlertTriggered += (_, _) => count++;
        Assert.True(svc.Evaluate(82.0, charging: true));
        Assert.False(svc.Evaluate(74.9, charging: true)); // baja de 75: rearma, sin disparar
        Assert.True(svc.IsArmed);
        Assert.True(svc.Evaluate(81.0, charging: true)); // nuevo ciclo: vuelve a avisar
        Assert.Equal(2, count);
    }

    [Fact]
    public void No_Rearm_At_Exactly_Rearm_Value()
    {
        var svc = Default();
        Assert.True(svc.Evaluate(82.0, charging: true));
        Assert.False(svc.Evaluate(75.0, charging: true)); // igual a 75: NO rearma (< estricto)
        Assert.False(svc.IsArmed);
        Assert.False(svc.Evaluate(83.0, charging: true)); // sigue sin avisar
    }

    [Fact]
    public void Disabled_Never_Fires()
    {
        var svc = new BatteryAlertService(new BatteryAlertOptions { Enabled = false });
        Assert.False(svc.Evaluate(95.0, charging: true));
    }

    [Fact]
    public void Null_Level_Never_Fires()
    {
        var svc = Default();
        Assert.False(svc.Evaluate(null, charging: true));
        Assert.True(svc.IsArmed);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(101.0)]
    [InlineData(double.NaN)]
    public void Invalid_Levels_Are_Ignored(double level)
    {
        var svc = Default();
        Assert.False(svc.Evaluate(level, charging: true));
    }

    [Fact]
    public void Custom_Thresholds_Are_Respected()
    {
        var svc = new BatteryAlertService(new BatteryAlertOptions
        {
            Enabled = true, ThresholdPct = 90, RearmPct = 85
        });
        Assert.False(svc.Evaluate(85.0, charging: true));
        Assert.True(svc.Evaluate(90.0, charging: true));
    }

    [Fact]
    public void Rearm_Is_Clamped_Below_Threshold()
    {
        var o = new BatteryAlertOptions { ThresholdPct = 80, RearmPct = 90 };
        o.Normalize();
        Assert.True(o.RearmPct < o.ThresholdPct);
    }

    [Fact]
    public void Full_Cycle_Replug_Scenario()
    {
        var svc = Default();
        var fired = new List<double>();
        svc.AlertTriggered += (_, pct) => fired.Add(pct);
        Assert.True(svc.Evaluate(80.0, charging: true));   // carga hasta 80: avisa
        Assert.False(svc.Evaluate(95.0, charging: true));  // sigue cargando: no repite
        Assert.False(svc.Evaluate(95.0, charging: false)); // desenchufado al 95: no avisa
        Assert.False(svc.Evaluate(70.0, charging: false));  // baja de 75: rearma
        Assert.True(svc.Evaluate(80.5, charging: true));    // nueva carga: avisa otra vez
        Assert.Equal(new[] { 80.0, 80.5 }, fired);
    }
}

/// <summary>Validación de la configuración persistente.</summary>
public sealed class AppConfigTests
{
    [Fact]
    public void Normalize_Enforces_Rearm_Below_Threshold()
    {
        var cfg = new AppConfig { BatteryThreshold = 80, BatteryRearm = 80 };
        cfg.Normalize();
        Assert.True(cfg.BatteryRearm < cfg.BatteryThreshold);
    }

    [Fact]
    public void ResetToDefaults_Restores_Battery_Values()
    {
        var cfg = new AppConfig
        {
            BatteryThreshold = 95, BatteryRearm = 60,
            BatteryAlertsEnabled = false, Unit = "F"
        };
        cfg.ResetToDefaults();
        Assert.Equal(80, cfg.BatteryThreshold);
        Assert.Equal(75, cfg.BatteryRearm);
        Assert.True(cfg.BatteryAlertsEnabled);
        Assert.Equal("C", cfg.Unit);
    }
}

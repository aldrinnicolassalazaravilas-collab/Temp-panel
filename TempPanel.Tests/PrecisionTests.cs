using TempPanel.Sensors;
using Xunit;

namespace TempPanel.Tests;

/// <summary>
/// Pruebas de la lectura precisa de batería (v2.5.0): nivel con decimales a
/// partir de capacidades reales, filtrado de centinelas de Windows y estado
/// honesto ("cargando" vs "retenida por el equipo"). Datos sintéticos.
/// </summary>
public sealed class BatteryReaderTests
{
    // Valores reales observados en el portátil de referencia: 42090/52800 mWh.
    [Fact]
    public void Percent_From_Capacities_Keeps_One_Decimal()
    {
        Assert.Equal(79.7, BatteryReader.PercentFrom(42090, 52800));
    }

    [Fact]
    public void Percent_From_Capacities_Clamps_To_100()
    {
        Assert.Equal(100.0, BatteryReader.PercentFrom(60000, 52800));
    }

    [Theory]
    [InlineData(null, 52800.0)]
    [InlineData(42090.0, null)]
    [InlineData(42090.0, 0.0)]
    [InlineData(-5.0, 52800.0)]
    public void Percent_From_Capacities_Null_When_Data_Missing(double? rem, double? full)
    {
        Assert.Null(BatteryReader.PercentFrom(rem, full));
    }

    // Windows publica 71582788 (0x04444444) cuando no sabe la autonomía.
    [Theory]
    [InlineData(71582788.0)]
    [InlineData(0.0)]
    [InlineData(-3.0)]
    [InlineData(double.NaN)]
    public void Runtime_Sentinels_Become_Null(double minutes)
    {
        Assert.Null(BatteryReader.SanitizeRuntime(minutes));
    }

    [Fact]
    public void Runtime_Real_Value_Is_Kept()
    {
        Assert.Equal(215.0, BatteryReader.SanitizeRuntime(215.4));
    }

    [Theory]
    // Enchufada, cargando y subiendo: cargando de verdad.
    [InlineData(true, true, 79.7, false, "Cargando")]
    // Enchufada, pero el nivel lleva 10 min igual: el equipo retiene la carga.
    [InlineData(true, true, 79.7, true, "Cargando · retenida por el equipo")]
    // Enchufada sin cargar (driver dice que no carga).
    [InlineData(true, false, 60.0, false, "Conectada · sin cargar")]
    // Completa.
    [InlineData(true, true, 100.0, false, "Cargada (en red)")]
    // Con batería: descargando.
    [InlineData(false, false, 55.0, false, "Descargando")]
    public void Status_Is_Honest(bool online, bool charging, double pct, bool held, string expected)
    {
        Assert.Equal(expected, BatteryReader.StatusFor(online, charging, pct, held, present: true));
    }

    [Fact]
    public void Status_Without_Battery_Or_Level()
    {
        Assert.Equal("Sin batería", BatteryReader.StatusFor(null, null, null, false, present: false));
        Assert.Equal("Desconocido", BatteryReader.StatusFor(true, null, null, false, present: true));
    }

    // Varios EC publican la misma potencia en ChargeRate y DischargeRate.
    [Fact]
    public void Rates_Keep_Only_The_Measured_Direction()
    {
        var discharging = BatteryReader.CoherentRates(charging: false, 32.6, 32.6);
        Assert.Null(discharging.ChargeW);
        Assert.Equal(32.6, discharging.DischargeW);

        var charging = BatteryReader.CoherentRates(charging: true, 18.1, 18.1);
        Assert.Equal(18.1, charging.ChargeW);
        Assert.Null(charging.DischargeW);

        // Sin estado confirmado se muestran ambas columnas tal cual.
        var unknown = BatteryReader.CoherentRates(charging: null, 5.0, 7.0);
        Assert.Equal(5.0, unknown.ChargeW);
        Assert.Equal(7.0, unknown.DischargeW);
    }
}

/// <summary>
/// Tendencia de carga: mide solo lo observado (no predice) y detecta el
/// nivel retenido de los modos conservación (típico ~80 %).
/// </summary>
public sealed class BatteryTrendTests
{
    [Fact]
    public void Needs_Minimum_Window_For_Honest_Data()
    {
        var t = new BatteryTrend();
        var t0 = new DateTime(2026, 1, 1, 10, 0, 0);
        t.Add(t0, 79.7, plugged: true);
        t.Add(t0.AddSeconds(4), 79.8, plugged: true); // 4 s: aún no es tendencia
        Assert.Null(t.PctPerHour);
        Assert.False(t.Held);
    }

    [Fact]
    public void Computes_Percent_Per_Hour_From_Real_Samples()
    {
        var t = new BatteryTrend();
        var t0 = new DateTime(2026, 1, 1, 10, 0, 0);
        t.Add(t0, 79.5, plugged: true);
        t.Add(t0.AddMinutes(15), 79.6, plugged: true);
        t.Add(t0.AddMinutes(30), 79.7, plugged: true);
        Assert.Equal(0.4, t.PctPerHour!.Value, 1);
    }

    [Fact]
    public void Detects_Held_Level_While_Plugged()
    {
        var t = new BatteryTrend();
        var t0 = new DateTime(2026, 1, 1, 10, 0, 0);
        t.Add(t0, 79.7, plugged: true);
        t.Add(t0.AddMinutes(10), 79.7, plugged: true);
        Assert.True(t.Held);
        Assert.Equal(10.0, t.HeldMinutes!.Value, 1);
    }

    [Fact]
    public void Not_Held_When_Discharging()
    {
        var t = new BatteryTrend();
        var t0 = new DateTime(2026, 1, 1, 10, 0, 0);
        t.Add(t0, 79.7, plugged: false);
        t.Add(t0.AddMinutes(10), 79.7, plugged: false);
        Assert.False(t.Held);
        Assert.NotNull(t.PctPerHour); // el dato de tendencia sí se mide
    }

    [Fact]
    public void Drops_Samples_Older_Than_Window()
    {
        var t = new BatteryTrend();
        var t0 = new DateTime(2026, 1, 1, 10, 0, 0);
        t.Add(t0, 50.0, plugged: false);
        t.Add(t0.AddMinutes(45), 79.7, plugged: false); // fuera de la ventana de 30 min
        Assert.Null(t.PctPerHour); // sin dos muestras válidas en ventana: sin dato
    }
}

/// <summary>Filtros de plausibilidad de sensores (evitan ceros ficticios).</summary>
public sealed class SensorPlausibilityTests
{
    [Theory]
    [InlineData(0.0, false)]
    [InlineData(-10.0, false)]
    [InlineData(1.9, false)]
    [InlineData(2.0, true)]
    [InlineData(36.5, true)]
    [InlineData(95.0, true)]
    [InlineData(130.0, true)]
    [InlineData(150.0, false)]
    [InlineData(double.NaN, false)]
    public void Temperature_Range_Is_Plausible(double value, bool expected)
    {
        Assert.Equal(expected, HardwareMonitor.IsPlausibleTemp(value));
    }
}

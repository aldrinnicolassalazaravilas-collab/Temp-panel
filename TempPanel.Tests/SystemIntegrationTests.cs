using System.Text.Json;
using TempPanel.Sensors;
using TempPanel.Services;
using Xunit;

namespace TempPanel.Tests;

/// <summary>
/// v3.0.0 — Lógica pura de la integración con Windows: gobernador de
/// rendimiento (intervalos y suspensión del motor) y detección de apps a
/// pantalla completa. Sin GUI y sin tocar hardware real.
/// </summary>
public sealed class PerformanceGovernorTests
{
    [Fact]
    public void Primer_plano_usa_el_intervalo_del_usuario()
    {
        var d = PerformanceGovernor.Evaluate(2000, visible: true, focused: true,
            fullscreen: false, powerSaver: false, adaptive: true);
        Assert.Equal(2000, d.EffectiveMs);
        Assert.Equal(PerformanceGovernor.Mode.Normal, d.ModeName);
        Assert.False(d.SuspendWeb);
    }

    [Fact]
    public void Oculto_o_minimizado_baja_a_5_s_y_suspende_el_motor()
    {
        var d = PerformanceGovernor.Evaluate(1000, visible: false, focused: false,
            fullscreen: false, powerSaver: false, adaptive: true);
        Assert.Equal(5000, d.EffectiveMs);
        Assert.Equal(PerformanceGovernor.Mode.Hidden, d.ModeName);
        Assert.True(d.SuspendWeb);
    }

    [Fact]
    public void Pantalla_completa_baja_a_5_s_y_suspende_el_motor()
    {
        var d = PerformanceGovernor.Evaluate(2000, visible: true, focused: false,
            fullscreen: true, powerSaver: false, adaptive: true);
        Assert.Equal(5000, d.EffectiveMs);
        Assert.Equal(PerformanceGovernor.Mode.Fullscreen, d.ModeName);
        Assert.True(d.SuspendWeb);
    }

    [Fact]
    public void Ahorro_de_energia_baja_a_5_s_sin_suspender_la_interfaz_visible()
    {
        var d = PerformanceGovernor.Evaluate(2000, visible: true, focused: true,
            fullscreen: false, powerSaver: true, adaptive: true);
        Assert.Equal(5000, d.EffectiveMs);
        Assert.Equal(PerformanceGovernor.Mode.Saver, d.ModeName);
        Assert.False(d.SuspendWeb);
    }

    [Fact]
    public void Sin_foco_visible_baja_a_3_s_como_minimo()
    {
        var d = PerformanceGovernor.Evaluate(2000, visible: true, focused: false,
            fullscreen: false, powerSaver: false, adaptive: true);
        Assert.Equal(3000, d.EffectiveMs);
        Assert.Equal(PerformanceGovernor.Mode.Background, d.ModeName);
        Assert.False(d.SuspendWeb);
    }

    [Fact]
    public void Con_reduccion_desactivada_se_respeta_el_intervalo_elegido()
    {
        var d = PerformanceGovernor.Evaluate(1500, visible: true, focused: false,
            fullscreen: true, powerSaver: true, adaptive: false);
        Assert.Equal(1500, d.EffectiveMs);
        Assert.Equal(PerformanceGovernor.Mode.Normal, d.ModeName);
        Assert.False(d.SuspendWeb);
    }

    [Fact]
    public void Oculto_siempre_suspende_aunque_la_reduccion_este_desactivada()
    {
        var d = PerformanceGovernor.Evaluate(2000, visible: false, focused: false,
            fullscreen: false, powerSaver: false, adaptive: false);
        Assert.Equal(5000, d.EffectiveMs);
        Assert.True(d.SuspendWeb);
    }

    [Theory]
    [InlineData(50, 1000)]
    [InlineData(60000, 10000)]
    public void Intervalo_base_se_limita_a_1_10_s(int entrada, int esperado)
    {
        var d = PerformanceGovernor.Evaluate(entrada, visible: true, focused: true,
            fullscreen: false, powerSaver: false, adaptive: true);
        Assert.Equal(esperado, d.EffectiveMs);
    }
}

public sealed class SystemInfoTests
{
    [Fact]
    public void Una_ventana_que_cubre_el_monitor_entero_es_pantalla_completa()
    {
        Assert.True(SystemInfo.CoversMonitor(0, 0, 1920, 1080, 0, 0, 1920, 1080));
    }

    [Fact]
    public void Una_ventana_maximizada_no_cuenta_porque_deja_la_barra_de_tareas()
    {
        // 1080 px de alto menos 48 px de barra de tareas = 95,5 % (<98 %).
        Assert.False(SystemInfo.CoversMonitor(0, 0, 1920, 1032, 0, 0, 1920, 1080));
    }

    [Fact]
    public void Una_ventana_parcial_tampoco_cuenta()
    {
        Assert.False(SystemInfo.CoversMonitor(100, 60, 1200, 900, 0, 0, 1920, 1080));
    }

    [Fact]
    public void Una_ventana_mayor_que_el_monitor_se_recorta_y_cuenta()
    {
        // Algunos juegos crean la ventana con unos píxeles de más.
        Assert.True(SystemInfo.CoversMonitor(-4, -4, 1924, 1084, 0, 0, 1920, 1080));
    }

    [Fact]
    public void Un_monitor_invalido_nunca_cuenta()
    {
        Assert.False(SystemInfo.CoversMonitor(0, 0, 100, 100, 0, 0, 0, 0));
    }
}

public sealed class AppConfigV3Tests
{
    [Fact]
    public void Configuracion_antigua_sin_claves_v3_mantiene_los_valores_nuevos()
    {
        // Un config.json de v2.5.1 no tiene las claves de v3.0.0: deben quedar
        // activas por defecto, sin migraciones manuales.
        var cfg = JsonSerializer.Deserialize<AppConfig>("{\"RefreshMs\":1500}");
        Assert.NotNull(cfg);
        Assert.Equal(1500, cfg!.RefreshMs);
        Assert.True(cfg.HotkeyEnabled);
        Assert.True(cfg.TaskbarBadge);
        Assert.True(cfg.AdaptivePerf);
    }

    [Fact]
    public void Restablecer_ajustes_devuelve_las_claves_v3_a_su_valor()
    {
        var cfg = new AppConfig
        {
            HotkeyEnabled = false,
            TaskbarBadge = false,
            AdaptivePerf = false
        };
        cfg.ResetToDefaults();
        Assert.True(cfg.HotkeyEnabled);
        Assert.True(cfg.TaskbarBadge);
        Assert.True(cfg.AdaptivePerf);
    }
}

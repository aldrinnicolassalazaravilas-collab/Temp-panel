namespace TempPanel.Services;

/// <summary>
/// v3.0.0 — Decide cada cuánto se refrescan los sensores y si conviene
/// suspender el motor WebView2 según lo que esté haciendo el equipo.
/// Es lógica pura (sin WPF) para poder probarla sin abrir ventanas.
///
/// Reglas:
///   · Ventana oculta/minimizada/bloqueada → 5 s y motor suspendido.
///   · Otra app a pantalla completa en primer plano → 5 s y motor suspendido
///     (juegos, vídeo, presentaciones: la interfaz no se ve).
///   · Ahorro de energía de Windows → 5 s.
///   · Ventana visible pero sin foco → 3 s como mínimo.
///   · Ventana en primer plano → el intervalo elegido por el usuario.
/// El usuario puede desactivar la reducción adaptativa en Configuración.
/// </summary>
public static class PerformanceGovernor
{
    public const int MinMs = 1000;
    public const int BackgroundMs = 3000;
    public const int SlowMs = 5000;

    /// <summary>Modos estables (claves) que la interfaz traduce a texto.</summary>
    public static class Mode
    {
        public const string Normal = "normal";
        public const string Background = "background";
        public const string Saver = "saver";
        public const string Fullscreen = "fullscreen";
        public const string Hidden = "hidden";
    }

    public readonly record struct Decision(int EffectiveMs, string ModeName, bool SuspendWeb);

    public static Decision Evaluate(
        int baseMs,
        bool visible,
        bool focused,
        bool fullscreen,
        bool powerSaver,
        bool adaptive)
    {
        baseMs = Math.Clamp(baseMs, MinMs, 10000);

        if (!visible)
            return new Decision(Math.Max(baseMs, SlowMs), Mode.Hidden, true);
        if (!adaptive)
            return new Decision(baseMs, Mode.Normal, false);
        if (fullscreen)
            return new Decision(Math.Max(baseMs, SlowMs), Mode.Fullscreen, true);
        if (powerSaver)
            return new Decision(Math.Max(baseMs, SlowMs), Mode.Saver, false);
        if (!focused)
            return new Decision(Math.Max(baseMs, BackgroundMs), Mode.Background, false);
        return new Decision(baseMs, Mode.Normal, false);
    }
}

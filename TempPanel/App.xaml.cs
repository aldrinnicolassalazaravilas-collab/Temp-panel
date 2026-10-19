using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using TempPanel.Sensors;
using TempPanel.Services;

namespace TempPanel;

/// <summary>
/// Punto de entrada WPF. Modos sin GUI para verificación:
/// --selftest escribe %TEMP%\temppanel-selftest.json y sale.
/// --test-battery-alert muestra la ventana de alerta en modo prueba.
/// --uicheck abre la ventana real (perfil de WebView2 aparte), comprueba que la
/// interfaz carga y sale con 0/1 según el resultado (se puede lanzar sin elevar).
/// </summary>
public partial class App : System.Windows.Application
{
    // Mutex para que el instalador detecte la app en ejecución (cualquier
    // nivel de integridad) y pida cerrarla antes de reemplazar archivos.
    private System.Threading.Mutex? _appMutex;
    private System.Threading.Mutex? _instanceMutex;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? cls, string title);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr h);
    private const int SW_SHOW = 5;
    private const string MainTitle = "TempPanel — Monitor de hardware en tiempo real";

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            AppLogger.Error("UnhandledException: " + ex.ExceptionObject);
        DispatcherUnhandledException += (_, ex) =>
        {
            AppLogger.Error("DispatcherException: " + ex.Exception);
            ex.Handled = true;
        };
        AppLogger.Info("TempPanel iniciado v" + (typeof(App).Assembly.GetName().Version?.ToString(3) ?? "?"));

        try { _appMutex = new System.Threading.Mutex(true, "TempPanelAppMutex"); }
        catch (Exception ex) { AppLogger.Error("Mutex: " + ex.Message); }

        var args = e.Args ?? Array.Empty<string>();
        bool cli = args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase) ||
                                 a.Equals("--test-battery-alert", StringComparison.OrdinalIgnoreCase) ||
                                 a.Equals("--uicheck", StringComparison.OrdinalIgnoreCase));
        if (!cli)
        {
            // Instancia única: si ya hay una, restaura su ventana y sale,
            // salvo relevo de reinicio elevado (--takeover), que espera a que
            // la instancia anterior termine y toma el relevo como principal.
            bool createdNew;
            _instanceMutex = new System.Threading.Mutex(true, "TempPanelSingleInstance", out createdNew);
            if (!createdNew)
            {
                bool takeover = args.Any(a => a.Equals("--takeover", StringComparison.OrdinalIgnoreCase));
                if (takeover && AcquireAfterWait())
                {
                    // La anterior terminó: seguimos como instancia principal.
                }
                else
                {
                    RestoreExisting();
                    Shutdown();
                    return;
                }
            }
        }
        if (args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            RunSelfTest();
            Shutdown();
            return;
        }
        if (args.Any(a => a.Equals("--test-battery-alert", StringComparison.OrdinalIgnoreCase)))
        {
            var cfg = Sensors.AppConfig.Load();
            var win = new BatteryAlertWindow(
                $"TempPanel — Batería al {cfg.BatteryThreshold:0} %",
                BatteryAlertWindow.ThresholdMessage(cfg.BatteryThreshold), isTest: true);
            win.Closed += (_, __) => Shutdown();
            win.Show();
            return;
        }
        if (args.Any(a => a.Equals("--uicheck", StringComparison.OrdinalIgnoreCase)))
        {
            // Verificación de la ventana real sin elevar: crea la ventana con un
            // perfil de WebView2 aparte (no toca la app abierta), espera a que la
            // interfaz cargue y sale con código 0 (OK) o 1 (fallo). Sin bandeja,
            // sin diálogos y sin instancia única.
            base.OnStartup(e);
            var win = new MainWindow(uiCheck: true);
            win.Show();
            StartUiCheck(win);
            return;
        }

        base.OnStartup(e);
        // App.xaml ya no usa StartupUri: la ventana principal se crea aquí (así el
        // modo --uicheck puede abrir la misma ventana con otro perfil sin duplicar).
        var main = new MainWindow();
        main.Show();
    }

    private void StartUiCheck(MainWindow win)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        var sensorsDeadline = DateTime.UtcNow.AddSeconds(20);
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_, __) =>
        {
            if (!win.UiReady && DateTime.UtcNow <= deadline) return;
            // Con la interfaz lista, se espera también al lector de sensores
            // (como máximo 20 s) para dejar evidencia en el log.
            if (win.UiReady && !win.SensorsReady && DateTime.UtcNow <= sensorsDeadline) return;
            timer.Stop();
            bool ok = win.UiReady;
            string control = win.GlassActive ? "cristal (transparencia)" : "clásico (opaco)";
            if (ok) AppLogger.Info($"UICHECK OK: interfaz cargada · control {control} · sensores {(win.SensorsReady ? "listos" : "no listos")}");
            else AppLogger.Error($"UICHECK FALLO: la interfaz no cargó en 30 s · control {control}");
            Environment.Exit(ok ? 0 : 1);
        };
        timer.Start();
    }

    private bool AcquireAfterWait()
    {
        try
        {
            if (_instanceMutex == null) return false;
            return _instanceMutex.WaitOne(TimeSpan.FromSeconds(25));
        }
        catch (AbandonedMutexException)
        {
            return true; // la anterior murió sin liberar: el relevo es nuestro
        }
        catch
        {
            return false;
        }
    }

    private static void RestoreExisting()
    {
        try
        {
            IntPtr h = FindWindow(null, MainTitle);
            if (h != IntPtr.Zero)
            {
                ShowWindow(h, SW_SHOW);
                SetForegroundWindow(h);
            }
        }
        catch (Exception ex)
        {
            Sensors.AppLogger.Error("RestoreExisting: " + ex.Message);
        }
    }

    private static void RunSelfTest()
    {
        try
        {
            using var monitor = new HardwareMonitor { IncludeAllSensors = true };
            var snap = monitor.GetSnapshot();
            string json = JsonSerializer.Serialize(snap, new JsonSerializerOptions { WriteIndented = true });
            string outFile = Path.Combine(Path.GetTempPath(), "temppanel-selftest.json");
            File.WriteAllText(outFile, json);
            AppLogger.Info("Selftest OK -> " + outFile);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Selftest FALLO: " + ex);
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "temppanel-selftest.error.txt"), ex.ToString()); }
            catch { }
        }
    }
}

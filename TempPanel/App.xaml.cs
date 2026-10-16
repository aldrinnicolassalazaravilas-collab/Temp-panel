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
        AppLogger.Info("TempPanel iniciado v1.3.2");

        try { _appMutex = new System.Threading.Mutex(true, "TempPanelAppMutex"); }
        catch (Exception ex) { AppLogger.Error("Mutex: " + ex.Message); }

        var args = e.Args ?? Array.Empty<string>();
        bool cli = args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase) ||
                                 a.Equals("--test-battery-alert", StringComparison.OrdinalIgnoreCase));
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

        base.OnStartup(e);
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
            using var monitor = new HardwareMonitor();
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

using System.IO;
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

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            AppLogger.Error("UnhandledException: " + ex.ExceptionObject);
        DispatcherUnhandledException += (_, ex) =>
        {
            AppLogger.Error("DispatcherException: " + ex.Exception);
            ex.Handled = true;
        };
        AppLogger.Info("TempPanel iniciado v1.1.0");

        try { _appMutex = new System.Threading.Mutex(true, "TempPanelAppMutex"); }
        catch (Exception ex) { AppLogger.Error("Mutex: " + ex.Message); }

        var args = e.Args ?? Array.Empty<string>();
        if (args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            RunSelfTest();
            Shutdown();
            return;
        }
        if (args.Any(a => a.Equals("--test-battery-alert", StringComparison.OrdinalIgnoreCase)))
        {
            var win = new BatteryAlertWindow(isTest: true);
            win.Closed += (_, __) => Shutdown();
            win.Show();
            return;
        }

        base.OnStartup(e);
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

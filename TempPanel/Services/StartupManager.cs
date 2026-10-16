using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace TempPanel.Services;

/// <summary>
/// Inicio con Windows mediante tarea programada de nivel alto
/// (schtasks /SC ONLOGON /RL HIGHEST): así la app arranca elevada sin pedir
/// UAC en cada inicio (patrón de deskpulse/FanControl). Si falla, se usa la
/// clásica clave Run de HKCU como alternativa.
/// Desactivado por defecto; reversible desde la app o el Programador de tareas.
/// </summary>
public static class StartupManager
{
    public const string TaskName = "TempPanel";
    private const string ValueName = "TempPanel";
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    public static string BuildCreateArgs(string exe) =>
        $"/Create /TN \"{TaskName}\" /TR \"\\\"{exe}\\\"\" /SC ONLOGON /RL HIGHEST /F";

    public static string BuildDeleteArgs() => $"/Delete /TN \"{TaskName}\" /F";

    private static string ExePath() =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TempPanel.exe");

    public static bool IsEnabled()
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks", $"/Query /TN \"{TaskName}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            using var p = Process.Start(psi);
            if (p == null) return IsRunKeyEnabled();
            p.WaitForExit(10000);
            if (p.ExitCode == 0) return true;
        }
        catch { }
        return IsRunKeyEnabled();
    }

    public static void Apply(bool enable)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks",
                enable ? BuildCreateArgs(ExePath()) : BuildDeleteArgs())
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(15000);
            if (p != null && p.ExitCode == 0)
            {
                if (!enable) RemoveRunKey();
                return;
            }
        }
        catch (Exception ex) { Sensors.AppLogger.Error("StartupManager task: " + ex.Message); }
        // Alternativa clásica si schtasks falla.
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (key == null) return;
            if (enable) key.SetValue(ValueName, "\"" + ExePath() + "\"");
            else RemoveRunKey();
        }
        catch (Exception ex) { Sensors.AppLogger.Error("StartupManager runkey: " + ex.Message); }
    }

    private static bool IsRunKeyEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(ValueName) != null;
        }
        catch { return false; }
    }

    private static void RemoveRunKey()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (IsRunKeyEnabled()) key!.DeleteValue(ValueName, false);
        }
        catch { }
    }
}

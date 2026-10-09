using System.IO;
using Microsoft.Win32;

namespace TempPanel.Services;

/// <summary>
/// Inicio con Windows mediante HKCU\...\Run (estándar, transparente,
/// desactivado por defecto, reversible desde la app o el Administrador de tareas).
/// </summary>
public static class StartupManager
{
    private const string ValueName = "TempPanel";
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(ValueName) != null;
        }
        catch { return false; }
    }

    public static void Apply(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (key == null) return;
            if (enable)
            {
                string exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TempPanel.exe");
                key.SetValue(ValueName, "\"" + exe + "\"");
            }
            else if (IsEnabled())
            {
                key.DeleteValue(ValueName, false);
            }
        }
        catch (Exception ex) { Sensors.AppLogger.Error("StartupManager: " + ex.Message); }
    }
}

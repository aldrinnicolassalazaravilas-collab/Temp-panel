using Microsoft.Win32;
using TempPanel.Sensors;

namespace TempPanel.Services;

/// <summary>
/// Lectura y cambio (con consentimiento explícito) de la integridad de memoria
/// (HVCI), que bloquea el driver de lectura de temperaturas. Escribir en HKLM
/// exige la app elevada. Los cambios requieren reinicio.
/// </summary>
public static class HvciManager
{
    private const string KeyPath =
        @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath, false);
            var v = key?.GetValue("Enabled");
            if (v is int i) return i != 0;
            if (v is long l) return l != 0;
        }
        catch (Exception ex) { AppLogger.Error("Hvci leer: " + ex.Message); }
        return false;
    }

    public enum ApplyResult { Ok, NoAdmin, Error }

    public static ApplyResult SetEnabled(bool enable)
    {
        try
        {
            if (!HardwareMonitor.IsRunningAsAdmin()) return ApplyResult.NoAdmin;
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath, true)
                ?? Registry.LocalMachine.CreateSubKey(KeyPath);
            if (key == null) return ApplyResult.Error;
            key.SetValue("Enabled", enable ? 1 : 0, RegistryValueKind.DWord);
            AppLogger.Info("Hvci -> " + (enable ? "activada" : "desactivada") + " (requiere reinicio)");
            return ApplyResult.Ok;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Hvci escribir: " + ex.Message);
            return ApplyResult.Error;
        }
    }
}

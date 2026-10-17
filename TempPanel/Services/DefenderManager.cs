using System.Diagnostics;
using System.IO;
using TempPanel.Sensors;

namespace TempPanel.Services;

/// <summary>
/// Exclusión de Windows Defender para las rutas de TempPanel.
/// Objetivo real: que el antivirus no retenga el instalador recién descargado
/// (bloqueo "el proceso no tiene acceso al archivo") ni ralentice las lecturas
/// cada vez que se actualiza. Es opcional, reversible y solo toca la lista de
/// exclusiones de Defender; no desactiva ninguna protección.
/// Nota honesta: SmartScreen (el aviso azul al abrir el instalador desde el
/// navegador) NO se puede evitar sin un certificado de firma de pago; esto
/// solo evita los bloqueos de archivo de Defender.
/// </summary>
public static class DefenderManager
{
    /// <summary>Rutas (o patrones) que se excluyen: carpeta instalada + instaladores temporales.</summary>
    public static string[] TargetPaths()
    {
        var list = new List<string>();
        try
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            if (dir.Length > 0) list.Add(dir);
        }
        catch { }
        try
        {
            string tmp = Path.GetTempPath().TrimEnd('\\', '/');
            if (tmp.Length > 0) list.Add(tmp + @"\TempPanel-Setup-*.exe");
        }
        catch { }
        return list.ToArray();
    }

    public sealed class DefenderStatus
    {
        public bool Ok { get; set; }
        public bool Excluded { get; set; }
        public List<string> Paths { get; set; } = new();
        public string Message { get; set; } = "";
    }

    /// <summary>Lee las exclusiones actuales y dice si la carpeta de la app ya está excluida.</summary>
    public static DefenderStatus GetStatus()
    {
        var st = new DefenderStatus();
        var (code, output, err) = RunPs("@(Get-MpPreference -ErrorAction Stop).ExclusionPath | ForEach-Object { $_ }");
        if (code != 0)
        {
            st.Ok = false;
            st.Message = "No se pudo consultar Windows Defender: " + (string.IsNullOrWhiteSpace(err) ? "error desconocido" : err.Trim());
            return st;
        }
        var paths = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        st.Ok = true;
        st.Paths = paths;
        string target = TargetPaths().FirstOrDefault() ?? "";
        st.Excluded = !string.IsNullOrEmpty(target) &&
            paths.Any(p => string.Equals(p.TrimEnd('\\', '/'), target, StringComparison.OrdinalIgnoreCase));
        st.Message = st.Excluded ? "TempPanel está excluido de Defender." : "TempPanel no está excluido de Defender.";
        return st;
    }

    /// <summary>Añade las exclusiones (carpeta instalada + TempPanel-Setup-*.exe).</summary>
    public static DefenderStatus AddExclusions()
    {
        var paths = TargetPaths();
        if (paths.Length == 0)
            return new DefenderStatus { Ok = false, Message = "No se pudieron determinar las rutas de TempPanel." };
        string quoted = string.Join(", ", paths.Select(p => "'" + p.Replace("'", "''") + "'"));
        var (code, _, err) = RunPs($"Add-MpPreference -ExclusionPath @({quoted}) -ErrorAction Stop; 'OK'");
        if (code != 0)
            return new DefenderStatus { Ok = false, Message = AdminHint(err) };
        var st = GetStatus();
        st.Message = "Exclusión añadida: Defender ya no retendrá los instaladores de TempPanel.";
        return st;
    }

    /// <summary>Quita las exclusiones que añadió TempPanel (solo esas rutas).</summary>
    public static DefenderStatus RemoveExclusions()
    {
        var paths = TargetPaths();
        string quoted = string.Join(", ", paths.Select(p => "'" + p.Replace("'", "''") + "'"));
        var (code, _, err) = RunPs($"Remove-MpPreference -ExclusionPath @({quoted}) -ErrorAction SilentlyContinue; 'OK'");
        if (code != 0)
            return new DefenderStatus { Ok = false, Message = AdminHint(err) };
        var st = GetStatus();
        st.Message = st.Excluded ? "No se pudo quitar (¿gestión por directiva de empresa?)." : "Exclusión quitada.";
        return st;
    }

    private static string AdminHint(string err)
    {
        string msg = "No se pudo aplicar en Defender: " + (string.IsNullOrWhiteSpace(err) ? "error desconocido" : err.Trim());
        if (!HardwareMonitor.IsRunningAsAdmin()) msg += " Abre TempPanel como administrador.";
        return msg;
    }

    private static (int Code, string Out, string Err) RunPs(string script)
    {
        try
        {
            string encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
            var psi = new ProcessStartInfo("powershell")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + encoded
            };
            using var p = Process.Start(psi);
            if (p == null) return (-1, "", "no se pudo iniciar PowerShell");
            string outText = p.StandardOutput.ReadToEnd();
            string errText = p.StandardError.ReadToEnd();
            p.WaitForExit(20000);
            return (p.HasExited ? p.ExitCode : -1, outText, errText);
        }
        catch (Exception ex)
        {
            AppLogger.Error("DefenderManager PowerShell: " + ex.Message);
            return (-1, "", ex.Message);
        }
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;

namespace TempPanel.Services;

/// <summary>
/// v3.0.0 — Lecturas del sistema operativo para la integración nativa
/// (tema, color de acento, ahorro de energía y apps a pantalla completa).
/// Todo es local, barato y tolerante a fallos: si algo no se puede leer se
/// devuelve un valor neutro; nunca se inventan datos de hardware.
/// </summary>
public static class SystemInfo
{
    // ---------------- Versión de Windows ----------------

    public static (string Name, int Build, bool Win11) WindowsVersion()
    {
        int build = Environment.OSVersion.Version.Build;
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (k != null)
            {
                if (int.TryParse(k.GetValue("CurrentBuildNumber") as string, out int b) && b > 0) build = b;
                string? display = k.GetValue("DisplayVersion") as string;
                string baseName = build >= 22000 ? "Windows 11" : "Windows 10";
                return (string.IsNullOrWhiteSpace(display) ? baseName : $"{baseName} {display}", build, build >= 22000);
            }
        }
        catch { }
        return (build >= 22000 ? "Windows 11" : "Windows 10", build, build >= 22000);
    }

    // ---------------- Apariencia ----------------

    /// <summary>Color de acento de Windows en #rrggbb (DWM lo guarda como ABGR).</summary>
    public static string AccentHex()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (k?.GetValue("AccentColor") is int v)
            {
                int r = v & 0xFF, g = (v >> 8) & 0xFF, b = (v >> 16) & 0xFF;
                if (r + g + b >= 24) return $"#{r:x2}{g:x2}{b:x2}";
            }
        }
        catch { }
        return "#4f8cff";
    }

    public static bool IsLightTheme()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v != 0;
        }
        catch { return false; }
    }

    public static bool HighContrast
    {
        get
        {
            try { return SystemParameters.HighContrast; }
            catch { return false; }
        }
    }

    // ---------------- Energía ----------------

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    /// <summary>Modo ahorro de energía (batería) activo en Windows.</summary>
    public static bool PowerSaverActive()
    {
        try { return GetSystemPowerStatus(out var s) && s.SystemStatusFlag == 1; }
        catch { return false; }
    }

    // ---------------- Ventana en primer plano ----------------

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint GA_ROOT = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    private const int GWL_STYLE = -16;
    private const int WS_MAXIMIZE = 0x01000000;

    /// <summary>
    /// Pure function used by the fullscreen detector and by unit tests:
    /// whether a window rectangle covers at least <paramref name="tolerance"/>
    /// of the monitor in both axes. A maximized window does not include the
    /// taskbar, so it stays below the threshold.
    /// </summary>
    public static bool CoversMonitor(
        int left, int top, int right, int bottom,
        int mLeft, int mTop, int mRight, int mBottom,
        double tolerance = 0.98)
    {
        int w = mRight - mLeft, h = mBottom - mTop;
        if (w <= 0 || h <= 0) return false;
        int iw = Math.Min(right, mRight) - Math.Max(left, mLeft);
        int ih = Math.Min(bottom, mBottom) - Math.Max(top, mTop);
        if (iw <= 0 || ih <= 0) return false;
        return iw / (double)w >= tolerance && ih / (double)h >= tolerance;
    }

    /// <summary>
    /// True si la ventana en primer plano es de otro proceso y cubre su monitor
    /// (juego/vídeo a pantalla completa). Ignora nuestras ventanas, el escritorio
    /// y el shell. No requiere permisos especiales.
    /// </summary>
    public static bool ForegroundIsFullscreen(IntPtr ownHwnd)
    {
        try
        {
            var fg = GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == ownHwnd) return false;
            if (fg == GetShellWindow() || fg == GetDesktopWindow()) return false;

            var root = GetAncestor(fg, GA_ROOT);
            if (root != IntPtr.Zero) fg = root;
            if (fg == ownHwnd) return false;

            _ = GetWindowThreadProcessId(fg, out uint pid);
            if (pid == (uint)Environment.ProcessId) return false;

            // Una ventana maximizada no es pantalla completa: con la barra de
            // tareas oculta también cubre el monitor entero, pero el usuario
            // sigue viendo el escritorio y puede alternar con normalidad.
            if ((GetWindowLong(fg, GWL_STYLE) & WS_MAXIMIZE) != 0) return false;

            if (!GetWindowRect(fg, out var r)) return false;
            if (r.Right - r.Left < 320 || r.Bottom - r.Top < 240) return false;

            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST), ref mi)) return false;

            return CoversMonitor(
                r.Left, r.Top, r.Right, r.Bottom,
                mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom);
        }
        catch { return false; }
    }

    // ---------------- Accesos directos a Configuración de Windows ----------------

    /// <summary>Abre una página de Configuración de Windows (Protocolo ms-settings).</summary>
    public static bool OpenWindowsSettings(string key)
    {
        string uri = key switch
        {
            "colors" => "ms-settings:personalization-colors",
            "battery" => "ms-settings:batterysaver",
            "startup" => "ms-settings:startupapps",
            "display" => "ms-settings:display",
            _ => ""
        };
        if (uri.Length == 0) return false;
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Sensors.AppLogger.Error("Abrir Configuración de Windows: " + ex.Message);
            return false;
        }
    }
}

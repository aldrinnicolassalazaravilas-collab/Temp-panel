using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace TempPanel;

/// <summary>
/// Efectos de ventana de Windows 11 (v2.5.0): retroceso acrílico translúcido
/// (cristal), esquinas redondeadas, barra oscura y borde sutil. Si el sistema
/// no lo soporta (p. ej. Windows 10), todo degrada con elegancia a un tema
/// oscuro sólido sin efectos.
/// </summary>
internal static class WindowFx
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    private const int DWMWCP_ROUND = 2;
    private const int DWMSBT_MAINWINDOW = 2;      // Mica (respaldo)
    private const int DWMSBT_TRANSIENTWINDOW = 3; // Acrílico (preferido)

    /// <summary>Windows 11 22H2+ soporta retroceso Mica/Acrílico y esquinas redondeadas.</summary>
    public static bool SupportsGlass() => Environment.OSVersion.Version.Build >= 22621;

    /// <summary>
    /// Aplica cristal acrílico + esquinas redondeadas + barra oscura.
    /// Devuelve true si se aplicó (el contenido de la ventana debe ser
    /// transparente para que el efecto se vea).
    /// </summary>
    public static bool ApplyAcrylic(Window w)
    {
        if (!SupportsGlass()) return false;
        try
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return false;

            int dark = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

            int round = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));

            int acrylic = DWMSBT_TRANSIENTWINDOW;
            int hr = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref acrylic, sizeof(int));
            if (hr != 0)
            {
                int mica = DWMSBT_MAINWINDOW;
                DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref mica, sizeof(int));
            }

            // Borde sutil gris azulado (COLORREF 0x00BBGGRR).
            int border = Rgb(96, 108, 132);
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));

            // Deja que el acrílico se vea a través del contenido WPF.
            if (HwndSource.FromHwnd(hwnd) is { } src)
                src.CompositionTarget.BackgroundColor = Colors.Transparent;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int Rgb(int r, int g, int b) => (r & 0xFF) | ((g & 0xFF) << 8) | ((b & 0xFF) << 16);
}

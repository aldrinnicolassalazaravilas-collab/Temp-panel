using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using TempPanel.Sensors;

namespace TempPanel;

/// <summary>
/// Mini ventana siempre visible (Topmost conmutable): muestra temperatura,
/// carga y batería con un minigráfico. No roba el foco al aparecer y no se
/// cierra al tocar otras apps. Arrastra desde la barra o el gráfico.
/// </summary>
public partial class MiniWindow : Window
{
    private readonly Action _onRestore;
    private readonly List<double?> _loads = new();
    private readonly List<double?> _temps = new();
    private readonly List<double?> _bat = new();
    private readonly List<double?> _ram = new();
    private readonly List<double?> _gpu = new();
    private double _ramTotalMb = 1;
    private const int MaxPoints = 60;

    public MiniWindow(Action onRestore)
    {
        InitializeComponent();
        _onRestore = onRestore;
        Left = SystemParameters.WorkArea.Width - Width - 16;
        Top = SystemParameters.WorkArea.Height - Height - 48;
    }

    public void Update(Snapshot snap)
    {
        Push(_loads, snap.Cpu.TotalLoad);
        Push(_temps, snap.Cpu.PackageTemp);
        Push(_bat, snap.Battery.Present ? snap.Battery.Percent : null);
        Push(_ram, snap.Memory.UsedMb);
        Push(_gpu, snap.Gpu.Load);
        if (snap.Memory.TotalMb.HasValue && snap.Memory.TotalMb.Value > 0)
            _ramTotalMb = snap.Memory.TotalMb.Value;

        TempText.Text = snap.Cpu.PackageTemp.HasValue
            ? $"{snap.Cpu.PackageTemp.Value:0.0} °C" : "— °C";
        LoadText.Text = snap.Cpu.TotalLoad.HasValue
            ? $"CPU {snap.Cpu.TotalLoad.Value:0.0} %" : "CPU —";
        BatText.Text = snap.Battery.Present && snap.Battery.Percent.HasValue
            ? $"Bat {snap.Battery.Percent.Value:0} % · {snap.Battery.Status}" : "Bat —";
        DrawChart();
    }

    private static void Push(List<double?> list, double? v)
    {
        list.Add(v);
        if (list.Count > MaxPoints) list.RemoveRange(0, list.Count - MaxPoints);
    }

    private void DrawChart()
    {
        Chart.Children.Clear();
        if (Chart.ActualWidth <= 0) return;
        double w = Chart.ActualWidth, h = Chart.ActualHeight;
        DrawSeries(_loads, 100.0, "#22D3EE", w, h);
        if (_temps.Any(t => t.HasValue))
            DrawSeries(_temps, null, "#FB923C", w, h);
        if (_bat.Any(t => t.HasValue))
            DrawSeries(_bat, 100.0, "#34D399", w, h);
        if (_ram.Any(t => t.HasValue))
            DrawSeries(_ram, _ramTotalMb, "#A78BFA", w, h);
        if (_gpu.Any(t => t.HasValue))
            DrawSeries(_gpu, 100.0, "#F472B6", w, h);
    }

    private void DrawSeries(List<double?> vals, double? fixedMax, string color, double w, double h)
    {
        var pts = MiniChart.ComputePoints(vals, w, h, fixedMax);
        if (pts.Count < 2) return;
        var pl = new Polyline
        {
            Stroke = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!,
            StrokeThickness = 1.5,
            Points = new PointCollection(pts.Select(p => new System.Windows.Point(p.X, p.Y)))
        };
        Chart.Children.Add(pl);
    }

    private void Bar_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) { try { DragMove(); } catch { } }
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        PinButton.Opacity = Topmost ? 1.0 : 0.45;
    }

    private void RestoreButton_Click(object sender, RoutedEventArgs e) => Restore();
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Restore();

    private void Restore()
    {
        try { Close(); } catch { }
        _onRestore();
    }
}

/// <summary>
/// Cálculo puro de puntos para el minigráfico (testeable, sin UI).
/// </summary>
public static class MiniChart
{
    public static List<(double X, double Y)> ComputePoints(IList<double?> values, double w, double h, double? fixedMax = null)
    {
        var pts = new List<(double X, double Y)>();
        if (w <= 0 || h <= 0) return pts;
        var vals = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        if (vals.Count < 2) return pts;
        double max = fixedMax ?? vals.Max();
        if (max <= 0) max = 1;
        for (int i = 0; i < vals.Count; i++)
        {
            double x = vals.Count == 1 ? 0 : w * i / (vals.Count - 1);
            double y = h - 3 - (h - 6) * Math.Min(1, Math.Max(0, vals[i] / max));
            pts.Add((x, y));
        }
        return pts;
    }
}

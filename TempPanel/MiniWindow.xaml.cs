using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using TempPanel.Sensors;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Cursors = System.Windows.Input.Cursors;
using Orientation = System.Windows.Controls.Orientation;

namespace TempPanel;

/// <summary>
/// Mini ventana siempre visible (Topmost conmutable): temperatura, carga y
/// batería con un minigráfico suavizado. Personalizable (series conmutables,
/// posición guardada) y ampliable (modo compacto ↔ ampliado). No roba el foco
/// al aparecer y no se cierra al usar otras apps: arrastra desde la cabecera
/// o el gráfico.
/// </summary>
public partial class MiniWindow : Window
{
    private readonly AppConfig _config;
    private readonly Action _onConfigChanged;
    private readonly Action _onRestore;
    private readonly Dictionary<string, List<double?>> _series = new();
    private readonly Dictionary<string, Border> _legendChips = new();
    private readonly Dictionary<string, SolidColorBrush> _brushes = new();
    private double _ramTotalMb = 1;
    private const int MaxPoints = 90;

    // Series disponibles: clave, etiqueta y color (coherente con la app).
    private static readonly (string Key, string Label, string Color)[] SeriesDefs =
    {
        ("cpu",  "CPU",  "#4F8CFF"),
        ("temp", "Temp", "#22D3EE"),
        ("bat",  "Bat",  "#34D399"),
        ("ram",  "RAM",  "#A78BFA"),
        ("gpu",  "GPU",  "#F472B6"),
    };

    public MiniWindow(AppConfig config, Action onConfigChanged, Action onRestore)
    {
        InitializeComponent();
        _config = config;
        _onConfigChanged = onConfigChanged;
        _onRestore = onRestore;
        foreach (var d in SeriesDefs)
        {
            _series[d.Key] = new List<double?>();
            _brushes[d.Key] = (SolidColorBrush)new BrushConverter().ConvertFromString(d.Color)!;
        }
        BuildLegend();
        ApplyOptions();
        RestorePosition();
        Chart.SizeChanged += (_, __) => DrawChart();
        Closed += (_, __) => SavePosition();
    }

    /// <summary>Re-aplica tamaño, series y leyenda desde la configuración.</summary>
    public void ApplyOptions()
    {
        try
        {
            Width = _config.MiniExpanded ? 424 : 340;
            Height = _config.MiniExpanded ? 376 : 216;
            StatsPanel.Visibility = _config.MiniExpanded ? Visibility.Visible : Visibility.Collapsed;
            ExpandButton.Content = _config.MiniExpanded ? "⤡" : "⤢";
            PinButton.Opacity = Topmost ? 1.0 : 0.45;
            ClampToScreen();
            UpdateLegendStates();
            DrawChart();
        }
        catch (Exception ex) { Sensors.AppLogger.Error("Mini ApplyOptions: " + ex.Message); }
    }

    public void Update(Snapshot snap)
    {
        Push(_series["cpu"], snap.Cpu.TotalLoad);
        Push(_series["temp"], snap.Cpu.PackageTemp);
        Push(_series["bat"], snap.Battery.Present ? snap.Battery.Percent : null);
        Push(_series["ram"], snap.Memory.UsedMb);
        Push(_series["gpu"], snap.Gpu.Load);
        if (snap.Memory.TotalMb.HasValue && snap.Memory.TotalMb.Value > 0)
            _ramTotalMb = snap.Memory.TotalMb.Value;

        TempText.Text = snap.Cpu.PackageTemp.HasValue
            ? $"{snap.Cpu.PackageTemp.Value:0.0} °C" : "— °C";
        TempText.Foreground = TempColor(_config.Unit, snap.Cpu.PackageTemp, _config.CpuWarnTemp, _config.CpuCritTemp);
        SourceText.Text = snap.Cpu.PackageTempSource ?? "";
        LoadText.Text = snap.Cpu.TotalLoad.HasValue
            ? $"CPU {snap.Cpu.TotalLoad.Value:0.0} %" : "CPU —";
        BatText.Text = snap.Battery.Present && snap.Battery.Percent.HasValue
            ? $"Bat {snap.Battery.Percent.Value:0} % · {snap.Battery.Status}" : "Bat —";

        RamText.Text = snap.Memory.PercentUsed.HasValue ? $"{snap.Memory.PercentUsed.Value:0.0} %"
            : snap.Memory.UsedMb.HasValue ? $"{snap.Memory.UsedMb.Value / 1024.0:0.0} GB" : "—";
        GpuText.Text = snap.Gpu.Load.HasValue ? $"{snap.Gpu.Load.Value:0.0} %"
            : snap.Gpu.Temp.HasValue ? $"{snap.Gpu.Temp.Value:0.0} °C" : "—";
        PwrText.Text = snap.Cpu.Power.HasValue ? $"{snap.Cpu.Power.Value:0.0} W" : "—";
        SessText.Text = snap.Session.CpuTempMin.HasValue && snap.Session.CpuTempMax.HasValue
            ? $"{snap.Session.CpuTempMin.Value:0.0} – {snap.Session.CpuTempMax.Value:0.0}"
            : "—";
        DrawChart();
    }

    private static void Push(List<double?> list, double? v)
    {
        list.Add(v);
        if (list.Count > MaxPoints) list.RemoveRange(0, list.Count - MaxPoints);
    }

    // ---------- Leyenda conmutable ----------
    private void BuildLegend()
    {
        Legend.Children.Clear();
        _legendChips.Clear();
        foreach (var d in SeriesDefs)
        {
            var dot = new Ellipse { Width = 7, Height = 7, Fill = _brushes[d.Key], VerticalAlignment = VerticalAlignment.Center };
            var text = new TextBlock { Text = d.Label, Foreground = (Brush)new BrushConverter().ConvertFromString("#9FB2D4")!, FontSize = 10, Margin = new Thickness(4, 0, 0, 0) };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(dot);
            row.Children.Add(text);
            var chip = new Border
            {
                Background = (Brush)new BrushConverter().ConvertFromString("#141F36")!,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(7, 3, 8, 3),
                Margin = new Thickness(0, 0, 5, 0),
                Cursor = Cursors.Hand,
                Child = row,
                ToolTip = "Clic para mostrar u ocultar esta serie"
            };
            string key = d.Key;
            chip.MouseLeftButtonUp += (_, __) => ToggleSeries(key);
            _legendChips[key] = chip;
            Legend.Children.Add(chip);
        }
    }

    private void ToggleSeries(string key)
    {
        _config.ToggleMiniSeries(key);
        _onConfigChanged();
        UpdateLegendStates();
        DrawChart();
    }

    private void UpdateLegendStates()
    {
        foreach (var d in SeriesDefs)
        {
            if (_legendChips.TryGetValue(d.Key, out var chip))
                chip.Opacity = _config.IsMiniSeriesEnabled(d.Key) ? 1.0 : 0.35;
        }
    }

    // ---------- Gráfico ----------
    private void DrawChart()
    {
        Chart.Children.Clear();
        if (Chart.ActualWidth <= 0 || Chart.ActualHeight <= 0) return;
        double w = Chart.ActualWidth, h = Chart.ActualHeight;

        // Rejilla sutil (3 líneas) para leer la escala sin ruido.
        var gridBrush = new SolidColorBrush(Color.FromArgb(38, 148, 163, 184));
        for (int g = 1; g <= 3; g++)
        {
            double y = h * g / 4.0;
            Chart.Children.Add(new Line
            {
                X1 = 0, X2 = w, Y1 = y, Y2 = y,
                Stroke = gridBrush, StrokeThickness = 1
            });
        }

        // Relleno bajo la temperatura (si está visible) para dar profundidad.
        if (_config.IsMiniSeriesEnabled("temp"))
            DrawArea(_series["temp"], TempRange(_series["temp"]), _brushes["temp"], w, h);
        else if (_config.IsMiniSeriesEnabled("cpu"))
            DrawArea(_series["cpu"], (100.0, 0.0), _brushes["cpu"], w, h);

        foreach (var d in SeriesDefs)
        {
            if (!_config.IsMiniSeriesEnabled(d.Key)) continue;
            var (max, min) = RangeFor(d.Key);
            DrawSeries(_series[d.Key], max, min, _brushes[d.Key], w, h);
        }
    }

    private (double? Max, double? Min) RangeFor(string key) => key switch
    {
        "cpu" or "gpu" or "bat" => (100.0, 0.0),
        "ram" => (_ramTotalMb, 0.0),
        _ => TempRange(_series["temp"]),
    };

    private static (double? Max, double? Min) TempRange(List<double?> vals)
    {
        var v = vals.Where(x => x.HasValue).Select(x => x!.Value).ToList();
        if (v.Count < 2) return (null, null);
        double min = v.Min() - 1, max = v.Max() + 1;
        if (max - min < 6) { double c = (max + min) / 2; min = c - 3; max = c + 3; }
        return (max, min);
    }

    private void DrawArea(List<double?> vals, (double? Max, double? Min) range, SolidColorBrush brush, double w, double h)
    {
        var pts = MiniChart.ComputePoints(vals, w, h, range.Max, range.Min);
        if (pts.Count < 2) return;
        var smooth = MiniChart.Smooth(pts);
        var poly = new Polygon
        {
            Fill = new LinearGradientBrush(brush.Color, Color.FromArgb(0, brush.Color.R, brush.Color.G, brush.Color.B),
                new Point(0, 0), new Point(0, 1)),
            Opacity = 0.22,
            Points = new PointCollection()
        };
        foreach (var p in smooth) poly.Points.Add(new System.Windows.Point(p.X, p.Y));
        poly.Points.Add(new System.Windows.Point(w, h));
        poly.Points.Add(new System.Windows.Point(0, h));
        Chart.Children.Add(poly);
    }

    private void DrawSeries(List<double?> vals, double? fixedMax, double? fixedMin, SolidColorBrush brush, double w, double h)
    {
        var pts = MiniChart.ComputePoints(vals, w, h, fixedMax, fixedMin);
        if (pts.Count < 2) return;
        var smooth = MiniChart.Smooth(pts);

        // Halo: trazo ancho y translúcido debajo (efecto neón suave).
        var glow = new SolidColorBrush(Color.FromArgb(52, brush.Color.R, brush.Color.G, brush.Color.B));
        var halo = new Polyline { Stroke = glow, StrokeThickness = 5, Points = new PointCollection() };
        foreach (var p in smooth) halo.Points.Add(new System.Windows.Point(p.X, p.Y));
        Chart.Children.Add(halo);

        var line = new Polyline
        {
            Stroke = brush,
            StrokeThickness = 1.8,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Points = new PointCollection()
        };
        foreach (var p in smooth) line.Points.Add(new System.Windows.Point(p.X, p.Y));
        Chart.Children.Add(line);

        // Punto actual con halo para saber "dónde estamos ahora".
        var last = smooth[^1];
        var dot = new Ellipse
        {
            Width = 6, Height = 6, Fill = brush,
            ToolTip = null
        };
        Canvas.SetLeft(dot, last.X - 3);
        Canvas.SetTop(dot, last.Y - 3);
        Chart.Children.Add(dot);
    }

    private static Brush TempColor(string unit, double? tempC, double warn, double crit)
    {
        if (!tempC.HasValue) return new SolidColorBrush(Color.FromRgb(0xEE, 0xF4, 0xFF));
        double v = unit == "F" ? tempC.Value * 9 / 5 + 32 : tempC.Value;
        double w = unit == "F" ? warn * 9 / 5 + 32 : warn;
        double c = unit == "F" ? crit * 9 / 5 + 32 : crit;
        if (v >= c) return new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
        if (v >= w) return new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
        return new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xEE));
    }

    // ---------- Posición ----------
    private void RestorePosition()
    {
        double left = _config.MiniLeft ?? (SystemParameters.WorkArea.Width - Width - 16);
        double top = _config.MiniTop ?? (SystemParameters.WorkArea.Height - Height - 48);
        Left = left; Top = top;
        ClampToScreen();
    }

    private void ClampToScreen()
    {
        double vl = SystemParameters.VirtualScreenLeft, vt = SystemParameters.VirtualScreenTop;
        double vw = SystemParameters.VirtualScreenWidth, vh = SystemParameters.VirtualScreenHeight;
        if (double.IsNaN(Left) || Left < vl || Left > vl + vw - Width) Left = Math.Max(vl, vl + vw - Width - 16);
        if (double.IsNaN(Top) || Top < vt || Top > vt + vh - Height) Top = Math.Max(vt, vt + vh - Height - 48);
    }

    private void SavePosition()
    {
        try
        {
            if (double.IsNaN(Left) || double.IsNaN(Top)) return;
            _config.MiniLeft = Left;
            _config.MiniTop = Top;
            _onConfigChanged();
        }
        catch { }
    }

    /// <summary>Vuelve a la esquina inferior derecha (desde Configuración o el botón de reinicio).</summary>
    public void ResetPosition()
    {
        _config.MiniLeft = null;
        _config.MiniTop = null;
        _onConfigChanged();
        RestorePosition();
    }

    // ---------- Eventos de ventana ----------
    private void Bar_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            try { DragMove(); } catch { }
            SavePosition();
        }
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        PinButton.Opacity = Topmost ? 1.0 : 0.45;
    }

    private void ExpandButton_Click(object sender, RoutedEventArgs e)
    {
        _config.MiniExpanded = !_config.MiniExpanded;
        _onConfigChanged();
        ApplyOptions();
        SavePosition();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Restore();

    private void Restore()
    {
        SavePosition();
        try { Close(); } catch { }
        _onRestore();
    }
}

/// <summary>
/// Cálculo puro de puntos para el minigráfico (testeable, sin UI):
/// escala con máximo/mínimo opcionales y suavizado Catmull-Rom.
/// </summary>
public static class MiniChart
{
    public static List<(double X, double Y)> ComputePoints(IList<double?> values, double w, double h,
        double? fixedMax = null, double? fixedMin = null)
    {
        var pts = new List<(double X, double Y)>();
        if (w <= 0 || h <= 0) return pts;
        var vals = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        if (vals.Count < 2) return pts;
        double max = fixedMax ?? vals.Max();
        double min = fixedMin ?? 0;
        if (max - min < 1e-9) { max += 1; min -= 1; }
        for (int i = 0; i < vals.Count; i++)
        {
            double x = w * i / (vals.Count - 1);
            double t = (vals[i] - min) / (max - min);
            double y = h - 3 - (h - 6) * Math.Min(1, Math.Max(0, t));
            pts.Add((x, y));
        }
        return pts;
    }

    /// <summary>Suaviza una polilínea con splines Catmull-Rom (densifica puntos).</summary>
    public static List<(double X, double Y)> Smooth(IList<(double X, double Y)> points, int perSegment = 8)
    {
        var src = points.ToList();
        if (src.Count < 3 || perSegment < 1) return src;
        var outp = new List<(double X, double Y)> { src[0] };
        for (int i = 0; i < src.Count - 1; i++)
        {
            var p0 = src[Math.Max(0, i - 1)];
            var p1 = src[i];
            var p2 = src[i + 1];
            var p3 = src[Math.Min(src.Count - 1, i + 2)];
            for (int s = 1; s <= perSegment; s++)
            {
                double t = (double)s / perSegment;
                double t2 = t * t, t3 = t2 * t;
                double x = 0.5 * ((2 * p1.X) + (-p0.X + p2.X) * t + (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2 + (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * t3);
                double y = 0.5 * ((2 * p1.Y) + (-p0.Y + p2.Y) * t + (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2 + (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3);
                if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y)) continue;
                outp.Add((x, y));
            }
        }
        return outp;
    }
}

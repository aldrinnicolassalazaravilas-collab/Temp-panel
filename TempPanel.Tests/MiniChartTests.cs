using Xunit;

namespace TempPanel.Tests;

/// <summary>Puntos del minigráfico: escala, nulos y casos borde.</summary>
public sealed class MiniChartTests
{
    [Fact]
    public void Scales_To_Area()
    {
        var pts = TempPanel.MiniChart.ComputePoints(
            new double?[] { 0, 50, 100 }, 200, 40, fixedMax: 100);
        Assert.Equal(3, pts.Count);
        Assert.Equal(0, pts[0].X, 3);
        Assert.Equal(200, pts[2].X, 3);
        Assert.True(pts[0].Y > pts[2].Y); // 0 abajo, 100 arriba
    }

    [Fact]
    public void Skips_Nulls_And_Clamps()
    {
        var pts = TempPanel.MiniChart.ComputePoints(
            new double?[] { null, 150, -5 }, 100, 40, fixedMax: 100);
        Assert.Equal(2, pts.Count);
        foreach (var p in pts)
        {
            Assert.InRange(p.X, 0, 100);
            Assert.InRange(p.Y, 0, 40);
        }
    }

    [Fact]
    public void Needs_At_Least_Two_Values()
    {
        Assert.Empty(TempPanel.MiniChart.ComputePoints(new double?[] { 5 }, 100, 40));
        Assert.Empty(TempPanel.MiniChart.ComputePoints(new double?[] { null }, 100, 40));
        Assert.Empty(TempPanel.MiniChart.ComputePoints(new double?[] { 1, 2 }, 0, 40));
    }

    [Fact]
    public void Scales_With_Fixed_Min()
    {
        var pts = TempPanel.MiniChart.ComputePoints(
            new double?[] { 40, 50 }, 100, 40, fixedMax: 60, fixedMin: 30);
        Assert.Equal(2, pts.Count);
        Assert.True(pts[0].Y > pts[1].Y); // 40 más abajo que 50
        Assert.InRange(pts[0].Y, 0, 40);
        Assert.InRange(pts[1].Y, 0, 40);
    }

    [Fact]
    public void Smooth_Keeps_Endpoints_And_Densifies()
    {
        var pts = new List<(double X, double Y)> { (0, 0), (10, 5), (20, 0), (30, 8) };
        var smooth = TempPanel.MiniChart.Smooth(pts);
        Assert.True(smooth.Count > pts.Count);
        Assert.Equal(pts[0].X, smooth[0].X, 6);
        Assert.Equal(pts[0].Y, smooth[0].Y, 6);
        Assert.Equal(pts[^1].X, smooth[^1].X, 6);
        Assert.Equal(pts[^1].Y, smooth[^1].Y, 6);
        foreach (var p in smooth)
        {
            Assert.False(double.IsNaN(p.X) || double.IsNaN(p.Y));
            Assert.False(double.IsInfinity(p.X) || double.IsInfinity(p.Y));
        }
    }

    [Fact]
    public void Smooth_Passthrough_When_Too_Few_Points()
    {
        var pts = new List<(double X, double Y)> { (0, 0), (10, 5) };
        var smooth = TempPanel.MiniChart.Smooth(pts);
        Assert.Equal(2, smooth.Count);
        Assert.Equal(pts[0], smooth[0]);
        Assert.Equal(pts[1], smooth[1]);
    }
}

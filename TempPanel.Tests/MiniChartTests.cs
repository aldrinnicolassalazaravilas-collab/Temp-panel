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
}

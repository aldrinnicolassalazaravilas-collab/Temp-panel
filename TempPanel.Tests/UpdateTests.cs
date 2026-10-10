using TempPanel.Sensors;
using TempPanel.Services;
using Xunit;

namespace TempPanel.Tests;

/// <summary>Comparación de versiones del actualizador (pura, sin red).</summary>
public sealed class UpdateServiceTests
{
    [Theory]
    [InlineData("v1.1.0", 1, 1, 0)]
    [InlineData("1.1.0", 1, 1, 0)]
    [InlineData("V2.0", 2, 0, -1)]
    public void ParseTag_Parses(string tag, int major, int minor, int build)
    {
        var v = UpdateService.ParseTag(tag);
        Assert.NotNull(v);
        Assert.Equal(major, v!.Major);
        Assert.Equal(minor, v.Minor);
        if (build >= 0) Assert.Equal(build, v.Build);
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("vX")]
    public void ParseTag_Rejects_Garbage(string tag)
    {
        Assert.Null(UpdateService.ParseTag(tag));
    }

    [Fact]
    public void IsNewer_Compares()
    {
        Assert.True(UpdateService.IsNewer(new Version(1, 1, 0), new Version(1, 2, 0)));
        Assert.False(UpdateService.IsNewer(new Version(1, 1, 0), new Version(1, 1, 0)));
        Assert.False(UpdateService.IsNewer(new Version(2, 0, 0), new Version(1, 9, 9)));
    }

    [Fact]
    public void RepoTarget_Is_Configured()
    {
        Assert.Equal("aldrinnicolassalazaravilas-collab", UpdateService.RepoOwner);
        Assert.Equal("Temp-panel", UpdateService.RepoName);
        Assert.Contains("api.github.com", UpdateService.ApiUrl);
    }

    [Fact]
    public async Task DownloadAndLaunch_BadUrl_Returns_Null_Fast()
    {
        // Sin red externa: localhost cerrado falla al instante.
        var r = await UpdateService.DownloadAndLaunchAsync("http://127.0.0.1:9/nope.exe", "v9.9.9-test");
        Assert.Null(r);
    }

    [Fact]
    public void NotifyBattery_Uses_Configured_Threshold()
    {
        string gotTitle = "", gotMsg = "";
        var n = new Notifier((t, m, s) => { gotTitle = t; gotMsg = m; }, (_, _, _) => { });
        var cfg = new AppConfig { BatteryThreshold = 67, BatterySound = false, BatteryMode = "popup" };
        n.NotifyBattery(cfg, 67.0);
        Assert.Contains("67", gotTitle);
        Assert.Contains("67", gotMsg);
        Assert.DoesNotContain("80", gotTitle + gotMsg);
    }
}

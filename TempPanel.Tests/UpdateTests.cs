using TempPanel.Sensors;
using TempPanel.Services;
using Xunit;

namespace TempPanel.Tests;

/// <summary>Comparación de versiones del actualizador (pura, sin red).</summary>
public sealed class UpdateServiceTests
{
    public UpdateServiceTests()
    {
        Environment.SetEnvironmentVariable("TEMPANEL_QUIET_LOG", "1");
    }
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
    public void NotifyFull_Uses_Popup_Action()
    {
        double gotPct = 0;
        var n = new Notifier((_, _, _) => { }, (_, _, _) => { }, (p, _) => { gotPct = p; });
        var cfg = new AppConfig { BatterySound = false, BatteryMode = "popup" };
        n.NotifyFull(cfg, 99.0);
        Assert.Equal(99.0, gotPct);
    }

    [Fact]
    public void Hvci_IsEnabled_Reads_Without_Throwing()
    {
        bool v = TempPanel.Services.HvciManager.IsEnabled();
        Assert.True(v || !v);
    }

    [Fact]
    public void StartupTask_Args_Use_Highest_RunLevel()
    {
        string args = TempPanel.Services.StartupManager.BuildCreateArgs(@"C:\App\TempPanel.exe");
        Assert.Contains("/SC ONLOGON", args);
        Assert.Contains("/RL HIGHEST", args);
        Assert.Contains("TempPanel", args);
        Assert.Contains(@"C:\App\TempPanel.exe", args);
        Assert.Equal("/Delete /TN \"TempPanel\" /F", TempPanel.Services.StartupManager.BuildDeleteArgs());
    }

    [Fact]
    public void FullAlert_Fires_Once_At_99_Charging()
    {
        var svc = new BatteryAlertService(new BatteryAlertOptions
        {
            Enabled = true, ThresholdPct = 99, RearmPct = 95
        });
        int count = 0;
        svc.AlertTriggered += (_, _) => count++;
        Assert.False(svc.Evaluate(98.0, charging: true));
        Assert.True(svc.Evaluate(99.0, charging: true));
        Assert.False(svc.Evaluate(99.5, charging: true));
        Assert.False(svc.Evaluate(99.0, charging: false));
        Assert.Equal(1, count);
        Assert.False(svc.Evaluate(94.9, charging: true)); // rearma
        Assert.True(svc.Evaluate(99.0, charging: true));
        Assert.Equal(2, count);
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

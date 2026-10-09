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
}

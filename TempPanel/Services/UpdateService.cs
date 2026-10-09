using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace TempPanel.Services;

/// <summary>
/// Actualizaciones vía GitHub Releases (gratuito, sin claves ni telemetría).
/// Solo contacta con la red cuando el usuario pulsa "Buscar actualizaciones"
/// o activa la comprobación automática (desactivada por defecto).
/// </summary>
public sealed class UpdateInfo
{
    public string Tag { get; set; } = "";
    public Version Version { get; set; } = new(0, 0, 0);
    public string SetupUrl { get; set; } = "";
    public string Notes { get; set; } = "";
}

public sealed class CheckResult
{
    public UpdateInfo? Info { get; set; }
    public bool NoReleases { get; set; }
}

public static class UpdateService
{
    public const string RepoOwner = "aldrinnicolassalazaravilas-collab";
    public const string RepoName = "Temp-panel";
    public const string AssetSuffix = "TempPanel-Setup-x64.exe";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    static UpdateService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("TempPanel");
        Http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public static string ApiUrl =>
        $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

    public static Version Current()
    {
        var v = typeof(UpdateService).Assembly.GetName().Version;
        return v == null ? new Version(1, 0, 0) : new Version(v.Major, v.Minor, v.Build);
    }

    /// <returns>info si hay versión nueva; null si está al día o sin red.</returns>
    public static async Task<CheckResult> CheckAsync()
    {
        try
        {
            using var res = await Http.GetAsync(ApiUrl);
            if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new CheckResult { NoReleases = true };
            if (!res.IsSuccessStatusCode) return new CheckResult();
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            var ver = ParseTag(tag);
            if (ver == null || !IsNewer(Current(), ver)) return new CheckResult();
            string url = "";
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var a in assets.EnumerateArray())
                {
                    string name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (name.EndsWith(AssetSuffix, StringComparison.OrdinalIgnoreCase) &&
                        a.TryGetProperty("browser_download_url", out var u))
                    { url = u.GetString() ?? ""; break; }
                }
            }
            if (string.IsNullOrEmpty(url)) return new CheckResult();
            string notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            return new CheckResult { Info = new UpdateInfo { Tag = tag, Version = ver, SetupUrl = url, Notes = notes } };
        }
        catch
        {
            return new CheckResult(); // sin red o error: silencio, la app sigue local
        }
    }

    /// <summary>Descarga el instalador a %TEMP% y lo lanza. El llamador cierra la app.</summary>
    public static async Task<string?> DownloadAndLaunchAsync(string url, string tag)
    {
        try
        {
            string safe = new(string.Concat(tag.Where(c => char.IsLetterOrDigit(c) || c is '.' or '-')));
            string dest = Path.Combine(Path.GetTempPath(), $"TempPanel-Setup-{safe}.exe");
            using var res = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!res.IsSuccessStatusCode) return null;
            await using var net = await res.Content.ReadAsStreamAsync();
            await using var file = File.Create(dest);
            await net.CopyToAsync(file);
            if (new FileInfo(dest).Length < 1024 * 1024) return null; // descarga corrupta
            Process.Start(new ProcessStartInfo(dest) { UseShellExecute = true });
            return dest;
        }
        catch (Exception ex)
        {
            Sensors.AppLogger.Error("Update download: " + ex.Message);
            return null;
        }
    }

    public static Version? ParseTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        string t = tag.Trim().TrimStart('v', 'V');
        return Version.TryParse(t, out var v) ? v : null;
    }

    public static bool IsNewer(Version current, Version latest) => latest > current;
}

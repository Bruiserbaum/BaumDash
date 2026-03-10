using System.Text.Json;

namespace WinUIAudioMixer.Services;

/// <summary>Information about a newer GitHub release.</summary>
public sealed record UpdateReleaseInfo(string Version, string DownloadUrl, string Notes);

/// <summary>
/// Checks the GitHub Releases API for newer versions and downloads/installs them.
/// No NuGet packages — uses HttpClient (BCL) and System.Text.Json.
/// </summary>
public static class UpdateService
{
    /// <summary>Version that is currently running.  Bump this with every release.</summary>
    public const string CurrentVersion = "2.3.1";

    private const string GitHubOwner = "Bruiserbaum";
    private const string GitHubRepo  = "BaumDash";
    private const string ApiUrl      = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

    /// <summary>Set after a successful <see cref="CheckAsync"/> that finds a newer version.</summary>
    public static UpdateReleaseInfo? AvailableRelease { get; private set; }

    // ── Version check ─────────────────────────────────────────────────────────

    /// <summary>
    /// Queries the GitHub Releases API.  Returns a <see cref="UpdateReleaseInfo"/> if a newer
    /// version is available, or <c>null</c> if already up-to-date or the check fails silently.
    /// </summary>
    public static async Task<UpdateReleaseInfo?> CheckAsync()
    {
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            http.DefaultRequestHeaders.Add("User-Agent", $"BaumDash/{CurrentVersion}");

            var json = await http.GetStringAsync(ApiUrl);
            using var doc  = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string tag   = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            string notes = root.TryGetProperty("body",     out var b) ? b.GetString() ?? "" : "";

            // Tags may be "v2.3.0" or "2.3.0"
            string version = tag.TrimStart('v');
            if (!IsNewer(version, CurrentVersion)) return null;

            // Find the installer .exe asset
            string downloadUrl = "";
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    string name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.TryGetProperty("browser_download_url", out var u)
                            ? u.GetString() ?? "" : "";
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(downloadUrl)) return null;

            var release      = new UpdateReleaseInfo(version, downloadUrl, notes);
            AvailableRelease = release;
            return release;
        }
        catch { return null; }
    }

    // ── Download + install ────────────────────────────────────────────────────

    /// <summary>
    /// Downloads the installer to %TEMP%, launches it, and exits BaumDash so the
    /// installer can replace the running executable.
    /// </summary>
    public static async Task DownloadAndInstallAsync(
        UpdateReleaseInfo release,
        IProgress<int>?   progress  = null,
        CancellationToken ct        = default)
    {
        var fileName  = $"BaumDash-Setup-{release.Version}.exe";
        var tempPath  = Path.Combine(Path.GetTempPath(), fileName);

        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.Add("User-Agent", $"BaumDash/{CurrentVersion}");

        using var response = await http.GetAsync(
            release.DownloadUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        long total  = response.Content.Headers.ContentLength ?? -1;
        long downloaded = 0;
        var  buffer = new byte[81_920];

        await using var src = await response.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(tempPath);

        int read;
        while ((read = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            downloaded += read;
            if (total > 0)
                progress?.Report((int)(downloaded * 100 / total));
        }

        dst.Close();

        // Launch installer; BaumDash exits so the installer can overwrite the exe
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName        = tempPath,
            UseShellExecute = true,
        });

        Application.Exit();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsNewer(string latest, string current)
    {
        if (Version.TryParse(latest,  out var v1) &&
            Version.TryParse(current, out var v2))
            return v1 > v2;

        // Fallback: lexicographic (handles unusual tag formats)
        return string.Compare(latest, current, StringComparison.OrdinalIgnoreCase) > 0;
    }
}

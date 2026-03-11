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
    /// <summary>
    /// Version that is currently running — uses AssemblyInformationalVersion so pre-release
    /// suffixes like "-dev" are preserved (e.g. "2.5.0-dev" instead of just "2.5.0").
    /// Build metadata appended by the SDK (+commit hash) is stripped.
    /// </summary>
    public static readonly string CurrentVersion = GetCurrentVersion();

    private static string GetCurrentVersion()
    {
        var asm  = System.Reflection.Assembly.GetExecutingAssembly();
        var attr = System.Reflection.CustomAttributeExtensions
                        .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(asm);
        var info = attr?.InformationalVersion ?? "";
        // Strip build metadata suffix (+abc1234) appended by the .NET SDK
        var plus = info.IndexOf('+');
        if (plus >= 0) info = info[..plus];
        if (!string.IsNullOrWhiteSpace(info)) return info;
        // Fallback to numeric-only version
        return asm.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    private const string GitHubOwner    = "Bruiserbaum";
    private const string GitHubRepo     = "BaumDash";
    private const string ApiLatest      = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
    private const string ApiAllReleases = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases?per_page=20";

    /// <summary>
    /// Release channel — set from GeneralConfig at startup.
    /// "stable" = production releases only (default); "dev" = include pre-release / Dev-branch builds.
    /// </summary>
    public static string Channel { get; set; } = "stable";

    /// <summary>Set after a successful <see cref="CheckAsync"/> that finds a newer version.</summary>
    public static UpdateReleaseInfo? AvailableRelease { get; private set; }

    // ── Version check ─────────────────────────────────────────────────────────

    /// <summary>
    /// Queries the GitHub Releases API.  Returns a <see cref="UpdateReleaseInfo"/> if a newer
    /// version is available, or <c>null</c> if already up-to-date or the check fails silently.
    /// Respects <see cref="Channel"/>: "dev" includes pre-releases, "stable" ignores them.
    /// </summary>
    public static async Task<UpdateReleaseInfo?> CheckAsync()
    {
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            http.DefaultRequestHeaders.Add("User-Agent", $"BaumDash/{CurrentVersion}");

            JsonElement root;
            if (Channel == "dev")
            {
                // Fetch the full list and take the first release (newest, including pre-releases)
                var listJson = await http.GetStringAsync(ApiAllReleases);
                using var listDoc = JsonDocument.Parse(listJson);
                var arr = listDoc.RootElement;
                if (arr.GetArrayLength() == 0) return null;
                // Clone so we can use it outside the using block
                root = arr[0].Clone();
            }
            else
            {
                var json = await http.GetStringAsync(ApiLatest);
                using var doc = JsonDocument.Parse(json);
                root = doc.RootElement.Clone();
            }

            string tag   = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            string notes = root.TryGetProperty("body",     out var b) ? b.GetString() ?? "" : "";
            bool   isPre = root.TryGetProperty("prerelease", out var pr) && pr.GetBoolean();

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

            var label    = isPre ? $"{version} (Dev)" : version;
            var release  = new UpdateReleaseInfo(label, downloadUrl, notes);
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

        // Write a PowerShell helper that waits for this process to fully exit
        // before running the installer — eliminates the race where the exe file
        // is still locked when the installer tries to replace it.
        int pid     = System.Diagnostics.Process.GetCurrentProcess().Id;
        var ps1Path = Path.Combine(Path.GetTempPath(), "baum-update.ps1");
        var escapedPath = tempPath.Replace("'", "''");
        var script = "# Wait for BaumDash to exit\n" +
                     "$proc = Get-Process -Id " + pid + " -ErrorAction SilentlyContinue\n" +
                     "if ($proc) { $proc.WaitForExit(10000) | Out-Null }\n" +
                     "Start-Sleep -Milliseconds 500\n" +
                     "# Run installer silently\n" +
                     "Start-Process -FilePath '" + escapedPath + "' -ArgumentList '/SILENT' -Wait\n";
        File.WriteAllText(ps1Path, script);

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName        = "powershell.exe",
            Arguments       = $"-NonInteractive -WindowStyle Hidden -File \"{ps1Path}\"",
            UseShellExecute = true,
        });

        Application.Exit();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsNewer(string latest, string current)
    {
        // Strip pre-release suffixes like "-dev", "-beta.1" before numeric comparison
        static string StripPreRelease(string v) =>
            v.Contains('-') ? v[..v.IndexOf('-')] : v;

        var latestClean  = StripPreRelease(latest);
        var currentClean = StripPreRelease(current);

        bool latestIsPre  = latest.Contains('-');
        bool currentIsPre = current.Contains('-');

        if (Version.TryParse(latestClean,  out var v1) &&
            Version.TryParse(currentClean, out var v2))
        {
            if (v1 != v2)
            {
                // Running a dev build and switching to stable: always offer the
                // stable even if its number is lower (e.g. 2.5.0 stable when on 2.5.9-dev).
                if (currentIsPre && !latestIsPre) return true;
                return v1 > v2;
            }
            // Same numeric version: stable release is newer than same-number pre-release.
            return !latestIsPre && currentIsPre;
        }

        // Fallback: lexicographic
        return string.Compare(latestClean, currentClean, StringComparison.OrdinalIgnoreCase) > 0;
    }
}

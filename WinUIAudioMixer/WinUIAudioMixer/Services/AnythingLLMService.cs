using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WinUIAudioMixer.Models;

namespace WinUIAudioMixer.Services;

public sealed class AnythingLLMService : IDisposable
{
    private readonly HttpClient _http;
    private readonly string     _workspace;

    public bool   IsConfigured { get; }
    public string Workspace    => _workspace;

    public AnythingLLMService(AnythingLLMConfig cfg)
    {
        _workspace = cfg.Workspace;
        IsConfigured = !string.IsNullOrEmpty(cfg.Url)       &&
                       !string.IsNullOrEmpty(cfg.ApiKey)    &&
                       cfg.ApiKey    != "YOUR_API_KEY"       &&
                       !string.IsNullOrEmpty(cfg.Workspace);

        _http = new HttpClient
        {
            BaseAddress = new Uri(cfg.Url.TrimEnd('/')),
            Timeout     = TimeSpan.FromMinutes(5),
        };
        if (!string.IsNullOrEmpty(cfg.ApiKey))
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", cfg.ApiKey);
    }

    public async Task<string> ChatAsync(string message, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new { message, mode = "chat" });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync(
            $"/api/v1/workspace/{_workspace}/chat", content, ct);

        var json = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            // Try to extract a meaningful message from the JSON body
            string detail = json;
            try
            {
                using var errDoc = JsonDocument.Parse(json);
                var r = errDoc.RootElement;
                if (r.TryGetProperty("message", out var m)) detail = m.GetString() ?? json;
                else if (r.TryGetProperty("error", out var e)) detail = e.GetString() ?? json;
            }
            catch { }
            throw new Exception($"HTTP {(int)resp.StatusCode}: {detail}");
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var err) &&
            err.ValueKind != JsonValueKind.Null &&
            !string.IsNullOrEmpty(err.GetString()))
            throw new Exception(err.GetString());

        return root.GetProperty("textResponse").GetString() ?? "";
    }

    /// <summary>Returns (name, slug) pairs for all workspaces, or null on failure.</summary>
    public async Task<List<(string Name, string Slug)>?> GetWorkspacesAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.GetAsync("/api/v1/workspaces", ct);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var list = new List<(string, string)>();
            foreach (var ws in doc.RootElement.GetProperty("workspaces").EnumerateArray())
            {
                var name = ws.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var slug = ws.TryGetProperty("slug", out var s) ? s.GetString() ?? "" : "";
                if (!string.IsNullOrEmpty(slug)) list.Add((name, slug));
            }
            return list;
        }
        catch { return null; }
    }

    public static AnythingLLMConfig? LoadConfig()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "anythingllm-config.json");
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<AnythingLLMConfig>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    public void Dispose() => _http.Dispose();
}

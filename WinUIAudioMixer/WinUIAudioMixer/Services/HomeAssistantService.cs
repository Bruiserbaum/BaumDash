using System.Text;
using System.Text.Json;
using WinUIAudioMixer.Models;

namespace WinUIAudioMixer.Services;

/// <summary>
/// Thin wrapper around the Home Assistant REST API.
/// Reads ha-config.json from AppContext.BaseDirectory.
/// </summary>
public sealed class HomeAssistantService : IDisposable
{
    private readonly HttpClient _http;
    private readonly string     _baseUrl;

    public HaConfig Config       { get; }
    public bool     IsConfigured { get; }

    public HomeAssistantService(HaConfig config)
    {
        Config       = config;
        IsConfigured = !string.IsNullOrWhiteSpace(config.Url) &&
                       !string.IsNullOrWhiteSpace(config.Token) &&
                       config.Token != "YOUR_LONG_LIVED_ACCESS_TOKEN";

        _baseUrl = config.Url.TrimEnd('/');
        _http    = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

        if (IsConfigured)
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.Token);
    }

    // ── State queries ─────────────────────────────────────────────────────────

    /// <summary>Returns (state, unit_of_measurement) for a sensor entity.</summary>
    public async Task<(string state, string unit)> GetSensorAsync(string entityId)
    {
        try
        {
            var resp = await _http.GetAsync($"{_baseUrl}/api/states/{entityId}");
            if (!resp.IsSuccessStatusCode) return ("?", "");
            var json = await resp.Content.ReadAsStringAsync();
            using var doc  = JsonDocument.Parse(json);
            var root  = doc.RootElement;
            var state = root.GetProperty("state").GetString() ?? "?";
            var attrs = root.GetProperty("attributes");
            var unit  = attrs.TryGetProperty("unit_of_measurement", out var u) ? u.GetString() ?? "" : "";
            return (state, unit);
        }
        catch { return ("?", ""); }
    }

    /// <summary>Returns true when the light entity state is "on".</summary>
    public async Task<bool> GetLightStateAsync(string entityId)
    {
        try
        {
            var resp = await _http.GetAsync($"{_baseUrl}/api/states/{entityId}");
            if (!resp.IsSuccessStatusCode) return false;
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("state").GetString() == "on";
        }
        catch { return false; }
    }

    // ── Service calls ─────────────────────────────────────────────────────────

    public Task ToggleLightAsync(string entityId) =>
        CallServiceAsync("light", "toggle", entityId);

    public Task TurnOnLightAsync(string entityId) =>
        CallServiceAsync("light", "turn_on", entityId);

    public Task TurnOffLightAsync(string entityId) =>
        CallServiceAsync("light", "turn_off", entityId);

    /// <summary>Returns true when the switch entity state is "on".</summary>
    public async Task<bool> GetSwitchStateAsync(string entityId)
    {
        try
        {
            var resp = await _http.GetAsync($"{_baseUrl}/api/states/{entityId}");
            if (!resp.IsSuccessStatusCode) return false;
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("state").GetString() == "on";
        }
        catch { return false; }
    }

    public Task ToggleSwitchAsync(string entityId) =>
        CallServiceAsync("switch", "toggle", entityId);

    private async Task CallServiceAsync(string domain, string service, string entityId)
    {
        try
        {
            var body = new StringContent(
                JsonSerializer.Serialize(new { entity_id = entityId }),
                Encoding.UTF8,
                "application/json");
            await _http.PostAsync($"{_baseUrl}/api/services/{domain}/{service}", body);
        }
        catch { }
    }

    // ── Config loading ────────────────────────────────────────────────────────

    /// <summary>
    /// Reads ha-config.json next to the exe.
    /// Returns null if the file doesn't exist or can't be parsed.
    /// </summary>
    public static HaConfig? LoadConfig()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ha-config.json");
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<HaConfig>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    public void Dispose() => _http.Dispose();
}

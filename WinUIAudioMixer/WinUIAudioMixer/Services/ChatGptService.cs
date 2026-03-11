using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WinUIAudioMixer.Models;

namespace WinUIAudioMixer.Services;

public sealed class ChatGptService : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly List<Dictionary<string, string>> _messages = new();

    public bool   IsConfigured { get; }
    public string Model        => _model;

    public ChatGptService(ChatGptConfig cfg)
    {
        _model = string.IsNullOrWhiteSpace(cfg.Model) ? "gpt-4o" : cfg.Model;
        IsConfigured = !string.IsNullOrEmpty(cfg.ApiKey) && cfg.ApiKey != "YOUR_API_KEY";

        _http = new HttpClient
        {
            BaseAddress = new Uri("https://api.openai.com"),
            Timeout     = TimeSpan.FromMinutes(2),
        };
        if (!string.IsNullOrEmpty(cfg.ApiKey))
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", cfg.ApiKey);
    }

    public async Task<string> ChatAsync(string message, CancellationToken ct = default)
    {
        _messages.Add(new Dictionary<string, string> { ["role"] = "user", ["content"] = message });

        var body = JsonSerializer.Serialize(new { model = _model, messages = _messages });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync("/v1/chat/completions", content, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            string detail = json;
            try
            {
                using var errDoc = JsonDocument.Parse(json);
                if (errDoc.RootElement.TryGetProperty("error", out var err) &&
                    err.TryGetProperty("message", out var m))
                    detail = m.GetString() ?? json;
            }
            catch (JsonException) { }
            _messages.RemoveAt(_messages.Count - 1); // don't keep failed message in history
            throw new Exception($"HTTP {(int)resp.StatusCode}: {detail}");
        }

        using var doc = JsonDocument.Parse(json);
        var text = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";

        _messages.Add(new Dictionary<string, string> { ["role"] = "assistant", ["content"] = text });
        return text;
    }

    public void ClearHistory() => _messages.Clear();

    public static ChatGptConfig? LoadConfig()
    {
        var secure = SecureStorage.Load();
        if (string.IsNullOrEmpty(secure.ChatGptApiKey)) return null;

        var path = Path.Combine(AppContext.BaseDirectory, "chatgpt-config.json");
        ChatGptConfig cfg;
        try
        {
            cfg = (File.Exists(path)
                ? JsonSerializer.Deserialize<ChatGptConfig>(
                    File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                : null) ?? new ChatGptConfig();
        }
        catch { cfg = new ChatGptConfig(); }

        cfg.ApiKey = secure.ChatGptApiKey;
        return cfg;
    }

    public void Dispose() => _http.Dispose();
}

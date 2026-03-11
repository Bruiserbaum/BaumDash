using System.Security.Cryptography;
using System.Text.Json;
using WinUIAudioMixer.Models;

namespace WinUIAudioMixer.Services;

/// <summary>
/// Persists all user secrets in a Windows DPAPI-encrypted file (baum-secure.dat).
/// The file can only be decrypted by the same Windows user account on the same machine.
/// </summary>
internal static class SecureStorage
{
    private static readonly string StorePath =
        Path.Combine(AppContext.BaseDirectory, "baum-secure.dat");

    private static SecurePayload? _cached;

    // ── Public API ────────────────────────────────────────────────────────────

    public static SecurePayload Load()
    {
        if (_cached != null) return _cached;
        try
        {
            if (!File.Exists(StorePath)) return _cached = new SecurePayload();
            var encrypted = File.ReadAllBytes(StorePath);
            var json      = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            _cached = JsonSerializer.Deserialize<SecurePayload>(json) ?? new SecurePayload();
        }
        catch (Exception ex)
        {
            CrashLogger.Error("SecureStorage.Load failed", ex);
            _cached = new SecurePayload();
        }
        return _cached;
    }

    public static void Save(SecurePayload payload)
    {
        try
        {
            _cached = payload;
            var json      = JsonSerializer.SerializeToUtf8Bytes(payload);
            var encrypted = ProtectedData.Protect(json, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(StorePath, encrypted);
            CrashLogger.Info("SecureStorage saved");
        }
        catch (Exception ex)
        {
            CrashLogger.Error("SecureStorage.Save failed", ex);
            throw;
        }
    }

    /// <summary>Force a reload from disk on the next Load() call (e.g. after import).</summary>
    public static void Invalidate() => _cached = null;

    // ── One-time migration from legacy plaintext files ────────────────────────

    /// <summary>
    /// If the secure store doesn't exist yet, reads secrets from the old plaintext config files,
    /// saves them encrypted, and deletes the sensitive source files.
    /// Safe to call every startup — exits immediately if already migrated.
    /// </summary>
    public static void MigrateFromPlaintext()
    {
        if (File.Exists(StorePath)) return;

        var payload = new SecurePayload();
        var dir     = AppContext.BaseDirectory;

        // Discord
        var idPath = Path.Combine(dir, "discord-client-id.txt");
        if (File.Exists(idPath))
            payload.DiscordClientId = File.ReadAllText(idPath).Trim();

        var secretPath = Path.Combine(dir, "discord-client-secret.txt");
        if (File.Exists(secretPath))
            payload.DiscordClientSecret = File.ReadAllText(secretPath).Trim();

        // ChatGPT — ApiKey only; Model stays in chatgpt-config.json
        TryReadJson(Path.Combine(dir, "chatgpt-config.json"), doc =>
        {
            if (doc.RootElement.TryGetProperty("ApiKey", out var v) ||
                doc.RootElement.TryGetProperty("apiKey", out v))
                payload.ChatGptApiKey = v.GetString() ?? "";
        });

        // AnythingLLM — ApiKey only; Url+Workspace stay in anythingllm-config.json
        TryReadJson(Path.Combine(dir, "anythingllm-config.json"), doc =>
        {
            if (doc.RootElement.TryGetProperty("ApiKey", out var v) ||
                doc.RootElement.TryGetProperty("apiKey", out v))
                payload.AnythingLLMApiKey = v.GetString() ?? "";
        });

        // Home Assistant — Token only; Url+entities stay in ha-config.json
        TryReadJson(Path.Combine(dir, "ha-config.json"), doc =>
        {
            if (doc.RootElement.TryGetProperty("Token", out var v) ||
                doc.RootElement.TryGetProperty("token", out v))
                payload.HaToken = v.GetString() ?? "";
        });

        // Google Calendar — iCal URLs are secrets (contain auth tokens)
        TryReadJson(Path.Combine(dir, "gcalendar-config.json"), doc =>
        {
            // Legacy single-URL migration
            if (doc.RootElement.TryGetProperty("ICalUrl", out var single) ||
                doc.RootElement.TryGetProperty("iCalUrl", out single))
            {
                var url = single.GetString();
                if (!string.IsNullOrEmpty(url))
                    payload.Calendars.Add(new Models.CalendarEntry { Name = "Calendar", ICalUrl = url });
            }

            if (doc.RootElement.TryGetProperty("Calendars", out var arr) ||
                doc.RootElement.TryGetProperty("calendars", out arr))
            {
                foreach (var el in arr.EnumerateArray())
                {
                    var name = el.TryGetProperty("Name", out var n) || el.TryGetProperty("name", out n)
                        ? n.GetString() ?? "Calendar" : "Calendar";
                    var url  = el.TryGetProperty("ICalUrl", out var u) || el.TryGetProperty("iCalUrl", out u)
                        ? u.GetString() ?? "" : "";
                    if (!string.IsNullOrEmpty(url))
                        payload.Calendars.Add(new Models.CalendarEntry { Name = name, ICalUrl = url });
                }
            }
        });

        Save(payload);
        CrashLogger.Info("Migrated secrets from plaintext files to secure storage");

        // Remove plaintext sensitive files
        TryDelete(idPath);
        TryDelete(secretPath);
    }

    // ── Ongoing legacy cleanup ────────────────────────────────────────────────

    /// <summary>
    /// Removes plaintext secret artifacts that are no longer needed now that
    /// secrets live in the DPAPI-encrypted store.  Safe to call every startup.
    /// </summary>
    public static void CleanupLegacyFiles()
    {
        var dir = AppContext.BaseDirectory;

        // Delete plaintext secret files (migrated or never used)
        TryDelete(Path.Combine(dir, "discord-client-id.txt"));
        TryDelete(Path.Combine(dir, "discord-client-secret.txt"));

        // gcalendar-config.json is now fully replaced by SecurePayload.Calendars
        TryDelete(Path.Combine(dir, "gcalendar-config.json"));

        // Strip secret keys from JSON configs so they no longer contain plaintext
        StripSecretsFromJson(Path.Combine(dir, "chatgpt-config.json"),     "ApiKey");
        StripSecretsFromJson(Path.Combine(dir, "anythingllm-config.json"), "ApiKey");
        StripSecretsFromJson(Path.Combine(dir, "ha-config.json"),          "Token");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void TryReadJson(string path, Action<JsonDocument> read)
    {
        if (!File.Exists(path)) return;
        try { using var doc = JsonDocument.Parse(File.ReadAllText(path)); read(doc); }
        catch { }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    /// <summary>
    /// Rewrites a JSON file with the specified top-level keys removed.
    /// Handles both PascalCase and camelCase variants of each key name.
    /// No-ops silently if the file doesn't exist or contains no matching keys.
    /// </summary>
    private static void StripSecretsFromJson(string path, params string[] keysToRemove)
    {
        if (!File.Exists(path)) return;
        try
        {
            var text = File.ReadAllText(path);
            var obj  = System.Text.Json.Nodes.JsonNode.Parse(text)
                       as System.Text.Json.Nodes.JsonObject;
            if (obj == null) return;

            bool changed = false;
            foreach (var key in keysToRemove)
            {
                // Remove both "ApiKey" and "apiKey" variants
                if (obj.ContainsKey(key))                                { obj.Remove(key); changed = true; }
                var camel = char.ToLowerInvariant(key[0]) + key[1..];
                if (key != camel && obj.ContainsKey(camel))              { obj.Remove(camel); changed = true; }
            }

            if (!changed) return;

            var opts = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(path, obj.ToJsonString(opts));
            CrashLogger.Info($"SecureStorage: stripped secrets from {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            CrashLogger.Error($"SecureStorage.StripSecretsFromJson failed for {path}", ex);
        }
    }
}

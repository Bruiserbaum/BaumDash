using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WinUIAudioMixer.Services;

public enum DiscordConnectionState { Disconnected, Connecting, Connected, Error }

public sealed class DiscordMember
{
    public string UserId   { get; init; } = "";
    public string Username { get; init; } = "";
    public string Nick     { get; init; } = "";
    public bool IsMuted    { get; init; }
    public bool IsDeafened { get; init; }
    public bool IsSpeaking { get; init; }
    public bool IsStreaming { get; init; }

    public string DisplayName => string.IsNullOrWhiteSpace(Nick) ? Username : Nick;
}

public sealed record DiscordMessage(string Author, string Content, string ChannelId, string GuildId = "");

/// <summary>
/// Discord local RPC client over the named pipe \\.\pipe\discord-ipc-N.
/// Requires a Discord Application client_id (create one at discord.com/developers).
/// </summary>
public sealed class DiscordService : IDisposable
{
    private readonly string _clientId;
    private NamedPipeClientStream? _pipe;
    private CancellationTokenSource? _cts;

    // Thread-safe pending-command registry (read loop + background tasks share it)
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonNode?>> _pending = new();
    private readonly ConcurrentDictionary<string, string> _channelGuildCache = new(); // channelId → guildId
    private readonly ConcurrentDictionary<string, string> _guildNames        = new(); // guildId → name

    // Recent message history (last 10) — shown in chat box on connect
    private readonly List<DiscordMessage> _messageHistory = new();
    private readonly object _historyLock = new();

    // Serialises pipe writes so concurrent Task.Run callers don't interleave frames
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private bool    _micMuted;
    private bool    _selfStreaming;
    private string? _currentChannelId;
    private string? _currentUserId;
    private TaskCompletionSource<bool>? _connectTcs;

    public event Action<DiscordConnectionState>? ConnectionStateChanged;
    public event Action<List<DiscordMember>>?    VoiceStateChanged;
    public event Action<bool>?                   MicMuteChanged;
    public event Action<DiscordMessage>?         MessageReceived;
    public event Action<string?>?                GuildChanged;
    public event Action<bool>?                   StreamingStateChanged;

    public DiscordConnectionState State         { get; private set; } = DiscordConnectionState.Disconnected;
    public bool    IsMicMuted        => _micMuted;
    public bool    IsStreaming       => _selfStreaming;
    public string? CurrentGuildName  { get; private set; }
    public List<DiscordMember> VoiceMembers { get; private set; } = new();

    public IReadOnlyList<DiscordMessage> RecentMessages
    {
        get { lock (_historyLock) return _messageHistory.ToList(); }
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_clientId) &&
        _clientId != "YOUR_DISCORD_CLIENT_ID";

    public DiscordService(string clientId) => _clientId = clientId;

    // ── Connection ────────────────────────────────────────────────────────────

    public async Task ConnectAsync()
    {
        _connectTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        SetState(DiscordConnectionState.Connecting);
        _cts = new CancellationTokenSource();

        for (int i = 0; i <= 9; i++)
        {
            try
            {
                var p = new NamedPipeClientStream(".", $"discord-ipc-{i}",
                    PipeDirection.InOut, PipeOptions.Asynchronous);
                await p.ConnectAsync(1500, _cts.Token);
                _pipe = p;
                break;
            }
            catch { }
        }

        if (_pipe == null || !_pipe.IsConnected)
        {
            SetState(DiscordConnectionState.Error);
            return;
        }

        await SendOpcodeAsync(0, new JsonObject { ["v"] = 1, ["client_id"] = _clientId });
        _ = Task.Run(ReadLoopAsync, _cts.Token);

        using var timeout = new CancellationTokenSource(8000);
        timeout.Token.Register(() => _connectTcs?.TrySetResult(false));
        await _connectTcs.Task;
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (_pipe is { IsConnected: true } && !(_cts?.IsCancellationRequested ?? true))
            {
                var (op, payload) = await ReadMessageAsync();
                HandleMessage(op, payload);   // synchronous – never blocks on pipe reads
            }
        }
        catch { }
        SetState(DiscordConnectionState.Disconnected);
    }

    // ── Message handling ──────────────────────────────────────────────────────
    // IMPORTANT: This method must stay synchronous and fast.
    // Any work that needs SendCommandAsync (which awaits a pipe response) must be
    // fired as Task.Run so the read loop is free to deliver that response.

    private void HandleMessage(int opcode, JsonNode? msg)
    {
        if (msg == null) return;

        if (opcode == 1) // FRAME
        {
            var cmd   = msg["cmd"]?.GetValue<string>();
            var evt   = msg["evt"]?.GetValue<string>();
            var nonce = msg["nonce"]?.GetValue<string>();
            var data  = msg["data"];

            // Resolve a pending command awaiter
            if (nonce != null && _pending.TryRemove(nonce, out var tcs))
                tcs.TrySetResult(data);

            switch (evt)
            {
                case "READY":
                    ParseReadyData(data);
                    SetState(DiscordConnectionState.Connected);
                    _ = Task.Run(PostReadySetupAsync);   // needs SendCommandAsync – off-loop
                    break;

                // Discord replays VOICE_STATE_CREATE for every current member when you
                // subscribe to a channel, giving us the initial list without GET_CHANNEL.
                case "VOICE_STATE_CREATE":
                case "VOICE_STATE_UPDATE":
                    ApplyVoiceStateCreate(data);
                    break;

                case "VOICE_STATE_DELETE":
                    ApplyVoiceStateDelete(data);
                    break;

                case "SPEAKING_START":
                    SetSpeaking(data?["user_id"]?.GetValue<string>(), true);
                    break;

                case "SPEAKING_STOP":
                    SetSpeaking(data?["user_id"]?.GetValue<string>(), false);
                    break;

                case "MESSAGE_CREATE":
                    _ = Task.Run(() => HandleMessageCreateAsync(data));
                    break;

                case "NOTIFICATION_CREATE":
                    _ = Task.Run(() => HandleNotificationAsync(data));
                    break;

                case "VOICE_SETTINGS_UPDATE":
                    _micMuted = data?["mute"]?.GetValue<bool>() ?? _micMuted;
                    MicMuteChanged?.Invoke(_micMuted);
                    break;

                case "VOICE_CHANNEL_SELECT":
                    _ = Task.Run(() => HandleVoiceChannelSelectAsync(data));
                    break;
            }

            // GET_VOICE_SETTINGS response also carries the current mute state
            if (cmd == "GET_VOICE_SETTINGS" && evt != "ERROR")
            {
                _micMuted = data?["mute"]?.GetValue<bool>() ?? _micMuted;
                MicMuteChanged?.Invoke(_micMuted);
            }
        }
        else if (opcode == 2) // CLOSE
        {
            _pipe?.Close();
        }
    }

    // Runs on a thread-pool task after READY so SendCommandAsync calls can get responses.
    private async Task PostReadySetupAsync()
    {
        await AuthenticateAsync();           // must succeed before voice commands work
        await SendCommandAsync("GET_VOICE_SETTINGS", new JsonObject());
        await PopulateGuildNamesAsync();
        await GetCurrentVoiceChannelAsync();
        // If not in a voice channel, still surface a guild name from the known guilds
        if (CurrentGuildName == null && _guildNames.Count > 0)
        {
            CurrentGuildName = _guildNames.Values.First();
            GuildChanged?.Invoke(CurrentGuildName);
        }
        await TrySubscribeAsync("NOTIFICATION_CREATE",  new JsonObject());
        await TrySubscribeAsync("VOICE_SETTINGS_UPDATE", new JsonObject());
        await TrySubscribeAsync("VOICE_CHANNEL_SELECT",  new JsonObject());
        await SubscribeToGuildTextChannelsAsync();
    }

    /// <summary>
    /// Subscribe to MESSAGE_CREATE for all text/announcement channels in every known guild.
    /// Requires the messages.read OAuth scope to have been granted.
    /// </summary>
    private async Task SubscribeToGuildTextChannelsAsync()
    {
        foreach (var guildId in _guildNames.Keys)
        {
            try
            {
                var data     = await SendCommandAsync("GET_CHANNELS", new JsonObject { ["guild_id"] = guildId });
                var channels = data?["channels"]?.AsArray();
                if (channels == null) continue;
                foreach (var ch in channels)
                {
                    var type = ch?["type"]?.GetValue<int>() ?? -1;
                    // 0 = GUILD_TEXT, 5 = GUILD_ANNOUNCEMENT — skip voice/stage/forum/etc.
                    if (type != 0 && type != 5) continue;
                    var chId = ch?["id"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(chId)) continue;
                    _channelGuildCache[chId] = guildId;
                    await TrySubscribeAsync("MESSAGE_CREATE", new JsonObject { ["channel_id"] = chId });
                }
            }
            catch { }
        }
    }

    // ── OAuth authentication ──────────────────────────────────────────────────

    private static readonly string _tokenPath =
        Path.Combine(AppContext.BaseDirectory, "discord-token.txt");

    private static readonly string _debugLogPath =
        Path.Combine(AppContext.BaseDirectory, "discord-debug.log");

    private async Task AuthenticateAsync()
    {
        var log = new StringBuilder();
        log.AppendLine($"[{DateTime.Now:HH:mm:ss}] AuthenticateAsync started. BaseDir={AppContext.BaseDirectory}");
        try
        {
            // 1. Try a previously saved token
            log.AppendLine($"  TokenPath exists: {File.Exists(_tokenPath)}");
            if (File.Exists(_tokenPath))
            {
                var saved = File.ReadAllText(_tokenPath).Trim();
                if (!string.IsNullOrEmpty(saved))
                {
                    log.AppendLine("  Trying saved token...");
                    var authData = await SendCommandAsync("AUTHENTICATE",
                        new JsonObject { ["access_token"] = saved });
                    log.AppendLine($"  AUTHENTICATE(saved) response: {authData?.ToJsonString() ?? "null"}");
                    if (authData?["access_token"] != null)
                    {
                        // Grab user ID from auth response as fallback if READY event didn't supply it
                        _currentUserId ??= authData?["user"]?["id"]?.GetValue<string>();
                        log.AppendLine($"  Saved token still valid. CurrentUserId={_currentUserId ?? "null"}");
                        return;
                    }
                    log.AppendLine("  Saved token expired/invalid.");
                }
            }

            // 2. No valid token — need client secret to do the AUTHORIZE flow
            var secret = LoadClientSecret();
            log.AppendLine($"  Client secret source: " +
                           (!string.IsNullOrEmpty(SecureStorage.Load().DiscordClientSecret)
                               ? "SecureStorage"
                               : File.Exists(Path.Combine(AppContext.BaseDirectory, "discord-client-secret.txt"))
                                   ? "plaintext file"
                                   : "not found"));
            if (string.IsNullOrEmpty(secret))
            {
                log.AppendLine("  ERROR: No client secret found. Aborting.");
                return;
            }
            log.AppendLine($"  Client secret loaded ({secret.Length} chars).");

            // 3. Send AUTHORIZE — Discord shows a popup; user has 60 s to click Authorize
            // Note: redirect_uri must NOT be sent for the local RPC flow (Discord error 5000)
            log.AppendLine("  Sending AUTHORIZE (waiting up to 60 s for user to click Discord popup)...");
            var authorizeData = await SendCommandAsync("AUTHORIZE", new JsonObject
            {
                ["client_id"] = _clientId,
                ["scopes"]    = new JsonArray("rpc", "rpc.voice.read", "rpc.voice.write", "messages.read"),
            }, timeoutMs: 60_000);
            log.AppendLine($"  AUTHORIZE response: {authorizeData?.ToJsonString() ?? "null (timed out or error)"}");

            // Success: "code" is a string auth code. Error: "code" is an integer error code.
            string? code = null;
            try { code = authorizeData?["code"]?.GetValue<string>(); }
            catch (InvalidOperationException)
            {
                var errMsg = authorizeData?["message"]?.GetValue<string>() ?? "unknown";
                log.AppendLine($"  ERROR: AUTHORIZE returned Discord error: {errMsg}");
                return;
            }
            if (string.IsNullOrEmpty(code))
            {
                log.AppendLine("  ERROR: No auth code received. Aborting.");
                return;
            }
            log.AppendLine($"  Auth code received ({code.Length} chars).");

            // 4. Exchange code for access token
            log.AppendLine("  Exchanging code for access token...");
            var token = await ExchangeCodeAsync(code, secret, log);
            if (string.IsNullOrEmpty(token))
            {
                log.AppendLine("  ERROR: Token exchange failed. Aborting.");
                return;
            }
            log.AppendLine("  Token obtained.");

            // 5. Authenticate with the new token
            var finalAuth = await SendCommandAsync("AUTHENTICATE",
                new JsonObject { ["access_token"] = token });
            log.AppendLine($"  AUTHENTICATE(new) response: {finalAuth?.ToJsonString() ?? "null"}");
            _currentUserId ??= finalAuth?["user"]?["id"]?.GetValue<string>();

            // 6. Save token so future sessions skip the popup
            File.WriteAllText(_tokenPath, token);
            log.AppendLine("  Token saved. Auth complete.");
        }
        catch (Exception ex)
        {
            log.AppendLine($"  EXCEPTION: {ex}");
        }
        finally
        {
            File.AppendAllText(_debugLogPath, log.ToString());
        }
    }

    private async Task<string?> ExchangeCodeAsync(string code, string clientSecret, StringBuilder? log = null)
    {
        try
        {
            using var http = new HttpClient();
            var body = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string,string>("client_id",     _clientId),
                new KeyValuePair<string,string>("client_secret", clientSecret),
                new KeyValuePair<string,string>("grant_type",    "authorization_code"),
                new KeyValuePair<string,string>("code",          code),
                new KeyValuePair<string,string>("redirect_uri",  "http://localhost"),
            });
            var resp = await http.PostAsync("https://discord.com/api/v10/oauth2/token", body);
            var json = await resp.Content.ReadAsStringAsync();
            log?.AppendLine($"  Token exchange HTTP {(int)resp.StatusCode}: {json}");
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("access_token").GetString();
        }
        catch (Exception ex) { log?.AppendLine($"  Token exchange EXCEPTION: {ex.Message}"); return null; }
    }

    private static string? LoadClientSecret()
    {
        // Primary source: DPAPI-encrypted secure storage (current builds)
        var fromSecure = SecureStorage.Load().DiscordClientSecret;
        if (!string.IsNullOrEmpty(fromSecure)) return fromSecure;

        // Fallback: legacy plaintext file (pre-SecureStorage builds, not yet migrated)
        var path = Path.Combine(AppContext.BaseDirectory, "discord-client-secret.txt");
        if (!File.Exists(path)) return null;
        var s = File.ReadAllText(path).Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    private void ParseReadyData(JsonNode? data)
    {
        if (data == null) return;
        _currentUserId = data["user"]?["id"]?.GetValue<string>();

        var guilds = data["guilds"]?.AsArray();
        if (guilds != null)
            foreach (var g in guilds)
            {
                var id   = g?["id"]  ?.GetValue<string>();
                var name = g?["name"]?.GetValue<string>();
                if (id != null && name != null) _guildNames[id] = name;
            }
    }

    // ── Voice member list (event-driven, no GET_CHANNEL needed) ──────────────

    private void ApplyVoiceStateCreate(JsonNode? data)
    {
        var m = ParseVoiceStateMember(data);
        if (m == null) return;

        // Preserve live IsSpeaking — voice_state events don't carry speaking state
        var prev = VoiceMembers.FirstOrDefault(x => x.UserId == m.UserId);
        if (prev?.IsSpeaking == true)
            m = new DiscordMember
            {
                UserId = m.UserId, Username = m.Username, Nick = m.Nick,
                IsMuted = m.IsMuted, IsDeafened = m.IsDeafened,
                IsStreaming = m.IsStreaming, IsSpeaking = true,
            };
        VoiceMembers = VoiceMembers
            .Where(x => x.UserId != m.UserId)
            .Append(m)
            .ToList();
        TrackSelfStreaming();

        // Keep our own mute button in sync with our voice state
        if (m.UserId == _currentUserId)
        {
            _micMuted = m.IsMuted;
            MicMuteChanged?.Invoke(_micMuted);
        }

        VoiceStateChanged?.Invoke(VoiceMembers);
    }

    private void ApplyVoiceStateDelete(JsonNode? data)
    {
        var userId = data?["user"]?["id"]?.GetValue<string>();
        if (userId == null) return;
        VoiceMembers = VoiceMembers.Where(m => m.UserId != userId).ToList();
        TrackSelfStreaming();
        VoiceStateChanged?.Invoke(VoiceMembers);
    }

    private static DiscordMember? ParseVoiceStateMember(JsonNode? data)
    {
        var userId = data?["user"]?["id"]?.GetValue<string>();
        if (userId == null) return null;
        var vs = data?["voice_state"];
        return new DiscordMember
        {
            UserId     = userId,
            Username   = data?["user"]?["username"]?.GetValue<string>() ?? "",
            Nick       = data?["nick"]?.GetValue<string>() ?? "",
            IsMuted    = vs?["self_mute"]  ?.GetValue<bool>() ?? false,
            IsDeafened = vs?["self_deaf"]  ?.GetValue<bool>() ?? false,
            IsStreaming = vs?["self_stream"]?.GetValue<bool>() ?? false,
        };
    }

    private void TrackSelfStreaming()
    {
        var nowStreaming = _currentUserId != null
            ? VoiceMembers.FirstOrDefault(m => m.UserId == _currentUserId)?.IsStreaming ?? false
            : false;
        if (nowStreaming == _selfStreaming) return;
        _selfStreaming = nowStreaming;
        StreamingStateChanged?.Invoke(_selfStreaming);
    }

    private void SetSpeaking(string? userId, bool speaking)
    {
        if (userId == null) return;
        VoiceMembers = VoiceMembers.Select(m => m.UserId == userId
            ? new DiscordMember
            {
                UserId = m.UserId, Username = m.Username, Nick = m.Nick,
                IsMuted = m.IsMuted, IsDeafened = m.IsDeafened,
                IsStreaming = m.IsStreaming, IsSpeaking = speaking
            } : m).ToList();
        VoiceStateChanged?.Invoke(VoiceMembers);
    }

    // ── Chat messages ─────────────────────────────────────────────────────────

    private Task HandleMessageCreateAsync(JsonNode? data)
    {
        if (data == null) return Task.CompletedTask;
        var channelId = data["channel_id"]?.GetValue<string>() ?? "";
        var msgNode   = data["message"];
        var author    = msgNode?["author"]?["username"]?.GetValue<string>() ?? "Unknown";
        var content   = msgNode?["content"]?.GetValue<string>() ?? "";
        if (string.IsNullOrWhiteSpace(content)) return Task.CompletedTask;

        _channelGuildCache.TryGetValue(channelId, out var guildId);
        var msg = new DiscordMessage(author, content, channelId, guildId ?? "");
        AddToHistory(msg);
        MessageReceived?.Invoke(msg);
        return Task.CompletedTask;
    }

    // ── Notification (chat) ───────────────────────────────────────────────────

    private async Task HandleNotificationAsync(JsonNode? data)
    {
        if (data == null) return;
        var channelId = data["channel_id"]?.GetValue<string>() ?? "";
        var msgNode   = data["message"];
        var author    = msgNode?["author"]?["username"]?.GetValue<string>()
                     ?? data["title"]?.GetValue<string>()
                     ?? "Unknown";
        var content   = msgNode?["content"]?.GetValue<string>()
                     ?? data["body"]?.GetValue<string>()
                     ?? "";

        if (!_channelGuildCache.TryGetValue(channelId, out var guildId))
        {
            try
            {
                var ch = await SendCommandAsync("GET_CHANNEL", new JsonObject { ["channel_id"] = channelId });
                guildId = ch?["guild_id"]?.GetValue<string>() ?? "";
                _channelGuildCache[channelId] = guildId;
            }
            catch { guildId = ""; }
        }

        var msg = new DiscordMessage(author, content, channelId, guildId ?? "");
        AddToHistory(msg);
        MessageReceived?.Invoke(msg);
    }

    private void AddToHistory(DiscordMessage msg)
    {
        lock (_historyLock)
        {
            _messageHistory.Add(msg);
            if (_messageHistory.Count > 10)
                _messageHistory.RemoveAt(0);
        }
    }

    // ── Voice channel ─────────────────────────────────────────────────────────

    private async Task GetCurrentVoiceChannelAsync()
    {
        try
        {
            var data = await SendCommandAsync("GET_SELECTED_VOICE_CHANNEL", new JsonObject());
            _currentChannelId = data?["id"]?.GetValue<string>();
            if (_currentChannelId == null) return;

            // Cache guild_id if present in the response (avoids an extra GET_CHANNEL call)
            var guildId = data?["guild_id"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(guildId))
                _channelGuildCache[_currentChannelId] = guildId;

            await SubscribeChannelEventsAsync(_currentChannelId);

            // GET_SELECTED_VOICE_CHANNEL already includes voice_states — use them directly
            // to avoid a separate GET_CHANNEL call that may fail without rpc.voice.read scope.
            var states = data?["voice_states"]?.AsArray();
            if (states != null && states.Count > 0)
                ParseVoiceStatesArray(states);
            else
                await RefreshChannelMembersAsync(); // fallback for older Discord versions

            await ResolveGuildNameAsync(_currentChannelId);
        }
        catch { }
    }

    private void ParseVoiceStatesArray(JsonArray states)
    {
        var members = new List<DiscordMember>();
        foreach (var s in states)
        {
            var m = ParseVoiceStateMember(s);
            if (m != null) members.Add(m);
        }
        VoiceMembers = members;
        TrackSelfStreaming();

        var self = _currentUserId != null
            ? members.FirstOrDefault(m => m.UserId == _currentUserId)
            : null;
        if (self != null)
        {
            _micMuted = self.IsMuted;
            MicMuteChanged?.Invoke(_micMuted);
        }

        VoiceStateChanged?.Invoke(VoiceMembers);
    }

    private async Task HandleVoiceChannelSelectAsync(JsonNode? data)
    {
        var channelId = data?["channel_id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(channelId))
        {
            _currentChannelId = null;
            VoiceMembers      = new();
            CurrentGuildName  = null;
            VoiceStateChanged?.Invoke(VoiceMembers);
            GuildChanged?.Invoke(null);
            return;
        }

        var guildId = data?["guild_id"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(guildId))
            _channelGuildCache[channelId] = guildId;

        if (_currentChannelId != channelId)
        {
            _currentChannelId = channelId;
            await SubscribeChannelEventsAsync(channelId);
        }

        // Reuse GET_SELECTED_VOICE_CHANNEL data for the member list if it's still for this channel
        var selData = await SendCommandAsync("GET_SELECTED_VOICE_CHANNEL", new JsonObject());
        var selStates = selData?["voice_states"]?.AsArray();
        if (selStates != null && selData?["id"]?.GetValue<string>() == channelId)
            ParseVoiceStatesArray(selStates);
        else
            await RefreshChannelMembersAsync();

        await ResolveGuildNameAsync(channelId);
    }

    private async Task RefreshChannelMembersAsync()
    {
        if (_currentChannelId == null) return;
        try
        {
            var data   = await SendCommandAsync("GET_CHANNEL", new JsonObject { ["channel_id"] = _currentChannelId });
            var states = data?["voice_states"]?.AsArray();
            if (states == null) return;

            var members = new List<DiscordMember>();
            foreach (var s in states)
            {
                var vs = s?["voice_state"];
                members.Add(new DiscordMember
                {
                    UserId     = s?["user"]?["id"]      ?.GetValue<string>() ?? "",
                    Username   = s?["user"]?["username"]?.GetValue<string>() ?? "",
                    Nick       = s?["nick"]              ?.GetValue<string>() ?? "",
                    IsMuted    = vs?["self_mute"]  ?.GetValue<bool>() ?? false,
                    IsDeafened = vs?["self_deaf"]  ?.GetValue<bool>() ?? false,
                    IsStreaming = vs?["self_stream"]?.GetValue<bool>() ?? false,
                });
            }
            VoiceMembers = members;
            TrackSelfStreaming();

            // Sync our own mute state from the voice state snapshot
            var self = _currentUserId != null
                ? members.FirstOrDefault(m => m.UserId == _currentUserId)
                : null;
            if (self != null)
            {
                _micMuted = self.IsMuted;
                MicMuteChanged?.Invoke(_micMuted);
            }

            VoiceStateChanged?.Invoke(VoiceMembers);
        }
        catch { }
    }

    private async Task ResolveGuildNameAsync(string channelId)
    {
        try
        {
            if (!_channelGuildCache.TryGetValue(channelId, out var guildId))
            {
                var ch = await SendCommandAsync("GET_CHANNEL", new JsonObject { ["channel_id"] = channelId });
                guildId = ch?["guild_id"]?.GetValue<string>() ?? "";
                if (!string.IsNullOrEmpty(guildId)) _channelGuildCache[channelId] = guildId;
            }

            if (string.IsNullOrEmpty(guildId)) return;

            if (!_guildNames.TryGetValue(guildId, out var name))
            {
                var guild = await SendCommandAsync("GET_GUILD", new JsonObject { ["guild_id"] = guildId });
                name = guild?["name"]?.GetValue<string>() ?? guildId;
                _guildNames[guildId] = name;
            }

            CurrentGuildName = name;
            GuildChanged?.Invoke(CurrentGuildName);
        }
        catch { }
    }

    private async Task SubscribeChannelEventsAsync(string channelId)
    {
        var args = new JsonObject { ["channel_id"] = channelId };
        foreach (var evt in new[] { "VOICE_STATE_CREATE", "VOICE_STATE_UPDATE",
                                    "VOICE_STATE_DELETE", "SPEAKING_START", "SPEAKING_STOP",
                                    "MESSAGE_CREATE" })
            await TrySubscribeAsync(evt, args);
    }

    private async Task PopulateGuildNamesAsync()
    {
        try
        {
            var data   = await SendCommandAsync("GET_GUILDS", new JsonObject());
            var guilds = data?["guilds"]?.AsArray();
            if (guilds == null) return;
            foreach (var g in guilds)
            {
                var id   = g?["id"]  ?.GetValue<string>();
                var name = g?["name"]?.GetValue<string>();
                if (id != null && name != null) _guildNames[id] = name;
            }
        }
        catch { }
    }

    // ── Controls ──────────────────────────────────────────────────────────────

    public async Task SetMicMuteAsync(bool mute)
    {
        await SendCommandAsync("SET_VOICE_SETTINGS", new JsonObject { ["mute"] = mute });
        _micMuted = mute;
        MicMuteChanged?.Invoke(_micMuted);
    }

    public Task ToggleMicMuteAsync() => SetMicMuteAsync(!_micMuted);

    /// <summary>Re-queries the current voice channel members and mic/stream state.</summary>
    public async Task RefreshVoiceAsync()
    {
        if (State != DiscordConnectionState.Connected) return;
        try
        {
            await GetCurrentVoiceChannelAsync();
            var vs = await SendCommandAsync("GET_VOICE_SETTINGS", new JsonObject());
            if (vs != null)
            {
                _micMuted = vs["mute"]?.GetValue<bool>() ?? _micMuted;
                MicMuteChanged?.Invoke(_micMuted);
            }
        }
        catch { }
    }

    // ── Poll loop (catches join/leave when events are missed) ─────────────────

    private async Task PollLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        int tick = 0;
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                await PollVoiceStateAsync();
                // Re-subscribe to channel events every 30 s to keep SPEAKING events flowing
                if (++tick % 15 == 0 && _currentChannelId != null)
                    await TrySubscribeChannelSpeakingAsync(_currentChannelId);
            }
        }
        catch (OperationCanceledException) { }
    }

    // Lightweight re-subscribe for just the speaking events (avoids full re-auth overhead)
    private async Task TrySubscribeChannelSpeakingAsync(string channelId)
    {
        await TrySubscribeAsync("SPEAKING_START", new JsonObject { ["channel_id"] = channelId });
        await TrySubscribeAsync("SPEAKING_STOP",  new JsonObject { ["channel_id"] = channelId });
    }

    private async Task PollVoiceStateAsync()
    {
        if (State != DiscordConnectionState.Connected) return;
        try
        {
            var data      = await SendCommandAsync("GET_SELECTED_VOICE_CHANNEL", new JsonObject());
            var channelId = data?["id"]?.GetValue<string>();

            if (channelId == _currentChannelId)
            {
                // Still in same channel — sync streaming state from fresh voice_states snapshot
                if (channelId != null)
                {
                    var states = data?["voice_states"]?.AsArray();
                    if (states != null)
                    {
                        // If we don't yet know our own ID and we're alone, infer it
                        if (_currentUserId == null && states.Count == 1)
                            _currentUserId = states[0]?["user"]?["id"]?.GetValue<string>();

                        // Update IsStreaming on each member without touching IsSpeaking
                        bool memberChanged = false;
                        foreach (var s in states)
                        {
                            var uid = s?["user"]?["id"]?.GetValue<string>();
                            if (uid == null) continue;
                            var isStreaming = s?["voice_state"]?["self_stream"]?.GetValue<bool>() ?? false;
                            var existing = VoiceMembers.FirstOrDefault(m => m.UserId == uid);
                            if (existing == null || existing.IsStreaming == isStreaming) continue;
                            VoiceMembers = VoiceMembers
                                .Select(m => m.UserId == uid
                                    ? new DiscordMember { UserId = m.UserId, Username = m.Username, Nick = m.Nick,
                                                          IsMuted = m.IsMuted, IsDeafened = m.IsDeafened,
                                                          IsStreaming = isStreaming, IsSpeaking = m.IsSpeaking }
                                    : m)
                                .ToList();
                            memberChanged = true;
                        }
                        if (memberChanged) TrackSelfStreaming();
                    }
                }
                return;
            }

            if (channelId == null)
            {
                _currentChannelId = null;
                VoiceMembers      = new();
                CurrentGuildName  = null;
                VoiceStateChanged?.Invoke(VoiceMembers);
                GuildChanged?.Invoke(null);
            }
            else
            {
                _currentChannelId = channelId;
                await SubscribeChannelEventsAsync(channelId);
                await RefreshChannelMembersAsync();
                await ResolveGuildNameAsync(channelId);
            }
        }
        catch { }
    }

    // ── Protocol helpers ──────────────────────────────────────────────────────

    private async Task TrySubscribeAsync(string evt, JsonObject args)
    {
        try { await SubscribeAsync(evt, args); }
        catch { }
    }

    private async Task SubscribeAsync(string evt, JsonObject args)
    {
        await SendOpcodeAsync(1, new JsonObject
        {
            ["cmd"] = "SUBSCRIBE", ["evt"] = evt,
            ["args"] = args, ["nonce"] = Guid.NewGuid().ToString()
        });
    }

    private async Task<JsonNode?> SendCommandAsync(string cmd, JsonObject args, int timeoutMs = 5000)
    {
        var nonce = Guid.NewGuid().ToString();
        var tcs   = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[nonce] = tcs;

        await SendOpcodeAsync(1, new JsonObject
        {
            ["cmd"] = cmd, ["args"] = args, ["nonce"] = nonce
        });

        using var timeout = new CancellationTokenSource(timeoutMs);
        timeout.Token.Register(() => { _pending.TryRemove(nonce, out _); tcs.TrySetCanceled(); });
        try { return await tcs.Task; } catch { return null; }
    }

    private async Task SendOpcodeAsync(int opcode, JsonNode payload)
    {
        if (_pipe is not { IsConnected: true }) return;
        var json  = payload.ToJsonString();
        var bytes = Encoding.UTF8.GetBytes(json);
        var buf   = new byte[8 + bytes.Length];
        BitConverter.TryWriteBytes(buf.AsSpan(0, 4), opcode);
        BitConverter.TryWriteBytes(buf.AsSpan(4, 4), bytes.Length);
        bytes.CopyTo(buf, 8);
        await _writeLock.WaitAsync();
        try
        {
            await _pipe.WriteAsync(buf);
            await _pipe.FlushAsync();
        }
        finally { _writeLock.Release(); }
    }

    private async Task<(int opcode, JsonNode? payload)> ReadMessageAsync()
    {
        var header = new byte[8];
        await ReadExactAsync(header, 8);
        var opcode = BitConverter.ToInt32(header, 0);
        var length = BitConverter.ToInt32(header, 4);
        if (length <= 0) return (opcode, null);
        var data = new byte[length];
        await ReadExactAsync(data, length);
        return (opcode, JsonNode.Parse(Encoding.UTF8.GetString(data)));
    }

    private async Task ReadExactAsync(byte[] buf, int count)
    {
        int offset = 0;
        while (offset < count)
        {
            var read = await _pipe!.ReadAsync(buf.AsMemory(offset, count - offset));
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
    }

    private void SetState(DiscordConnectionState s)
    {
        State = s;
        ConnectionStateChanged?.Invoke(s);
        if (s != DiscordConnectionState.Connecting)
            _connectTcs?.TrySetResult(s == DiscordConnectionState.Connected);
        if (s == DiscordConnectionState.Connected && _cts != null)
            _ = Task.Run(() => PollLoopAsync(_cts.Token));
    }

    public void Disconnect()
    {
        _cts?.Cancel();
        _pipe?.Close();
        _pipe = null;
        CurrentGuildName  = null;
        _selfStreaming     = false;
        _currentChannelId = null;
        SetState(DiscordConnectionState.Disconnected);
    }

    public void Dispose()
    {
        Disconnect();
        _cts?.Dispose();
        _writeLock.Dispose();
    }
}

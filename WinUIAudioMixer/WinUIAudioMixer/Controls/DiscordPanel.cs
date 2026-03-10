using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using WinUIAudioMixer.Services;

namespace WinUIAudioMixer.Controls;

/// <summary>
/// Right panel – togglable between Discord voice/chat view and AnythingLLM AI chat.
/// Tab bar sits at the top; toggle buttons switch the active view.
/// </summary>
public sealed class DiscordPanel : UserControl
{
    private readonly DiscordService       _discord;
    private readonly AnythingLLMService?  _aiSvc;
    private readonly string?              _guildFilter;

    // State
    private DiscordConnectionState _connState  = DiscordConnectionState.Disconnected;
    private List<DiscordMember>    _members    = new();
    private enum                   ActiveTab   { Discord, Ai, ChatGpt, Pc, Calendar, Ha, Apps }
    private ActiveTab              _activeTab  = ActiveTab.Discord;
    private bool                   _aiWelcomed = false;

    // Tab buttons
    private readonly Button _tabDiscord;
    private readonly Button _tabAi;
    private readonly Button _tabChatGpt;
    private readonly Button _tabPc;
    private readonly Button _tabCalendar;
    private readonly Button _tabHa;
    private readonly Button _tabApps;

    // Home Assistant panel
    private readonly HomeAssistantService?               _haSvc;
    private readonly Panel                               _haPanel;
    private readonly List<Label>                         _haSensorLabels  = new();
    private readonly List<(Button btn, string entityId)> _haLightButtons  = new();
    private readonly List<(Button btn, string entityId)> _haSwitchButtons = new();
    private Label? _haSensorsHeader;
    private Panel? _haSensorsSep;
    private Label? _haLightsHeader;
    private Panel? _haLightsSep;
    private Label? _haSwitchesHeader;
    private Panel? _haSwitchesSep;
    private System.Windows.Forms.Timer?                  _haTimer;
    private bool                                         _haConnected;

    // Discord controls
    private readonly Button      _connectButton;
    private readonly Button      _refreshButton;
    private readonly Button      _micButton;
    private readonly Button      _streamButton;
    private readonly Panel       _memberListPanel;
    private readonly Label       _statusLabel;
    private readonly RichTextBox _chatBox;

    // AI chat controls (inside _aiPanel)
    private readonly Panel       _aiPanel;
    private readonly RichTextBox _aiHistory;
    private readonly TextBox     _aiInput;
    private readonly Button      _aiMic;
    private readonly Button      _aiSend;

    // ChatGPT controls (inside _chatGptPanel)
    private readonly ChatGptService? _chatGptSvc;
    private readonly Panel           _chatGptPanel;
    private readonly RichTextBox     _chatGptHistory;
    private readonly TextBox         _chatGptInput;
    private readonly Button          _chatGptMic;
    private readonly Button          _chatGptSend;
    private bool                     _chatGptWelcomed;
    private bool                     _isListening;

    // PC performance controls
    private readonly Panel                _pcPanel;
    private readonly PcPerformanceService _pcSvc    = new();
    private System.Windows.Forms.Timer?   _pcTimer;
    private readonly List<PerfMeter>      _pcMeters = new();
    private Label?                        _pcInfoLabel;

    // Google Calendar panel
    private readonly GoogleCalendarPanel _calendarPanel;
    private bool                         _calendarLoaded;

    // App shortcuts panel
    private readonly AppShortcutsPanel _appsPanel;

    // Layout constants
    private const int TabBarH     = 48;
    private const int VoiceListY  = 108 + TabBarH;             // 148
    private const int VoiceListH  = 200;
    private const int ChatHeaderY = VoiceListY + VoiceListH + 8; // 356
    private const int ChatY       = ChatHeaderY + 24;            // 380

    public DiscordPanel(DiscordService discord, AnythingLLMService? aiSvc = null, ChatGptService? chatGptSvc = null, GoogleCalendarService? calSvc = null, HomeAssistantService? haSvc = null)
    {
        _discord     = discord;
        _aiSvc       = aiSvc;
        _chatGptSvc  = chatGptSvc;
        _haSvc       = haSvc;
        _guildFilter = LoadGuildFilter();

        BackColor = AppTheme.BgPanel;
        SetStyle(ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint, true);

        // ── Tab buttons ───────────────────────────────────────────────────────
        _tabDiscord = MakeTabButton("Discord", active: true);
        _tabDiscord.Click += (_, _) => SwitchTab(ActiveTab.Discord);

        _tabAi = MakeTabButton("AI Chat", active: false);
        _tabAi.Click += (_, _) => SwitchTab(ActiveTab.Ai);

        _tabChatGpt = MakeTabButton("ChatGPT", active: false);
        _tabChatGpt.Click += (_, _) => SwitchTab(ActiveTab.ChatGpt);

        _tabPc = MakeTabButton("PC Perf", active: false);
        _tabPc.Click += (_, _) => SwitchTab(ActiveTab.Pc);

        _tabCalendar = MakeTabButton("Calendar", active: false);
        _tabCalendar.Click += (_, _) => SwitchTab(ActiveTab.Calendar);

        _tabHa = MakeTabButton("Home Asst", active: false);
        _tabHa.Click += (_, _) => SwitchTab(ActiveTab.Ha);

        _tabApps = MakeTabButton("Apps", active: false);
        _tabApps.Click += (_, _) => SwitchTab(ActiveTab.Apps);

        // ── Discord controls ──────────────────────────────────────────────────
        bool configured = discord.IsConfigured;
        _statusLabel = new Label
        {
            Text      = configured ? "Not connected" : "Add your Client ID to discord-client-id.txt",
            Font      = AppTheme.FontSmall,
            ForeColor = configured ? AppTheme.TextMuted : AppTheme.Warning,
            BackColor = Color.Transparent,
            AutoSize  = false,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        _connectButton = MakeFlatButton("Connect to Discord", AppTheme.Accent, 220, 34);
        _connectButton.Enabled = configured;
        _connectButton.Click  += OnConnectClick;

        _refreshButton = MakeFlatButton("↻  Refresh", AppTheme.BgCard, 120, 34);
        _refreshButton.Visible = false;
        _refreshButton.Click  += async (_, _) =>
        {
            _refreshButton.Enabled = false;
            _refreshButton.Text    = "Refreshing…";
            try { await _discord.RefreshVoiceAsync(); }
            catch { }
            finally
            {
                _refreshButton.Enabled = true;
                _refreshButton.Text    = "↻  Refresh";
            }
        };

        _micButton = MakeFlatButton("🎙  DISCORD MUTE", AppTheme.Accent, 170, 40);
        _micButton.Click += OnMicClick;

        _streamButton = MakeFlatButton("📺  STREAM IN DISCORD", AppTheme.BgCard, 190, 40);
        _streamButton.Click += OnStreamClick;

        _memberListPanel = new Panel { AutoScroll = true, BackColor = Color.Transparent };

        _chatBox = new RichTextBox
        {
            ReadOnly    = true,
            BackColor   = AppTheme.BgCard,
            ForeColor   = AppTheme.TextPrimary,
            Font        = new Font("Segoe UI", 12f),
            BorderStyle = BorderStyle.None,
            ScrollBars  = RichTextBoxScrollBars.Vertical,
            WordWrap    = true,
            DetectUrls  = false,
        };

        // ── AI chat panel ─────────────────────────────────────────────────────
        _aiPanel = new Panel { BackColor = Color.Transparent, Visible = false };

        _aiHistory = new RichTextBox
        {
            ReadOnly    = true,
            BackColor   = AppTheme.BgCard,
            ForeColor   = AppTheme.TextPrimary,
            Font        = new Font("Segoe UI", 12f),
            BorderStyle = BorderStyle.None,
            ScrollBars  = RichTextBoxScrollBars.Vertical,
            WordWrap    = true,
            DetectUrls  = false,
        };

        _aiInput = new TextBox
        {
            BackColor       = AppTheme.BgCard,
            ForeColor       = AppTheme.TextPrimary,
            Font            = AppTheme.FontLabel,
            BorderStyle     = BorderStyle.None,
            PlaceholderText = "Ask AnythingLLM…",
        };
        _aiInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                _ = OnAiSendAsync();
            }
        };

        _aiMic = MakeFlatButton("MIC", AppTheme.BgCard, 42, 38);
        _aiMic.Click += async (_, _) => await OnMicClickAsync(_aiInput, _aiMic);

        _aiSend = MakeFlatButton("Send", AppTheme.Accent, 72, 38);
        _aiSend.Click += async (_, _) => await OnAiSendAsync();

        _aiPanel.Controls.AddRange(new Control[] { _aiHistory, _aiInput, _aiMic, _aiSend });

        // ── ChatGPT panel ─────────────────────────────────────────────────────
        _chatGptPanel = new Panel { BackColor = Color.Transparent, Visible = false };

        _chatGptHistory = new RichTextBox
        {
            ReadOnly    = true,
            BackColor   = AppTheme.BgCard,
            ForeColor   = AppTheme.TextPrimary,
            Font        = new Font("Segoe UI", 12f),
            BorderStyle = BorderStyle.None,
            ScrollBars  = RichTextBoxScrollBars.Vertical,
            WordWrap    = true,
            DetectUrls  = false,
        };

        _chatGptInput = new TextBox
        {
            BackColor       = AppTheme.BgCard,
            ForeColor       = AppTheme.TextPrimary,
            Font            = AppTheme.FontLabel,
            BorderStyle     = BorderStyle.None,
            PlaceholderText = "Ask ChatGPT…",
        };
        _chatGptInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                _ = OnChatGptSendAsync();
            }
        };

        _chatGptMic = MakeFlatButton("MIC", AppTheme.BgCard, 42, 38);
        _chatGptMic.Click += async (_, _) => await OnMicClickAsync(_chatGptInput, _chatGptMic);

        _chatGptSend = MakeFlatButton("Send", AppTheme.Accent, 72, 38);
        _chatGptSend.Click += async (_, _) => await OnChatGptSendAsync();

        _chatGptPanel.Controls.AddRange(new Control[] { _chatGptHistory, _chatGptInput, _chatGptMic, _chatGptSend });

        // PC performance panel
        _pcPanel = new Panel { BackColor = Color.Transparent, Visible = false };

        // Google Calendar panel
        _calendarPanel = new GoogleCalendarPanel(calSvc) { Visible = false };

        // App shortcuts panel
        _appsPanel = new AppShortcutsPanel { Visible = false };

        // ── Home Assistant panel ───────────────────────────────────────────────
        _haPanel = new Panel { BackColor = Color.Transparent, AutoScroll = true, Visible = false };
        BuildHaPanel();

        // ── Add everything ────────────────────────────────────────────────────
        Controls.AddRange(new Control[]
        {
            _statusLabel, _connectButton, _refreshButton, _micButton, _streamButton,
            _memberListPanel, _chatBox,
            _tabDiscord, _tabAi, _tabChatGpt, _tabPc, _tabCalendar, _tabHa, _tabApps,
            _aiPanel, _chatGptPanel, _pcPanel, _calendarPanel, _haPanel, _appsPanel,
        });

        if (configured)
            HandleCreated += (_, _) => BeginInvoke(OnConnectClick, this, EventArgs.Empty);

        _discord.ConnectionStateChanged += OnConnectionChanged;
        _discord.VoiceStateChanged      += OnVoiceStateChanged;
        _discord.MicMuteChanged         += OnMicMuteChanged;
        _discord.MessageReceived        += OnMessageReceived;
        _discord.GuildChanged           += OnGuildChanged;
        _discord.StreamingStateChanged  += OnStreamingStateChanged;

        Resize += (_, _) => LayoutAll();
        LayoutAll();
    }

    // ── Layout ────────────────────────────────────────────────────────────────

    private void LayoutAll()
    {
        int w  = ClientSize.Width;
        int h  = ClientSize.Height;
        int x  = 16;
        int cw = w - 32;

        // Tab buttons
        _tabDiscord .SetBounds(x,        8, 84, 32);
        _tabAi      .SetBounds(x + 90,   8, 80, 32);
        _tabChatGpt .SetBounds(x + 176,  8, 84, 32);
        _tabPc      .SetBounds(x + 266,  8, 80, 32);
        _tabCalendar.SetBounds(x + 352,  8, 90, 32);
        _tabHa      .SetBounds(x + 448,  8, 102, 32);
        _tabApps    .SetBounds(x + 556,  8, 62, 32);

        // Discord controls
        _statusLabel   .SetBounds(x, TabBarH + 6,  cw, 20);
        _connectButton .SetBounds(x, TabBarH + 28, 180, 34);
        _refreshButton .SetBounds(x, TabBarH + 28, 120, 34);
        _memberListPanel.SetBounds(x, VoiceListY,  cw, VoiceListH);

        int chatH = Math.Max(60, h - 60 - ChatY);
        _chatBox.SetBounds(x, ChatY, cw, chatH);

        int by = h - 52;
        _micButton   .SetBounds(x,       by, 170, 40);
        _streamButton.SetBounds(x + 178, by, 190, 40);

        // AI / ChatGPT / PC / Calendar / HA / Apps panels
        _aiPanel       .SetBounds(0, TabBarH, w, h - TabBarH);
        _chatGptPanel  .SetBounds(0, TabBarH, w, h - TabBarH);
        _pcPanel       .SetBounds(0, TabBarH, w, h - TabBarH);
        _calendarPanel .SetBounds(0, TabBarH, w, h - TabBarH);
        _haPanel       .SetBounds(0, TabBarH, w, h - TabBarH);
        _appsPanel     .SetBounds(0, TabBarH, w, h - TabBarH);
        LayoutPcPanel();

        const int inputH = 38, sendW = 80, micW = 42, pad = 8;
        int aiW = Math.Max(1, w);
        int aiH = Math.Max(1, h - TabBarH);

        _aiHistory.SetBounds(pad, pad, aiW - pad * 2, aiH - inputH - pad * 3);
        _aiInput  .SetBounds(pad, aiH - inputH - pad, aiW - sendW - micW - pad * 2 - 8, inputH);
        _aiMic    .SetBounds(aiW - sendW - micW - pad - 4, aiH - inputH - pad, micW, inputH);
        _aiSend   .SetBounds(aiW - sendW - pad, aiH - inputH - pad, sendW, inputH);

        _chatGptHistory.SetBounds(pad, pad, aiW - pad * 2, aiH - inputH - pad * 3);
        _chatGptInput  .SetBounds(pad, aiH - inputH - pad, aiW - sendW - micW - pad * 2 - 8, inputH);
        _chatGptMic    .SetBounds(aiW - sendW - micW - pad - 4, aiH - inputH - pad, micW, inputH);
        _chatGptSend   .SetBounds(aiW - sendW - pad, aiH - inputH - pad, sendW, inputH);

        RebuildMemberList();
    }

    // ── Tab switching ─────────────────────────────────────────────────────────

    private void SwitchTab(ActiveTab tab)
    {
        _activeTab = tab;
        bool discord  = tab == ActiveTab.Discord;
        bool ai       = tab == ActiveTab.Ai;
        bool gpt      = tab == ActiveTab.ChatGpt;
        bool pc       = tab == ActiveTab.Pc;
        bool calendar = tab == ActiveTab.Calendar;
        bool ha       = tab == ActiveTab.Ha;
        bool apps     = tab == ActiveTab.Apps;

        _aiPanel       .Visible = ai;
        _chatGptPanel  .Visible = gpt;
        _pcPanel       .Visible = pc;
        _calendarPanel .Visible = calendar;
        _haPanel       .Visible = ha;
        _appsPanel     .Visible = apps;

        bool connected = _connState == DiscordConnectionState.Connected;
        _statusLabel    .Visible = discord;
        _connectButton  .Visible = discord && !connected;
        _refreshButton  .Visible = discord && connected;
        _memberListPanel.Visible = discord;
        _chatBox        .Visible = discord;
        _micButton      .Visible = discord && connected;
        _streamButton   .Visible = discord && connected;

        foreach (var (btn, active) in new[]
        {
            (_tabDiscord,  discord),
            (_tabAi,       ai),
            (_tabChatGpt,  gpt),
            (_tabPc,       pc),
            (_tabCalendar, calendar),
            (_tabHa,       ha),
            (_tabApps,     apps),
        })
        {
            btn.BackColor = active ? AppTheme.BgCard     : Color.Transparent;
            btn.ForeColor = active ? AppTheme.TextPrimary : AppTheme.TextMuted;
        }

        if (ai && !_aiWelcomed)
        {
            _aiWelcomed = true;
            if (_aiSvc == null || !_aiSvc.IsConfigured)
                AppendAiMessage("System",
                    "AnythingLLM is not configured.\n" +
                    "Edit anythingllm-config.json next to the exe:\n" +
                    "  • url       — e.g. http://localhost:3001\n" +
                    "  • apiKey    — your AnythingLLM API key\n" +
                    "  • workspace — your workspace slug\n" +
                    "Then restart BaumDash.",
                    AppTheme.Warning);
            else
                _ = ShowReadyWithWorkspacesAsync();
        }

        if (gpt && !_chatGptWelcomed)
        {
            _chatGptWelcomed = true;
            if (_chatGptSvc == null || !_chatGptSvc.IsConfigured)
                AppendChatGptMessage("System",
                    "ChatGPT is not configured.\n" +
                    "Edit chatgpt-config.json next to the exe:\n" +
                    "  • apiKey — your OpenAI API key\n" +
                    "  • model  — e.g. gpt-4o, gpt-4o-mini, gpt-3.5-turbo\n" +
                    "Then restart BaumDash.",
                    AppTheme.Warning);
            else
                AppendChatGptMessage("System",
                    $"Ready — using model \"{_chatGptSvc.Model}\".",
                    AppTheme.Success);
        }

        if (pc)
        {
            if (_pcTimer == null)
            {
                var snap = _pcSvc.GetSnapshot();
                BuildPcControls(snap);
                _pcTimer = new System.Windows.Forms.Timer { Interval = 2000 };
                _pcTimer.Tick += (_, _) => RefreshPcPanel();
            }
            _pcTimer.Start();
        }
        else
        {
            _pcTimer?.Stop();
        }

        if (calendar && !_calendarLoaded)
        {
            _calendarLoaded = true;
            _ = _calendarPanel.LoadAsync();
        }

        if (ha)
        {
            if (_haTimer == null && _haSvc?.IsConfigured == true)
            {
                _haTimer = new System.Windows.Forms.Timer { Interval = 30_000 };
                _haTimer.Tick += (_, _) => _ = Task.Run(RefreshHaAsync);
            }
            _haTimer?.Start();
            _ = Task.Run(RefreshHaAsync);
        }
        else
        {
            _haTimer?.Stop();
        }

        Invalidate();
    }

    private async Task ShowReadyWithWorkspacesAsync()
    {
        if (_aiSvc == null) return;

        var workspaces = await _aiSvc.GetWorkspacesAsync();
        if (workspaces != null && workspaces.Count > 0)
        {
            var lines = string.Join("\n", workspaces.Select(w => $"  • {w.Slug}  ({w.Name})"));
            var current = _aiSvc.Workspace;
            bool valid = workspaces.Any(w => w.Slug == current);
            var color  = valid ? AppTheme.Success : AppTheme.Warning;
            var status = valid
                ? $"Ready — using workspace \"{current}\"."
                : $"Workspace \"{current}\" not found!\n\nAvailable slugs — update anythingllm-config.json:\n{lines}";
            AppendAiMessage("System", status, color);
        }
        else
        {
            AppendAiMessage("System", "Ready — ask me anything!", AppTheme.Success);
        }
    }

    // ── Speech-to-text ────────────────────────────────────────────────────────

    private async Task OnMicClickAsync(TextBox inputBox, Button micBtn)
    {
        if (_isListening) return;
        _isListening     = true;
        micBtn.Text      = "●REC";
        micBtn.BackColor = AppTheme.Danger;

        try
        {
            var tcs = new TaskCompletionSource<string?>();

            await Task.Run(() =>
            {
                try
                {
                    using var engine = new System.Speech.Recognition.SpeechRecognitionEngine(
                        System.Globalization.CultureInfo.CurrentCulture);
                    engine.LoadGrammar(new System.Speech.Recognition.DictationGrammar());
                    engine.SetInputToDefaultAudioDevice();

                    engine.SpeechRecognized   += (_, e) => tcs.TrySetResult(e.Result.Text);
                    engine.RecognizeCompleted += (_, e) => tcs.TrySetResult(e.Result?.Text);
                    engine.RecognizeAsync(System.Speech.Recognition.RecognizeMode.Single);

                    tcs.Task.Wait(TimeSpan.FromSeconds(15));
                    engine.RecognizeAsyncCancel();
                }
                catch (Exception ex) { tcs.TrySetException(ex); }
            });

            var text = tcs.Task.IsCompletedSuccessfully ? tcs.Task.Result : null;
            if (!string.IsNullOrWhiteSpace(text))
            {
                var existing  = inputBox.Text.TrimEnd();
                inputBox.Text = existing.Length > 0 ? $"{existing} {text}" : text;
                inputBox.SelectionStart = inputBox.Text.Length;
            }
        }
        catch { /* microphone unavailable or not configured in Windows speech settings */ }
        finally
        {
            _isListening     = false;
            micBtn.Text      = "MIC";
            micBtn.BackColor = AppTheme.BgCard;
        }
    }

    // ── AI chat ───────────────────────────────────────────────────────────────

    private async Task OnAiSendAsync()
    {
        var q = _aiInput.Text.Trim();
        if (string.IsNullOrEmpty(q)) return;

        if (_aiSvc == null || !_aiSvc.IsConfigured)
        {
            AppendAiMessage("System", "AnythingLLM not configured — see anythingllm-config.json.", AppTheme.Warning);
            return;
        }

        _aiInput.Clear();
        _aiSend.Enabled = false;
        _aiSend.Text    = "…";
        AppendAiMessage("You", q, AppTheme.Accent);

        try
        {
            var answer = await _aiSvc.ChatAsync(q);
            AppendAiMessage("AI", answer, AppTheme.TextPrimary);
        }
        catch (Exception ex)
        {
            AppendAiMessage("Error", ex.Message, AppTheme.Danger);
        }
        finally
        {
            _aiSend.Enabled = true;
            _aiSend.Text    = "Send";
            _aiInput.Focus();
        }
    }

    private async Task OnChatGptSendAsync()
    {
        var q = _chatGptInput.Text.Trim();
        if (string.IsNullOrEmpty(q)) return;

        if (_chatGptSvc == null || !_chatGptSvc.IsConfigured)
        {
            AppendChatGptMessage("System", "ChatGPT not configured — see chatgpt-config.json.", AppTheme.Warning);
            return;
        }

        _chatGptInput.Clear();
        _chatGptSend.Enabled = false;
        _chatGptSend.Text    = "…";
        AppendChatGptMessage("You", q, AppTheme.Accent);

        try
        {
            var answer = await _chatGptSvc.ChatAsync(q);
            AppendChatGptMessage("ChatGPT", answer, AppTheme.TextPrimary);
        }
        catch (Exception ex)
        {
            AppendChatGptMessage("Error", ex.Message, AppTheme.Danger);
        }
        finally
        {
            _chatGptSend.Enabled = true;
            _chatGptSend.Text    = "Send";
            _chatGptInput.Focus();
        }
    }

    private void AppendChatGptMessage(string role, string text, Color color)
    {
        _chatGptHistory.SelectionStart  = _chatGptHistory.TextLength;
        _chatGptHistory.SelectionLength = 0;
        _chatGptHistory.SelectionColor  = color;
        _chatGptHistory.SelectionFont   = AppTheme.FontBold;
        _chatGptHistory.AppendText($"{role}:\n");
        _chatGptHistory.SelectionColor  = AppTheme.TextPrimary;
        _chatGptHistory.SelectionFont   = new Font("Segoe UI", 12f);
        _chatGptHistory.AppendText($"{text}\n\n");
        _chatGptHistory.SelectionStart  = _chatGptHistory.TextLength;
        _chatGptHistory.ScrollToCaret();
    }

    private void AppendAiMessage(string role, string text, Color color)
    {
        _aiHistory.SelectionStart  = _aiHistory.TextLength;
        _aiHistory.SelectionLength = 0;
        _aiHistory.SelectionColor  = color;
        _aiHistory.SelectionFont   = AppTheme.FontBold;
        _aiHistory.AppendText($"{role}:\n");
        _aiHistory.SelectionColor  = AppTheme.TextPrimary;
        _aiHistory.SelectionFont   = new Font("Segoe UI", 12f);
        _aiHistory.AppendText($"{text}\n\n");
        _aiHistory.SelectionStart  = _aiHistory.TextLength;
        _aiHistory.ScrollToCaret();
    }

    // ── Discord event handlers ────────────────────────────────────────────────

    private void OnConnectionChanged(DiscordConnectionState state)
    {
        if (InvokeRequired) { BeginInvoke(() => OnConnectionChanged(state)); return; }
        _connState = state;
        UpdateStatusLabel();
        if (_activeTab == ActiveTab.Discord)
        {
            bool con = state == DiscordConnectionState.Connected;
            _connectButton.Visible = !con;
            _refreshButton.Visible = con;
            _micButton    .Visible = con;
            _streamButton .Visible = con;
        }
        if (state == DiscordConnectionState.Connected)
        {
            // Apply current service state to buttons immediately (don't wait for events)
            OnMicMuteChanged(_discord.IsMicMuted);
            OnStreamingStateChanged(_discord.IsStreaming);
            PopulateChatHistory();
        }
        Invalidate();
    }

    private void PopulateChatHistory()
    {
        var recent = _discord.RecentMessages;
        if (recent.Count == 0) return;

        _chatBox.SelectionStart  = _chatBox.TextLength;
        _chatBox.SelectionLength = 0;
        _chatBox.SelectionColor  = AppTheme.TextMuted;
        _chatBox.AppendText("── Previous messages ──\n");

        foreach (var msg in recent)
        {
            _chatBox.SelectionColor = AppTheme.TextMuted;
            _chatBox.AppendText($"  ");
            _chatBox.SelectionColor = AppTheme.Accent;
            _chatBox.AppendText($"{msg.Author}: ");
            _chatBox.SelectionColor = AppTheme.TextPrimary;
            _chatBox.AppendText($"{msg.Content}\n");
        }

        _chatBox.SelectionColor = AppTheme.TextMuted;
        _chatBox.AppendText("────────────────────────\n");
        _chatBox.SelectionStart = _chatBox.TextLength;
        _chatBox.ScrollToCaret();
    }

    private void UpdateStatusLabel()
    {
        _statusLabel.Text = _connState switch
        {
            DiscordConnectionState.Connecting => "Connecting…",
            DiscordConnectionState.Connected  => string.IsNullOrEmpty(_discord.CurrentGuildName)
                                                    ? "Connected"
                                                    : $"● {_discord.CurrentGuildName}",
            DiscordConnectionState.Error      => "Failed – Discord not running?",
            _                                 => "Not connected",
        };
        _statusLabel.ForeColor = _connState switch
        {
            DiscordConnectionState.Connected  => AppTheme.Success,
            DiscordConnectionState.Error      => AppTheme.Danger,
            DiscordConnectionState.Connecting => AppTheme.Warning,
            _                                 => AppTheme.TextMuted,
        };
    }

    private void OnVoiceStateChanged(List<DiscordMember> members)
    {
        if (InvokeRequired) { BeginInvoke(() => OnVoiceStateChanged(members)); return; }
        _members = members;
        RebuildMemberList();
        Invalidate();
    }

    private void OnMicMuteChanged(bool muted)
    {
        if (InvokeRequired) { BeginInvoke(() => OnMicMuteChanged(muted)); return; }
        _micButton.Text      = muted ? "🔇  DISCORD MUTED" : "🎙  DISCORD MUTE";
        _micButton.BackColor = muted ? AppTheme.BgCard     : AppTheme.Accent;
        _micButton.ForeColor = muted ? AppTheme.TextMuted  : Color.White;
        _micButton.FlatAppearance.MouseOverBackColor =
            muted ? AppTheme.BgPanel : AppTheme.AccentHover;
    }

    private void OnGuildChanged(string? guildName)
    {
        if (InvokeRequired) { BeginInvoke(() => OnGuildChanged(guildName)); return; }
        UpdateStatusLabel();
        Invalidate();
    }

    private void OnStreamingStateChanged(bool streaming)
    {
        if (InvokeRequired) { BeginInvoke(() => OnStreamingStateChanged(streaming)); return; }
        _streamButton.Text      = streaming ? "🔴  STREAMING"         : "📺  STREAM IN DISCORD";
        _streamButton.BackColor = streaming ? AppTheme.Danger          : AppTheme.BgCard;
    }

    private void OnMessageReceived(DiscordMessage msg)
    {
        if (InvokeRequired) { BeginInvoke(() => OnMessageReceived(msg)); return; }

        if (_guildFilter != null &&
            !string.IsNullOrEmpty(msg.GuildId) &&
            msg.GuildId != _guildFilter)
            return;

        _chatBox.SelectionStart  = _chatBox.TextLength;
        _chatBox.SelectionLength = 0;
        _chatBox.SelectionColor  = AppTheme.TextMuted;
        _chatBox.AppendText($"[{DateTime.Now:HH:mm}] ");
        _chatBox.SelectionColor = AppTheme.Accent;
        _chatBox.AppendText($"{msg.Author}: ");
        _chatBox.SelectionColor = AppTheme.TextPrimary;
        _chatBox.AppendText($"{msg.Content}\n");

        const int maxLines = 200;
        var lines = _chatBox.Lines;
        if (lines.Length > maxLines)
        {
            _chatBox.Select(0, _chatBox.GetFirstCharIndexFromLine(lines.Length - maxLines));
            _chatBox.SelectedText = "";
        }

        _chatBox.SelectionStart = _chatBox.TextLength;
        _chatBox.ScrollToCaret();
    }

    // ── Member list ───────────────────────────────────────────────────────────

    private void RebuildMemberList()
    {
        _memberListPanel.SuspendLayout();
        _memberListPanel.Controls.Clear();
        int y = 0;
        foreach (var m in _members)
        {
            var row = new MemberRowPanel(m)
                { Left = 0, Top = y, Width = Math.Max(_memberListPanel.ClientSize.Width, 100) };
            _memberListPanel.Controls.Add(row);
            y += row.Height;
        }
        _memberListPanel.ResumeLayout(true);
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    private async void OnConnectClick(object? sender, EventArgs e)
    {
        _connectButton.Enabled = false;
        _connectButton.Text    = "Connecting…";
        try { await _discord.ConnectAsync(); }
        catch { }
        finally
        {
            if (_connState != DiscordConnectionState.Connected)
            {
                _connectButton.Enabled = true;
                _connectButton.Text    = "Connect to Discord";
            }
        }
    }

    private void OnMicClick(object? sender, EventArgs e) =>
        Task.Run(NativeMethods.ToggleDiscordMute);

    private void OnStreamClick(object? sender, EventArgs e)
    {
        try
        {
            var proc = System.Diagnostics.Process.GetProcessesByName("Discord").FirstOrDefault();
            if (proc != null) NativeMethods.SetForegroundWindow(proc.MainWindowHandle);
        }
        catch { }
    }

    // ── Painting ──────────────────────────────────────────────────────────────

    protected override void OnPaintBackground(PaintEventArgs e)
        => AppTheme.PaintBackground(e.Graphics, this, AppTheme.BgPanel);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        int x = 16, w = ClientSize.Width - 32;

        using var sepPen    = new Pen(AppTheme.Border);
        using var mutedBrush = new SolidBrush(AppTheme.TextMuted);

        // Separator below tab bar
        g.DrawLine(sepPen, x, TabBarH, x + w, TabBarH);

        if (_activeTab == ActiveTab.Discord)
        {
            // Voice members header
            if (_members.Count > 0 || _connState == DiscordConnectionState.Connected)
            {
                using var secBrush = new SolidBrush(AppTheme.TextSecondary);
                g.DrawString($"VOICE MEMBERS  ({_members.Count})",
                    AppTheme.FontPanelHeader, secBrush, x, VoiceListY - 18);
            }

            // Chat section
            g.DrawLine(sepPen, x, ChatHeaderY - 2, x + w, ChatHeaderY - 2);
            g.DrawString("MESSAGES", AppTheme.FontPanelHeader, mutedBrush, x, ChatHeaderY + 2);

            // Bottom separator above buttons
            g.DrawLine(sepPen, x, ClientSize.Height - 60, x + w, ClientSize.Height - 60);
        }
        else if (_activeTab == ActiveTab.Ai || _activeTab == ActiveTab.ChatGpt)
        {
            // Separator above input row
            int aiSepY = ClientSize.Height - 54;
            g.DrawLine(sepPen, x, aiSepY, x + w, aiSepY);
        }
    }

    // ── PC Performance ────────────────────────────────────────────────────────

    private void BuildPcControls(PcSnapshot snap)
    {
        _pcPanel.Controls.Clear();
        _pcMeters.Clear();

        // [0] CPU
        var cpu = new PerfMeter("CPU", GetBarColor(snap.CpuPercent));
        cpu.Update(snap.CpuPercent, $"{snap.CpuPercent:F0}%");
        _pcMeters.Add(cpu);

        // [1] RAM
        var ram = new PerfMeter("MEMORY", AppTheme.Accent);
        ram.Update(snap.RamPercent, $"{snap.RamUsedGb:F1} / {snap.RamTotalGb:F0} GB");
        _pcMeters.Add(ram);

        // [2] GPU utilisation
        var gpu = new PerfMeter("GPU", GetBarColor(snap.GpuPercent));
        gpu.Update(snap.GpuPercent, $"{snap.GpuPercent:F0}%");
        _pcMeters.Add(gpu);

        // [3] GPU VRAM
        int gpuMemPct = snap.GpuMemTotalGb > 0 ? (int)(snap.GpuMemUsedGb / snap.GpuMemTotalGb * 100) : 0;
        string gpuMemLabel = snap.GpuMemTotalGb > 0
            ? $"{snap.GpuMemUsedGb:F1} / {snap.GpuMemTotalGb:F0} GB"
            : $"{snap.GpuMemUsedGb:F1} GB";
        var gpuMem = new PerfMeter("GPU VRAM", AppTheme.Accent);
        gpuMem.Update(gpuMemPct, gpuMemLabel);
        _pcMeters.Add(gpuMem);

        // [4] Network
        double netMb = snap.NetBytesPerSec / (1024.0 * 1024);
        var net = new PerfMeter("NETWORK", AppTheme.TextSecondary);
        net.Update((int)Math.Min(netMb, 100), $"{netMb:F1} MB/s");
        _pcMeters.Add(net);

        // [5] Disk I/O activity
        var diskIo = new PerfMeter("DISK I/O", GetBarColor(snap.DiskActivityPercent));
        diskIo.Update(snap.DiskActivityPercent, $"{snap.DiskActivityPercent:F0}%");
        _pcMeters.Add(diskIo);

        // [6+] Drive space
        foreach (var d in snap.Drives)
        {
            var dm = new PerfMeter($"DISK  {d.Name}", AppTheme.TextSecondary);
            dm.Update(d.Percent, $"{d.UsedGb:F0} / {d.TotalGb:F0} GB");
            _pcMeters.Add(dm);
        }

        _pcInfoLabel = new Label
        {
            ForeColor = AppTheme.TextMuted,
            BackColor = Color.Transparent,
            Font      = AppTheme.FontSmall,
            AutoSize  = false,
            Text      = FormatPcInfo(snap),
        };

        foreach (var m in _pcMeters) _pcPanel.Controls.Add(m);
        _pcPanel.Controls.Add(_pcInfoLabel);
        LayoutPcPanel();
    }

    private void RefreshPcPanel()
    {
        if (_pcMeters.Count == 0) return;
        var snap = _pcSvc.GetSnapshot();

        // [0] CPU
        _pcMeters[0].BarColor = GetBarColor(snap.CpuPercent);
        _pcMeters[0].Update(snap.CpuPercent, $"{snap.CpuPercent:F0}%");

        // [1] RAM
        if (_pcMeters.Count > 1)
            _pcMeters[1].Update(snap.RamPercent, $"{snap.RamUsedGb:F1} / {snap.RamTotalGb:F0} GB");

        // [2] GPU utilisation
        if (_pcMeters.Count > 2)
        {
            _pcMeters[2].BarColor = GetBarColor(snap.GpuPercent);
            _pcMeters[2].Update(snap.GpuPercent, $"{snap.GpuPercent:F0}%");
        }

        // [3] GPU VRAM
        if (_pcMeters.Count > 3)
        {
            int gpuMemPct = snap.GpuMemTotalGb > 0 ? (int)(snap.GpuMemUsedGb / snap.GpuMemTotalGb * 100) : 0;
            string gpuMemLabel = snap.GpuMemTotalGb > 0
                ? $"{snap.GpuMemUsedGb:F1} / {snap.GpuMemTotalGb:F0} GB"
                : $"{snap.GpuMemUsedGb:F1} GB";
            _pcMeters[3].Update(gpuMemPct, gpuMemLabel);
        }

        // [4] Network
        if (_pcMeters.Count > 4)
        {
            double netMb = snap.NetBytesPerSec / (1024.0 * 1024);
            _pcMeters[4].Update((int)Math.Min(netMb, 100), $"{netMb:F1} MB/s");
        }

        // [5] Disk I/O
        if (_pcMeters.Count > 5)
        {
            _pcMeters[5].BarColor = GetBarColor(snap.DiskActivityPercent);
            _pcMeters[5].Update(snap.DiskActivityPercent, $"{snap.DiskActivityPercent:F0}%");
        }

        // [6+] Drive space
        for (int i = 0; i < snap.Drives.Count && i + 6 < _pcMeters.Count; i++)
        {
            var d = snap.Drives[i];
            _pcMeters[i + 6].Update(d.Percent, $"{d.UsedGb:F0} / {d.TotalGb:F0} GB");
        }

        if (_pcInfoLabel != null)
            _pcInfoLabel.Text = FormatPcInfo(snap);
    }

    private void LayoutPcPanel()
    {
        if (_pcMeters.Count == 0) return;
        int x = 16, cw = Math.Max(1, _pcPanel.Width - 32);
        int y = 8;
        foreach (var m in _pcMeters)
        {
            m.SetBounds(x, y, cw, m.Height);
            y += m.Height + 4;
        }
        _pcInfoLabel?.SetBounds(x, y + 8, cw, 40);
    }

    private static Color GetBarColor(double pct) =>
        pct >= 90 ? AppTheme.Danger  :
        pct >= 70 ? AppTheme.Warning :
        AppTheme.Success;

    private static string FormatPcInfo(PcSnapshot snap)
    {
        var up = snap.Uptime;
        string upStr = up.TotalDays >= 1
            ? $"{(int)up.TotalDays}d {up.Hours}h {up.Minutes}m"
            : $"{up.Hours}h {up.Minutes}m {up.Seconds}s";
        return $"Processes: {snap.ProcessCount}     Uptime: {upStr}";
    }

    // ── Home Assistant ────────────────────────────────────────────────────────

    private void BuildHaPanel()
    {
        if (_haSvc?.IsConfigured != true)
        {
            var lbl = new Label
            {
                Text      = "Home Assistant is not configured.\nEdit ha-config.json next to the exe.",
                Font      = AppTheme.FontLabel,
                ForeColor = AppTheme.TextMuted,
                BackColor = Color.Transparent,
                AutoSize  = false,
                TextAlign = ContentAlignment.TopLeft,
            };
            lbl.SetBounds(16, 16, 400, 60);
            _haPanel.Controls.Add(lbl);
            return;
        }

        int x = 16, y = 12;
        int w = 400; // will be re-laid out on resize; use a reasonable default

        if (_haSvc.Config.Sensors.Count > 0)
        {
            _haSensorsHeader = MakeHaSectionLabel("SENSORS");
            _haSensorsHeader.SetBounds(x, y, w, 18);
            _haPanel.Controls.Add(_haSensorsHeader);
            y += 22;
            _haSensorsSep = new Panel { BackColor = AppTheme.Border };
            _haSensorsSep.SetBounds(x, y, w, 1);
            _haPanel.Controls.Add(_haSensorsSep);
            y += 9;
        }

        foreach (var sensor in _haSvc.Config.Sensors)
        {
            var lbl = new Label
            {
                Text      = $"{sensor.Name}: …",
                Font      = new Font("Segoe UI", 11f),
                ForeColor = AppTheme.TextPrimary,
                BackColor = Color.Transparent,
                AutoSize  = false,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Tag       = sensor,
            };
            lbl.SetBounds(x, y, w, 24);
            _haSensorLabels.Add(lbl);
            _haPanel.Controls.Add(lbl);
            y += 26;
        }

        if (_haSensorLabels.Count > 0) y += 12;

        if (_haSvc.Config.Lights.Count > 0)
        {
            _haLightsHeader = MakeHaSectionLabel("LIGHTS");
            _haLightsHeader.SetBounds(x, y, w, 18);
            _haPanel.Controls.Add(_haLightsHeader);
            y += 22;
            _haLightsSep = new Panel { BackColor = AppTheme.Border };
            _haLightsSep.SetBounds(x, y, w, 1);
            _haPanel.Controls.Add(_haLightsSep);
            y += 9;
        }

        foreach (var light in _haSvc.Config.Lights)
        {
            var btn = MakeHaButton($"💡  {light.Name.ToUpper()}");
            var eid = light.Id;
            btn.Click += async (_, _) =>
            {
                try
                {
                    await _haSvc.ToggleLightAsync(eid);
                    await Task.Delay(400);
                    await RefreshHaLightAsync(btn, eid);
                }
                catch { }
            };
            btn.SetBounds(x, y, w, 36);
            _haLightButtons.Add((btn, eid));
            _haPanel.Controls.Add(btn);
            y += 44;
        }

        if (_haLightButtons.Count > 0) y += 12;

        if (_haSvc.Config.Switches.Count > 0)
        {
            _haSwitchesHeader = MakeHaSectionLabel("SWITCHES");
            _haSwitchesHeader.SetBounds(x, y, w, 18);
            _haPanel.Controls.Add(_haSwitchesHeader);
            y += 22;
            _haSwitchesSep = new Panel { BackColor = AppTheme.Border };
            _haSwitchesSep.SetBounds(x, y, w, 1);
            _haPanel.Controls.Add(_haSwitchesSep);
            y += 9;
        }

        foreach (var sw in _haSvc.Config.Switches)
        {
            var btn = MakeHaButton($"🔌  {sw.Name.ToUpper()}");
            var eid = sw.Id;
            btn.Click += async (_, _) =>
            {
                try
                {
                    await _haSvc.ToggleSwitchAsync(eid);
                    await Task.Delay(400);
                    await RefreshHaSwitchAsync(btn, eid);
                }
                catch { }
            };
            btn.SetBounds(x, y, w, 36);
            _haSwitchButtons.Add((btn, eid));
            _haPanel.Controls.Add(btn);
            y += 44;
        }

        _haPanel.Resize += (_, _) => LayoutHaPanel();
    }

    private void LayoutHaPanel()
    {
        int x = 16, w = Math.Max(100, _haPanel.ClientSize.Width - 32);
        int y = 12;

        if (_haSensorsHeader != null)
        {
            _haSensorsHeader.SetBounds(x, y, w, 18); y += 22;
            _haSensorsSep?.SetBounds(x, y, w, 1);    y += 9;
        }
        foreach (var lbl in _haSensorLabels)
        {
            lbl.SetBounds(x, y, w, 24);
            y += 26;
        }
        if (_haSensorLabels.Count > 0) y += 12;

        if (_haLightsHeader != null)
        {
            _haLightsHeader.SetBounds(x, y, w, 18); y += 22;
            _haLightsSep?.SetBounds(x, y, w, 1);    y += 9;
        }
        foreach (var (btn, _) in _haLightButtons)
        {
            btn.SetBounds(x, y, w, 36);
            y += 44;
        }
        if (_haLightButtons.Count > 0) y += 12;

        if (_haSwitchesHeader != null)
        {
            _haSwitchesHeader.SetBounds(x, y, w, 18); y += 22;
            _haSwitchesSep?.SetBounds(x, y, w, 1);    y += 9;
        }
        foreach (var (btn, _) in _haSwitchButtons)
        {
            btn.SetBounds(x, y, w, 36);
            y += 44;
        }
    }

    private static Label MakeHaSectionLabel(string title) => new()
    {
        Text      = title,
        Font      = AppTheme.FontSectionHeader,
        ForeColor = AppTheme.TextMuted,
        BackColor = Color.Transparent,
        AutoSize  = false,
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private async Task RefreshHaAsync()
    {
        if (_haSvc == null) return;
        bool anySuccess = false;

        for (int i = 0; i < _haSvc.Config.Sensors.Count && i < _haSensorLabels.Count; i++)
        {
            var (state, unit) = await _haSvc.GetSensorAsync(_haSvc.Config.Sensors[i].Id);
            if (state != "?") anySuccess = true;
            var name = _haSvc.Config.Sensors[i].Name;
            var text = state == "?" ? $"{name}: –" : $"{name}: {state}{unit}";
            var lbl  = _haSensorLabels[i];
            if (lbl.InvokeRequired) lbl.BeginInvoke(() => lbl.Text = text);
            else                    lbl.Text = text;
        }

        for (int i = 0; i < _haLightButtons.Count; i++)
        {
            var (btn, eid) = _haLightButtons[i];
            await RefreshHaLightAsync(btn, eid);
            anySuccess = true;
        }

        for (int i = 0; i < _haSwitchButtons.Count; i++)
        {
            var (btn, eid) = _haSwitchButtons[i];
            await RefreshHaSwitchAsync(btn, eid);
            anySuccess = true;
        }

        if (_haConnected != anySuccess)
        {
            _haConnected = anySuccess;
            if (InvokeRequired) BeginInvoke(Invalidate);
            else                Invalidate();
        }
    }

    private async Task RefreshHaLightAsync(Button btn, string entityId)
    {
        if (_haSvc == null) return;
        var isOn  = await _haSvc.GetLightStateAsync(entityId);
        var color = isOn ? AppTheme.Accent : AppTheme.BgCard;
        if (btn.InvokeRequired) btn.BeginInvoke(() => btn.BackColor = color);
        else                    btn.BackColor = color;
    }

    private async Task RefreshHaSwitchAsync(Button btn, string entityId)
    {
        if (_haSvc == null) return;
        var isOn  = await _haSvc.GetSwitchStateAsync(entityId);
        var color = isOn ? AppTheme.Accent : AppTheme.BgCard;
        if (btn.InvokeRequired) btn.BeginInvoke(() => btn.BackColor = color);
        else                    btn.BackColor = color;
    }

    private static Button MakeHaButton(string text) => new()
    {
        Text      = text,
        Font      = AppTheme.FontButton,
        BackColor = AppTheme.BgCard,
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
        Cursor    = Cursors.Hand,
        FlatAppearance = { BorderSize = 0 },
    };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? LoadGuildFilter()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "discord-server-id.txt");
        if (!File.Exists(path)) return null;
        var id = File.ReadAllText(path).Trim();
        return string.IsNullOrEmpty(id) ? null : id;
    }

    private static Button MakeTabButton(string text, bool active) =>
        new()
        {
            Text      = text,
            Font      = AppTheme.FontPanelSub,
            ForeColor = active ? AppTheme.TextPrimary : AppTheme.TextMuted,
            BackColor = active ? AppTheme.BgCard       : Color.Transparent,
            FlatStyle = FlatStyle.Flat,
            Cursor    = Cursors.Hand,
            FlatAppearance = { BorderSize = 0, MouseOverBackColor = AppTheme.BgCard },
        };

    private static Button MakeFlatButton(string text, Color bg, int width, int height) =>
        new()
        {
            Text      = text,
            Font      = AppTheme.FontButton,
            BackColor = bg,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Size      = new Size(width, height),
            Cursor    = Cursors.Hand,
            FlatAppearance = { BorderSize = 0 },
        };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pcTimer?.Stop();
            _pcTimer?.Dispose();
            _haTimer?.Stop();
            _haTimer?.Dispose();
            _pcSvc.Dispose();
            _discord.ConnectionStateChanged -= OnConnectionChanged;
            _discord.VoiceStateChanged      -= OnVoiceStateChanged;
            _discord.MicMuteChanged         -= OnMicMuteChanged;
            _discord.MessageReceived        -= OnMessageReceived;
            _discord.GuildChanged           -= OnGuildChanged;
            _discord.StreamingStateChanged  -= OnStreamingStateChanged;
        }
        base.Dispose(disposing);
    }
}

/// <summary>Double-buffered panel that paints a single Discord voice member row.</summary>
internal sealed class MemberRowPanel : Panel
{
    private readonly DiscordMember _member;

    public MemberRowPanel(DiscordMember member)
    {
        _member   = member;
        Height    = 44;
        BackColor = Color.Transparent;
        SetStyle(ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint  |
                 ControlStyles.UserPaint, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var dotColor = _member.IsMuted    ? AppTheme.Danger
                     : _member.IsSpeaking ? AppTheme.Success
                     : AppTheme.TextMuted;
        using var dotBrush = new SolidBrush(dotColor);
        g.FillEllipse(dotBrush, 4, (Height - 10) / 2, 10, 10);

        using var nameBrush = new SolidBrush(_member.IsMuted ? AppTheme.TextMuted : AppTheme.TextPrimary);
        g.DrawString(_member.DisplayName, AppTheme.FontLabel, nameBrush, 20, (Height - 16) / 2);

        if (_member.IsStreaming)
        {
            using var sb = new SolidBrush(AppTheme.Accent);
            g.DrawString("📺", AppTheme.FontSmall, sb, Width - 52, (Height - 14) / 2);
        }

        if (_member.IsDeafened || _member.IsMuted)
        {
            string icon = _member.IsDeafened ? "🔇" : "🎙";
            using var ib = new SolidBrush(AppTheme.Danger);
            g.DrawString(icon, AppTheme.FontSmall, ib, Width - 26, (Height - 14) / 2);
        }

        using var pen = new Pen(AppTheme.Border);
        g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }
}

/// <summary>Owner-drawn control showing a labelled progress bar with a value string.</summary>
internal sealed class PerfMeter : Control
{
    private readonly string _label;
    private double _value;
    private string _text = "";
    public Color BarColor { get; set; }

    public PerfMeter(string label, Color barColor)
    {
        _label    = label;
        BarColor  = barColor;
        Height    = 54;
        BackColor = AppTheme.BgPanel;
        SetStyle(ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint  |
                 ControlStyles.UserPaint, true);
    }

    public void Update(double value, string text)
    {
        _value = value;
        _text  = text;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g   = e.Graphics;
        int pad = 8, barY = 32, barH = 10;
        int barW = Math.Max(1, Width - pad * 2);

        using var mutedBrush = new SolidBrush(AppTheme.TextMuted);
        using var textBrush  = new SolidBrush(AppTheme.TextPrimary);

        g.DrawString(_label, AppTheme.FontSectionHeader, mutedBrush, pad, 10);

        var sz = g.MeasureString(_text, AppTheme.FontLabel);
        g.DrawString(_text, AppTheme.FontLabel, textBrush, Width - pad - sz.Width, 10);

        using var bgBrush   = new SolidBrush(AppTheme.BgCard);
        g.FillRectangle(bgBrush, pad, barY, barW, barH);

        int fillW = (int)Math.Round(barW * Math.Clamp(_value / 100.0, 0, 1));
        if (fillW > 0)
        {
            using var fillBrush = new SolidBrush(BarColor);
            g.FillRectangle(fillBrush, pad, barY, fillW, barH);
        }
    }
}

internal static class NativeMethods
{
    [DllImport("user32.dll")] internal static extern bool  SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private  static extern uint  SendInput(uint n, INPUT[] inputs, int cbSize);

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT
    {
        [FieldOffset(0)] public uint       Type;
        [FieldOffset(8)] public KEYBDINPUT Ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint   dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    private const uint   INPUT_KEYBOARD  = 1;
    private const uint   KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_CTRL = 0x11, VK_SHIFT = 0x10, VK_ALT = 0x12, VK_M = 0x4D, VK_S = 0x53, VK_F10 = 0x79;

    internal static void ToggleDiscordMute()
    {
        try
        {
            var hwnd = System.Diagnostics.Process.GetProcessesByName("Discord")
                             .Select(p => p.MainWindowHandle)
                             .FirstOrDefault(h => h != IntPtr.Zero);
            if (hwnd == IntPtr.Zero) return;
            var prev = GetForegroundWindow();
            SetForegroundWindow(hwnd);
            System.Threading.Thread.Sleep(80);
            SendCtrlShiftM();
            if (prev != IntPtr.Zero) SetForegroundWindow(prev);
        }
        catch { }
    }

    private static void SendCtrlShiftM()
    {
        var inputs = new[]
        {
            Key(VK_CTRL,  0),               Key(VK_SHIFT, 0),               Key(VK_M, 0),
            Key(VK_M,     KEYEVENTF_KEYUP), Key(VK_SHIFT, KEYEVENTF_KEYUP), Key(VK_CTRL, KEYEVENTF_KEYUP),
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    internal static void SaveAmdReplay()
    {
        try
        {
            var inputs = new[]
            {
                Key(VK_CTRL,  0),               Key(VK_SHIFT, 0),               Key(VK_S, 0),
                Key(VK_S,     KEYEVENTF_KEYUP), Key(VK_SHIFT, KEYEVENTF_KEYUP), Key(VK_CTRL, KEYEVENTF_KEYUP),
            };
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        }
        catch { }
    }

    /// <summary>Sends Alt+F10 — GeForce Experience / NVIDIA App ShadowPlay "Save Replay" shortcut.</summary>
    internal static void SaveNvidiaReplay()
    {
        try
        {
            const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
            var inputs = new[]
            {
                Key(VK_ALT,  KEYEVENTF_EXTENDEDKEY),
                Key(VK_F10,  KEYEVENTF_EXTENDEDKEY),
                Key(VK_F10,  KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP),
                Key(VK_ALT,  KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP),
            };
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        }
        catch { }
    }

    private static INPUT Key(ushort vk, uint flags) => new()
        { Type = INPUT_KEYBOARD, Ki = new KEYBDINPUT { wVk = vk, dwFlags = flags } };
}

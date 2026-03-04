using System.Runtime.InteropServices;
using WinUIAudioMixer.Controls;
using WinUIAudioMixer.Services;
using WinUIAudioMixer.Models;

namespace WinUIAudioMixer;

/// <summary>
/// Root 1920 × 720 window.  Layout:
///   [AudioDevicePanel 300px] [AppVolumePanel 500px] [MediaPanel 400px] [DiscordPanel fill]
/// </summary>
public sealed class MainForm : Form
{
    // Services
    private readonly AudioDeviceService       _audioDeviceSvc  = new();
    private readonly AudioSessionService      _audioSessionSvc = new();
    private readonly AudioNotificationService _audioNotifySvc;
    private readonly DiscordService           _discordSvc;
    private readonly HomeAssistantService?    _haSvc;
    private readonly AnythingLLMService?     _aiSvc;
    private readonly ChatGptService?         _chatGptSvc;
    private MediaSessionService?              _mediaSvc;

    // Panels
    private AudioDevicePanel? _devicePanel;
    private AppVolumePanel?   _volumePanel;
    private MediaPanel?       _mediaPanel;
    private DiscordPanel?     _discordPanel;

    // Drag support (borderless move)
    private Point   _dragStart;
    private bool    _dragging;
    private Button? _btnMax;
    private Button? _btnSettings, _btnHelp; // overlay buttons — brought to front in OnLoad

    private static readonly string _windowStatePath =
        Path.Combine(AppContext.BaseDirectory, "window-state.json");

    public MainForm()
    {
        // Audio notification service needs the UI sync context – capture it after handle created
        _audioNotifySvc = new AudioNotificationService(
            SynchronizationContext.Current ?? new SynchronizationContext(),
            OnAudioChanged);

        // Discord: client ID loaded from discord-settings.txt next to the exe
        var clientId = LoadDiscordClientId();
        _discordSvc = new DiscordService(clientId);

        // Home Assistant: ha-config.json next to the exe (optional)
        var haConfig = HomeAssistantService.LoadConfig();
        _haSvc = haConfig != null ? new HomeAssistantService(haConfig) : null;

        // AnythingLLM: anythingllm-config.json next to the exe (optional)
        var aiConfig = AnythingLLMService.LoadConfig();
        _aiSvc = aiConfig != null ? new AnythingLLMService(aiConfig) : null;

        // ChatGPT: chatgpt-config.json next to the exe (optional)
        var gptConfig = ChatGptService.LoadConfig();
        _chatGptSvc = gptConfig != null ? new ChatGptService(gptConfig) : null;

        InitForm();
        CreatePanels();

        // Wire DWM dark title bar + save window state on close
        Load        += OnLoad;
        FormClosing += (_, _) => SaveWindowState();
    }

    // ── Initialisation ────────────────────────────────────────────────────────

    private void InitForm()
    {
        Text            = "Audio Mixer";
        FormBorderStyle = FormBorderStyle.None;
        BackColor       = AppTheme.BgDeep;
        MinimumSize     = new Size(1280, 540);

        // Restore last saved position, or fall back to working area
        StartPosition = FormStartPosition.Manual;
        TryRestoreWindowState(Screen.PrimaryScreen!.WorkingArea);

        // Custom title bar area for dragging / close
        var titleBar = new Panel
        {
            Height    = 32,
            Dock      = DockStyle.Top,
            BackColor = AppTheme.BgDeep,
        };
        titleBar.MouseDown  += (_, e) => { if (e.Button == MouseButtons.Left) { _dragging = true; _dragStart = e.Location; } };
        titleBar.MouseMove  += (_, e) => { if (_dragging) Location = new Point(Location.X + e.X - _dragStart.X, Location.Y + e.Y - _dragStart.Y); };
        titleBar.MouseUp    += (_, _) => _dragging = false;
        titleBar.DoubleClick += (_, _) => ToggleMaximize();

        // App title in the bar
        var titleLabel = new Label
        {
            Text      = "BaumDash",
            Font      = AppTheme.FontBold,
            ForeColor = AppTheme.TextSecondary,
            BackColor = Color.Transparent,
            AutoSize  = true,
        };
        titleLabel.Location = new Point(12, (32 - titleLabel.PreferredHeight) / 2);
        // Forward drag events through the label so the whole bar is draggable
        titleLabel.MouseDown  += (_, e) => { if (e.Button == MouseButtons.Left) { _dragging = true; _dragStart = e.Location; } };
        titleLabel.MouseMove  += (_, e) => { if (_dragging) Location = new Point(Location.X + e.X - _dragStart.X, Location.Y + e.Y - _dragStart.Y); };
        titleLabel.MouseUp    += (_, _) => _dragging = false;

        // Close / Maximise / Minimise buttons (far right)
        var btnClose = MakeTitleBarButton("✕", AppTheme.Danger);
        btnClose.Click += (_, _) => Close();

        var btnMin = MakeTitleBarButton("─", AppTheme.TextMuted);
        btnMin.Click += (_, _) => WindowState = FormWindowState.Minimized;

        _btnMax = MakeTitleBarButton("⬜", AppTheme.TextMuted);
        _btnMax.Click += (_, _) => ToggleMaximize();

        // Right-docked container keeps buttons at the right edge regardless of window width
        var winBtnPanel = new Panel { Width = 132, Dock = DockStyle.Right, BackColor = AppTheme.BgDeep };
        btnClose.Location = new Point(88, 0);
        btnMin  .Location = new Point(44, 0);
        _btnMax .Location = new Point(0,  0);
        winBtnPanel.Controls.AddRange(new Control[] { _btnMax, btnMin, btnClose });

        // 1px bottom border so the title bar is visually distinct from panel content
        var titleBorder = new Panel { Height = 1, Dock = DockStyle.Bottom, BackColor = AppTheme.Border };

        titleBar.Controls.AddRange(new Control[] { titleLabel, winBtnPanel, titleBorder });
        Controls.Add(titleBar);

        // Settings button (⚙) and Help button (?) — bottom-left overlay
        var btnSettings = new Button
        {
            Text      = "⚙",
            Font      = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = AppTheme.TextSecondary,
            BackColor = AppTheme.BgCard,
            FlatStyle = FlatStyle.Flat,
            Size      = new Size(28, 28),
            Cursor    = Cursors.Hand,
            Anchor    = AnchorStyles.Bottom | AnchorStyles.Left,
            FlatAppearance = { BorderSize = 1, BorderColor = AppTheme.Border,
                               MouseOverBackColor = AppTheme.BgPanel },
        };
        btnSettings.Location = new Point(8, ClientSize.Height - btnSettings.Height - 8);
        btnSettings.Click   += (_, _) => new SettingsDialog().ShowDialog(this);
        Controls.Add(btnSettings);
        _btnSettings = btnSettings; // brought to front in OnLoad

        var btnHelp = new Button
        {
            Text      = "?",
            Font      = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = AppTheme.TextSecondary,
            BackColor = AppTheme.BgCard,
            FlatStyle = FlatStyle.Flat,
            Size      = new Size(28, 28),
            Cursor    = Cursors.Hand,
            Anchor    = AnchorStyles.Bottom | AnchorStyles.Left,
            FlatAppearance = { BorderSize = 1, BorderColor = AppTheme.Border,
                               MouseOverBackColor = AppTheme.BgPanel },
        };
        btnHelp.Location = new Point(44, ClientSize.Height - btnHelp.Height - 8);
        btnHelp.Click   += (_, _) => ShowHelpDialog();
        Controls.Add(btnHelp);
        _btnHelp = btnHelp; // brought to front in OnLoad
    }

    private void CreatePanels()
    {
        var layout = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 4,
            RowCount    = 1,
            BackColor   = AppTheme.BgDeep,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
            Margin      = new Padding(0),
            Padding     = new Padding(0),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 500));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 400));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,  100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _devicePanel  = new AudioDevicePanel(_audioDeviceSvc, _haSvc) { Dock = DockStyle.Fill };
        _volumePanel  = new AppVolumePanel(_audioSessionSvc)    { Dock = DockStyle.Fill };
        _mediaPanel   = new MediaPanel()                        { Dock = DockStyle.Fill };
        _discordPanel = new DiscordPanel(_discordSvc, _aiSvc, _chatGptSvc) { Dock = DockStyle.Fill };

        // Media control event wiring
        _mediaPanel.PlayPauseRequested += async () => { if (_mediaSvc != null) await _mediaSvc.TogglePlayPauseAsync(); };
        _mediaPanel.NextRequested      += async () => { if (_mediaSvc != null) await _mediaSvc.NextAsync(); };
        _mediaPanel.PreviousRequested  += async () => { if (_mediaSvc != null) await _mediaSvc.PreviousAsync(); };

        // Vertical dividers
        var div1 = MakeDivider();
        var div2 = MakeDivider();
        var div3 = MakeDivider();

        // Wrap panels in divider-bearing containers
        var col1 = WrapWithRightDivider(_devicePanel,  div1);
        var col2 = WrapWithRightDivider(_volumePanel,  div2);
        var col3 = WrapWithRightDivider(_mediaPanel,   div3);

        layout.Controls.Add(col1,         0, 0);
        layout.Controls.Add(col2,         1, 0);
        layout.Controls.Add(col3,         2, 0);
        layout.Controls.Add(_discordPanel, 3, 0);

        Controls.Add(layout);
        layout.BringToFront(); // must be front for correct Dock=Fill layout under Dock=Top titleBar
    }

    private async void OnLoad(object? sender, EventArgs e)
    {
        // Apply dark title bar via DWM
        ApplyDarkMode();

        // Bring overlay buttons in front of layout without triggering a dock recalculation
        SuspendLayout();
        _btnSettings?.BringToFront();
        _btnHelp    ?.BringToFront();
        ResumeLayout(false);

        // Start audio notification listener
        _audioNotifySvc.Start();

        // Bootstrap SMTC media service
        try
        {
            _mediaSvc = await MediaSessionService.CreateAsync();
            _mediaSvc.MediaChanged += info =>
            {
                if (InvokeRequired) BeginInvoke(() => _mediaPanel?.UpdateMedia(info));
                else                _mediaPanel?.UpdateMedia(info);
            };
            await _mediaSvc.RefreshAsync();
        }
        catch { /* SMTC unavailable */ }
    }

    // ── Audio changed callback ────────────────────────────────────────────────

    private void OnAudioChanged()
    {
        // SynchronizationContext captured in the constructor may be null (before
        // Application.Run), so callbacks can arrive on a ThreadPool MTA thread.
        // COM audio objects are STA-bound — always marshal to the UI thread.
        if (InvokeRequired) { BeginInvoke(OnAudioChanged); return; }
        _devicePanel?.LoadDevices();
        _volumePanel?.LoadSessions();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ToggleMaximize()
    {
        WindowState = WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal
            : FormWindowState.Maximized;
        if (_btnMax != null)
            _btnMax.Text = WindowState == FormWindowState.Maximized ? "❐" : "⬜";
    }

    private void TryRestoreWindowState(Rectangle fallback)
    {
        try
        {
            if (!File.Exists(_windowStatePath)) { Bounds = fallback; return; }
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(_windowStatePath));
            var r = doc.RootElement;
            var b = new Rectangle(
                r.GetProperty("X").GetInt32(), r.GetProperty("Y").GetInt32(),
                r.GetProperty("W").GetInt32(), r.GetProperty("H").GetInt32());

            // Only restore if the rect is actually visible on some screen
            if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(b)))
            {
                Bounds = b;
                if (r.GetProperty("Maximized").GetBoolean())
                {
                    WindowState = FormWindowState.Maximized;
                    if (_btnMax != null) _btnMax.Text = "❐";
                }
                return;
            }
        }
        catch { }
        Bounds = fallback;
    }

    private void SaveWindowState()
    {
        try
        {
            var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            File.WriteAllText(_windowStatePath, System.Text.Json.JsonSerializer.Serialize(new
            {
                X = b.X, Y = b.Y, W = b.Width, H = b.Height,
                Maximized = WindowState == FormWindowState.Maximized
            }));
        }
        catch { }
    }

    private static Button MakeTitleBarButton(string text, Color hoverColor) =>
        new()
        {
            Text      = text,
            Font      = new Font("Segoe UI", 11f),
            ForeColor = AppTheme.TextSecondary,
            BackColor = Color.Transparent,
            FlatStyle = FlatStyle.Flat,
            Size      = new Size(44, 32),
            Cursor    = Cursors.Hand,
            FlatAppearance = { BorderSize = 0,
                MouseOverBackColor = hoverColor,
                MouseDownBackColor = ControlPaint.Dark(hoverColor, 0.3f) },
        };

    private static Panel MakeDivider() =>
        new() { Width = 1, BackColor = AppTheme.Border, Dock = DockStyle.Right };

    private static Panel WrapWithRightDivider(Control content, Panel divider)
    {
        var wrapper = new Panel { Dock = DockStyle.Fill };
        content.Dock = DockStyle.Fill;
        wrapper.Controls.Add(content);
        wrapper.Controls.Add(divider);
        divider.BringToFront();
        return wrapper;
    }

    private static string LoadDiscordClientId()
    {
        var settingsFile = Path.Combine(
            AppContext.BaseDirectory, "discord-client-id.txt");
        if (File.Exists(settingsFile))
            return File.ReadAllText(settingsFile).Trim();
        return "YOUR_DISCORD_CLIENT_ID"; // replace or create discord-client-id.txt
    }

    private void ApplyDarkMode()
    {
        try
        {
            int dark = 1;
            DwmSetWindowAttribute(Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, 4);
        }
        catch { }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    // ── Help dialog ───────────────────────────────────────────────────────────

    private void ShowHelpDialog()
    {
        var installPath = AppContext.BaseDirectory.TrimEnd('\\', '/');

        var dlg = new Form
        {
            Text            = "BaumDash – Setup Help",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            BackColor       = AppTheme.BgDeep,
            ForeColor       = AppTheme.TextPrimary,
            Size            = new Size(580, 620),
            StartPosition   = FormStartPosition.CenterParent,
            MaximizeBox     = false,
            MinimizeBox     = false,
            ShowInTaskbar   = false,
        };

        var txt = new RichTextBox
        {
            Dock        = DockStyle.Fill,
            BackColor   = AppTheme.BgDeep,
            ForeColor   = AppTheme.TextPrimary,
            Font        = new Font("Segoe UI", 10f),
            ReadOnly    = true,
            BorderStyle = BorderStyle.None,
            Padding     = new Padding(16),
            ScrollBars  = RichTextBoxScrollBars.Vertical,
        };

        txt.Text =
            "DISCORD SETUP\r\n" +
            "─────────────────────────────────────────────────\r\n" +
            "1. Go to discord.com/developers and create an application.\r\n" +
            "2. Copy the Application (Client) ID.\r\n" +
            "3. Open discord-client-id.txt in the install folder.\r\n" +
            "4. Paste your Client ID and save the file.\r\n" +
            "5. Restart BaumDash.\r\n" +
            "\r\n" +
            "HOME ASSISTANT SETUP\r\n" +
            "─────────────────────────────────────────────────\r\n" +
            "1. Open ha-config.json in the install folder.\r\n" +
            "2. Set \"url\" to your Nabu Casa or local HA address.\r\n" +
            "   e.g. https://xxxxx.ui.nabu.casa\r\n" +
            "3. Set \"token\" to a Long-Lived Access Token:\r\n" +
            "   HA → Profile → Long-Lived Access Tokens → Create Token\r\n" +
            "4. Add lights:  { \"id\": \"light.living_room\", \"name\": \"Room\" }\r\n" +
            "5. Add sensors: { \"id\": \"sensor.room_temperature\", \"name\": \"Temp\" }\r\n" +
            "6. Save the file and restart BaumDash.\r\n" +
            "\r\n" +
            "ANYTHINGLLM SETUP\r\n" +
            "─────────────────────────────────────────────────\r\n" +
            "AnythingLLM is a local AI assistant. Install it from anythingllm.com.\r\n" +
            "\r\n" +
            "1. Open anythingllm-config.json in the install folder.\r\n" +
            "2. Set \"url\":\r\n" +
            "   • Local install:  http://localhost:3001\r\n" +
            "   • Docker/remote:  http://<host>:<port>\r\n" +
            "3. Set \"apiKey\":\r\n" +
            "   AnythingLLM → ⚙ Settings → Security → API Keys → Generate\r\n" +
            "4. Set \"workspace\":\r\n" +
            "   Open a workspace in AnythingLLM — the slug is in the URL:\r\n" +
            "   e.g. http://localhost:3001/workspace/general → slug is \"general\"\r\n" +
            "5. Save the file and restart BaumDash.\r\n" +
            "6. Click the AI CHAT tab in the right panel to start chatting.\r\n" +
            "\r\n" +
            "CHATGPT SETUP\r\n" +
            "─────────────────────────────────────────────────\r\n" +
            "1. Go to platform.openai.com and create an account.\r\n" +
            "2. Navigate to API Keys and create a new secret key.\r\n" +
            "3. Open chatgpt-config.json in the install folder.\r\n" +
            "4. Set \"apiKey\" to your OpenAI API key.\r\n" +
            "5. Set \"model\" to your preferred model:\r\n" +
            "   • gpt-4o          — most capable\r\n" +
            "   • gpt-4o-mini     — faster, cheaper\r\n" +
            "   • gpt-3.5-turbo   — budget option\r\n" +
            "6. Save the file and restart BaumDash.\r\n" +
            "7. Click the CHATGPT tab in the right panel to start chatting.\r\n" +
            "\r\n" +
            "INSTALL LOCATION\r\n" +
            "─────────────────────────────────────────────────\r\n" +
            installPath + "\r\n";

        var btnClose = new Button
        {
            Text      = "Close",
            Font      = AppTheme.FontBold,
            ForeColor = AppTheme.TextPrimary,
            BackColor = AppTheme.BgCard,
            FlatStyle = FlatStyle.Flat,
            Size      = new Size(90, 30),
            Cursor    = Cursors.Hand,
            Anchor    = AnchorStyles.Bottom | AnchorStyles.Right,
            FlatAppearance = { BorderSize = 1, BorderColor = AppTheme.Border },
        };
        btnClose.Location = new Point(dlg.ClientSize.Width - 106, dlg.ClientSize.Height - 46);
        btnClose.Click   += (_, _) => dlg.Close();

        var btnOpenFolder = new Button
        {
            Text      = "Open Folder",
            Font      = AppTheme.FontBold,
            ForeColor = AppTheme.TextPrimary,
            BackColor = AppTheme.BgCard,
            FlatStyle = FlatStyle.Flat,
            Size      = new Size(100, 30),
            Cursor    = Cursors.Hand,
            Anchor    = AnchorStyles.Bottom | AnchorStyles.Right,
            FlatAppearance = { BorderSize = 1, BorderColor = AppTheme.Border },
        };
        btnOpenFolder.Location = new Point(dlg.ClientSize.Width - 214, dlg.ClientSize.Height - 46);
        btnOpenFolder.Click   += (_, _) =>
        {
            try { System.Diagnostics.Process.Start("explorer.exe", installPath); } catch { }
        };

        dlg.Controls.Add(txt);
        dlg.Controls.Add(btnClose);
        dlg.Controls.Add(btnOpenFolder);
        btnClose.BringToFront();
        btnOpenFolder.BringToFront();

        dlg.ShowDialog(this);
    }

    // ── Disposal ──────────────────────────────────────────────────────────────

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _audioNotifySvc.Dispose();
            _discordSvc    .Dispose();
            _haSvc         ?.Dispose();
            _aiSvc         ?.Dispose();
            _chatGptSvc    ?.Dispose();
            _mediaSvc      ?.Dispose();
        }
        base.Dispose(disposing);
    }
}

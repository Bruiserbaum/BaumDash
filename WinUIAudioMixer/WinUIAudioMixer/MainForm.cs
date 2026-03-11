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
    private readonly AnythingLLMService?      _aiSvc;
    private readonly ChatGptService?          _chatGptSvc;
    private readonly GoogleCalendarService?   _calSvc;
    private readonly WeatherService?          _weatherSvc;
    private MediaSessionService?              _mediaSvc;

    // Panels
    private AudioDevicePanel?  _devicePanel;
    private AppVolumePanel?    _volumePanel;
    private MediaPanel?        _mediaPanel;
    private DiscordPanel?      _discordPanel;
    private TableLayoutPanel?  _contentLayout;

    // Layout profile: "auto" / "1920" / "2560"
    private string _layoutProfile = "auto";

    // Drag support (borderless move)
    private Point   _dragStart;
    private bool    _dragging;
    private Button? _btnMax;
    private Button? _btnSettings, _btnHelp, _btnUpdate; // overlay buttons — brought to front in OnLoad
    private NotifyIcon? _trayIcon;
    private bool _exitRequested;
    private bool _closeToTray = true;

    /// <summary>Set by SettingsDialog.OnImport — Program.cs restarts to tray after Application.Run returns.</summary>
    internal static bool PendingImportRestart;

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

        // Google Calendar: gcalendar-config.json next to the exe (optional)
        var calConfig = GoogleCalendarService.LoadConfig();
        _calSvc = calConfig != null ? new GoogleCalendarService(calConfig) : null;

        // Weather: weather-config.json next to the exe (optional)
        _weatherSvc = WeatherService.LoadAndCreate();

        // General settings — apply theme + background image BEFORE building any UI
        try
        {
            var genPath = Path.Combine(AppContext.BaseDirectory, "general-config.json");
            if (File.Exists(genPath))
            {
                var cfg = System.Text.Json.JsonSerializer.Deserialize<Models.GeneralConfig>(
                    File.ReadAllText(genPath),
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (cfg != null)
                {
                    _closeToTray   = cfg.CloseToTray;
                    _layoutProfile = cfg.LayoutProfile ?? "auto";
                    Services.UpdateService.Channel = cfg.ReleaseChannel ?? "stable";
                    AppTheme.Apply(cfg.Theme, cfg.CustomAccentHex);
                    if (!string.IsNullOrEmpty(cfg.BgImagePath) && File.Exists(cfg.BgImagePath))
                    {
                        try
                        {
                            AppTheme.BgImage        = Image.FromFile(cfg.BgImagePath);
                            AppTheme.BgImageMode    = cfg.BgImageMode;
                            AppTheme.BgOverlayAlpha = cfg.BgOverlayAlpha;
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }

        InitForm();
        CreatePanels();

        // Wire DWM dark title bar + save window state on close
        Load        += OnLoad;
        FormClosing += OnFormClosing;

        // Log power state changes (sleep / wake) which are a known crash trigger
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
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
        btnClose.Click += (_, _) =>
        {
            if (_closeToTray) HideToTray();
            else { _exitRequested = true; Close(); }
        };

        var btnMin = MakeTitleBarButton("─", AppTheme.TextMuted);
        btnMin.Click += (_, _) =>
        {
            if (_closeToTray) HideToTray();
            else WindowState = FormWindowState.Minimized;
        };

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
            Font      = new Font("Segoe UI", 16f, FontStyle.Bold),
            ForeColor = AppTheme.TextSecondary,
            BackColor = AppTheme.BgCard,
            FlatStyle = FlatStyle.Flat,
            Size      = new Size(54, 54),
            Cursor    = Cursors.Hand,
            Anchor    = AnchorStyles.Bottom | AnchorStyles.Left,
            FlatAppearance = { BorderSize = 1, BorderColor = AppTheme.Border,
                               MouseOverBackColor = AppTheme.BgPanel },
        };
        btnSettings.Location = new Point(8, ClientSize.Height - btnSettings.Height - 8);
        btnSettings.Click   += OnSettingsClick;
        Controls.Add(btnSettings);
        _btnSettings = btnSettings; // brought to front in OnLoad

        var btnHelp = new Button
        {
            Text      = "?",
            Font      = new Font("Segoe UI", 16f, FontStyle.Bold),
            ForeColor = AppTheme.TextSecondary,
            BackColor = AppTheme.BgCard,
            FlatStyle = FlatStyle.Flat,
            Size      = new Size(54, 54),
            Cursor    = Cursors.Hand,
            Anchor    = AnchorStyles.Bottom | AnchorStyles.Left,
            FlatAppearance = { BorderSize = 1, BorderColor = AppTheme.Border,
                               MouseOverBackColor = AppTheme.BgPanel },
        };
        btnHelp.Location = new Point(70, ClientSize.Height - btnHelp.Height - 8);
        btnHelp.Click   += (_, _) => ShowHelpDialog();
        Controls.Add(btnHelp);
        _btnHelp = btnHelp; // brought to front in OnLoad

        // Update-available button (hidden until a newer release is found)
        var btnUpdate = new Button
        {
            Text      = "⬆ Update",
            Font      = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = AppTheme.Warning,
            FlatStyle = FlatStyle.Flat,
            Size      = new Size(72, 28),
            Cursor    = Cursors.Hand,
            Anchor    = AnchorStyles.Bottom | AnchorStyles.Left,
            Visible   = false,
            FlatAppearance = { BorderSize = 0,
                               MouseOverBackColor = ControlPaint.Dark(AppTheme.Warning, 0.12f) },
        };
        btnUpdate.Location = new Point(132, ClientSize.Height - btnUpdate.Height - 8);
        btnUpdate.Click   += OnUpdateButtonClick;
        Controls.Add(btnUpdate);
        _btnUpdate = btnUpdate;

        InitTrayIcon();
    }

    private void InitTrayIcon()
    {
        // Build a simple 16x16 icon programmatically (dark bg, white "B")
        var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(AppTheme.BgDeep);
            using var br = new SolidBrush(AppTheme.Accent);
            g.FillRectangle(br, 0, 0, 16, 16);
            using var f  = new Font("Segoe UI", 8f, FontStyle.Bold);
            using var tb = new SolidBrush(Color.White);
            var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("B", f, tb, new RectangleF(0, 0, 16, 16), fmt);
        }
        var icon = Icon.FromHandle(bmp.GetHicon());

        var menu = new ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => RestoreFromTray());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => { _exitRequested = true; Application.Exit(); });

        _trayIcon = new NotifyIcon
        {
            Icon    = icon,
            Text    = "BaumDash",
            Visible = false,
            ContextMenuStrip = menu,
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void HideToTray()
    {
        SaveWindowState();
        Hide();
        if (_trayIcon != null) _trayIcon.Visible = true;
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        if (_trayIcon != null) _trayIcon.Visible = false;
    }

    // ── Layout profiles ───────────────────────────────────────────────────────

    private static (int c0, int c1, int c2, int formW) ResolveLayout(string profile, Control? reference = null)
    {
        string resolved = profile;
        if (profile == "auto")
        {
            var screen = reference != null ? Screen.FromControl(reference) : Screen.PrimaryScreen;
            resolved = (screen?.WorkingArea.Width ?? 1920) >= 2500 ? "2560" : "1920";
        }
        return resolved == "2560"
            ? (320, 700, 500, 2560)   // 2560×720: wider AppVolume + Media for ultrawide
            : (300, 500, 400, 1920);  // 1920×720: default
    }

    private void ApplyLayout(string profile)
    {
        _layoutProfile = profile;
        var (c0, c1, c2, formW) = ResolveLayout(profile, this);

        if (_contentLayout != null)
        {
            _contentLayout.SuspendLayout();
            _contentLayout.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, c0);
            _contentLayout.ColumnStyles[1] = new ColumnStyle(SizeType.Absolute, c1);
            _contentLayout.ColumnStyles[2] = new ColumnStyle(SizeType.Absolute, c2);
            _contentLayout.ResumeLayout(true);
        }

        if (Width != formW)
            Width = formW;
    }

    private void CreatePanels()
    {
        var (c0, c1, c2, _) = ResolveLayout(_layoutProfile, this);

        _contentLayout = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 4,
            RowCount    = 1,
            BackColor   = AppTheme.BgDeep,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
            Margin      = new Padding(0),
            Padding     = new Padding(0),
        };
        var layout = _contentLayout;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, c0));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, c1));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, c2));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,  100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _devicePanel  = new AudioDevicePanel(_audioDeviceSvc) { Dock = DockStyle.Fill };
        _volumePanel  = new AppVolumePanel(_audioSessionSvc)    { Dock = DockStyle.Fill };
        _mediaPanel   = new MediaPanel(_weatherSvc)             { Dock = DockStyle.Fill };
        _discordPanel = new DiscordPanel(_discordSvc, _aiSvc, _chatGptSvc, _calSvc, _haSvc) { Dock = DockStyle.Fill };

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
        Services.CrashLogger.Info("MainForm loaded — starting services");
        // Register auto-start by default if not already set
        EnsureAutoStart();

        // If launched with --tray (e.g. after settings import), go straight to tray
        if (Environment.GetCommandLineArgs().Contains("--tray"))
        {
            HideToTray();
            return;
        }

        // Apply dark title bar via DWM
        ApplyDarkMode();

        // Bring overlay buttons in front of layout without triggering a dock recalculation
        SuspendLayout();
        _btnSettings?.BringToFront();
        _btnHelp    ?.BringToFront();
        _btnUpdate  ?.BringToFront();
        ResumeLayout(false);

        // Start audio notification listener
        _audioNotifySvc.Start();

        // Check for updates silently in the background
        _ = Task.Run(async () =>
        {
            var release = await Services.UpdateService.CheckAsync();
            if (release != null)
                BeginInvoke(() => ShowUpdateButton(release));
        });

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

    private void OnSettingsClick(object? sender, EventArgs e)
    {
        using var dlg = new SettingsDialog();
        dlg.ShowDialog(this);
        ApplySettingsRefresh();
    }

    /// <summary>
    /// Re-reads all config files after the Settings dialog closes and hot-applies
    /// any changes to the running panels — no restart needed.
    /// </summary>
    private void ApplySettingsRefresh()
    {
        // ── General config ────────────────────────────────────────────────────
        try
        {
            var genPath = Path.Combine(AppContext.BaseDirectory, "general-config.json");
            if (File.Exists(genPath))
            {
                var cfg = System.Text.Json.JsonSerializer.Deserialize<Models.GeneralConfig>(
                    File.ReadAllText(genPath),
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (cfg != null)
                {
                    _closeToTray = cfg.CloseToTray;
                    _devicePanel?.ApplyGpuPlatform(cfg.GpuPlatform);
                    ApplyLayout(cfg.LayoutProfile ?? "auto");
                    _discordPanel?.ApplyTabVisibility(cfg.HiddenDiscordTabs ?? new List<string>());
                }
            }
        }
        catch { }

        // ── Weather config ────────────────────────────────────────────────────
        // Reload the service from disk and hand the new instance to MediaPanel.
        try
        {
            var newWeather = Services.WeatherService.LoadAndCreate();
            _mediaPanel?.UpdateWeatherService(newWeather);
        }
        catch { }

        // ── Audio devices ─────────────────────────────────────────────────────
        _devicePanel?.LoadDevices();
        _volumePanel?.LoadSessions();
    }

    private void OnAudioChanged()
    {
        // SynchronizationContext captured in the constructor may be null (before
        // Application.Run), so callbacks can arrive on a ThreadPool MTA thread.
        // COM audio objects are STA-bound — always marshal to the UI thread.
        if (InvokeRequired) { BeginInvoke(OnAudioChanged); return; }
        Services.CrashLogger.Info("Audio device/session change — reloading panels");
        try
        {
            _devicePanel?.LoadDevices();
            _volumePanel?.LoadSessions();
        }
        catch (Exception ex)
        {
            Services.CrashLogger.Error("Exception in OnAudioChanged", ex);
        }
    }

    private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        Services.CrashLogger.Info($"Power mode changed: {e.Mode}");
        if (e.Mode == Microsoft.Win32.PowerModes.Resume)
        {
            // Wake from sleep — re-enumerate audio devices which are a common crash point
            Services.CrashLogger.Info("System resumed from sleep — refreshing audio");
            try
            {
                if (InvokeRequired) BeginInvoke(() => OnAudioChanged());
                else                OnAudioChanged();
            }
            catch (Exception ex)
            {
                Services.CrashLogger.Error("Exception refreshing audio after sleep resume", ex);
            }
        }
    }

    private void ShowUpdateButton(Services.UpdateReleaseInfo release)
    {
        if (_btnUpdate == null) return;
        _btnUpdate.Text    = $"⬆ v{release.Version}";
        _btnUpdate.Visible = true;
        _btnUpdate.BringToFront();

        // Also show a tray balloon if running in the tray
        if (_trayIcon?.Visible == true)
            _trayIcon.ShowBalloonTip(6000, "BaumDash Update Available",
                $"Version {release.Version} is ready to download.", ToolTipIcon.Info);
    }

    private void OnUpdateButtonClick(object? sender, EventArgs e)
    {
        var release = Services.UpdateService.AvailableRelease;
        if (release == null) return;

        var result = MessageBox.Show(this,
            $"BaumDash {release.Version} is available  (current: {Services.UpdateService.CurrentVersion}).\n\n" +
            "Download and install now?\n" +
            "BaumDash will close and the installer will open.",
            "Update Available",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information);

        if (result != DialogResult.Yes) return;

        // Show a simple progress dialog while downloading
        using var dlg = new Form
        {
            Text            = "Downloading Update…",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            BackColor       = AppTheme.BgDeep,
            ForeColor       = AppTheme.TextPrimary,
            ClientSize      = new Size(360, 90),
            StartPosition   = FormStartPosition.CenterParent,
            MaximizeBox     = false,
            MinimizeBox     = false,
            ShowInTaskbar   = false,
            ControlBox      = false,
        };
        var lbl = new Label
        {
            Text      = $"Downloading BaumDash {release.Version}…",
            Font      = AppTheme.FontLabel,
            ForeColor = AppTheme.TextPrimary,
            BackColor = Color.Transparent,
            AutoSize  = false,
            Location  = new Point(16, 14),
            Size      = new Size(328, 20),
        };
        var bar = new ProgressBar
        {
            Style    = ProgressBarStyle.Continuous,
            Location = new Point(16, 44),
            Size     = new Size(328, 22),
            Minimum  = 0,
            Maximum  = 100,
        };
        dlg.Controls.AddRange(new Control[] { lbl, bar });

        var cts      = new CancellationTokenSource();
        var progress = new Progress<int>(pct =>
        {
            if (!dlg.IsDisposed)
            {
                bar.Value    = Math.Min(pct, 100);
                lbl.Text     = $"Downloading BaumDash {release.Version}…  {pct}%";
            }
        });

        dlg.Shown += async (_, _) =>
        {
            try
            {
                await Services.UpdateService.DownloadAndInstallAsync(release, progress, cts.Token);
            }
            catch (OperationCanceledException) { dlg.Close(); }
            catch (Exception ex)
            {
                dlg.Close();
                MessageBox.Show(this, $"Download failed:\n{ex.Message}",
                    "Update Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };

        dlg.ShowDialog(this);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
        => AppTheme.PaintBackground(e.Graphics, this, AppTheme.BgDeep);

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

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        SaveWindowState();
        // Allow exit when explicitly requested (tray Exit item) or triggered by Application.Exit() (import restart)
        if (!_exitRequested && e.CloseReason != CloseReason.ApplicationExitCall)
        {
            Services.CrashLogger.Info($"Close intercepted (reason={e.CloseReason}) — hiding to tray");
            e.Cancel = true;
            HideToTray();
        }
        else
        {
            Services.CrashLogger.Info($"Application exiting (reason={e.CloseReason}, exitRequested={_exitRequested})");
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
        }
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

    private static void EnsureAutoStart()
    {
        const string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(runKey, writable: true)!;
            if (key.GetValue("BaumDash") == null)
                key.SetValue("BaumDash", $"\"{Application.ExecutablePath}\"");
        }
        catch { }
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
            "BAUMDASH SETUP GUIDE\r\n" +
            "═════════════════════════════════════════════════\r\n" +
            "All settings are configured through the ⚙ Settings button\r\n" +
            "in the top-right corner of the app. Click Save after each change,\r\n" +
            "then restart BaumDash to apply.\r\n" +
            "\r\n" +
            "GENERAL SETTINGS\r\n" +
            "─────────────────────────────────────────────────\r\n" +
            "• Launch on startup — adds BaumDash to Windows startup.\r\n" +
            "• Close / minimize to tray — when enabled, the ✕ and ─ buttons\r\n" +
            "  hide BaumDash to the system tray instead of exiting.\r\n" +
            "  Right-click the tray icon to exit completely.\r\n" +
            "\r\n" +
            "DISCORD SETUP\r\n" +
            "─────────────────────────────────────────────────\r\n" +
            "1. Go to discord.com/developers → New Application.\r\n" +
            "2. In Settings → General, copy the Application (Client) ID.\r\n" +
            "3. In Settings → OAuth2, copy the Client Secret.\r\n" +
            "4. Open BaumDash → ⚙ Settings → DISCORD tab.\r\n" +
            "5. Paste both values, click Save.\r\n" +
            "6. Restart BaumDash. On first connect, Discord will show an\r\n" +
            "   authorization popup — click Authorize.\r\n" +
            "   A token is saved so future launches skip the popup.\r\n" +
            "\r\n" +
            "DISCORD — VOICE MEMBERS / TROUBLESHOOTING\r\n" +
            "─────────────────────────────────────────────────\r\n" +
            "• Voice members, mute state, and streaming state update\r\n" +
            "  automatically when you join/leave a voice channel.\r\n" +
            "• Use the ↻ Refresh button on the Discord tab to force a\r\n" +
            "  manual re-query of voice members and mic state.\r\n" +
            "• If voice members show 0 or state is wrong, open\r\n" +
            "  Settings → DISCORD → click Reauthorize Discord, then\r\n" +
            "  reconnect and click Authorize in the Discord popup.\r\n" +
            "• The MESSAGES box shows incoming notifications (DMs,\r\n" +
            "  mentions) and voice-channel text chat in real time.\r\n" +
            "  History is kept for the current session only.\r\n" +
            "\r\n" +
            "HOME ASSISTANT SETUP\r\n" +
            "─────────────────────────────────────────────────\r\n" +
            "1. Open Settings → HOME ASSISTANT tab.\r\n" +
            "2. SERVER URL — your Nabu Casa or local address:\r\n" +
            "   e.g. https://xxxxx.ui.nabu.casa\r\n" +
            "3. LONG-LIVED ACCESS TOKEN:\r\n" +
            "   HA → Profile → Long-Lived Access Tokens → Create Token\r\n" +
            "4. LIGHTS — one per line: entity_id = Display Name\r\n" +
            "   e.g.  light.living_room = Living Room\r\n" +
            "5. SWITCHES — one per line: entity_id = Display Name\r\n" +
            "   e.g.  switch.desk_power = Desk Power\r\n" +
            "6. SENSORS — one per line: entity_id = Display Name\r\n" +
            "   e.g.  sensor.room_temperature = Room Temp\r\n" +
            "7. Click Save and restart BaumDash.\r\n" +
            "   Light and switch buttons appear in the left panel.\r\n" +
            "   Sensor readings refresh every 30 seconds.\r\n" +
            "\r\n" +
            "ANYTHINGLLM SETUP\r\n" +
            "─────────────────────────────────────────────────\r\n" +
            "1. Install AnythingLLM from anythingllm.com.\r\n" +
            "2. Open Settings → ANYTHING LLM tab.\r\n" +
            "3. SERVER URL:\r\n" +
            "   • Local:   http://localhost:3001\r\n" +
            "   • Remote:  http://<host>:<port>\r\n" +
            "4. API KEY: AnythingLLM → ⚙ Settings → Security → API Keys\r\n" +
            "5. WORKSPACE SLUG: from the URL of your workspace\r\n" +
            "   e.g. localhost:3001/workspace/general → \"general\"\r\n" +
            "6. Click Save, restart, then use the AI CHAT tab.\r\n" +
            "\r\n" +
            "CHATGPT SETUP\r\n" +
            "─────────────────────────────────────────────────\r\n" +
            "1. Get an API key from platform.openai.com → API Keys.\r\n" +
            "2. Open Settings → CHATGPT tab.\r\n" +
            "3. Paste your API key and choose a model:\r\n" +
            "   • gpt-4o        — most capable\r\n" +
            "   • gpt-4o-mini   — faster, cheaper\r\n" +
            "   • gpt-3.5-turbo — budget option\r\n" +
            "4. Click Save, restart, then use the CHATGPT tab.\r\n" +
            "\r\n" +
            "GOOGLE CALENDAR SETUP\r\n" +
            "─────────────────────────────────────────────────\r\n" +
            "1. Open Settings → CALENDAR tab.\r\n" +
            "2. Click '+ Add Calendar' for each calendar.\r\n" +
            "3. In Google Calendar: ⋮ → Settings → Integrate calendar\r\n" +
            "   → copy the 'Secret address in iCal format' URL.\r\n" +
            "4. Paste the URL and give the calendar a name.\r\n" +
            "5. Click Save, restart, then use the CALENDAR tab.\r\n" +
            "\r\n" +
            "BACKUP & RESTORE\r\n" +
            "─────────────────────────────────────────────────\r\n" +
            "Use Export… / Import… buttons at the bottom of Settings\r\n" +
            "to back up or restore all config files at once.\r\n" +
            "The backup is a .baumdash-backup file (JSON archive).\r\n" +
            "Importing restarts BaumDash automatically.\r\n" +
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
            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _audioNotifySvc.Dispose();
            _discordSvc    .Dispose();
            _haSvc         ?.Dispose();
            _aiSvc         ?.Dispose();
            _chatGptSvc    ?.Dispose();
            _weatherSvc    ?.Dispose();
            _mediaSvc      ?.Dispose();
        }
        base.Dispose(disposing);
    }
}

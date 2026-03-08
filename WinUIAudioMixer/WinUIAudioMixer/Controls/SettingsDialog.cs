using System.Text.Json;
using Microsoft.Win32;
using WinUIAudioMixer.Models;
using WinUIAudioMixer.Services;

namespace WinUIAudioMixer.Controls;

/// <summary>Dark-themed settings dialog for all API / config entries.</summary>
public sealed class SettingsDialog : Form
{
    // ── Field references ──────────────────────────────────────────────────────

    private readonly TextBox _discordClientId;
    private readonly TextBox _discordClientSecret;

    private readonly TextBox _aiUrl, _aiKey, _aiWorkspace;

    private readonly TextBox _gptKey, _gptModel;

    private readonly TextBox _haUrl, _haToken, _haLights, _haSensors, _haSwitches;

    private Panel? _calEntriesContainer;
    private readonly List<(TextBox Name, TextBox Url)> _calEntries = new();

    private readonly Label _statusLabel;
    private readonly CheckBox _chkAutoStart;
    private readonly CheckBox _chkCloseToTray;

    // ── Tab state ─────────────────────────────────────────────────────────────

    private readonly Button[] _tabBtns;
    private readonly Panel[]  _tabPanels;

    // ── Constructor ───────────────────────────────────────────────────────────

    public SettingsDialog()
    {
        Text            = "BaumDash – Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        BackColor       = AppTheme.BgDeep;
        ForeColor       = AppTheme.TextPrimary;
        ClientSize      = new Size(520, 504);
        StartPosition   = FormStartPosition.CenterParent;
        MaximizeBox     = false;
        MinimizeBox     = false;
        ShowInTaskbar   = false;

        // ── Tab buttons ───────────────────────────────────────────────────────
        string[] labels = { "GENERAL", "DISCORD", "ANYTHING LLM", "CHATGPT", "HOME ASSISTANT", "CALENDAR" };
        int[]    widths = { 60, 72, 100, 70, 118, 72 };
        _tabBtns   = new Button[6];
        _tabPanels = new Panel[6];

        int tx = 8;
        for (int i = 0; i < 6; i++)
        {
            int idx = i; // capture for lambda
            _tabBtns[i] = new Button
            {
                Text      = labels[i],
                Font      = AppTheme.FontBold,
                ForeColor = i == 0 ? AppTheme.TextPrimary : AppTheme.TextMuted,
                BackColor = i == 0 ? AppTheme.BgCard : Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Cursor    = Cursors.Hand,
                FlatAppearance = { BorderSize = 0, MouseOverBackColor = AppTheme.BgCard },
            };
            _tabBtns[i].SetBounds(tx, 6, widths[i], 26);
            _tabBtns[i].Click += (_, _) => ShowTab(idx);
            tx += widths[i] + 4;

            _tabPanels[i] = new Panel
            {
                BackColor = Color.Transparent,
                Visible   = i == 0,
            };
            _tabPanels[i].SetBounds(0, 40, 520, 420);
        }

        // ── Build tab content ─────────────────────────────────────────────────
        (_chkAutoStart, _chkCloseToTray) = BuildGeneralPanel(_tabPanels[0]);
        (_discordClientId, _discordClientSecret) = BuildDiscordPanel(_tabPanels[1]);
        AddDiscordAuthSection(_tabPanels[1]);
        (_aiUrl, _aiKey, _aiWorkspace) = BuildAiPanel(_tabPanels[2]);
        (_gptKey, _gptModel) = BuildGptPanel(_tabPanels[3]);
        (_haUrl, _haToken, _haLights, _haSensors, _haSwitches) = BuildHaPanel(_tabPanels[4]);
        BuildCalendarPanel(_tabPanels[5]);

        // ── Separator lines ───────────────────────────────────────────────────
        var sep1 = new Panel { BackColor = AppTheme.Border };
        sep1.SetBounds(0, 38, 520, 1);

        var sep2 = new Panel { BackColor = AppTheme.Border };
        sep2.SetBounds(0, 460, 520, 1);

        // ── Buttons ───────────────────────────────────────────────────────────
        var btnExport = MakeButton("Export…", AppTheme.BgCard);
        btnExport.SetBounds(12, 465, 88, 30);
        btnExport.Click += OnExport;

        var btnImport = MakeButton("Import…", AppTheme.BgCard);
        btnImport.SetBounds(106, 465, 88, 30);
        btnImport.Click += OnImport;

        var btnSave = MakeButton("Save", AppTheme.Accent);
        btnSave.SetBounds(316, 465, 88, 30);
        btnSave.Click += OnSave;

        var btnCancel = MakeButton("Cancel", AppTheme.BgCard);
        btnCancel.SetBounds(412, 465, 88, 30);
        btnCancel.Click += (_, _) => Close();

        // ── Status label ──────────────────────────────────────────────────────
        _statusLabel = new Label
        {
            ForeColor = AppTheme.Success,
            BackColor = AppTheme.BgDeep,
            Font      = AppTheme.FontSmall,
            AutoSize  = false,
            Size      = new Size(290, 20),
            Location  = new Point(12, 472),
            Visible   = false,
        };

        // ── Add everything ────────────────────────────────────────────────────
        Controls.AddRange(_tabBtns);
        Controls.AddRange(_tabPanels);
        Controls.Add(sep1);
        Controls.Add(sep2);
        Controls.Add(btnExport);
        Controls.Add(btnImport);
        Controls.Add(btnSave);
        Controls.Add(btnCancel);
        Controls.Add(_statusLabel);
        sep1.BringToFront();
        sep2.BringToFront();

        // ── Load current values ───────────────────────────────────────────────
        LoadAllSettings();
    }

    // ── Tab switching ─────────────────────────────────────────────────────────

    private void ShowTab(int idx)
    {
        for (int i = 0; i < 6; i++)
        {
            _tabPanels[i].Visible    = i == idx;
            _tabBtns[i]  .BackColor  = i == idx ? AppTheme.BgCard     : Color.Transparent;
            _tabBtns[i]  .ForeColor  = i == idx ? AppTheme.TextPrimary : AppTheme.TextMuted;
        }
        _statusLabel.Visible = false;
    }

    // ── Panel builders ────────────────────────────────────────────────────────

    private static (TextBox clientId, TextBox clientSecret) BuildDiscordPanel(Panel p)
    {
        int y = 16;
        AddSectionLabel(p, "CLIENT ID", ref y);
        var id = AddField(p, ref y, placeholder: "paste your Application ID here");
        AddHint(p, "discord.com/developers → New Application → copy Application (Client) ID", ref y);

        AddSectionLabel(p, "CLIENT SECRET", ref y);
        var secret = AddField(p, ref y, placeholder: "paste your Client Secret here");
        AddHint(p, "discord.com/developers → your app → OAuth2 → copy Client Secret", ref y);

        return (id, secret);
    }

    private void AddDiscordAuthSection(Panel p)
    {
        int y = 192; // below CLIENT ID + CLIENT SECRET sections
        AddSectionLabel(p, "AUTHENTICATION", ref y);

        var tokenPath = Path.Combine(AppContext.BaseDirectory, "discord-token.txt");
        var tokenLbl = new Label
        {
            Text      = File.Exists(tokenPath)
                            ? "Saved token exists. Use Reauthorize to force a fresh Discord login popup."
                            : "No saved token — Discord will prompt authorization on next connect.",
            Font      = AppTheme.FontSmall,
            ForeColor = AppTheme.TextMuted,
            BackColor = Color.Transparent,
            AutoSize  = false,
            Location  = new Point(16, y),
            Size      = new Size(476, 28),
        };
        p.Controls.Add(tokenLbl);
        y += 32;

        var btnReauth = MakeButton("Reauthorize Discord", AppTheme.Warning);
        btnReauth.SetBounds(16, y, 170, 30);
        btnReauth.Click += (_, _) =>
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "discord-token.txt");
                if (File.Exists(path)) File.Delete(path);
                tokenLbl.Text      = "Token deleted — reconnect Discord to authorize again.";
                tokenLbl.ForeColor = AppTheme.Success;
                ShowStatus("Token removed — reconnect BaumDash to re-authorize.", success: true);
            }
            catch (Exception ex)
            {
                ShowStatus($"Error: {ex.Message}", success: false);
            }
        };
        p.Controls.Add(btnReauth);
        AddHint(p, "Needed after upgrading or if Discord voice members stop showing.", ref y);
    }

    private static (TextBox url, TextBox key, TextBox workspace) BuildAiPanel(Panel p)
    {
        int y = 16;
        AddSectionLabel(p, "SERVER URL", ref y);
        var url = AddField(p, ref y, placeholder: "http://localhost:3001");
        AddHint(p, "Local: http://localhost:3001   Docker/remote: http://<host>:<port>", ref y);

        AddSectionLabel(p, "API KEY", ref y);
        var key = AddField(p, ref y);
        AddHint(p, "AnythingLLM → ⚙ Settings → Security → API Keys → Generate", ref y);

        AddSectionLabel(p, "WORKSPACE SLUG", ref y);
        var ws = AddField(p, ref y, placeholder: "my-workspace");
        AddHint(p, "From the URL: localhost:3001/workspace/{slug}  (lowercase-hyphenated)", ref y);

        return (url, key, ws);
    }

    private static (TextBox key, TextBox model) BuildGptPanel(Panel p)
    {
        int y = 16;
        AddSectionLabel(p, "API KEY", ref y);
        var key = AddField(p, ref y, placeholder: "sk-…");
        AddHint(p, "platform.openai.com → API Keys → Create new secret key", ref y);

        AddSectionLabel(p, "MODEL", ref y);
        var model = AddField(p, ref y, placeholder: "gpt-4o");
        AddHint(p, "Options: gpt-4o  •  gpt-4o-mini  •  gpt-4-turbo  •  gpt-3.5-turbo", ref y);

        return (key, model);
    }

    private static (TextBox url, TextBox token, TextBox lights, TextBox sensors, TextBox switches) BuildHaPanel(Panel p)
    {
        int y = 16;
        AddSectionLabel(p, "SERVER URL", ref y);
        var url = AddField(p, ref y, placeholder: "https://xxxxx.ui.nabu.casa");

        AddSectionLabel(p, "LONG-LIVED ACCESS TOKEN", ref y);
        var token = AddField(p, ref y);
        AddHint(p, "HA → Profile → Long-Lived Access Tokens → Create Token", ref y);

        AddSectionLabel(p, "LIGHTS  (entity_id = Display Name, one per line)", ref y);
        var lights = AddField(p, ref y, multiline: true, height: 56);
        y += 4;

        AddSectionLabel(p, "SWITCHES  (entity_id = Display Name, one per line)", ref y);
        var switches = AddField(p, ref y, multiline: true, height: 56);
        y += 4;

        AddSectionLabel(p, "SENSORS  (entity_id = Display Name, one per line)", ref y);
        var sensors = AddField(p, ref y, multiline: true, height: 56);

        return (url, token, lights, sensors, switches);
    }

    private void BuildCalendarPanel(Panel p)
    {
        int y = 16;
        AddSectionLabel(p, "GOOGLE CALENDARS", ref y);
        AddHint(p, "Google Calendar → ⋮ → Settings → Integrate calendar → Secret address in iCal format", ref y);

        // Scrollable container for calendar entry rows
        _calEntriesContainer = new Panel
        {
            AutoScroll = true,
            BackColor  = Color.Transparent,
            Location   = new Point(0, y),
            Size       = new Size(508, 300),
        };
        p.Controls.Add(_calEntriesContainer);

        var btnAdd = new Button
        {
            Text      = "+ Add Calendar",
            Font      = AppTheme.FontBold,
            ForeColor = AppTheme.TextPrimary,
            BackColor = AppTheme.BgCard,
            FlatStyle = FlatStyle.Flat,
            Cursor    = Cursors.Hand,
            Location  = new Point(16, y + 308),
            Size      = new Size(140, 28),
            FlatAppearance = { BorderSize = 0, MouseOverBackColor = AppTheme.Accent },
        };
        btnAdd.Click += (_, _) => AddCalendarRow("", "");
        p.Controls.Add(btnAdd);
    }

    private void AddCalendarRow(string name, string url)
    {
        if (_calEntriesContainer == null) return;
        int rowY = _calEntries.Count * 62;

        var nameBox = new TextBox
        {
            BackColor       = AppTheme.BgCard,
            ForeColor       = AppTheme.TextPrimary,
            Font            = AppTheme.FontLabel,
            BorderStyle     = BorderStyle.FixedSingle,
            PlaceholderText = "Name (e.g. Work)",
            Text            = name,
            Location        = new Point(16, rowY),
            Size            = new Size(130, 26),
        };

        var urlBox = new TextBox
        {
            BackColor       = AppTheme.BgCard,
            ForeColor       = AppTheme.TextPrimary,
            Font            = AppTheme.FontLabel,
            BorderStyle     = BorderStyle.FixedSingle,
            PlaceholderText = "https://calendar.google.com/calendar/ical/…/basic.ics",
            Text            = url,
            Location        = new Point(152, rowY),
            Size            = new Size(300, 26),
        };

        var btnRemove = new Button
        {
            Text      = "✕",
            Font      = AppTheme.FontBold,
            ForeColor = AppTheme.TextMuted,
            BackColor = Color.Transparent,
            FlatStyle = FlatStyle.Flat,
            Cursor    = Cursors.Hand,
            Location  = new Point(458, rowY),
            Size      = new Size(28, 26),
            FlatAppearance = { BorderSize = 0, MouseOverBackColor = AppTheme.Danger },
        };

        var entry = (nameBox, urlBox);
        _calEntries.Add(entry);
        btnRemove.Click += (_, _) =>
        {
            _calEntries.Remove(entry);
            _calEntriesContainer.Controls.Remove(nameBox);
            _calEntriesContainer.Controls.Remove(urlBox);
            _calEntriesContainer.Controls.Remove(btnRemove);
            RelayoutCalendarRows();
        };

        _calEntriesContainer.Controls.AddRange(new Control[] { nameBox, urlBox, btnRemove });
        _calEntriesContainer.AutoScrollMinSize = new Size(0, (_calEntries.Count) * 62);
    }

    private void RelayoutCalendarRows()
    {
        if (_calEntriesContainer == null) return;
        for (int i = 0; i < _calEntries.Count; i++)
        {
            int rowY = i * 62;
            _calEntries[i].Name.Location = new Point(16,  rowY);
            _calEntries[i].Url .Location = new Point(152, rowY);
            // Remove button is the 3rd control per row — reposition by index
            var btn = _calEntriesContainer.Controls
                .OfType<Button>()
                .ElementAtOrDefault(i);
            if (btn != null) btn.Location = new Point(458, rowY);
        }
        _calEntriesContainer.AutoScrollMinSize = new Size(0, _calEntries.Count * 62);
    }

    private static (CheckBox autoStart, CheckBox closeToTray) BuildGeneralPanel(Panel p)
    {
        int y = 16;
        AddSectionLabel(p, "WINDOWS STARTUP", ref y);

        var chkAutoStart = new CheckBox
        {
            Text      = "Launch BaumDash when Windows starts",
            Font      = AppTheme.FontLabel,
            ForeColor = AppTheme.TextPrimary,
            BackColor = Color.Transparent,
            AutoSize  = true,
            Cursor    = Cursors.Hand,
            Location  = new Point(16, y),
        };
        p.Controls.Add(chkAutoStart);
        y += chkAutoStart.PreferredSize.Height + 4;

        AddHint(p, "Adds BaumDash to HKCU\\...\\Run so it starts automatically on login.", ref y);

        AddSectionLabel(p, "WINDOW BEHAVIOUR", ref y);

        var chkCloseToTray = new CheckBox
        {
            Text      = "Minimize and close to system tray instead of exiting",
            Font      = AppTheme.FontLabel,
            ForeColor = AppTheme.TextPrimary,
            BackColor = Color.Transparent,
            AutoSize  = true,
            Cursor    = Cursors.Hand,
            Location  = new Point(16, y),
        };
        p.Controls.Add(chkCloseToTray);
        y += chkCloseToTray.PreferredSize.Height + 4;

        AddHint(p, "When off, the ✕ button exits BaumDash completely.", ref y);

        return (chkAutoStart, chkCloseToTray);
    }

    // ── UI helpers ────────────────────────────────────────────────────────────

    private static void AddSectionLabel(Panel p, string text, ref int y)
    {
        var lbl = new Label
        {
            Text      = text,
            Font      = AppTheme.FontSectionHeader,
            ForeColor = AppTheme.TextMuted,
            BackColor = Color.Transparent,
            AutoSize  = true,
            Location  = new Point(16, y),
        };
        p.Controls.Add(lbl);
        y += 18;
    }

    private static TextBox AddField(Panel p, ref int y, string placeholder = "",
                                    bool multiline = false, int height = 28)
    {
        var txt = new TextBox
        {
            BackColor       = AppTheme.BgCard,
            ForeColor       = AppTheme.TextPrimary,
            Font            = AppTheme.FontLabel,
            BorderStyle     = BorderStyle.FixedSingle,
            Multiline       = multiline,
            ScrollBars      = multiline ? ScrollBars.Vertical : ScrollBars.None,
            PlaceholderText = placeholder,
            Location        = new Point(16, y),
            Size            = new Size(476, height),
        };
        p.Controls.Add(txt);
        y += height + 6;
        return txt;
    }

    private static void AddHint(Panel p, string text, ref int y)
    {
        var lbl = new Label
        {
            Text      = text,
            Font      = AppTheme.FontSmall,
            ForeColor = AppTheme.TextMuted,
            BackColor = Color.Transparent,
            AutoSize  = false,
            Location  = new Point(16, y),
            Size      = new Size(476, 16),
        };
        p.Controls.Add(lbl);
        y += 26;
    }

    private static Button MakeButton(string text, Color bg) => new()
    {
        Text      = text,
        Font      = AppTheme.FontBold,
        ForeColor = Color.White,
        BackColor = bg,
        FlatStyle = FlatStyle.Flat,
        Cursor    = Cursors.Hand,
        FlatAppearance = { BorderSize = 0 },
    };

    // ── Load / Save ───────────────────────────────────────────────────────────

    private void LoadAllSettings()
    {
        // Discord
        var discordPath = Path.Combine(AppContext.BaseDirectory, "discord-client-id.txt");
        if (File.Exists(discordPath))
            _discordClientId.Text = File.ReadAllText(discordPath).Trim();

        var secretPath2 = Path.Combine(AppContext.BaseDirectory, "discord-client-secret.txt");
        if (File.Exists(secretPath2))
            _discordClientSecret.Text = File.ReadAllText(secretPath2).Trim();

        // AnythingLLM
        try
        {
            var cfg = AnythingLLMService.LoadConfig();
            if (cfg != null)
            {
                _aiUrl      .Text = cfg.Url;
                _aiKey      .Text = cfg.ApiKey;
                _aiWorkspace.Text = cfg.Workspace;
            }
        }
        catch { }

        // ChatGPT
        try
        {
            var cfg = ChatGptService.LoadConfig();
            if (cfg != null)
            {
                _gptKey  .Text = cfg.ApiKey;
                _gptModel.Text = cfg.Model;
            }
        }
        catch { }

        // Home Assistant
        try
        {
            var cfg = HomeAssistantService.LoadConfig();
            if (cfg != null)
            {
                _haUrl      .Text = cfg.Url;
                _haToken    .Text = cfg.Token;
                _haLights   .Text = string.Join("\r\n", cfg.Lights  .Select(e => $"{e.Id} = {e.Name}"));
                _haSwitches .Text = string.Join("\r\n", cfg.Switches.Select(e => $"{e.Id} = {e.Name}"));
                _haSensors  .Text = string.Join("\r\n", cfg.Sensors .Select(e => $"{e.Id} = {e.Name}"));
            }
        }
        catch { }

        // Google Calendar
        try
        {
            var cfg = GoogleCalendarService.LoadConfig();
            if (cfg != null)
            {
                _calEntries.Clear();
                _calEntriesContainer?.Controls.Clear();
                foreach (var entry in cfg.Calendars)
                    AddCalendarRow(entry.Name, entry.ICalUrl);
            }
        }
        catch { }

        // General
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "general-config.json");
            if (File.Exists(path))
            {
                var cfg = System.Text.Json.JsonSerializer.Deserialize<WinUIAudioMixer.Models.GeneralConfig>(
                    File.ReadAllText(path),
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (cfg != null) _chkCloseToTray.Checked = cfg.CloseToTray;
            }
            else
            {
                _chkCloseToTray.Checked = true; // default on
            }
        }
        catch { _chkCloseToTray.Checked = true; }

        // Startup
        _chkAutoStart.Checked = IsAutoStartEnabled();
    }

    private void OnSave(object? sender, EventArgs e)
    {
        try
        {
            var dir = AppContext.BaseDirectory;
            var opts = new JsonSerializerOptions { WriteIndented = true };

            // Discord
            File.WriteAllText(Path.Combine(dir, "discord-client-id.txt"),
                _discordClientId.Text.Trim());

            var secretVal = _discordClientSecret.Text.Trim();
            if (!string.IsNullOrEmpty(secretVal))
                File.WriteAllText(Path.Combine(dir, "discord-client-secret.txt"), secretVal);

            // AnythingLLM
            File.WriteAllText(Path.Combine(dir, "anythingllm-config.json"),
                JsonSerializer.Serialize(new AnythingLLMConfig
                {
                    Url       = _aiUrl      .Text.Trim(),
                    ApiKey    = _aiKey      .Text.Trim(),
                    Workspace = _aiWorkspace.Text.Trim(),
                }, opts));

            // ChatGPT
            File.WriteAllText(Path.Combine(dir, "chatgpt-config.json"),
                JsonSerializer.Serialize(new ChatGptConfig
                {
                    ApiKey = _gptKey.Text.Trim(),
                    Model  = string.IsNullOrWhiteSpace(_gptModel.Text) ? "gpt-4o" : _gptModel.Text.Trim(),
                }, opts));

            // Home Assistant
            File.WriteAllText(Path.Combine(dir, "ha-config.json"),
                JsonSerializer.Serialize(new HaConfig
                {
                    Url      = _haUrl    .Text.Trim(),
                    Token    = _haToken  .Text.Trim(),
                    Lights   = ParseEntities(_haLights  .Text),
                    Switches = ParseEntities(_haSwitches.Text),
                    Sensors  = ParseEntities(_haSensors .Text),
                }, opts));

            // Google Calendar
            File.WriteAllText(Path.Combine(dir, "gcalendar-config.json"),
                JsonSerializer.Serialize(new GoogleCalendarConfig
                {
                    Calendars = _calEntries
                        .Where(e => !string.IsNullOrWhiteSpace(e.Url.Text))
                        .Select(e => new CalendarEntry
                        {
                            Name    = string.IsNullOrWhiteSpace(e.Name.Text) ? "Calendar" : e.Name.Text.Trim(),
                            ICalUrl = e.Url.Text.Trim().Replace("webcal://", "https://"),
                        })
                        .ToList(),
                }, opts));

            // General
            File.WriteAllText(Path.Combine(dir, "general-config.json"),
                JsonSerializer.Serialize(new WinUIAudioMixer.Models.GeneralConfig
                {
                    CloseToTray = _chkCloseToTray.Checked,
                }, opts));

            // Startup
            SetAutoStart(_chkAutoStart.Checked);

            ShowStatus("Saved — restart BaumDash to apply changes.", success: true);
        }
        catch (Exception ex)
        {
            ShowStatus($"Error: {ex.Message}", success: false);
        }
    }

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("BaumDash") != null;
        }
        catch { return false; }
    }

    private static void SetAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)!;
            if (enable)
                key.SetValue("BaumDash", $"\"{Application.ExecutablePath}\"");
            else
                key.DeleteValue("BaumDash", throwOnMissingValue: false);
        }
        catch { }
    }

    // ── Export / Import ───────────────────────────────────────────────────────

    // Files bundled into the backup archive
    private static readonly string[] BackupFiles =
    {
        "discord-client-id.txt",
        "anythingllm-config.json",
        "chatgpt-config.json",
        "ha-config.json",
        "gcalendar-config.json",
        "app-shortcuts.json",
        "general-config.json",
    };

    private void OnExport(object? sender, EventArgs e)
    {
        using var dlg = new SaveFileDialog
        {
            Title      = "Export BaumDash Settings",
            Filter     = "BaumDash Backup (*.baumdash-backup)|*.baumdash-backup|JSON files (*.json)|*.json",
            FileName   = $"BaumDash-Backup-{DateTime.Now:yyyy-MM-dd}",
            DefaultExt = "baumdash-backup",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var dir     = AppContext.BaseDirectory;
            var backup  = new Dictionary<string, string?>();
            foreach (var file in BackupFiles)
            {
                var path = Path.Combine(dir, file);
                backup[file] = File.Exists(path) ? File.ReadAllText(path) : null;
            }

            var opts = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(backup, opts));

            ShowStatus($"Exported to {Path.GetFileName(dlg.FileName)}", success: true);
        }
        catch (Exception ex)
        {
            ShowStatus($"Export failed: {ex.Message}", success: false);
        }
    }

    private void OnImport(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title  = "Import BaumDash Settings",
            Filter = "BaumDash Backup (*.baumdash-backup)|*.baumdash-backup|JSON files (*.json)|*.json",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var json    = File.ReadAllText(dlg.FileName);
            var backup  = JsonSerializer.Deserialize<Dictionary<string, string?>>(json)
                          ?? throw new InvalidDataException("Invalid backup file.");

            var dir = AppContext.BaseDirectory;
            foreach (var (file, content) in backup)
            {
                if (content == null) continue;
                // Safety: only restore known config files, no path traversal
                if (!BackupFiles.Contains(file)) continue;
                File.WriteAllText(Path.Combine(dir, file), content);
            }

            // Signal Program.cs to restart to tray after the mutex is released
            MainForm.PendingImportRestart = true;
            Application.Exit();
        }
        catch (Exception ex)
        {
            ShowStatus($"Import failed: {ex.Message}", success: false);
        }
    }

    private void ShowStatus(string msg, bool success)
    {
        _statusLabel.ForeColor = success ? AppTheme.Success : AppTheme.Danger;
        _statusLabel.Text      = msg;
        _statusLabel.Visible   = true;
    }

    private static List<HaEntity> ParseEntities(string text)
    {
        var result = new List<HaEntity>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (string.IsNullOrEmpty(line)) continue;
            var sep = line.IndexOf('=');
            if (sep > 0)
            {
                var id   = line[..sep].Trim();
                var name = line[(sep + 1)..].Trim();
                if (!string.IsNullOrEmpty(id))
                    result.Add(new HaEntity { Id = id, Name = string.IsNullOrEmpty(name) ? id : name });
            }
            else
            {
                result.Add(new HaEntity { Id = line, Name = line });
            }
        }
        return result;
    }
}

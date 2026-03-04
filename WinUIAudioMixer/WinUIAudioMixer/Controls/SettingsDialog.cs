using System.Text.Json;
using WinUIAudioMixer.Models;
using WinUIAudioMixer.Services;

namespace WinUIAudioMixer.Controls;

/// <summary>Dark-themed settings dialog for all API / config entries.</summary>
public sealed class SettingsDialog : Form
{
    // ── Field references ──────────────────────────────────────────────────────

    private readonly TextBox _discordClientId;

    private readonly TextBox _aiUrl, _aiKey, _aiWorkspace;

    private readonly TextBox _gptKey, _gptModel;

    private readonly TextBox _haUrl, _haToken, _haLights, _haSensors;

    private readonly Label _statusLabel;

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
        string[] labels = { "DISCORD", "ANYTHING LLM", "CHATGPT", "HOME ASSISTANT" };
        int[]    widths = { 80, 110, 80, 128 };
        _tabBtns   = new Button[4];
        _tabPanels = new Panel[4];

        int tx = 8;
        for (int i = 0; i < 4; i++)
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
        _discordClientId = BuildDiscordPanel(_tabPanels[0]);
        (_aiUrl, _aiKey, _aiWorkspace) = BuildAiPanel(_tabPanels[1]);
        (_gptKey, _gptModel) = BuildGptPanel(_tabPanels[2]);
        (_haUrl, _haToken, _haLights, _haSensors) = BuildHaPanel(_tabPanels[3]);

        // ── Separator lines ───────────────────────────────────────────────────
        var sep1 = new Panel { BackColor = AppTheme.Border };
        sep1.SetBounds(0, 38, 520, 1);

        var sep2 = new Panel { BackColor = AppTheme.Border };
        sep2.SetBounds(0, 460, 520, 1);

        // ── Buttons ───────────────────────────────────────────────────────────
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
        for (int i = 0; i < 4; i++)
        {
            _tabPanels[i].Visible    = i == idx;
            _tabBtns[i]  .BackColor  = i == idx ? AppTheme.BgCard     : Color.Transparent;
            _tabBtns[i]  .ForeColor  = i == idx ? AppTheme.TextPrimary : AppTheme.TextMuted;
        }
        _statusLabel.Visible = false;
    }

    // ── Panel builders ────────────────────────────────────────────────────────

    private static TextBox BuildDiscordPanel(Panel p)
    {
        int y = 16;
        AddSectionLabel(p, "CLIENT ID", ref y);
        var txt = AddField(p, ref y, placeholder: "paste your Application ID here");
        AddHint(p, "discord.com/developers → New Application → copy Application (Client) ID", ref y);
        return txt;
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

    private static (TextBox url, TextBox token, TextBox lights, TextBox sensors) BuildHaPanel(Panel p)
    {
        int y = 16;
        AddSectionLabel(p, "SERVER URL", ref y);
        var url = AddField(p, ref y, placeholder: "https://xxxxx.ui.nabu.casa");

        AddSectionLabel(p, "LONG-LIVED ACCESS TOKEN", ref y);
        var token = AddField(p, ref y);
        AddHint(p, "HA → Profile → Long-Lived Access Tokens → Create Token", ref y);

        AddSectionLabel(p, "LIGHTS  (entity_id = Display Name, one per line)", ref y);
        var lights = AddField(p, ref y, multiline: true, height: 68);
        y += 4;

        AddSectionLabel(p, "SENSORS  (entity_id = Display Name, one per line)", ref y);
        var sensors = AddField(p, ref y, multiline: true, height: 68);

        return (url, token, lights, sensors);
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
                _haUrl    .Text = cfg.Url;
                _haToken  .Text = cfg.Token;
                _haLights .Text = string.Join("\r\n", cfg.Lights .Select(e => $"{e.Id} = {e.Name}"));
                _haSensors.Text = string.Join("\r\n", cfg.Sensors.Select(e => $"{e.Id} = {e.Name}"));
            }
        }
        catch { }
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
                    Url     = _haUrl  .Text.Trim(),
                    Token   = _haToken.Text.Trim(),
                    Lights  = ParseEntities(_haLights .Text),
                    Sensors = ParseEntities(_haSensors.Text),
                }, opts));

            _statusLabel.ForeColor = AppTheme.Success;
            _statusLabel.Text      = "Saved — restart BaumDash to apply changes.";
            _statusLabel.Visible   = true;
        }
        catch (Exception ex)
        {
            _statusLabel.ForeColor = AppTheme.Danger;
            _statusLabel.Text      = $"Error: {ex.Message}";
            _statusLabel.Visible   = true;
        }
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

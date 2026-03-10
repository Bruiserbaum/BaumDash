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

    // GPU platform
    private RadioButton? _rbGpuAmd, _rbGpuNvidia;

    // Layout profile
    private RadioButton? _rbLayoutAuto, _rbLayout1920, _rbLayout2560;

    // Appearance
    private RadioButton? _rbDark, _rbLight, _rbCustom;
    private Button?      _btnAccentPicker;
    private Color        _customAccent = AppTheme.Accent;
    private TextBox?     _bgPathBox;
    private ComboBox?    _bgModeCombo;
    private TrackBar?    _bgAlphaSlider;
    private Label?       _bgAlphaValueLabel;

    // Weather
    private TextBox?  _weatherCity;
    private Label?    _weatherGeoStatus;
    private TextBox?  _weatherLat;
    private TextBox?  _weatherLon;
    private ComboBox? _weatherUnit;

    // ── Tab state ─────────────────────────────────────────────────────────────

    private readonly Button[] _tabBtns;
    private readonly Panel[]  _tabPanels;

    // ── Layout constants ──────────────────────────────────────────────────────

    private const int DlgW      = 680;  // dialog client width
    private const int FieldW    = 636;  // width of a full-row text field (DlgW - 16*2 - 12)
    private const int FieldX    = 16;   // left margin for controls
    private const int TabCount  = 7;

    // ── Constructor ───────────────────────────────────────────────────────────

    public SettingsDialog()
    {
        Text            = "BaumDash – Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        BackColor       = AppTheme.BgDeep;
        ForeColor       = AppTheme.TextPrimary;
        ClientSize      = new Size(DlgW, 646);
        StartPosition   = FormStartPosition.CenterParent;
        MaximizeBox     = false;
        MinimizeBox     = false;
        ShowInTaskbar   = false;
        Load           += (_, _) => AppTheme.ApplyDarkTitleBar(Handle);

        // ── Tab buttons ───────────────────────────────────────────────────────
        string[] labels = { "GENERAL", "WEATHER", "DISCORD", "ANYTHING LLM", "CHATGPT", "HOME ASSISTANT", "CALENDAR" };
        int[]    widths = { 78, 80, 80, 112, 80, 132, 82 };
        _tabBtns   = new Button[TabCount];
        _tabPanels = new Panel[TabCount];

        int tx = 8;
        for (int i = 0; i < TabCount; i++)
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
            _tabPanels[i].SetBounds(0, 40, DlgW, 560);
        }

        // ── Build tab content ─────────────────────────────────────────────────
        (_chkAutoStart, _chkCloseToTray) = BuildGeneralPanel(_tabPanels[0]);
        (_weatherLat, _weatherLon, _weatherUnit) = BuildWeatherPanel(_tabPanels[1]);
        (_discordClientId, _discordClientSecret) = BuildDiscordPanel(_tabPanels[2]);
        AddDiscordAuthSection(_tabPanels[2]);
        (_aiUrl, _aiKey, _aiWorkspace) = BuildAiPanel(_tabPanels[3]);
        (_gptKey, _gptModel) = BuildGptPanel(_tabPanels[4]);
        (_haUrl, _haToken, _haLights, _haSensors, _haSwitches) = BuildHaPanel(_tabPanels[5]);
        BuildCalendarPanel(_tabPanels[6]);

        // ── Separator lines ───────────────────────────────────────────────────
        var sep1 = new Panel { BackColor = AppTheme.Border };
        sep1.SetBounds(0, 38, DlgW, 1);

        var sep2 = new Panel { BackColor = AppTheme.Border };
        sep2.SetBounds(0, 602, DlgW, 1);

        // ── Buttons ───────────────────────────────────────────────────────────
        var btnExport = MakeButton("Export…", AppTheme.BgCard);
        btnExport.SetBounds(12, 608, 88, 30);
        btnExport.Click += OnExport;

        var btnImport = MakeButton("Import…", AppTheme.BgCard);
        btnImport.SetBounds(106, 608, 88, 30);
        btnImport.Click += OnImport;

        var btnSave = MakeButton("Save", AppTheme.Accent);
        btnSave.SetBounds(DlgW - 204, 608, 88, 30);
        btnSave.Click += OnSave;

        var btnCancel = MakeButton("Cancel", AppTheme.BgCard);
        btnCancel.SetBounds(DlgW - 108, 608, 88, 30);
        btnCancel.Click += (_, _) => Close();

        // ── Status label ──────────────────────────────────────────────────────
        _statusLabel = new Label
        {
            ForeColor = AppTheme.Success,
            BackColor = AppTheme.BgDeep,
            Font      = AppTheme.FontSmall,
            AutoSize  = false,
            // Sits between the Import button (ends ~206) and the Save button (starts at DlgW-204)
            Location  = new Point(210, 614),
            Size      = new Size(DlgW - 210 - 212, 20),
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
        for (int i = 0; i < TabCount; i++)
        {
            _tabPanels[i].Visible    = i == idx;
            _tabBtns[i]  .BackColor  = i == idx ? AppTheme.BgCard     : Color.Transparent;
            _tabBtns[i]  .ForeColor  = i == idx ? AppTheme.TextPrimary : AppTheme.TextMuted;
        }
        _statusLabel.Visible = false;
    }

    // ── Panel builders ────────────────────────────────────────────────────────

    private (TextBox lat, TextBox lon, ComboBox unit) BuildWeatherPanel(Panel p)
    {
        int y = 16;

        // ── City / State lookup ───────────────────────────────────────────────
        AddSectionLabel(p, "CITY LOOKUP", ref y);
        AddHint(p, "Enter a city name (and optionally state/country) to auto-fill coordinates.", ref y);

        var cityLbl = new Label
        {
            Text      = "City:",
            Font      = AppTheme.FontLabel,
            ForeColor = AppTheme.TextSecondary,
            BackColor = Color.Transparent,
            AutoSize  = true,
            Location  = new Point(FieldX, y + 5),
        };
        _weatherCity = new TextBox
        {
            BackColor       = AppTheme.BgCard,
            ForeColor       = AppTheme.TextPrimary,
            Font            = AppTheme.FontLabel,
            BorderStyle     = BorderStyle.FixedSingle,
            PlaceholderText = "e.g. Seattle, WA   or   London, UK",
            Location        = new Point(FieldX + 42, y),
            Size            = new Size(FieldW - 42 - 90 - 8, 26),
        };
        var btnLookup = new Button
        {
            Text      = "Look Up",
            Font      = AppTheme.FontBold,
            ForeColor = AppTheme.TextPrimary,
            BackColor = AppTheme.BgCard,
            FlatStyle = FlatStyle.Flat,
            Cursor    = Cursors.Hand,
            Location  = new Point(FieldX + 42 + _weatherCity.Width + 8, y),
            Size      = new Size(90, 26),
            FlatAppearance = { BorderSize = 0, MouseOverBackColor = AppTheme.Accent },
        };
        p.Controls.AddRange(new Control[] { cityLbl, _weatherCity, btnLookup });
        y += 32;

        // Geocoding result label
        _weatherGeoStatus = new Label
        {
            Text      = "",
            Font      = AppTheme.FontSmall,
            ForeColor = AppTheme.TextMuted,
            BackColor = Color.Transparent,
            AutoSize  = false,
            Location  = new Point(FieldX, y),
            Size      = new Size(FieldW, 16),
        };
        p.Controls.Add(_weatherGeoStatus);
        y += 24;

        // ── Manual coordinates ────────────────────────────────────────────────
        AddSectionLabel(p, "COORDINATES  (auto-filled by Look Up, or enter manually)", ref y);

        var latLbl = new Label
        {
            Text      = "Latitude:",
            Font      = AppTheme.FontLabel,
            ForeColor = AppTheme.TextSecondary,
            BackColor = Color.Transparent,
            AutoSize  = true,
            Location  = new Point(FieldX, y + 5),
        };
        var latBox = new TextBox
        {
            BackColor       = AppTheme.BgCard,
            ForeColor       = AppTheme.TextPrimary,
            Font            = AppTheme.FontLabel,
            BorderStyle     = BorderStyle.FixedSingle,
            PlaceholderText = "e.g. 40.7128",
            Location        = new Point(FieldX + 76, y),
            Size            = new Size(180, 26),
        };

        var lonLbl = new Label
        {
            Text      = "Longitude:",
            Font      = AppTheme.FontLabel,
            ForeColor = AppTheme.TextSecondary,
            BackColor = Color.Transparent,
            AutoSize  = true,
            Location  = new Point(FieldX + 276, y + 5),
        };
        var lonBox = new TextBox
        {
            BackColor       = AppTheme.BgCard,
            ForeColor       = AppTheme.TextPrimary,
            Font            = AppTheme.FontLabel,
            BorderStyle     = BorderStyle.FixedSingle,
            PlaceholderText = "e.g. -74.0060",
            Location        = new Point(FieldX + 364, y),
            Size            = new Size(180, 26),
        };

        p.Controls.AddRange(new Control[] { latLbl, latBox, lonLbl, lonBox });
        y += 32;
        AddHint(p, "Manual lookup: Google Maps → right-click a location → 'What's here?'", ref y);

        // Wire up the Look Up button now that latBox/lonBox exist
        btnLookup.Click += async (_, _) =>
        {
            var query = _weatherCity?.Text.Trim() ?? "";
            if (string.IsNullOrEmpty(query)) return;

            btnLookup.Enabled = false;
            btnLookup.Text    = "…";
            if (_weatherGeoStatus != null)
            {
                _weatherGeoStatus.ForeColor = AppTheme.TextMuted;
                _weatherGeoStatus.Text      = "Looking up…";
            }

            try
            {
                var encoded = Uri.EscapeDataString(query);
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("BaumDash/2.2.1");

                // ── Primary: Nominatim (OpenStreetMap) — full global + small-town coverage ──
                double lat = 0, lon = 0;
                string foundLabel = "";
                bool found = false;

                try
                {
                    var nominatimUrl = $"https://nominatim.openstreetmap.org/search?q={encoded}&format=json&limit=5&addressdetails=1";
                    var json = await http.GetStringAsync(nominatimUrl);
                    using var doc = JsonDocument.Parse(json);
                    var arr = doc.RootElement;
                    if (arr.GetArrayLength() > 0)
                    {
                        var first = arr[0];
                        lat = double.Parse(first.GetProperty("lat").GetString() ?? "0",
                            System.Globalization.CultureInfo.InvariantCulture);
                        lon = double.Parse(first.GetProperty("lon").GetString() ?? "0",
                            System.Globalization.CultureInfo.InvariantCulture);
                        foundLabel = first.TryGetProperty("display_name", out var dn) ? dn.GetString() ?? "" : query;
                        // Trim to first two comma-segments for readability
                        var parts = foundLabel.Split(',');
                        foundLabel = string.Join(", ", parts.Take(Math.Min(3, parts.Length)).Select(s => s.Trim()));
                        found = true;
                    }
                }
                catch { /* Nominatim failed — fall through to Open-Meteo */ }

                // ── Fallback: Open-Meteo geocoding ─────────────────────────────────────────
                if (!found)
                {
                    var omUrl = $"https://geocoding-api.open-meteo.com/v1/search?name={encoded}&count=5&language=en&format=json";
                    var json  = await http.GetStringAsync(omUrl);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
                    {
                        var first  = results[0];
                        lat        = first.GetProperty("latitude").GetDouble();
                        lon        = first.GetProperty("longitude").GetDouble();
                        string nm  = first.TryGetProperty("name",    out var n) ? n.GetString() ?? "" : "";
                        string a1  = first.TryGetProperty("admin1",  out var a) ? a.GetString() ?? "" : "";
                        string co  = first.TryGetProperty("country", out var c) ? c.GetString() ?? "" : "";
                        foundLabel = string.Join(", ", new[] { nm, a1, co }.Where(s => !string.IsNullOrEmpty(s)));
                        found      = true;
                    }
                }

                if (!found)
                {
                    if (_weatherGeoStatus != null)
                    {
                        _weatherGeoStatus.ForeColor = AppTheme.Danger;
                        _weatherGeoStatus.Text      = $"No results for \"{query}\". Try \"City, State\" or \"City, Country\".";
                    }
                    return;
                }

                latBox.Text = lat.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
                lonBox.Text = lon.ToString("G", System.Globalization.CultureInfo.InvariantCulture);

                if (_weatherGeoStatus != null)
                {
                    _weatherGeoStatus.ForeColor = AppTheme.Success;
                    _weatherGeoStatus.Text      = $"Found: {foundLabel}  ({lat:F4}, {lon:F4})";
                }
            }
            catch (Exception ex)
            {
                if (_weatherGeoStatus != null)
                {
                    _weatherGeoStatus.ForeColor = AppTheme.Danger;
                    _weatherGeoStatus.Text      = $"Lookup failed: {ex.Message}";
                }
            }
            finally
            {
                btnLookup.Enabled = true;
                btnLookup.Text    = "Look Up";
            }
        };

        // ── Temperature unit ──────────────────────────────────────────────────
        y += 4;
        AddSectionLabel(p, "TEMPERATURE UNIT", ref y);

        var unitLbl = new Label
        {
            Text      = "Display temperatures in:",
            Font      = AppTheme.FontLabel,
            ForeColor = AppTheme.TextSecondary,
            BackColor = Color.Transparent,
            AutoSize  = true,
            Location  = new Point(FieldX, y + 4),
        };
        var unitCombo = new ComboBox
        {
            DropDownStyle   = ComboBoxStyle.DropDownList,
            BackColor       = AppTheme.BgCard,
            ForeColor       = AppTheme.TextPrimary,
            Font            = AppTheme.FontLabel,
            Location        = new Point(FieldX + 192, y),
            Size            = new Size(180, 26),
        };
        unitCombo.Items.AddRange(new object[] { "Fahrenheit (°F)", "Celsius (°C)" });
        unitCombo.SelectedIndex = 0;
        p.Controls.AddRange(new Control[] { unitLbl, unitCombo });
        y += 34;

        AddHint(p, "Save and restart BaumDash for weather changes to take effect.", ref y);

        return (latBox, lonBox, unitCombo);
    }

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
            Location  = new Point(FieldX, y),
            Size      = new Size(FieldW, 28),
        };
        p.Controls.Add(tokenLbl);
        y += 32;

        var btnReauth = MakeButton("Reauthorize Discord", AppTheme.Warning);
        btnReauth.SetBounds(FieldX, y, 170, 30);
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
            Size       = new Size(DlgW - 12, 300),
        };
        AppTheme.ApplyDarkScrollBar(_calEntriesContainer);
        p.Controls.Add(_calEntriesContainer);

        var btnAdd = new Button
        {
            Text      = "+ Add Calendar",
            Font      = AppTheme.FontBold,
            ForeColor = AppTheme.TextPrimary,
            BackColor = AppTheme.BgCard,
            FlatStyle = FlatStyle.Flat,
            Cursor    = Cursors.Hand,
            Location  = new Point(FieldX, y + 308),
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
            Location        = new Point(FieldX, rowY),
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
            Size            = new Size(DlgW - 152 - 50, 26),
        };

        var btnRemove = new Button
        {
            Text      = "✕",
            Font      = AppTheme.FontBold,
            ForeColor = AppTheme.TextMuted,
            BackColor = Color.Transparent,
            FlatStyle = FlatStyle.Flat,
            Cursor    = Cursors.Hand,
            Location  = new Point(DlgW - 48, rowY),
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
            _calEntries[i].Name.Location = new Point(FieldX, rowY);
            _calEntries[i].Url .Location = new Point(152, rowY);
            var btn = _calEntriesContainer.Controls
                .OfType<Button>()
                .ElementAtOrDefault(i);
            if (btn != null) btn.Location = new Point(DlgW - 48, rowY);
        }
        _calEntriesContainer.AutoScrollMinSize = new Size(0, _calEntries.Count * 62);
    }

    private (CheckBox autoStart, CheckBox closeToTray) BuildGeneralPanel(Panel p)
    {
        var scroll = new Panel
        {
            AutoScroll = true,
            Location   = new Point(0, 0),
            Size       = new Size(DlgW, 560),
            BackColor  = Color.Transparent,
        };
        AppTheme.ApplyDarkScrollBar(scroll);
        p.Controls.Add(scroll);

        int y = 8;

        // ── Application / Updates ─────────────────────────────────────────────
        AddSectionLabel(scroll, "APPLICATION", ref y);

        var verLbl = new Label
        {
            Text      = $"Version {Services.UpdateService.CurrentVersion}",
            Font      = AppTheme.FontLabel,
            ForeColor = AppTheme.TextSecondary,
            BackColor = Color.Transparent,
            AutoSize  = true,
            Location  = new Point(FieldX, y + 4),
        };
        scroll.Controls.Add(verLbl);

        var btnCheckUpdate = MakeButton("Check for Updates", AppTheme.BgCard);
        btnCheckUpdate.SetBounds(FieldX + 180, y, 148, 28);
        scroll.Controls.Add(btnCheckUpdate);

        var updateStatusLbl = new Label
        {
            Text      = "",
            Font      = AppTheme.FontSmall,
            ForeColor = AppTheme.TextMuted,
            BackColor = Color.Transparent,
            AutoSize  = false,
            Location  = new Point(FieldX + 336, y + 6),
            Size      = new Size(FieldW - 336, 16),
        };
        scroll.Controls.Add(updateStatusLbl);
        y += 28;

        // Download button — hidden until an update is found
        var btnDownloadUpdate = MakeButton("Download & Install", AppTheme.Warning);
        btnDownloadUpdate.SetBounds(FieldX, y, 160, 28);
        btnDownloadUpdate.Visible = false;
        scroll.Controls.Add(btnDownloadUpdate);

        var updateNotesLbl = new Label
        {
            Text      = "",
            Font      = AppTheme.FontSmall,
            ForeColor = AppTheme.TextMuted,
            BackColor = Color.Transparent,
            AutoSize  = false,
            Location  = new Point(FieldX + 168, y + 6),
            Size      = new Size(FieldW - 168, 16),
        };
        updateNotesLbl.Visible = false;
        scroll.Controls.Add(updateNotesLbl);
        y += 28;

        // If an update was already found (button visible in main form), show it here too
        if (Services.UpdateService.AvailableRelease is { } cached)
        {
            btnDownloadUpdate.Visible = true;
            updateNotesLbl.Visible    = true;
            updateNotesLbl.ForeColor  = AppTheme.Success;
            updateNotesLbl.Text       = $"v{cached.Version} is available!";
        }

        btnCheckUpdate.Click += async (_, _) =>
        {
            btnCheckUpdate.Enabled = false;
            updateStatusLbl.ForeColor = AppTheme.TextMuted;
            updateStatusLbl.Text      = "Checking…";

            var release = await Services.UpdateService.CheckAsync();

            if (release == null)
            {
                updateStatusLbl.ForeColor = AppTheme.Success;
                updateStatusLbl.Text      = "Up to date!";
                btnDownloadUpdate.Visible = false;
                updateNotesLbl.Visible    = false;
            }
            else
            {
                updateStatusLbl.ForeColor    = AppTheme.Warning;
                updateStatusLbl.Text         = "";
                updateNotesLbl.Text          = $"v{release.Version} is available!";
                updateNotesLbl.ForeColor     = AppTheme.Warning;
                updateNotesLbl.Visible       = true;
                btnDownloadUpdate.Visible    = true;
            }

            btnCheckUpdate.Enabled = true;
        };

        btnDownloadUpdate.Click += async (_, _) =>
        {
            var release = Services.UpdateService.AvailableRelease;
            if (release == null) return;

            btnDownloadUpdate.Enabled = false;
            btnDownloadUpdate.Text    = "Downloading…";

            try
            {
                var progress = new Progress<int>(pct =>
                    btnDownloadUpdate.Text = $"Downloading… {pct}%");

                await Services.UpdateService.DownloadAndInstallAsync(release, progress);
            }
            catch (Exception ex)
            {
                btnDownloadUpdate.Text    = "Download & Install";
                btnDownloadUpdate.Enabled = true;
                updateStatusLbl.ForeColor = AppTheme.Danger;
                updateStatusLbl.Text      = $"Error: {ex.Message}";
            }
        };

        AddHint(scroll, "Updates are downloaded from github.com/Bruiserbaum/BaumDash/releases.", ref y, 22);

        // ── Windows Startup ───────────────────────────────────────────────────
        AddSectionLabel(scroll, "WINDOWS STARTUP", ref y);

        var chkAutoStart = new CheckBox
        {
            Text      = "Launch BaumDash when Windows starts",
            Font      = AppTheme.FontLabel,
            ForeColor = AppTheme.TextPrimary,
            BackColor = Color.Transparent,
            AutoSize  = true,
            Cursor    = Cursors.Hand,
            Location  = new Point(FieldX, y),
        };
        scroll.Controls.Add(chkAutoStart);
        y += chkAutoStart.PreferredSize.Height + 4;
        AddHint(scroll, "Adds BaumDash to HKCU\\...\\Run so it starts automatically on login.", ref y, 22);

        // ── Window Behaviour ──────────────────────────────────────────────────
        AddSectionLabel(scroll, "WINDOW BEHAVIOUR", ref y);

        var chkCloseToTray = new CheckBox
        {
            Text      = "Minimize and close to system tray instead of exiting",
            Font      = AppTheme.FontLabel,
            ForeColor = AppTheme.TextPrimary,
            BackColor = Color.Transparent,
            AutoSize  = true,
            Cursor    = Cursors.Hand,
            Location  = new Point(FieldX, y),
        };
        scroll.Controls.Add(chkCloseToTray);
        y += chkCloseToTray.PreferredSize.Height + 4;
        AddHint(scroll, "When off, the ✕ button exits BaumDash completely.", ref y, 22);

        // ── GPU Platform ──────────────────────────────────────────────────────
        // Each radio group MUST be in its own container Panel so WinForms
        // treats them as independent groups (all radios in the same parent = one group).
        y += 2;
        AddSectionLabel(scroll, "GPU PLATFORM  (Instant Replay button)", ref y);

        var gpuRow = new Panel { BackColor = Color.Transparent, Location = new Point(0, y), Size = new Size(DlgW, 26) };
        _rbGpuAmd    = MakeRadioButton("AMD  (ReLive / Adrenalin — Ctrl+Shift+S)", gpuRow, FieldX,       0);
        _rbGpuNvidia = MakeRadioButton("NVIDIA  (ShadowPlay — Alt+F10)",           gpuRow, FieldX + 310, 0);
        _rbGpuAmd.Checked = true;
        scroll.Controls.Add(gpuRow);
        y += 26;
        AddHint(scroll, "Controls which hotkey the '🎬 SAVE REPLAY' button sends.", ref y, 22);

        // ── Layout Profile ────────────────────────────────────────────────────
        y += 2;
        AddSectionLabel(scroll, "LAYOUT PROFILE", ref y);

        var layoutRow = new Panel { BackColor = Color.Transparent, Location = new Point(0, y), Size = new Size(DlgW, 26) };
        _rbLayoutAuto  = MakeRadioButton("Auto-detect",   layoutRow, FieldX,       0);
        _rbLayout1920  = MakeRadioButton("1920 × 720",    layoutRow, FieldX + 150, 0);
        _rbLayout2560  = MakeRadioButton("2560 × 720",    layoutRow, FieldX + 310, 0);
        _rbLayoutAuto.Checked = true;
        scroll.Controls.Add(layoutRow);
        y += 26;
        AddHint(scroll, "Sets panel column widths and target window width. Applied immediately on Save.", ref y, 22);

        // ── Appearance / Theme ────────────────────────────────────────────────
        y += 2;
        AddSectionLabel(scroll, "THEME", ref y);

        var themeRow = new Panel { BackColor = Color.Transparent, Location = new Point(0, y), Size = new Size(DlgW, 26) };
        _rbDark  = MakeRadioButton("Dark (default)", themeRow, FieldX,       0);
        _rbLight = MakeRadioButton("Light",          themeRow, FieldX + 130, 0);
        _rbCustom= MakeRadioButton("Custom accent",  themeRow, FieldX + 230, 0);
        _rbDark.Checked = true;
        scroll.Controls.Add(themeRow);
        y += 26;

        // Accent colour picker (enabled only when Custom is selected)
        var accentRow = new Panel
        {
            BackColor = Color.Transparent,
            Location  = new Point(FieldX, y),
            Size      = new Size(FieldW, 30),
        };

        var accentLbl = new Label
        {
            Text      = "Accent colour:",
            Font      = AppTheme.FontLabel,
            ForeColor = AppTheme.TextSecondary,
            BackColor = Color.Transparent,
            AutoSize  = true,
            Location  = new Point(0, 6),
        };

        _btnAccentPicker = new Button
        {
            Text      = "",
            BackColor = _customAccent,
            FlatStyle = FlatStyle.Flat,
            Size      = new Size(32, 22),
            Location  = new Point(100, 4),
            Cursor    = Cursors.Hand,
            FlatAppearance = { BorderSize = 1, BorderColor = AppTheme.Border },
        };
        _btnAccentPicker.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { Color = _customAccent, FullOpen = true };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _customAccent = dlg.Color;
                _btnAccentPicker.BackColor = _customAccent;
            }
        };

        var accentHexLbl = new Label
        {
            Text      = "  Pick a custom accent colour (used with Custom theme)",
            Font      = AppTheme.FontSmall,
            ForeColor = AppTheme.TextMuted,
            BackColor = Color.Transparent,
            AutoSize  = true,
            Location  = new Point(140, 8),
        };

        accentRow.Controls.AddRange(new Control[] { accentLbl, _btnAccentPicker, accentHexLbl });
        scroll.Controls.Add(accentRow);
        y += 28;

        void UpdateAccentRowEnabled()
        {
            bool en = _rbCustom?.Checked ?? false;
            accentLbl     .ForeColor = en ? AppTheme.TextSecondary : AppTheme.TextMuted;
            _btnAccentPicker.Enabled  = en;
        }
        _rbDark  .CheckedChanged += (_, _) => UpdateAccentRowEnabled();
        _rbLight .CheckedChanged += (_, _) => UpdateAccentRowEnabled();
        _rbCustom.CheckedChanged += (_, _) => UpdateAccentRowEnabled();
        UpdateAccentRowEnabled();

        AddHint(scroll, "Restart BaumDash after saving to apply theme changes.", ref y, 22);

        // ── Background Image ──────────────────────────────────────────────────
        y += 2;
        AddSectionLabel(scroll, "BACKGROUND IMAGE", ref y);
        AddHint(scroll, "Recommended: 1920×720 px or larger (PNG / JPG / BMP). Visible through all panels.", ref y, 22);

        // Path row
        const int browseW = 68, clearW = 20, gap = 6;
        int pathW = FieldW - browseW - clearW - gap * 2;
        _bgPathBox = new TextBox
        {
            BackColor       = AppTheme.BgCard,
            ForeColor       = AppTheme.TextPrimary,
            Font            = AppTheme.FontLabel,
            BorderStyle     = BorderStyle.FixedSingle,
            ReadOnly        = true,
            PlaceholderText = "No image selected",
            Location        = new Point(FieldX, y),
            Size            = new Size(pathW, 26),
        };
        var btnBrowse = new Button
        {
            Text      = "Browse…",
            Font      = AppTheme.FontBold,
            ForeColor = AppTheme.TextPrimary,
            BackColor = AppTheme.BgCard,
            FlatStyle = FlatStyle.Flat,
            Cursor    = Cursors.Hand,
            Location  = new Point(FieldX + pathW + gap, y),
            Size      = new Size(browseW, 26),
            FlatAppearance = { BorderSize = 0, MouseOverBackColor = AppTheme.Accent },
        };
        btnBrowse.Click += (_, _) =>
        {
            using var ofd = new OpenFileDialog
            {
                Title  = "Select Background Image",
                Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*",
            };
            if (!string.IsNullOrEmpty(_bgPathBox.Text) && File.Exists(_bgPathBox.Text))
                ofd.InitialDirectory = Path.GetDirectoryName(_bgPathBox.Text);
            if (ofd.ShowDialog(this) == DialogResult.OK)
                _bgPathBox.Text = ofd.FileName;
        };
        var btnClearBg = new Button
        {
            Text      = "✕",
            Font      = AppTheme.FontBold,
            ForeColor = AppTheme.TextMuted,
            BackColor = Color.Transparent,
            FlatStyle = FlatStyle.Flat,
            Cursor    = Cursors.Hand,
            Location  = new Point(FieldX + pathW + gap + browseW + gap, y),
            Size      = new Size(clearW, 26),
            FlatAppearance = { BorderSize = 0, MouseOverBackColor = AppTheme.Danger },
        };
        btnClearBg.Click += (_, _) => _bgPathBox.Text = "";
        scroll.Controls.AddRange(new Control[] { _bgPathBox, btnBrowse, btnClearBg });
        y += 26;

        // Image mode row
        var modeLbl = new Label
        {
            Text      = "Fit mode:",
            Font      = AppTheme.FontLabel,
            ForeColor = AppTheme.TextSecondary,
            BackColor = Color.Transparent,
            AutoSize  = true,
            Location  = new Point(FieldX, y + 4),
        };
        _bgModeCombo = new ComboBox
        {
            DropDownStyle   = ComboBoxStyle.DropDownList,
            BackColor       = AppTheme.BgCard,
            ForeColor       = AppTheme.TextPrimary,
            Font            = AppTheme.FontLabel,
            Location        = new Point(FieldX + 74, y),
            Size            = new Size(130, 26),
        };
        _bgModeCombo.Items.AddRange(new object[] { "stretch", "fill", "fit", "tile", "center" });
        _bgModeCombo.SelectedIndex = 0;
        scroll.Controls.AddRange(new Control[] { modeLbl, _bgModeCombo });
        y += 28;

        // Overlay opacity row
        var alphaLbl = new Label
        {
            Text      = "Panel opacity over image:",
            Font      = AppTheme.FontLabel,
            ForeColor = AppTheme.TextSecondary,
            BackColor = Color.Transparent,
            AutoSize  = true,
            Location  = new Point(FieldX, y + 6),
        };
        _bgAlphaSlider = new TrackBar
        {
            Minimum   = 0,
            Maximum   = 255,
            Value     = 190,
            TickStyle = TickStyle.None,
            BackColor = AppTheme.BgDeep,
            Location  = new Point(FieldX + 192, y),
            Size      = new Size(300, 30),
        };
        _bgAlphaValueLabel = new Label
        {
            Text      = "75%",
            Font      = AppTheme.FontLabel,
            ForeColor = AppTheme.TextMuted,
            BackColor = Color.Transparent,
            AutoSize  = true,
            Location  = new Point(FieldX + 498, y + 6),
        };
        _bgAlphaSlider.Scroll += (_, _) =>
        {
            int pct = (int)(_bgAlphaSlider.Value / 255.0 * 100);
            if (_bgAlphaValueLabel != null) _bgAlphaValueLabel.Text = $"{pct}%";
        };
        scroll.Controls.AddRange(new Control[] { alphaLbl, _bgAlphaSlider, _bgAlphaValueLabel });
        y += 28;
        AddHint(scroll, "0% = image fully visible, 100% = panel covers image completely.", ref y, 22);

        scroll.AutoScrollMinSize = new Size(0, y + 20);

        return (chkAutoStart, chkCloseToTray);
    }

    private RadioButton MakeRadioButton(string text, Panel parent, int x, int y)
    {
        var rb = new RadioButton
        {
            Text      = text,
            Font      = AppTheme.FontLabel,
            ForeColor = AppTheme.TextPrimary,
            BackColor = Color.Transparent,
            AutoSize  = true,
            Cursor    = Cursors.Hand,
            Location  = new Point(x, y),
        };
        parent.Controls.Add(rb);
        return rb;
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
            Location  = new Point(FieldX, y),
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
            Location        = new Point(FieldX, y),
            Size            = new Size(FieldW, height),
        };
        p.Controls.Add(txt);
        y += height + 6;
        return txt;
    }

    private static void AddHint(Panel p, string text, ref int y, int spacing = 26)
    {
        var lbl = new Label
        {
            Text      = text,
            Font      = AppTheme.FontSmall,
            ForeColor = AppTheme.TextMuted,
            BackColor = Color.Transparent,
            AutoSize  = false,
            Location  = new Point(FieldX, y),
            Size      = new Size(FieldW, 16),
        };
        p.Controls.Add(lbl);
        y += spacing;
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

        // Weather
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "weather-config.json");
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var r    = doc.RootElement;
                double lat  = r.TryGetProperty("latitude",  out var lp) ? lp.GetDouble() : 0;
                double lon  = r.TryGetProperty("longitude", out var lo) ? lo.GetDouble() : 0;
                string unit = r.TryGetProperty("unit",      out var up) ? up.GetString() ?? "f" : "f";
                string city = r.TryGetProperty("city",      out var cp) ? cp.GetString() ?? "" : "";

                if (_weatherLat  != null) _weatherLat.Text  = lat == 0 ? "" : lat.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
                if (_weatherLon  != null) _weatherLon.Text  = lon == 0 ? "" : lon.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
                if (_weatherCity != null) _weatherCity.Text = city;
                if (_weatherUnit != null)
                    _weatherUnit.SelectedIndex = unit.StartsWith("c", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

                // Show stored location in geo status
                if (_weatherGeoStatus != null && lat != 0)
                {
                    string label = !string.IsNullOrEmpty(city) ? city : $"{lat:F4}, {lon:F4}";
                    _weatherGeoStatus.ForeColor = AppTheme.TextMuted;
                    _weatherGeoStatus.Text      = $"Current location: {label}";
                }
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
                if (cfg != null)
                {
                    _chkCloseToTray.Checked = cfg.CloseToTray;

                    switch (cfg.Theme.ToLowerInvariant())
                    {
                        case "light":  if (_rbLight  != null) _rbLight .Checked = true; break;
                        case "custom": if (_rbCustom != null) _rbCustom.Checked = true; break;
                        default:       if (_rbDark   != null) _rbDark  .Checked = true; break;
                    }
                    if (!string.IsNullOrEmpty(cfg.CustomAccentHex) && _btnAccentPicker != null)
                    {
                        var c = AppTheme.ParseHex(cfg.CustomAccentHex);
                        if (c.HasValue)
                        {
                            _customAccent = c.Value;
                            _btnAccentPicker.BackColor = _customAccent;
                        }
                    }

                    if (_bgPathBox   != null) _bgPathBox.Text = cfg.BgImagePath;
                    if (_bgModeCombo != null)
                    {
                        int idx = _bgModeCombo.Items.IndexOf(cfg.BgImageMode);
                        _bgModeCombo.SelectedIndex = idx >= 0 ? idx : 0;
                    }
                    if (_bgAlphaSlider != null)
                    {
                        _bgAlphaSlider.Value = Math.Clamp(cfg.BgOverlayAlpha, 0, 255);
                        if (_bgAlphaValueLabel != null)
                            _bgAlphaValueLabel.Text = $"{(int)(_bgAlphaSlider.Value / 255.0 * 100)}%";
                    }

                    if (cfg.GpuPlatform.Equals("nvidia", StringComparison.OrdinalIgnoreCase))
                    {
                        if (_rbGpuNvidia != null) _rbGpuNvidia.Checked = true;
                    }
                    else
                    {
                        if (_rbGpuAmd != null) _rbGpuAmd.Checked = true;
                    }

                    switch ((cfg.LayoutProfile ?? "auto").ToLowerInvariant())
                    {
                        case "1920": if (_rbLayout1920 != null) _rbLayout1920.Checked = true; break;
                        case "2560": if (_rbLayout2560 != null) _rbLayout2560.Checked = true; break;
                        default:     if (_rbLayoutAuto != null) _rbLayoutAuto.Checked = true; break;
                    }
                }
            }
            else
            {
                _chkCloseToTray.Checked = true;
            }
        }
        catch { _chkCloseToTray.Checked = true; }

        _chkAutoStart.Checked = IsAutoStartEnabled();
    }

    private void OnSave(object? sender, EventArgs e)
    {
        try
        {
            var dir  = AppContext.BaseDirectory;
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

            // Weather
            if (_weatherLat != null && _weatherLon != null && _weatherUnit != null)
            {
                double.TryParse(_weatherLat.Text.Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double lat);
                double.TryParse(_weatherLon.Text.Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double lon);
                string unitStr = _weatherUnit.SelectedIndex == 1 ? "c" : "f";
                string cityStr = _weatherCity?.Text.Trim() ?? "";
                File.WriteAllText(Path.Combine(dir, "weather-config.json"),
                    JsonSerializer.Serialize(new { latitude = lat, longitude = lon, unit = unitStr, city = cityStr }, opts));
            }

            // General
            string theme = (_rbLight?.Checked  == true) ? "light"
                         : (_rbCustom?.Checked == true) ? "custom"
                         : "dark";
            int    alpha    = _bgAlphaSlider?.Value ?? 190;
            string bgPath   = _bgPathBox?.Text.Trim() ?? "";
            string bgMode   = _bgModeCombo?.SelectedItem?.ToString() ?? "stretch";
            string accentHx = AppTheme.ToHex(_customAccent);
            string gpuPlat     = (_rbGpuNvidia?.Checked == true) ? "nvidia" : "amd";
            string layoutProf  = (_rbLayout1920?.Checked == true) ? "1920"
                               : (_rbLayout2560?.Checked == true) ? "2560"
                               : "auto";

            File.WriteAllText(Path.Combine(dir, "general-config.json"),
                JsonSerializer.Serialize(new WinUIAudioMixer.Models.GeneralConfig
                {
                    CloseToTray     = _chkCloseToTray.Checked,
                    Theme           = theme,
                    CustomAccentHex = accentHx,
                    BgImagePath     = bgPath,
                    BgImageMode     = bgMode,
                    BgOverlayAlpha  = alpha,
                    GpuPlatform     = gpuPlat,
                    LayoutProfile   = layoutProf,
                }, opts));

            // Startup
            SetAutoStart(_chkAutoStart.Checked);

            // ── Live theme application ─────────────────────────────────────────
            var oldColors = SnapshotThemeColors();

            AppTheme.Apply(theme, theme == "custom" ? accentHx : "");
            AppTheme.BgOverlayAlpha = alpha;
            AppTheme.BgImageMode    = bgMode;

            if (!string.IsNullOrEmpty(bgPath) && File.Exists(bgPath))
            {
                try
                {
                    AppTheme.BgImage?.Dispose();
                    AppTheme.BgImage = Image.FromFile(bgPath);
                }
                catch { }
            }
            else if (string.IsNullOrEmpty(bgPath))
            {
                AppTheme.BgImage?.Dispose();
                AppTheme.BgImage = null;
            }

            if (Owner != null)
                ApplyColorMapping(Owner, oldColors);

            AppTheme.RaiseThemeChanged();

            ShowStatus("Settings saved.", success: true);
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

    private static readonly string[] BackupFiles =
    {
        "discord-client-id.txt",
        "anythingllm-config.json",
        "chatgpt-config.json",
        "ha-config.json",
        "gcalendar-config.json",
        "app-shortcuts.json",
        "general-config.json",
        "weather-config.json",
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
            var dir    = AppContext.BaseDirectory;
            var backup = new Dictionary<string, string?>();
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
            var json   = File.ReadAllText(dlg.FileName);
            var backup = JsonSerializer.Deserialize<Dictionary<string, string?>>(json)
                         ?? throw new InvalidDataException("Invalid backup file.");

            var dir = AppContext.BaseDirectory;
            foreach (var (file, content) in backup)
            {
                if (content == null) continue;
                if (!BackupFiles.Contains(file)) continue;
                File.WriteAllText(Path.Combine(dir, file), content);
            }

            MainForm.PendingImportRestart = true;
            MessageBox.Show(
                "Settings imported successfully.\n\nBaumDash will now close and restart.",
                "Import Complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            Application.Exit();
        }
        catch (Exception ex)
        {
            ShowStatus($"Import failed: {ex.Message}", success: false);
        }
    }

    // ── Live theme helpers ────────────────────────────────────────────────────

    private static Dictionary<string, Color> SnapshotThemeColors() => new()
    {
        ["BgDeep"]        = AppTheme.BgDeep,
        ["BgMain"]        = AppTheme.BgMain,
        ["BgPanel"]       = AppTheme.BgPanel,
        ["BgCard"]        = AppTheme.BgCard,
        ["Accent"]        = AppTheme.Accent,
        ["AccentHover"]   = AppTheme.AccentHover,
        ["TextPrimary"]   = AppTheme.TextPrimary,
        ["TextSecondary"] = AppTheme.TextSecondary,
        ["TextMuted"]     = AppTheme.TextMuted,
        ["Border"]        = AppTheme.Border,
    };

    private static void ApplyColorMapping(Control root, Dictionary<string, Color> snapshot)
    {
        var map = new Dictionary<Color, Color>();
        void Add(string key, Color newVal)
        {
            if (snapshot.TryGetValue(key, out var old) && old != newVal)
                map.TryAdd(old, newVal);
        }
        Add("BgDeep",        AppTheme.BgDeep);
        Add("BgMain",        AppTheme.BgMain);
        Add("BgPanel",       AppTheme.BgPanel);
        Add("BgCard",        AppTheme.BgCard);
        Add("Accent",        AppTheme.Accent);
        Add("AccentHover",   AppTheme.AccentHover);
        Add("TextPrimary",   AppTheme.TextPrimary);
        Add("TextSecondary", AppTheme.TextSecondary);
        Add("TextMuted",     AppTheme.TextMuted);
        Add("Border",        AppTheme.Border);

        if (map.Count > 0)
            WalkAndRemap(root, map);

        root.Invalidate(true);
    }

    private static void WalkAndRemap(Control c, Dictionary<Color, Color> map)
    {
        if (c.BackColor != Color.Transparent && map.TryGetValue(c.BackColor, out var newBg))
            c.BackColor = newBg;
        if (map.TryGetValue(c.ForeColor, out var newFg))
            c.ForeColor = newFg;

        if (c is Button btn)
        {
            if (map.TryGetValue(btn.FlatAppearance.MouseOverBackColor, out var newHover))
                btn.FlatAppearance.MouseOverBackColor = newHover;
            if (map.TryGetValue(btn.FlatAppearance.MouseDownBackColor, out var newDown))
                btn.FlatAppearance.MouseDownBackColor = newDown;
        }

        foreach (Control child in c.Controls)
            WalkAndRemap(child, map);
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

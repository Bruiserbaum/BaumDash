using WinUIAudioMixer.Interop;
using WinUIAudioMixer.Models;
using WinUIAudioMixer.Services;

namespace WinUIAudioMixer.Controls;

/// <summary>
/// Left panel – microphone mute toggle, speaker selector, master volume.
/// Width: ~300 px
/// </summary>
public sealed class AudioDevicePanel : UserControl
{
    private readonly AudioDeviceService _deviceService;
    private List<OutputDevice> _outputDevices = new();

    // Mic
    private readonly Label  _micNameLabel;
    private readonly Button _micMuteButton;
    private IAudioEndpointVolume? _micEpVol;
    private bool _micMuted;

    // Speaker
    private readonly ComboBox   _speakerCombo;
    private readonly DarkSlider _masterSlider;
    private readonly Label      _masterPctLabel;
    private IAudioEndpointVolume? _speakerEpVol;
    private bool _updatingCombo;

    // AMD instant replay
    private readonly Button _amdReplayButton;

    // Home Assistant
    private readonly HomeAssistantService?              _haSvc;
    private readonly List<Label>                        _haSensorLabels  = new();
    private readonly List<(Button btn, string entityId)> _haLightButtons = new();
    private readonly System.Windows.Forms.Timer?        _haTimer;
    private bool _haConnected;

    // Periodic volume sync
    private readonly System.Windows.Forms.Timer _volumeTimer;
    private bool _timerUpdating;

    public AudioDevicePanel(AudioDeviceService deviceService, HomeAssistantService? haSvc = null)
    {
        _deviceService = deviceService;
        _haSvc         = haSvc;
        BackColor = AppTheme.BgPanel;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        _micNameLabel = MakeLabel("…", AppTheme.TextPrimary, AppTheme.FontLabel);

        _micMuteButton = MakeFlatButton("MIC ON", AppTheme.Success);
        _micMuteButton.Click += OnMicMuteClick;

        _speakerCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font          = AppTheme.FontLabel,
        };
        _speakerCombo.SelectedIndexChanged += OnSpeakerChanged;

        _masterSlider   = new DarkSlider { Value = 1f };
        _masterPctLabel = MakeLabel("100%", AppTheme.TextSecondary, AppTheme.FontSmall);
        _masterSlider.ValueChanged += OnMasterVolumeChanged;

        _amdReplayButton = MakeFlatButton("🎬  SAVE AMD REPLAY", Color.FromArgb(180, 0, 0));
        _amdReplayButton.Click += (_, _) => Task.Run(NativeMethods.SaveAmdReplay);

        Controls.AddRange(new Control[]
        {
            _micNameLabel, _micMuteButton,
            _speakerCombo,
            _masterSlider, _masterPctLabel,
            _amdReplayButton
        });

        // ── Home Assistant controls (dynamic, based on config) ────────────────
        if (_haSvc?.IsConfigured == true)
        {
            foreach (var sensor in _haSvc.Config.Sensors)
            {
                var lbl = MakeLabel($"{sensor.Name}: …", AppTheme.TextPrimary, new Font("Segoe UI", 11f));
                _haSensorLabels.Add(lbl);
                Controls.Add(lbl);
            }

            foreach (var light in _haSvc.Config.Lights)
            {
                var btn = MakeFlatButton($"💡  {light.Name.ToUpper()}", AppTheme.BgCard);
                var eid = light.Id;
                btn.Click += async (_, _) =>
                {
                    try
                    {
                        await _haSvc.ToggleLightAsync(eid);
                        await Task.Delay(400);
                        await RefreshLightButtonAsync(btn, eid);
                    }
                    catch { }
                };
                _haLightButtons.Add((btn, eid));
                Controls.Add(btn);
            }

            _haTimer = new System.Windows.Forms.Timer { Interval = 30_000 };
            _haTimer.Tick += OnHaTick;
        }

        _volumeTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _volumeTimer.Tick += OnVolumeTick;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Defer COM calls until the WinForms message pump is running.
        // OnHandleCreated fires during Show(), before Application.Run pumps messages,
        // so CoCreateInstance fails for audio COM objects at that point.
        BeginInvoke(InitializeAudio);
    }

    private void InitializeAudio()
    {
        LoadMic();
        LoadDevices();
        LayoutAll();
        _volumeTimer.Start();
        if (_haTimer != null) { _haTimer.Start(); _ = Task.Run(RefreshHaAsync); }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutAll();
    }

    // ── Layout ────────────────────────────────────────────────────────────────

    private void LayoutAll()
    {
        if (ClientSize.Width < 10) return;
        int x = 16, w = ClientSize.Width - 32;

        _micNameLabel    .SetBounds(x,          58,  w,      20);
        _micMuteButton   .SetBounds(x,          82,  w,      36);
        _speakerCombo    .SetBounds(x,         162,  w,       0);  // height auto for combo
        _masterSlider    .SetBounds(x,         220,  w - 44, 28);
        _masterPctLabel  .SetBounds(x + w - 42, 220, 42,     28);
        _amdReplayButton .SetBounds(x,         294,  w,      36);

        // ── Home Assistant ─────────────────────────────────────────────────────
        // Section header drawn in OnPaint at y=342/348; controls start at y=366
        const int HaTop = 366;
        for (int i = 0; i < _haSensorLabels.Count; i++)
            _haSensorLabels[i].SetBounds(x, HaTop + i * 26, w, 24);

        int lightTop = HaTop + _haSensorLabels.Count * 26 + (_haSensorLabels.Count > 0 ? 8 : 0);
        for (int i = 0; i < _haLightButtons.Count; i++)
            _haLightButtons[i].btn.SetBounds(x, lightTop + i * 44, w, 36);
    }

    // ── Painting ──────────────────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        int x = 16, w = ClientSize.Width - 32;

        using var muted = new SolidBrush(AppTheme.TextMuted);
        using var sub   = new SolidBrush(AppTheme.TextSecondary);
        using var sep   = new Pen(AppTheme.Border);

        g.DrawString("AUDIO DEVICES",  AppTheme.FontSectionHeader, muted, x, 14);
        g.DrawLine(sep, x, 36, x + w, 36);

        g.DrawString("Microphone",     AppTheme.FontBold, sub, x, 42);
        g.DrawLine(sep, x, 130, x + w, 130);

        g.DrawString("Speaker Output", AppTheme.FontBold, sub, x, 138);
        g.DrawString("Master Volume",  AppTheme.FontSmall, muted, x, 200);
        g.DrawLine(sep, x, 268, x + w, 268);
        g.DrawString("AMD Instant Replay", AppTheme.FontBold, sub, x, 274);

        if (_haSvc?.IsConfigured == true)
        {
            g.DrawLine(sep, x, 338, x + w, 338);
            g.DrawString("HOME ASSISTANT", AppTheme.FontSectionHeader, muted, x, 344);
            var dotColor = _haConnected ? AppTheme.Success : AppTheme.TextMuted;
            using var dotBrush = new SolidBrush(dotColor);
            g.FillEllipse(dotBrush, x + 120, 346, 8, 8);
        }
    }

    // ── Data loading ──────────────────────────────────────────────────────────

    public void LoadDevices()
    {
        _updatingCombo = true;
        try
        {
            _outputDevices = _deviceService.GetOutputDevices().ToList();

            _speakerCombo.Items.Clear();
            if (_outputDevices.Count == 0)
            {
                _speakerCombo.Items.Add("No output devices");
                _speakerCombo.SelectedIndex = 0;
                return;
            }

            foreach (var d in _outputDevices)
                _speakerCombo.Items.Add(d.Name);

            int def = _outputDevices.FindIndex(d => d.IsDefault);
            _speakerCombo.SelectedIndex = def >= 0 ? def : 0;

            var selId = _outputDevices[_speakerCombo.SelectedIndex].Id;
            RefreshSpeakerVolume(selId);
        }
        catch (Exception ex)
        {
            _speakerCombo.Items.Clear();
            _speakerCombo.Items.Add($"Err: {ex.GetType().Name}: {ex.Message}");
            _speakerCombo.SelectedIndex = 0;
        }
        finally
        {
            _updatingCombo = false;
        }
    }

    private void RefreshSpeakerVolume(string? deviceId)
    {
        try
        {
            MarshalHelpers.ReleaseComObject(_speakerEpVol);
            _speakerEpVol = null;
            if (deviceId == null) return;

            var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            en.GetDevice(deviceId, out var device);
            MarshalHelpers.ReleaseComObject(en);

            var iid = typeof(IAudioEndpointVolume).GUID;
            device.Activate(ref iid, CLSCTX.InprocServer, IntPtr.Zero, out var volObj);
            MarshalHelpers.ReleaseComObject(device);

            _speakerEpVol = (IAudioEndpointVolume)volObj;
            _speakerEpVol.GetMasterVolumeLevelScalar(out var vol);
            _masterSlider.Value  = vol;
            _masterPctLabel.Text = $"{(int)(vol * 100)}%";
        }
        catch { }
    }

    public void LoadMic()
    {
        try
        {
            MarshalHelpers.ReleaseComObject(_micEpVol);
            _micEpVol = null;

            var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            en.GetDefaultAudioEndpoint(EDataFlow.Capture, ERole.Communications, out var device);
            MarshalHelpers.ReleaseComObject(en);

            const int StgmRead = 0;
            device.OpenPropertyStore(StgmRead, out var store);
            var key = PropertyKeys.PkeyDeviceFriendlyName;
            store.GetValue(ref key, out var prop);
            _micNameLabel.Text = prop.GetString() ?? "Unknown Mic";
            prop.Dispose();
            MarshalHelpers.ReleaseComObject(store);

            var iid = typeof(IAudioEndpointVolume).GUID;
            device.Activate(ref iid, CLSCTX.InprocServer, IntPtr.Zero, out var volObj);
            MarshalHelpers.ReleaseComObject(device);

            _micEpVol = (IAudioEndpointVolume)volObj;
            _micEpVol.GetMute(out _micMuted);
            UpdateMicButton();
        }
        catch
        {
            _micNameLabel.Text = "No microphone";
        }
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnMicMuteClick(object? sender, EventArgs e)
    {
        try
        {
            _micMuted = !_micMuted;
            var ctx = Guid.Empty;
            _micEpVol?.SetMute(_micMuted, ref ctx);
            UpdateMicButton();
        }
        catch { }
    }

    private void UpdateMicButton()
    {
        _micMuteButton.Text      = _micMuted ? "  MIC MUTED"  : "  MIC ACTIVE";
        _micMuteButton.BackColor = _micMuted ? AppTheme.Danger : AppTheme.Success;
        _micMuteButton.ForeColor = Color.White;
    }

    private void OnSpeakerChanged(object? sender, EventArgs e)
    {
        if (_updatingCombo) return;
        int idx = _speakerCombo.SelectedIndex;
        if (idx < 0 || idx >= _outputDevices.Count) return;
        try
        {
            _deviceService.SetDefaultDevice(_outputDevices[idx].Id);
            RefreshSpeakerVolume(_outputDevices[idx].Id);
        }
        catch { }
    }

    private void OnMasterVolumeChanged(object? sender, EventArgs e)
    {
        if (_timerUpdating) return;
        try
        {
            var vol = _masterSlider.Value;
            var ctx = Guid.Empty;
            _speakerEpVol?.SetMasterVolumeLevelScalar(vol, ref ctx);
            _masterPctLabel.Text = $"{(int)(vol * 100)}%";
        }
        catch { }
    }

    private void OnVolumeTick(object? sender, EventArgs e)
    {
        _timerUpdating = true;
        try
        {
            if (_speakerEpVol != null)
            {
                _speakerEpVol.GetMasterVolumeLevelScalar(out var vol);
                if (Math.Abs(vol - _masterSlider.Value) > 0.01f)
                {
                    _masterSlider.Value  = vol;
                    _masterPctLabel.Text = $"{(int)(vol * 100)}%";
                }
            }
            if (_micEpVol != null)
            {
                _micEpVol.GetMute(out var muted);
                if (muted != _micMuted)
                {
                    _micMuted = muted;
                    UpdateMicButton();
                }
            }
        }
        catch { }
        finally
        {
            _timerUpdating = false;
        }
    }

    // ── Home Assistant ────────────────────────────────────────────────────────

    private void OnHaTick(object? sender, EventArgs e) => _ = Task.Run(RefreshHaAsync);

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
            await RefreshLightButtonAsync(btn, eid);
            anySuccess = true;
        }

        if (_haConnected != anySuccess)
        {
            _haConnected = anySuccess;
            if (InvokeRequired) BeginInvoke(Invalidate);
            else                Invalidate();
        }
    }

    private async Task RefreshLightButtonAsync(Button btn, string entityId)
    {
        if (_haSvc == null) return;
        var isOn = await _haSvc.GetLightStateAsync(entityId);
        var color = isOn ? AppTheme.Accent : AppTheme.BgCard;
        if (btn.InvokeRequired) btn.BeginInvoke(() => btn.BackColor = color);
        else                    btn.BackColor = color;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Label MakeLabel(string text, Color fg, Font font) => new()
    {
        Text         = text,
        Font         = font,
        ForeColor    = fg,
        BackColor    = Color.Transparent,
        AutoSize     = false,
        AutoEllipsis = true,
        TextAlign    = ContentAlignment.MiddleLeft,
    };

    private static Button MakeFlatButton(string text, Color bg) => new()
    {
        Text      = text,
        Font      = AppTheme.FontButton,
        BackColor = bg,
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
        Cursor    = Cursors.Hand,
        FlatAppearance = { BorderSize = 0 },
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _volumeTimer.Stop();
            _volumeTimer.Dispose();
            _haTimer?.Stop();
            _haTimer?.Dispose();
            MarshalHelpers.ReleaseComObject(_micEpVol);
            MarshalHelpers.ReleaseComObject(_speakerEpVol);
        }
        base.Dispose(disposing);
    }
}

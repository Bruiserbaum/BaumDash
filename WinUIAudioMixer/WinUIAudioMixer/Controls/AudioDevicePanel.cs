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
    private int _currentDeviceIndex;

    // Mic (arrow selector + mute)
    private List<OutputDevice> _micDevices = new();
    private int _currentMicIndex;
    private readonly Button _micPrevBtn;
    private readonly Button _micNextBtn;
    private readonly Label  _micNameLabel;
    private readonly Button _micMuteButton;
    private IAudioEndpointVolume? _micEpVol;
    private bool _micMuted;

    // Speaker (arrow selector + mute)
    private readonly Button _speakerPrevBtn;
    private readonly Button _speakerNextBtn;
    private readonly Label  _speakerNameLabel;
    private readonly Button _speakerMuteButton;
    private readonly DarkSlider _masterSlider;
    private readonly Label      _masterPctLabel;
    private IAudioEndpointVolume? _speakerEpVol;
    private bool _speakerMuted;

    // Instant replay (AMD or Nvidia depending on config)
    private readonly Button _replayButton;
    private Action _replayAction = NativeMethods.SaveAmdReplay;
    private string _replayLabel  = "AMD Instant Replay";

    // Periodic volume sync
    private readonly System.Windows.Forms.Timer _volumeTimer;
    private bool _timerUpdating;

    // Larger fonts for this panel only
    private static readonly Font _fSmall   = new("Segoe UI", 13f, FontStyle.Regular);
    private static readonly Font _fControl = new("Segoe UI", 14f, FontStyle.Regular);
    private static readonly Font _fButton  = new("Segoe UI", 13f, FontStyle.Bold);

    public AudioDevicePanel(AudioDeviceService deviceService)
    {
        _deviceService = deviceService;
        BackColor = AppTheme.BgPanel;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        _micPrevBtn      = MakeFlatButton("◀", AppTheme.BgCard);
        _micPrevBtn.Font = _fButton;
        _micPrevBtn.Click += (_, _) => StepMic(-1);

        _micNameLabel = MakeLabel("…", AppTheme.TextPrimary, _fControl);
        _micNameLabel.TextAlign = ContentAlignment.MiddleCenter;

        _micNextBtn      = MakeFlatButton("▶", AppTheme.BgCard);
        _micNextBtn.Font = _fButton;
        _micNextBtn.Click += (_, _) => StepMic(+1);

        _micMuteButton = MakeFlatButton("MIC ON", AppTheme.Accent);
        _micMuteButton.Font  = _fButton;
        _micMuteButton.Click += OnMicMuteClick;

        _speakerPrevBtn      = MakeFlatButton("◀", AppTheme.BgCard);
        _speakerPrevBtn.Font = _fButton;
        _speakerPrevBtn.Click += (_, _) => StepDevice(-1);

        _speakerNameLabel = MakeLabel("", AppTheme.TextPrimary, _fControl);
        _speakerNameLabel.TextAlign = ContentAlignment.MiddleCenter;

        _speakerNextBtn      = MakeFlatButton("▶", AppTheme.BgCard);
        _speakerNextBtn.Font = _fButton;
        _speakerNextBtn.Click += (_, _) => StepDevice(+1);

        _speakerMuteButton = MakeFlatButton("SPEAKER ON", AppTheme.Accent);
        _speakerMuteButton.Font  = _fButton;
        _speakerMuteButton.Click += OnSpeakerMuteClick;

        _masterSlider   = new DarkSlider { Value = 1f };
        _masterPctLabel = MakeLabel("100%", AppTheme.TextSecondary, _fSmall);
        _masterSlider.ValueChanged += OnMasterVolumeChanged;

        _replayButton      = MakeFlatButton("🎬  SAVE AMD REPLAY", AppTheme.Accent);
        _replayButton.Font = _fButton;
        _replayButton.Click += OnReplayClick;

        Controls.AddRange(new Control[]
        {
            _micPrevBtn, _micNameLabel, _micNextBtn, _micMuteButton,
            _speakerPrevBtn, _speakerNameLabel, _speakerNextBtn, _speakerMuteButton,
            _masterSlider, _masterPctLabel,
            _replayButton,
        });

        _volumeTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _volumeTimer.Tick += OnVolumeTick;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        BeginInvoke(InitializeAudio);
    }

    private void InitializeAudio()
    {
        LoadMic();
        LoadDevices();
        ApplyGpuPlatformFromConfig();
        LayoutAll();
        _volumeTimer.Start();
    }

    private void ApplyGpuPlatformFromConfig()
    {
        try
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "general-config.json");
            if (!System.IO.File.Exists(path)) return;
            var cfg = System.Text.Json.JsonSerializer.Deserialize<Models.GeneralConfig>(
                System.IO.File.ReadAllText(path),
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (cfg == null) return;
            ApplyGpuPlatform(cfg.GpuPlatform);
        }
        catch { }
    }

    /// <summary>Call with "amd" or "nvidia" to switch the replay button label and hotkey.</summary>
    public void ApplyGpuPlatform(string platform)
    {
        bool nvidia = platform.Equals("nvidia", StringComparison.OrdinalIgnoreCase);
        _replayAction = nvidia ? NativeMethods.SaveNvidiaReplay : NativeMethods.SaveAmdReplay;
        _replayLabel  = nvidia ? "NVIDIA Instant Replay" : "AMD Instant Replay";
        _replayButton.Text = nvidia ? "🎬  SAVE NVIDIA CLIP" : "🎬  SAVE AMD REPLAY";
        Invalidate();
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

        // Mic section: header y=18, sep y=50, label y=58
        // Selector y=88, mute y=140
        _micPrevBtn      .SetBounds(x,           88,  44,      44);
        _micNameLabel    .SetBounds(x + 48,      88,  w - 96,  44);
        _micNextBtn      .SetBounds(x + w - 44,  88,  44,      44);
        _micMuteButton   .SetBounds(x,          140,  w,       44);

        // Speaker section: sep y=196, label y=204
        // Selector y=234, mute y=286
        _speakerPrevBtn  .SetBounds(x,          234,  44,      44);
        _speakerNameLabel.SetBounds(x + 48,     234,  w - 96,  44);
        _speakerNextBtn  .SetBounds(x + w - 44, 234,  44,      44);
        _speakerMuteButton.SetBounds(x,         286,  w,       44);

        // Master volume: label y=342, slider y=366
        _masterSlider    .SetBounds(x,          366,  w - 56,  34);
        _masterPctLabel  .SetBounds(x + w - 54, 364,  54,      36);

        // Replay section: sep y=416, label y=424, button y=454
        _replayButton    .SetBounds(x,          454,  w,       44);
    }

    // ── Painting ──────────────────────────────────────────────────────────────

    protected override void OnPaintBackground(PaintEventArgs e)
        => AppTheme.PaintBackground(e.Graphics, this, AppTheme.BgPanel);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        int x = 16, w = ClientSize.Width - 32;

        using var muted = new SolidBrush(AppTheme.TextMuted);
        using var sub   = new SolidBrush(AppTheme.TextSecondary);
        using var sep   = new Pen(AppTheme.Border);

        g.DrawString("AUDIO DEVICES",  AppTheme.FontPanelHeader, muted, x, 18);
        g.DrawLine(sep, x, 50, x + w, 50);

        g.DrawString("Microphone",     AppTheme.FontPanelSub, sub, x, 58);
        g.DrawLine(sep, x, 196, x + w, 196);

        g.DrawString("Speaker Output", AppTheme.FontPanelSub, sub, x, 204);
        g.DrawString("Master Volume",  _fSmall, muted, x, 342);
        g.DrawLine(sep, x, 416, x + w, 416);
        g.DrawString(_replayLabel,     AppTheme.FontPanelSub, sub, x, 424);
    }

    // ── Data loading ──────────────────────────────────────────────────────────

    public void LoadDevices()
    {
        try
        {
            _outputDevices = _deviceService.GetOutputDevices().ToList();

            if (_outputDevices.Count == 0)
            {
                _speakerNameLabel.Text  = "No output devices";
                _speakerPrevBtn.Enabled = false;
                _speakerNextBtn.Enabled = false;
                return;
            }

            int def = _outputDevices.FindIndex(d => d.IsDefault);
            _currentDeviceIndex = def >= 0 ? def : 0;
            UpdateSpeakerLabel();
            RefreshSpeakerVolume(_outputDevices[_currentDeviceIndex].Id);
        }
        catch (Exception ex)
        {
            _speakerNameLabel.Text  = $"Error: {ex.GetType().Name}";
            _speakerPrevBtn.Enabled = false;
            _speakerNextBtn.Enabled = false;
        }
    }

    private void StepDevice(int delta)
    {
        if (_outputDevices.Count == 0) return;
        _currentDeviceIndex = (_currentDeviceIndex + delta + _outputDevices.Count) % _outputDevices.Count;
        UpdateSpeakerLabel();
        try
        {
            _deviceService.SetDefaultDevice(_outputDevices[_currentDeviceIndex].Id);
            RefreshSpeakerVolume(_outputDevices[_currentDeviceIndex].Id);
        }
        catch { }
    }

    private void UpdateSpeakerLabel()
    {
        if (_outputDevices.Count == 0) return;
        _speakerNameLabel.Text = _outputDevices[_currentDeviceIndex].Name;
    }

    public void LoadMic()
    {
        try
        {
            _micDevices = _deviceService.GetInputDevices().ToList();

            if (_micDevices.Count == 0)
            {
                _micNameLabel.Text  = "No microphone";
                _micPrevBtn.Enabled = false;
                _micNextBtn.Enabled = false;
                return;
            }

            int def = _micDevices.FindIndex(d => d.IsDefault);
            _currentMicIndex = def >= 0 ? def : 0;
            _micNameLabel.Text = _micDevices[_currentMicIndex].Name;

            RefreshMicVolume(_micDevices[_currentMicIndex].Id);
        }
        catch
        {
            _micNameLabel.Text = "No microphone";
        }
    }

    private void RefreshMicVolume(string deviceId)
    {
        try
        {
            MarshalHelpers.ReleaseComObject(_micEpVol);
            _micEpVol = null;

            var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            en.GetDevice(deviceId, out var device);
            MarshalHelpers.ReleaseComObject(en);

            var iid = typeof(IAudioEndpointVolume).GUID;
            device.Activate(ref iid, CLSCTX.InprocServer, IntPtr.Zero, out var volObj);
            MarshalHelpers.ReleaseComObject(device);

            _micEpVol = (IAudioEndpointVolume)volObj;
            _micEpVol.GetMute(out _micMuted);
            UpdateMicButton();
        }
        catch { }
    }

    private void StepMic(int delta)
    {
        if (_micDevices.Count == 0) return;
        _currentMicIndex = (_currentMicIndex + delta + _micDevices.Count) % _micDevices.Count;
        _micNameLabel.Text = _micDevices[_currentMicIndex].Name;
        try
        {
            _deviceService.SetDefaultDevice(_micDevices[_currentMicIndex].Id);
            RefreshMicVolume(_micDevices[_currentMicIndex].Id);
        }
        catch { }
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
            _speakerEpVol.GetMute(out _speakerMuted);
            UpdateSpeakerMuteButton();
        }
        catch { }
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
        _micMuteButton.BackColor = _micMuted ? AppTheme.BgCard : AppTheme.Accent;
        _micMuteButton.ForeColor = _micMuted ? AppTheme.TextMuted : Color.White;
        _micMuteButton.FlatAppearance.MouseOverBackColor =
            _micMuted ? AppTheme.BgPanel : AppTheme.AccentHover;
    }

    private void OnSpeakerMuteClick(object? sender, EventArgs e)
    {
        try
        {
            _speakerMuted = !_speakerMuted;
            var ctx = Guid.Empty;
            _speakerEpVol?.SetMute(_speakerMuted, ref ctx);
            UpdateSpeakerMuteButton();
        }
        catch { }
    }

    private void UpdateSpeakerMuteButton()
    {
        _speakerMuteButton.Text      = _speakerMuted ? "  SPEAKER MUTED" : "  SPEAKER ON";
        _speakerMuteButton.BackColor = _speakerMuted ? AppTheme.BgCard : AppTheme.Accent;
        _speakerMuteButton.ForeColor = _speakerMuted ? AppTheme.TextMuted : Color.White;
        _speakerMuteButton.FlatAppearance.MouseOverBackColor =
            _speakerMuted ? AppTheme.BgPanel : AppTheme.AccentHover;
    }

    private async void OnReplayClick(object? sender, EventArgs e)
    {
        _replayButton.Enabled   = false;
        _replayButton.Text      = "⏳  SAVING…";
        _replayButton.BackColor = AppTheme.AccentHover;

        await Task.Run(_replayAction);

        await Task.Delay(4000);

        if (IsHandleCreated)
            BeginInvoke(() =>
            {
                _replayButton.Text      = _replayLabel.Contains("NVIDIA") ? "🎬  SAVE NVIDIA CLIP" : "🎬  SAVE AMD REPLAY";
                _replayButton.BackColor = AppTheme.Accent;
                _replayButton.Enabled   = true;
            });
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
                _speakerEpVol.GetMute(out var spkMuted);
                if (spkMuted != _speakerMuted)
                {
                    _speakerMuted = spkMuted;
                    UpdateSpeakerMuteButton();
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
            MarshalHelpers.ReleaseComObject(_micEpVol);
            MarshalHelpers.ReleaseComObject(_speakerEpVol);
        }
        base.Dispose(disposing);
    }
}

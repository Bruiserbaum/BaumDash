using System.Drawing.Drawing2D;
using WinUIAudioMixer.Models;
using WinUIAudioMixer.Services;

namespace WinUIAudioMixer.Controls;

/// <summary>
/// Centre-left panel – scrollable list of per-app volume sliders with weather footer.
/// Width: ~500 px
/// </summary>
public sealed class AppVolumePanel : UserControl
{
    private readonly AudioSessionService _sessionService;
    private readonly Panel  _scrollContainer;
    private readonly Button _refreshButton;
    private readonly List<AppSessionRow> _rows = new();
    private readonly System.Windows.Forms.Timer _refreshTimer;

    // Weather
    private const int WeatherH = 168; // height reserved at bottom for weather footer
    private System.Windows.Forms.Timer? _weatherTimer;
    private WeatherService?  _weatherSvc;
    private WeatherSnapshot? _weather;

    public AppVolumePanel(AudioSessionService sessionService, WeatherService? weatherSvc = null)
    {
        _sessionService = sessionService;
        _weatherSvc     = weatherSvc;
        BackColor = AppTheme.BgMain;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        _scrollContainer = new Panel
        {
            AutoScroll = true,
            BackColor  = Color.Transparent,
        };
        AppTheme.ApplyDarkScrollBar(_scrollContainer);

        _refreshButton = new Button
        {
            Text      = "↻",
            Font      = AppTheme.FontBold,
            ForeColor = AppTheme.TextMuted,
            BackColor = Color.Transparent,
            FlatStyle = FlatStyle.Flat,
            Cursor    = Cursors.Hand,
            FlatAppearance = { BorderSize = 0 },
        };
        _refreshButton.Click += (_, _) => LoadSessions();

        Controls.Add(_scrollContainer);
        Controls.Add(_refreshButton);

        Resize += (_, _) => LayoutPanel();
        LayoutPanel();
        LoadSessions();

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _refreshTimer.Tick += OnRefreshTick;
        _refreshTimer.Start();

        if (_weatherSvc != null)
        {
            _weatherTimer = new System.Windows.Forms.Timer { Interval = 10 * 60 * 1000 };
            _weatherTimer.Tick += (_, _) => _ = Task.Run(FetchWeatherAsync);
            _weatherTimer.Start();
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (_weatherSvc != null)
            BeginInvoke(async () => await FetchWeatherAsync());
    }

    private async Task FetchWeatherAsync()
    {
        if (_weatherSvc == null) return;
        var snap = await _weatherSvc.GetWeatherAsync();
        if (snap == null) return;
        if (InvokeRequired) BeginInvoke(() => { _weather = snap; Invalidate(); });
        else                { _weather = snap; Invalidate(); }
    }

    /// <summary>Hot-swap the weather service after settings change.</summary>
    public void UpdateWeatherService(WeatherService? newSvc)
    {
        var old = _weatherSvc;
        _weatherSvc = newSvc;
        old?.Dispose();

        _weatherTimer?.Stop();
        _weatherTimer?.Dispose();
        _weatherTimer = null;
        _weather = null;
        Invalidate();

        if (_weatherSvc == null) return;

        _weatherTimer = new System.Windows.Forms.Timer { Interval = 10 * 60 * 1000 };
        _weatherTimer.Tick += (_, _) => _ = Task.Run(FetchWeatherAsync);
        _weatherTimer.Start();
        _ = Task.Run(FetchWeatherAsync);
    }

    private void LayoutPanel()
    {
        // Header: 52px top. Weather footer: WeatherH px bottom. Scroll fills the middle.
        _scrollContainer.SetBounds(0, 52, ClientSize.Width, ClientSize.Height - 52 - WeatherH);
        _refreshButton.SetBounds(ClientSize.Width - 28, 8, 22, 22);
    }

    private void OnRefreshTick(object? sender, EventArgs e)
    {
        try
        {
            var current = _rows.Select(r => r.SessionName).OrderBy(n => n).ToList();
            var fetched = _sessionService.GetSessionsForDefaultDevice();
            var fresh   = fetched
                             .Select(s => s.DisplayName)
                             .Where(n => !n.StartsWith("ArmoryCrate", StringComparison.OrdinalIgnoreCase) &&
                                         !n.StartsWith("AMDRSServ",   StringComparison.OrdinalIgnoreCase))
                             .OrderBy(n => n)
                             .ToList();
            foreach (var s in fetched) s.Dispose();
            if (!current.SequenceEqual(fresh, StringComparer.OrdinalIgnoreCase))
                LoadSessions();
        }
        catch { }
    }

    public void LoadSessions()
    {
        SuspendLayout();
        _scrollContainer.SuspendLayout();

        foreach (var row in _rows)
        {
            _scrollContainer.Controls.Remove(row);
            row.Dispose();
        }
        _rows.Clear();

        try
        {
            var sessions = _sessionService.GetSessionsForDefaultDevice();
            int y = 0;
            foreach (var session in sessions)
            {
                var name = session.DisplayName ?? "";
                if (name.StartsWith("ArmoryCrate", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("AMDRSServ",   StringComparison.OrdinalIgnoreCase))
                {
                    session.Dispose();
                    continue;
                }
                var row = new AppSessionRow(session)
                {
                    Left   = 0,
                    Top    = y,
                    Width  = _scrollContainer.ClientSize.Width,
                    Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
                };
                _rows.Add(row);
                _scrollContainer.Controls.Add(row);
                y += row.Height;
            }
        }
        catch { }

        _scrollContainer.ResumeLayout(true);
        ResumeLayout(true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
        => AppTheme.PaintBackground(e.Graphics, this, AppTheme.BgMain);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode     = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        int x = 16, w = ClientSize.Width - 32;
        int cx = ClientSize.Width / 2;

        using var hBrush = new SolidBrush(AppTheme.TextMuted);
        g.DrawString("APP VOLUME MIXER", AppTheme.FontPanelHeader, hBrush, x, 14);

        using var sepPen = new Pen(AppTheme.Border);
        g.DrawLine(sepPen, x, 46, x + w, 46);

        // ── Weather footer ────────────────────────────────────────────────────
        int wy = ClientSize.Height - WeatherH;
        g.DrawLine(sepPen, x, wy, x + w, wy);
        wy += 18;

        var wFmt = new StringFormat { Alignment = StringAlignment.Center };

        if (_weather != null)
        {
            DrawWeatherIcon(g, _weather.Condition, cx, wy);
            wy += 56;

            using var condBrush   = new SolidBrush(AppTheme.TextPrimary);
            using var detailBrush = new SolidBrush(AppTheme.TextSecondary);

            var condRect = new RectangleF(0, wy, ClientSize.Width, 26);
            g.DrawString(_weather.Condition, AppTheme.FontClockDate, condBrush, condRect, wFmt);
            wy += 28;

            string hiLo   = $"H: {_weather.TempHigh:F0}{_weather.TempUnit}   L: {_weather.TempLow:F0}{_weather.TempUnit}";
            string wind    = $"Wind: {_weather.WindSpeed:F0} {_weather.WindUnit}";
            var detailRect = new RectangleF(0, wy, ClientSize.Width, 20);
            g.DrawString($"{hiLo}     {wind}", AppTheme.FontLabel, detailBrush, detailRect, wFmt);
        }
        else
        {
            using var placeholderBr = new SolidBrush(AppTheme.TextMuted);
            string msg = _weatherSvc == null ? "Weather not configured" : "Loading weather…";
            var msgRect = new RectangleF(0, wy + 16, ClientSize.Width, 22);
            g.DrawString(msg, AppTheme.FontLabel, placeholderBr, msgRect, wFmt);
        }
    }

    // ── Weather icons (identical to former MediaPanel helpers) ────────────────

    private static void DrawWeatherIcon(Graphics g, string condition, int cx, int top)
    {
        int cy = top + 26;
        switch (condition)
        {
            case "Clear":
                DrawSun(g, cx, cy, 12, 22);
                break;
            case "Partly Cloudy":
                DrawSun(g, cx - 10, cy - 8, 9, 16);
                DrawCloud(g, Color.FromArgb(170, 185, 200), cx + 4, cy + 6);
                break;
            case "Overcast":
                DrawCloud(g, Color.FromArgb(130, 145, 158), cx, cy);
                break;
            case "Foggy":
                DrawFog(g, cx, top);
                break;
            case "Rainy":
                DrawCloud(g, Color.FromArgb(100, 125, 160), cx, cy - 8);
                DrawRain(g, cx, cy + 10);
                break;
            case "Snowy":
                DrawCloud(g, Color.FromArgb(175, 188, 208), cx, cy - 8);
                DrawSnow(g, cx, cy + 10);
                break;
            case "Thunderstorm":
                DrawCloud(g, Color.FromArgb(75, 85, 105), cx, cy - 8);
                DrawLightning(g, cx, cy + 10);
                break;
        }
    }

    private static void DrawSun(Graphics g, int cx, int cy, int innerR, int outerR)
    {
        using var fill = new SolidBrush(Color.FromArgb(255, 196, 40));
        using var ray  = new Pen(Color.FromArgb(255, 196, 40), 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4;
            g.DrawLine(ray,
                cx + (float)(Math.Cos(a) * (innerR + 4)), cy + (float)(Math.Sin(a) * (innerR + 4)),
                cx + (float)(Math.Cos(a) * outerR),       cy + (float)(Math.Sin(a) * outerR));
        }
        g.FillEllipse(fill, cx - innerR, cy - innerR, innerR * 2, innerR * 2);
    }

    private static void DrawCloud(Graphics g, Color color, int cx, int cy)
    {
        using var b = new SolidBrush(color);
        g.FillEllipse(b, cx - 20, cy - 3,  40, 16);
        g.FillEllipse(b, cx - 16, cy - 12, 18, 16);
        g.FillEllipse(b, cx -  5, cy - 15, 20, 18);
        g.FillEllipse(b, cx +  5, cy - 11, 16, 14);
    }

    private static void DrawRain(Graphics g, int cx, int baseY)
    {
        using var pen = new Pen(Color.FromArgb(110, 165, 225), 1.8f)
            { StartCap = LineCap.Round, EndCap = LineCap.Round };
        for (int i = -1; i <= 1; i++)
        {
            int x = cx + i * 11;
            g.DrawLine(pen, x, baseY, x - 5, baseY + 16);
        }
    }

    private static void DrawSnow(Graphics g, int cx, int baseY)
    {
        using var pen = new Pen(Color.FromArgb(195, 220, 255), 1.6f)
            { StartCap = LineCap.Round, EndCap = LineCap.Round };
        int[] xs = { cx - 12, cx, cx + 12 };
        foreach (int x in xs)
        {
            int y = baseY + 8;
            g.DrawLine(pen, x - 5, y,     x + 5, y);
            g.DrawLine(pen, x,     y - 5, x,     y + 5);
            g.DrawLine(pen, x - 4, y - 4, x + 4, y + 4);
            g.DrawLine(pen, x + 4, y - 4, x - 4, y + 4);
        }
    }

    private static void DrawLightning(Graphics g, int cx, int baseY)
    {
        using var pen = new Pen(Color.FromArgb(255, 218, 50), 2.5f)
            { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        PointF[] pts = [
            new(cx + 5, baseY),     new(cx - 2, baseY + 9),
            new(cx + 3, baseY + 9), new(cx - 6, baseY + 22),
        ];
        g.DrawLines(pen, pts);
    }

    private static void DrawFog(Graphics g, int cx, int top)
    {
        using var pen = new Pen(Color.FromArgb(155, 165, 175), 2f)
            { DashStyle = DashStyle.Dash, StartCap = LineCap.Round, EndCap = LineCap.Round };
        int[] widths = { 36, 44, 34, 40 };
        for (int i = 0; i < widths.Length; i++)
        {
            int y = top + 10 + i * 11;
            g.DrawLine(pen, cx - widths[i] / 2, y, cx + widths[i] / 2, y);
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        foreach (var row in _rows)
            row.Width = _scrollContainer.ClientSize.Width;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _weatherTimer?.Stop();
            _weatherTimer?.Dispose();
            _weatherSvc?.Dispose();
        }
        base.Dispose(disposing);
    }
}

using System.Drawing.Drawing2D;
using WinUIAudioMixer.Services;

namespace WinUIAudioMixer.Controls;

/// <summary>
/// Centre-right panel – album art, media controls (prev/play/next), live clock, and weather.
/// Width: ~400 px
/// </summary>
public sealed class MediaPanel : UserControl
{
    private readonly System.Windows.Forms.Timer _clockTimer;
    private System.Windows.Forms.Timer?         _weatherTimer;
    private MediaInfo       _current = new();
    private WeatherService? _weatherSvc;
    private WeatherSnapshot? _weather;

    // Cached bitmaps
    private Bitmap? _thumbScaled;

    // Media buttons
    private readonly Button _btnPrev;
    private readonly Button _btnPlay;
    private readonly Button _btnNext;

    public event Func<Task>? PlayPauseRequested;
    public event Func<Task>? NextRequested;
    public event Func<Task>? PreviousRequested;

    public MediaPanel(WeatherService? weatherSvc = null)
    {
        _weatherSvc = weatherSvc;
        BackColor = AppTheme.BgPanel;
        SetStyle(ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint  |
                 ControlStyles.UserPaint             |
                 ControlStyles.ResizeRedraw, true);

        // Media control buttons
        _btnPrev = MakeMediaButton("⏮", 88);
        _btnPlay = MakeMediaButton("▶", 104);
        _btnNext = MakeMediaButton("⏭", 88);

        _btnPrev.Click += (_, _) => PreviousRequested?.Invoke();
        _btnPlay.Click += (_, _) => PlayPauseRequested?.Invoke();
        _btnNext.Click += (_, _) => NextRequested?.Invoke();

        Controls.AddRange(new Control[] { _btnPrev, _btnPlay, _btnNext });
        Resize += (_, _) => PositionButtons();
        PositionButtons();

        // Clock tick
        _clockTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _clockTimer.Tick += (_, _) => InvalidateClock();
        _clockTimer.Start();

        // Weather refresh every 10 minutes
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

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Hot-swap the weather service after settings change.
    /// Disposes the old service, starts the timer if needed, and triggers an immediate fetch.
    /// </summary>
    public void UpdateWeatherService(WeatherService? newSvc)
    {
        // Dispose old service (MediaPanel owns it)
        var old = _weatherSvc;
        _weatherSvc = newSvc;
        old?.Dispose();

        // Stop the existing timer
        _weatherTimer?.Stop();
        _weatherTimer?.Dispose();
        _weatherTimer = null;

        // Clear stale snapshot so the placeholder shows if there's no service
        _weather = null;
        Invalidate();

        if (_weatherSvc == null) return;

        // Restart 10-minute refresh timer
        _weatherTimer = new System.Windows.Forms.Timer { Interval = 10 * 60 * 1000 };
        _weatherTimer.Tick += (_, _) => _ = Task.Run(FetchWeatherAsync);
        _weatherTimer.Start();

        // Fetch immediately
        _ = Task.Run(FetchWeatherAsync);
    }

    public void UpdateMedia(MediaInfo info)
    {
        _thumbScaled?.Dispose();
        _thumbScaled = null;

        if (info.Thumbnail != null)
        {
            const int thumbSize = 160;
            _thumbScaled = new Bitmap(thumbSize, thumbSize);
            using var g = Graphics.FromImage(_thumbScaled);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(info.Thumbnail, 0, 0, thumbSize, thumbSize);
        }

        _current = info;
        _btnPlay.Text = info.IsPlaying ? "⏸" : "▶";
        Invalidate();
    }

    // ── Layout ────────────────────────────────────────────────────────────────

    private void PositionButtons()
    {
        int cx = ClientSize.Width / 2;
        // Buttons centred; shifted up to stay above the separator at y=430
        // Total width = 88 + 8 + 104 + 8 + 88 = 296, centred on cx
        int by = 326;
        _btnPrev.SetBounds(cx - 148, by, 88, 88);
        _btnPlay.SetBounds(cx -  52, by, 104, 88);
        _btnNext.SetBounds(cx +  60, by, 88, 88);
    }

    private void InvalidateClock() => Invalidate();

    // ── Painting ──────────────────────────────────────────────────────────────

    protected override void OnPaintBackground(PaintEventArgs e)
        => AppTheme.PaintBackground(e.Graphics, this, AppTheme.BgPanel);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g  = e.Graphics;
        int cx = ClientSize.Width / 2;

        g.SmoothingMode    = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // ── Section header ────────────────────────────────────────────────────
        using var mutedBrush = new SolidBrush(AppTheme.TextMuted);
        g.DrawString("MEDIA CONTROLS", AppTheme.FontPanelHeader, mutedBrush, 16, 14);

        using var sepPen = new Pen(AppTheme.Border);
        g.DrawLine(sepPen, 16, 44, ClientSize.Width - 16, 44);

        // ── Album art ─────────────────────────────────────────────────────────
        const int thumbSize = 152;
        int tx = cx - thumbSize / 2, ty = 54;
        var thumbRect = new Rectangle(tx, ty, thumbSize, thumbSize);

        if (_thumbScaled != null)
        {
            // Draw with rounded corners
            using var clipPath = RoundedRect(thumbRect, 12);
            g.SetClip(clipPath);
            g.DrawImage(_thumbScaled, thumbRect);
            g.ResetClip();
        }
        else if (_current.HasSession)
        {
            // Placeholder gradient
            using var bg1 = new LinearGradientBrush(thumbRect,
                Color.FromArgb(50, 50, 80), Color.FromArgb(30, 30, 50), 135f);
            using var clipPath = RoundedRect(thumbRect, 12);
            g.FillPath(bg1, clipPath);

            using var noteBrush = new SolidBrush(Color.FromArgb(80, 255, 255, 255));
            g.DrawString("♪", new Font("Segoe UI", 40), noteBrush,
                thumbRect, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
        }
        else
        {
            using var placeholderBrush = new SolidBrush(AppTheme.BgCard);
            g.FillRoundedRectangle(placeholderBrush, tx, ty, thumbSize, thumbSize, 12);
        }

        // ── Track info ────────────────────────────────────────────────────────
        int infoY = ty + thumbSize + 14;
        var titleFmt  = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        var artistFmt = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };

        using var titleBrush  = new SolidBrush(_current.HasSession ? AppTheme.TextPrimary : AppTheme.TextMuted);
        using var artistBrush = new SolidBrush(AppTheme.TextSecondary);

        string title  = _current.HasSession ? (string.IsNullOrWhiteSpace(_current.Title)  ? "Nothing Playing"  : _current.Title) : "No media";
        string artist = _current.HasSession ? (string.IsNullOrWhiteSpace(_current.Artist) ? ""                 : _current.Artist) : "";

        var titleRect  = new RectangleF(16, infoY,      ClientSize.Width - 32, 24);
        var artistRect = new RectangleF(16, infoY + 28, ClientSize.Width - 32, 26);

        g.DrawString(title,  AppTheme.FontMedia,    titleBrush,  titleRect,  titleFmt);
        g.DrawString(artist, AppTheme.FontMediaSub, artistBrush, artistRect, artistFmt);

        // ── Separator before clock ────────────────────────────────────────────
        int divY = 430;
        g.DrawLine(sepPen, 16, divY, ClientSize.Width - 16, divY);

        // ── Clock ─────────────────────────────────────────────────────────────
        var now    = DateTime.Now;
        string time = now.ToString("h:mm:ss tt");
        string date = now.ToString("dddd, MMMM d");

        int clockY = divY + 20;
        var timeFmt = new StringFormat { Alignment = StringAlignment.Center };
        var dateFmt = new StringFormat { Alignment = StringAlignment.Center };

        using var clockBrush = new SolidBrush(AppTheme.TextPrimary);
        using var dateBrush  = new SolidBrush(AppTheme.TextSecondary);

        var timeRect = new RectangleF(0, clockY, ClientSize.Width, 82);
        var dateRect = new RectangleF(0, clockY + 84, ClientSize.Width, 28);

        g.DrawString(time, AppTheme.FontClock,     clockBrush, timeRect, timeFmt);
        g.DrawString(date, AppTheme.FontClockDate, dateBrush,  dateRect, dateFmt);

        // ── Weather ───────────────────────────────────────────────────────────
        {
            int wy = clockY + 84 + 28 + 10; // just below the date

            g.DrawLine(sepPen, 16, wy, ClientSize.Width - 16, wy);
            wy += 12;

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

                string hiLo = $"H: {_weather.TempHigh:F0}{_weather.TempUnit}   " +
                              $"L: {_weather.TempLow:F0}{_weather.TempUnit}";
                string wind = $"Wind: {_weather.WindSpeed:F0} {_weather.WindUnit}";
                string detail = $"{hiLo}     {wind}";

                var detailRect = new RectangleF(0, wy, ClientSize.Width, 20);
                g.DrawString(detail, AppTheme.FontLabel, detailBrush, detailRect, wFmt);
            }
            else
            {
                using var placeholderBr = new SolidBrush(AppTheme.TextMuted);
                string msg = _weatherSvc == null
                    ? "Weather not configured"
                    : "Loading weather…";
                var msgRect = new RectangleF(0, wy + 16, ClientSize.Width, 22);
                g.DrawString(msg, AppTheme.FontLabel, placeholderBr, msgRect, wFmt);
            }
        }
    }

    // ── Weather icons ─────────────────────────────────────────────────────────

    /// <summary>Draws a 52×52 weather icon centred at (cx, top+26).</summary>
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
                cx + (float)(Math.Cos(a) * (innerR + 4)),
                cy + (float)(Math.Sin(a) * (innerR + 4)),
                cx + (float)(Math.Cos(a) * outerR),
                cy + (float)(Math.Sin(a) * outerR));
        }
        g.FillEllipse(fill, cx - innerR, cy - innerR, innerR * 2, innerR * 2);
    }

    private static void DrawCloud(Graphics g, Color color, int cx, int cy)
    {
        using var b = new SolidBrush(color);
        g.FillEllipse(b, cx - 20, cy - 3,  40, 16); // base body
        g.FillEllipse(b, cx - 16, cy - 12, 18, 16); // left bump
        g.FillEllipse(b, cx -  5, cy - 15, 20, 18); // centre bump
        g.FillEllipse(b, cx +  5, cy - 11, 16, 14); // right bump
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
        PointF[] pts =
        [
            new(cx + 5,  baseY),
            new(cx - 2,  baseY + 9),
            new(cx + 3,  baseY + 9),
            new(cx - 6,  baseY + 22),
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, radius * 2, radius * 2, 180, 90);
        path.AddArc(r.Right - radius * 2, r.Y, radius * 2, radius * 2, 270, 90);
        path.AddArc(r.Right - radius * 2, r.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
        path.AddArc(r.X, r.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Button MakeMediaButton(string symbol, int width) =>
        new()
        {
            Text      = symbol,
            Font      = new Font("Segoe UI", 32f),
            BackColor = AppTheme.BgCard,
            ForeColor = AppTheme.TextPrimary,
            FlatStyle = FlatStyle.Flat,
            Size      = new Size(width, 88),
            Cursor    = Cursors.Hand,
            FlatAppearance = { BorderSize = 0,
                MouseOverBackColor = AppTheme.Accent,
                MouseDownBackColor = AppTheme.AccentHover },
        };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _clockTimer.Stop();
            _clockTimer.Dispose();
            _weatherTimer?.Stop();
            _weatherTimer?.Dispose();
            _weatherSvc?.Dispose();
            _thumbScaled?.Dispose();
        }
        base.Dispose(disposing);
    }
}

using System.Drawing.Drawing2D;
using WinUIAudioMixer.Services;

namespace WinUIAudioMixer.Controls;

/// <summary>
/// Centre-right panel – album art, media controls (prev/play/next), and a live clock.
/// Width: ~400 px
/// </summary>
public sealed class MediaPanel : UserControl
{
    private readonly System.Windows.Forms.Timer _clockTimer;
    private MediaInfo _current = new();

    // Cached bitmaps
    private Bitmap? _thumbScaled;

    // Media buttons
    private readonly Button _btnPrev;
    private readonly Button _btnPlay;
    private readonly Button _btnNext;

    public event Func<Task>? PlayPauseRequested;
    public event Func<Task>? NextRequested;
    public event Func<Task>? PreviousRequested;

    public MediaPanel()
    {
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
    }

    // ── Public API ────────────────────────────────────────────────────────────

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

    private void InvalidateClock() => Invalidate(ClockRegion());

    private Rectangle ClockRegion()
    {
        int w = ClientSize.Width, h = ClientSize.Height;
        return new Rectangle(0, h / 2 + 30, w, h / 2 - 30);
    }

    // ── Painting ──────────────────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        var g  = e.Graphics;
        int cx = ClientSize.Width / 2;

        g.SmoothingMode    = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // ── Section header ────────────────────────────────────────────────────
        using var mutedBrush = new SolidBrush(AppTheme.TextMuted);
        g.DrawString("MEDIA CONTROLS", AppTheme.FontSectionHeader, mutedBrush, 16, 14);

        using var sepPen = new Pen(AppTheme.Border);
        g.DrawLine(sepPen, 16, 36, ClientSize.Width - 16, 36);

        // ── Album art ─────────────────────────────────────────────────────────
        const int thumbSize = 152;
        int tx = cx - thumbSize / 2, ty = 48;
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
        var artistFmt = new StringFormat { Alignment = StringAlignment.Center };

        using var titleBrush  = new SolidBrush(_current.HasSession ? AppTheme.TextPrimary : AppTheme.TextMuted);
        using var artistBrush = new SolidBrush(AppTheme.TextSecondary);

        string title  = _current.HasSession ? (string.IsNullOrWhiteSpace(_current.Title)  ? "Nothing Playing"  : _current.Title) : "No media";
        string artist = _current.HasSession ? (string.IsNullOrWhiteSpace(_current.Artist) ? ""                 : _current.Artist) : "";

        var titleRect  = new RectangleF(16, infoY,      ClientSize.Width - 32, 24);
        var artistRect = new RectangleF(16, infoY + 26, ClientSize.Width - 32, 20);

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

        var timeRect = new RectangleF(0, clockY, ClientSize.Width, 70);
        var dateRect = new RectangleF(0, clockY + 72, ClientSize.Width, 28);

        g.DrawString(time, AppTheme.FontClock,     clockBrush, timeRect, timeFmt);
        g.DrawString(date, AppTheme.FontClockDate, dateBrush,  dateRect, dateFmt);
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
            _thumbScaled?.Dispose();
        }
        base.Dispose(disposing);
    }
}

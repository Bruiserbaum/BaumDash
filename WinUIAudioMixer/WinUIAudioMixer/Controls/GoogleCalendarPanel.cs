using System.Drawing.Drawing2D;
using WinUIAudioMixer.Services;

namespace WinUIAudioMixer.Controls;

/// <summary>
/// Top half: month calendar grid with event-day highlights.
/// Bottom half: scrollable list of upcoming events.
/// </summary>
public sealed class GoogleCalendarPanel : UserControl
{
    private readonly GoogleCalendarService? _svc;
    private List<CalendarEvent> _events = new();
    private string _statusText = "";
    private bool _loading;
    private DateTime _viewMonth;

    private readonly Button  _refreshBtn;
    private readonly Button  _prevMonthBtn;
    private readonly Button  _nextMonthBtn;
    private readonly Control _monthPanel;  // CalendarGrid — file-scoped, stored as Control
    private readonly Panel   _scrollPanel;

    private const int HeaderH    = 44;
    private const int MonthAreaH = 32 + 22 + 32 * 6; // NavH + DowH + 6 rows = 246

    public GoogleCalendarPanel(GoogleCalendarService? svc)
    {
        _svc       = svc;
        _viewMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        BackColor  = AppTheme.BgPanel;
        SetStyle(ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint  |
                 ControlStyles.UserPaint             |
                 ControlStyles.ResizeRedraw, true);

        _prevMonthBtn = MakeNavButton("◀");
        _prevMonthBtn.Click += (_, _) =>
        {
            _viewMonth = _viewMonth.AddMonths(-1);
            ((CalendarGrid)_monthPanel).SetData(_viewMonth, BuildEventDays());
        };

        _nextMonthBtn = MakeNavButton("▶");
        _nextMonthBtn.Click += (_, _) =>
        {
            _viewMonth = _viewMonth.AddMonths(1);
            ((CalendarGrid)_monthPanel).SetData(_viewMonth, BuildEventDays());
        };

        _refreshBtn = new Button
        {
            Text      = "↻  Refresh",
            Font      = AppTheme.FontBold,
            ForeColor = AppTheme.TextSecondary,
            BackColor = AppTheme.BgCard,
            FlatStyle = FlatStyle.Flat,
            Cursor    = Cursors.Hand,
            Size      = new Size(100, 28),
            FlatAppearance = { BorderSize = 0, MouseOverBackColor = AppTheme.Accent },
        };
        _refreshBtn.Click += (_, _) => _ = LoadAsync();

        var grid = new CalendarGrid();
        grid.SetData(_viewMonth, BuildEventDays());
        _monthPanel = grid;

        _scrollPanel = new Panel
        {
            AutoScroll = true,
            BackColor  = Color.Transparent,
        };
        AppTheme.ApplyDarkScrollBar(_scrollPanel);

        Controls.Add(_scrollPanel);
        Controls.Add(_monthPanel);
        Controls.Add(_prevMonthBtn);
        Controls.Add(_nextMonthBtn);
        Controls.Add(_refreshBtn);

        Resize += (_, _) => LayoutControls();
        LayoutControls();
    }

    // ── Public ────────────────────────────────────────────────────────────────

    public async Task LoadAsync()
    {
        if (_loading) return;
        _loading    = true;
        _statusText = "Loading…";
        RebuildCards();

        try
        {
            if (_svc == null || !_svc.IsConfigured)
            {
                _statusText = "Add your Google Calendar iCal URL in Settings → CALENDAR.";
                _events     = new();
            }
            else
            {
                var (events, errors) = await _svc.FetchUpcomingAsync();
                _events = events;

                if (errors.Count > 0 && events.Count == 0)
                    _statusText = string.Join("\n", errors);
                else if (errors.Count > 0)
                    _statusText = string.Join("\n", errors);
                else
                    _statusText = events.Count == 0 ? "No upcoming events in the next 60 days." : "";
            }
        }
        catch (Exception ex)
        {
            _statusText = $"Error: {ex.Message}";
            _events     = new();
        }

        _loading = false;
        if (IsHandleCreated)
            BeginInvoke(() =>
            {
                ((CalendarGrid)_monthPanel).SetData(_viewMonth, BuildEventDays());
                RebuildCards();
            });
    }

    // ── Layout ────────────────────────────────────────────────────────────────

    private void LayoutControls()
    {
        int w = ClientSize.Width;

        _refreshBtn.Location = new Point(w - _refreshBtn.Width - 10, 8);

        int monthTop = HeaderH + 4;
        _prevMonthBtn.SetBounds(8,      monthTop + 4, 28, 24);
        _nextMonthBtn.SetBounds(w - 36, monthTop + 4, 28, 24);
        _monthPanel.SetBounds(0, monthTop, w, MonthAreaH);

        int listTop = monthTop + MonthAreaH + 8;
        _scrollPanel.SetBounds(0, listTop, w, Math.Max(1, ClientSize.Height - listTop));

        RebuildCards();
    }

    // ── Event list ────────────────────────────────────────────────────────────

    private void RebuildCards()
    {
        _scrollPanel.Controls.Clear();

        // Reserve scrollbar width so adding content never triggers a horizontal bar
        int contentW = Math.Max(1, _scrollPanel.Width - SystemInformation.VerticalScrollBarWidth - 16);
        int y = 8;

        if (!string.IsNullOrEmpty(_statusText))
        {
            var lines = _statusText.Split('\n');
            foreach (var line in lines)
            {
                bool isError = line.StartsWith("HTTP ") || line.StartsWith("Failed");
                _scrollPanel.Controls.Add(new Label
                {
                    Text      = line,
                    Font      = AppTheme.FontLabel,
                    ForeColor = isError ? AppTheme.Danger : AppTheme.TextMuted,
                    BackColor = Color.Transparent,
                    AutoSize  = false,
                    Size      = new Size(contentW, 20),
                    Location  = new Point(12, y),
                    TextAlign = ContentAlignment.MiddleLeft,
                });
                y += 24;
            }
            if (_events.Count == 0)
            {
                _scrollPanel.AutoScrollMinSize = new Size(1, y + 8);
                return;
            }
            y += 4;
        }

        string? lastDayHeader = null;
        foreach (var ev in _events)
        {
            string dayKey = ev.Start.ToString("yyyy-MM-dd");
            if (dayKey != lastDayHeader)
            {
                lastDayHeader = dayKey;
                bool isToday    = ev.Start.Date == DateTime.Today;
                bool isTomorrow = ev.Start.Date == DateTime.Today.AddDays(1);
                string label = isToday    ? $"TODAY  –  {ev.Start:dddd, MMMM d}"
                             : isTomorrow ? $"TOMORROW  –  {ev.Start:dddd, MMMM d}"
                             : ev.Start.ToString("dddd, MMMM d");

                _scrollPanel.Controls.Add(new Label
                {
                    Text      = label,
                    Font      = AppTheme.FontSectionHeader,
                    ForeColor = isToday ? AppTheme.Accent : AppTheme.TextMuted,
                    BackColor = Color.Transparent,
                    AutoSize  = false,
                    Size      = new Size(contentW, 20),
                    Location  = new Point(12, y),
                });
                y += 26;
            }

            var card = new EventCard(ev, contentW);
            card.Location = new Point(12, y);
            _scrollPanel.Controls.Add(card);
            y += card.Height + 6;
        }

        _scrollPanel.AutoScrollMinSize = new Size(1, y + 8);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private HashSet<DateTime> BuildEventDays() =>
        _events.Select(e => e.Start.Date).ToHashSet();

    private static Button MakeNavButton(string text) => new()
    {
        Text      = text,
        Font      = AppTheme.FontBold,
        ForeColor = AppTheme.TextMuted,
        BackColor = Color.Transparent,
        FlatStyle = FlatStyle.Flat,
        Cursor    = Cursors.Hand,
        FlatAppearance = { BorderSize = 0, MouseOverBackColor = AppTheme.BgCard },
    };

    // ── Painting ──────────────────────────────────────────────────────────────

    protected override void OnPaintBackground(PaintEventArgs e)
        => AppTheme.PaintBackground(e.Graphics, this, AppTheme.BgPanel);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using var mutedBrush = new SolidBrush(AppTheme.TextMuted);
        g.DrawString("GOOGLE CALENDAR", AppTheme.FontPanelHeader, mutedBrush, 12, 10);

        using var sepPen = new Pen(AppTheme.Border);
        g.DrawLine(sepPen, 12, HeaderH - 2, ClientSize.Width - 12, HeaderH - 2);

        // Separator between month grid and event list
        int sepY = HeaderH + 4 + MonthAreaH + 4;
        g.DrawLine(sepPen, 12, sepY, ClientSize.Width - 12, sepY);
    }
}

// ── Month calendar grid ───────────────────────────────────────────────────────

file sealed class CalendarGrid : Control
{
    private DateTime          _viewMonth;
    private HashSet<DateTime> _eventDays = new();

    private const int NavH = 32;
    private const int DowH = 22;
    private const int RowH = 32;
    private const int Rows = 6;

    public CalendarGrid()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint  |
                 ControlStyles.UserPaint             |
                 ControlStyles.ResizeRedraw, true);
    }

    public void SetData(DateTime viewMonth, HashSet<DateTime> eventDays)
    {
        _viewMonth = viewMonth;
        _eventDays = eventDays;
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
        => AppTheme.PaintBackground(e.Graphics, this, AppTheme.BgPanel);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.SmoothingMode     = SmoothingMode.AntiAlias;

        int w        = ClientSize.Width;
        int cellW    = (w - 16) / 7;
        int gridLeft = 8;

        // ── Month title ───────────────────────────────────────────────────────
        using var titleBrush = new SolidBrush(AppTheme.TextPrimary);
        using var centerFmt  = new StringFormat
            { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(_viewMonth.ToString("MMMM yyyy"), AppTheme.FontBold, titleBrush,
            new RectangleF(36, 0, w - 72, NavH), centerFmt);

        // ── Day-of-week header ────────────────────────────────────────────────
        string[] dow = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
        using var dowBrush = new SolidBrush(AppTheme.TextMuted);
        using var cellFmt  = new StringFormat
            { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        for (int c = 0; c < 7; c++)
            g.DrawString(dow[c], AppTheme.FontSmall, dowBrush,
                new RectangleF(gridLeft + c * cellW, NavH, cellW, DowH), cellFmt);

        using var sepPen = new Pen(AppTheme.Border);
        g.DrawLine(sepPen, gridLeft, NavH + DowH - 1, gridLeft + cellW * 7, NavH + DowH - 1);

        // ── Day cells ─────────────────────────────────────────────────────────
        var today     = DateTime.Today;
        int startDay  = (int)_viewMonth.DayOfWeek;
        int daysInMon = DateTime.DaysInMonth(_viewMonth.Year, _viewMonth.Month);

        for (int row = 0; row < Rows; row++)
        {
            for (int col = 0; col < 7; col++)
            {
                int dayNum = row * 7 + col - startDay + 1;
                if (dayNum < 1 || dayNum > daysInMon) continue;

                var  cellDate = new DateTime(_viewMonth.Year, _viewMonth.Month, dayNum);
                bool isToday  = cellDate == today;
                bool hasEvent = _eventDays.Contains(cellDate);

                int cellX = gridLeft + col * cellW;
                int cellY = NavH + DowH + row * RowH;

                // Background
                if (isToday)
                {
                    int r  = Math.Min(cellW, RowH) / 2 - 3;
                    int cx = cellX + cellW / 2;
                    int cy = cellY + RowH / 2;
                    using var todayBr = new SolidBrush(AppTheme.Accent);
                    g.FillEllipse(todayBr, cx - r, cy - r, r * 2, r * 2);
                }
                else if (hasEvent)
                {
                    using var evBgBr = new SolidBrush(AppTheme.BgCard);
                    g.FillRectangle(evBgBr, cellX + 2, cellY + 2, cellW - 4, RowH - 4);
                }

                // Day number
                var numColor = isToday  ? Color.White
                             : hasEvent ? AppTheme.Accent
                             : AppTheme.TextSecondary;
                using (var numBr = new SolidBrush(numColor))
                    g.DrawString(dayNum.ToString(), AppTheme.FontSmall, numBr,
                        new RectangleF(cellX, cellY, cellW, RowH), cellFmt);

                // Event dot below number (not shown for today)
                if (hasEvent && !isToday)
                {
                    using var dotBr = new SolidBrush(AppTheme.Accent);
                    g.FillEllipse(dotBr, cellX + cellW / 2 - 2, cellY + RowH - 7, 4, 4);
                }
            }
        }
    }
}

// ── Individual event card ─────────────────────────────────────────────────────

file sealed class EventCard : Control
{
    private readonly CalendarEvent _ev;

    public EventCard(CalendarEvent ev, int width)
    {
        _ev       = ev;
        Height    = string.IsNullOrWhiteSpace(ev.Description) ? 44 : 62;
        Width     = width;
        BackColor = AppTheme.BgCard;
        SetStyle(ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint  |
                 ControlStyles.UserPaint, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g      = e.Graphics;
        bool allDay = _ev.Start.TimeOfDay == TimeSpan.Zero && _ev.End.TimeOfDay == TimeSpan.Zero;

        using var accentBrush = new SolidBrush(AppTheme.Accent);
        g.FillRectangle(accentBrush, 0, 0, 3, Height);

        string timeStr = allDay ? "All day"
            : _ev.Start.Date == _ev.End.Date
                ? $"{_ev.Start:h:mm tt} – {_ev.End:h:mm tt}"
                : $"{_ev.Start:MMM d h:mm tt} – {_ev.End:MMM d h:mm tt}";

        using var timeBrush  = new SolidBrush(AppTheme.TextMuted);
        using var titleBrush = new SolidBrush(AppTheme.TextPrimary);
        using var descBrush  = new SolidBrush(AppTheme.TextSecondary);

        g.DrawString(timeStr,   AppTheme.FontSmall, timeBrush,  12, 6);
        g.DrawString(_ev.Title, AppTheme.FontBold,  titleBrush, 12, 22);

        if (!string.IsNullOrWhiteSpace(_ev.Description))
        {
            var desc = _ev.Description.Replace("\n", " ");
            if (desc.Length > 80) desc = desc[..80] + "…";
            g.DrawString(desc, AppTheme.FontSmall, descBrush, 12, 42);
        }
    }
}

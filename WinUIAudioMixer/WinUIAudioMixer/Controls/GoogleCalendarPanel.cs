using System.Drawing.Drawing2D;
using WinUIAudioMixer.Services;

namespace WinUIAudioMixer.Controls;

/// <summary>
/// Displays upcoming Google Calendar events fetched from a public iCal feed.
/// </summary>
public sealed class GoogleCalendarPanel : UserControl
{
    private readonly GoogleCalendarService? _svc;
    private List<CalendarEvent> _events = new();
    private string _statusText = "";
    private bool _loading;

    private readonly Button _refreshBtn;
    private readonly Panel  _scrollPanel;

    public GoogleCalendarPanel(GoogleCalendarService? svc)
    {
        _svc      = svc;
        BackColor = AppTheme.BgPanel;
        SetStyle(ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint  |
                 ControlStyles.UserPaint             |
                 ControlStyles.ResizeRedraw, true);

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

        _scrollPanel = new Panel
        {
            AutoScroll = true,
            BackColor  = Color.Transparent,
        };

        Controls.Add(_scrollPanel);
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
                    _statusText = string.Join("\n", errors); // show errors above events
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
        if (IsHandleCreated) BeginInvoke(RebuildCards);
    }

    // ── Layout ────────────────────────────────────────────────────────────────

    private void LayoutControls()
    {
        _refreshBtn.Location = new Point(ClientSize.Width - _refreshBtn.Width - 10, 8);
        _scrollPanel.SetBounds(0, 44, ClientSize.Width, ClientSize.Height - 44);
        RebuildCards();
    }

    // ── Event cards ───────────────────────────────────────────────────────────

    private void RebuildCards()
    {
        _scrollPanel.Controls.Clear();

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
                    Size      = new Size(_scrollPanel.ClientSize.Width - 24, 20),
                    Location  = new Point(12, y),
                    TextAlign = ContentAlignment.MiddleLeft,
                });
                y += 24;
            }
            if (_events.Count == 0) return;
            y += 4; // gap before events
        }
        string? lastDayHeader = null;

        foreach (var ev in _events)
        {
            // Day header
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
                    Size      = new Size(_scrollPanel.ClientSize.Width - 24, 20),
                    Location  = new Point(12, y),
                });
                y += 26;
            }

            // Event card
            var card = new EventCard(ev, _scrollPanel.ClientSize.Width - 24);
            card.Location = new Point(12, y);
            _scrollPanel.Controls.Add(card);
            y += card.Height + 6;
        }
    }

    // ── OnPaint (header) ──────────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using var mutedBrush = new SolidBrush(AppTheme.TextMuted);
        g.DrawString("GOOGLE CALENDAR", AppTheme.FontSectionHeader, mutedBrush, 12, 14);

        using var sepPen = new Pen(AppTheme.Border);
        g.DrawLine(sepPen, 12, 36, ClientSize.Width - 12, 36);
    }
}

// ── Individual event card ─────────────────────────────────────────────────────

file sealed class EventCard : Control
{
    private readonly CalendarEvent _ev;

    public EventCard(CalendarEvent ev, int width)
    {
        _ev       = ev;
        bool multiLine = !string.IsNullOrWhiteSpace(ev.Description);
        Height    = multiLine ? 62 : 44;
        Width     = width;
        BackColor = AppTheme.BgCard;
        SetStyle(ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint  |
                 ControlStyles.UserPaint, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g   = e.Graphics;
        int w   = ClientSize.Width;
        bool allDay = _ev.Start.TimeOfDay == TimeSpan.Zero && _ev.End.TimeOfDay == TimeSpan.Zero;

        // Accent left bar
        using var accentBrush = new SolidBrush(AppTheme.Accent);
        g.FillRectangle(accentBrush, 0, 0, 3, Height);

        // Time
        string timeStr = allDay ? "All day"
            : _ev.Start.Date == _ev.End.Date
                ? $"{_ev.Start:h:mm tt} – {_ev.End:h:mm tt}"
                : $"{_ev.Start:MMM d h:mm tt} – {_ev.End:MMM d h:mm tt}";

        using var timeBrush  = new SolidBrush(AppTheme.TextMuted);
        using var titleBrush = new SolidBrush(AppTheme.TextPrimary);
        using var descBrush  = new SolidBrush(AppTheme.TextSecondary);

        g.DrawString(timeStr, AppTheme.FontSmall, timeBrush, 12, 6);
        g.DrawString(_ev.Title, AppTheme.FontBold, titleBrush, 12, 22);

        if (!string.IsNullOrWhiteSpace(_ev.Description))
        {
            var desc = _ev.Description.Replace("\n", " ");
            if (desc.Length > 80) desc = desc[..80] + "…";
            g.DrawString(desc, AppTheme.FontSmall, descBrush, 12, 42);
        }
    }
}

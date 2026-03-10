using WinUIAudioMixer.Models;
using WinUIAudioMixer.Services;

namespace WinUIAudioMixer.Controls;

/// <summary>
/// Centre-left panel – scrollable list of per-app volume sliders.
/// Width: ~500 px
/// </summary>
public sealed class AppVolumePanel : UserControl
{
    private readonly AudioSessionService _sessionService;
    private readonly Panel  _scrollContainer;
    private readonly Button _refreshButton;
    private readonly List<AppSessionRow> _rows = new();
    private readonly System.Windows.Forms.Timer _refreshTimer;

    public AppVolumePanel(AudioSessionService sessionService)
    {
        _sessionService = sessionService;
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
    }

    private void LayoutPanel()
    {
        // Header painted: leave 52px at top
        _scrollContainer.SetBounds(0, 52, ClientSize.Width, ClientSize.Height - 52);
        _refreshButton.SetBounds(ClientSize.Width - 28, 8, 22, 22);
    }

    private void OnRefreshTick(object? sender, EventArgs e)
    {
        // Only reload if the visible session list has changed to avoid unnecessary flicker
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
        int x = 16, w = ClientSize.Width - 32;

        using var hBrush = new SolidBrush(AppTheme.TextMuted);
        g.DrawString("APP VOLUME MIXER", AppTheme.FontPanelHeader, hBrush, x, 14);

        using var sepPen = new Pen(AppTheme.Border);
        g.DrawLine(sepPen, x, 46, x + w, 46);
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
        }
        base.Dispose(disposing);
    }
}

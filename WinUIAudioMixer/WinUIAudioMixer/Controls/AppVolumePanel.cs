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
    private readonly Panel _scrollContainer;
    private readonly List<AppSessionRow> _rows = new();

    public AppVolumePanel(AudioSessionService sessionService)
    {
        _sessionService = sessionService;
        BackColor = AppTheme.BgMain;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        _scrollContainer = new Panel
        {
            AutoScroll  = true,
            BackColor   = Color.Transparent,
        };

        Controls.Add(_scrollContainer);
        Resize += (_, _) => LayoutPanel();
        LayoutPanel();
        LoadSessions();
    }

    private void LayoutPanel()
    {
        // Header painted: leave 46px at top
        _scrollContainer.SetBounds(0, 44, ClientSize.Width, ClientSize.Height - 44);
    }

    public void LoadSessions()
    {
        SuspendLayout();
        _scrollContainer.SuspendLayout();

        // Dispose old rows
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
                    name.StartsWith("AMDRSServ", StringComparison.OrdinalIgnoreCase))
                {
                    session.Dispose();
                    continue;
                }
                var row = new AppSessionRow(session)
                {
                    Left  = 0,
                    Top   = y,
                    Width = _scrollContainer.ClientSize.Width,
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

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        int x = 16, w = ClientSize.Width - 32;

        using var hBrush = new SolidBrush(AppTheme.TextMuted);
        g.DrawString("APP VOLUME MIXER", AppTheme.FontSectionHeader, hBrush, x, 14);

        using var sepPen = new Pen(AppTheme.Border);
        g.DrawLine(sepPen, x, 38, x + w, 38);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        foreach (var row in _rows)
            row.Width = _scrollContainer.ClientSize.Width;
    }
}

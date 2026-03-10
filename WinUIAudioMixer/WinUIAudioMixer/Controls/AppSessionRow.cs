using System.Drawing.Drawing2D;
using WinUIAudioMixer.Models;

namespace WinUIAudioMixer.Controls;

/// <summary>
/// Single row in the app-volume mixer:  [Icon] [Name ......] [Slider====] [75%]
/// </summary>
public sealed class AppSessionRow : UserControl
{
    private readonly AudioSessionItem _session;
    private readonly DarkSlider _slider;
    private readonly Label _nameLabel;
    private readonly Label _pctLabel;
    private readonly PictureBox _iconBox;

    public string SessionName => _session.DisplayName;

    public AppSessionRow(AudioSessionItem session)
    {
        _session = session;
        Height   = 46;
        BackColor = AppTheme.BgCard;

        SetStyle(ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint, true);

        // Icon
        _iconBox = new PictureBox
        {
            Size      = new Size(22, 22),
            SizeMode  = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
        };
        TryLoadIcon();

        // Name label
        _nameLabel = new Label
        {
            Text      = session.DisplayName,
            Font      = AppTheme.FontLabel,
            ForeColor = AppTheme.TextPrimary,
            BackColor = Color.Transparent,
            AutoSize  = false,
            Height    = 22,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        // Percent label
        _pctLabel = new Label
        {
            Text      = $"{session.VolumePercent}%",
            Font      = AppTheme.FontSmall,
            ForeColor = AppTheme.TextSecondary,
            BackColor = Color.Transparent,
            AutoSize  = false,
            Width     = 36,
            Height    = 22,
            TextAlign = ContentAlignment.MiddleRight,
        };

        // Slider
        _slider = new DarkSlider { Value = session.Volume };
        _slider.ValueChanged += OnSliderChanged;

        Controls.AddRange(new Control[] { _iconBox, _nameLabel, _slider, _pctLabel });
        Resize += (_, _) => PositionControls();
        PositionControls();
    }

    private void PositionControls()
    {
        int y    = (Height - 22) / 2;
        int xOff = 10;

        _iconBox.Location  = new Point(xOff, (Height - 22) / 2);
        xOff += 28;

        int nameW  = 130;
        _nameLabel.SetBounds(xOff, y, nameW, 22);
        xOff += nameW + 6;

        int pctW    = 36;
        int sliderW = Width - xOff - pctW - 10;
        if (sliderW < 40) sliderW = 40;

        _slider.SetBounds(xOff, (Height - 26) / 2, sliderW, 26);
        _pctLabel.SetBounds(xOff + sliderW + 2, y, pctW, 22);
    }

    private void OnSliderChanged(object? sender, EventArgs e)
    {
        _session.Volume = _slider.Value;
        _pctLabel.Text  = $"{_slider.ValuePercent}%";
    }

    private void TryLoadIcon()
    {
        if (_session.ProcessId == 0) { SetDefaultIcon(); return; }
        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById((int)_session.ProcessId);
            var file = proc.MainModule?.FileName;
            if (file != null)
            {
                var ico = Icon.ExtractAssociatedIcon(file);
                if (ico != null) { _iconBox.Image = ico.ToBitmap(); return; }
            }
        }
        catch { }
        SetDefaultIcon();
    }

    private void SetDefaultIcon()
    {
        // Draw a simple speaker glyph placeholder
        var bmp = new Bitmap(22, 22);
        using var g  = Graphics.FromImage(bmp);
        using var br = new SolidBrush(AppTheme.TextMuted);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // Simple speaker shape: filled triangle + rectangle
        g.FillRectangle(br, 2, 8, 6, 6);
        g.FillPolygon(br, new[] { new Point(8, 4), new Point(8, 18), new Point(16, 22), new Point(16, 0) });
        _iconBox.Image = bmp;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        // Bottom border
        using var pen = new Pen(AppTheme.Border);
        e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _session.Dispose();
            _iconBox.Image?.Dispose();
        }
        base.Dispose(disposing);
    }
}

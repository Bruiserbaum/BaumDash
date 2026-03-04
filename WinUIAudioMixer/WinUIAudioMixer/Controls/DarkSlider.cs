using System.Drawing.Drawing2D;

namespace WinUIAudioMixer.Controls;

/// <summary>Custom dark-themed horizontal volume slider.</summary>
public sealed class DarkSlider : Control
{
    private float _value;
    private bool  _dragging;

    public event EventHandler? ValueChanged;

    public DarkSlider()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint  |
                 ControlStyles.UserPaint             |
                 ControlStyles.ResizeRedraw, true);
        Height = 28;
        Cursor = Cursors.Hand;
    }

    public float Value
    {
        get => _value;
        set
        {
            value = Math.Clamp(value, 0f, 1f);
            if (Math.Abs(_value - value) < 0.001f) return;
            _value = value;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public int ValuePercent
    {
        get => (int)Math.Round(_value * 100);
        set => Value = value / 100f;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        const int pad    = 10;
        const int trackH = 4;
        const int thumbR = 7;
        int trackY = Height / 2;
        int trackW = Width - pad * 2;
        int fillW  = (int)(trackW * _value);
        int thumbX = pad + fillW;

        // Track background
        using var trackBrush = new SolidBrush(Color.FromArgb(50, 50, 72));
        g.FillRoundedRectangle(trackBrush, pad, trackY - trackH / 2, trackW, trackH, 2);

        // Filled portion
        if (fillW > 0)
        {
            using var fillBrush = new SolidBrush(AppTheme.Accent);
            g.FillRoundedRectangle(fillBrush, pad, trackY - trackH / 2, fillW, trackH, 2);
        }

        // Thumb
        var thumbColor = _dragging ? Color.White : AppTheme.TextPrimary;
        using var thumbBrush = new SolidBrush(thumbColor);
        g.FillEllipse(thumbBrush, thumbX - thumbR, trackY - thumbR, thumbR * 2, thumbR * 2);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        Capture = true;
        UpdateFromX(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging) UpdateFromX(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        Capture = false;
        Invalidate();
    }

    private void UpdateFromX(int x)
    {
        const int pad = 10;
        Value = (float)(x - pad) / (Width - pad * 2);
    }
}

// Extension method for rounded rectangle fills
internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics g, Brush brush,
        int x, int y, int width, int height, int radius)
    {
        if (width <= 0 || height <= 0) return;
        radius = Math.Min(radius, Math.Min(width, height) / 2);
        if (radius <= 0) { g.FillRectangle(brush, x, y, width, height); return; }
        using var path = new GraphicsPath();
        path.AddArc(x, y, radius * 2, radius * 2, 180, 90);
        path.AddArc(x + width - radius * 2, y, radius * 2, radius * 2, 270, 90);
        path.AddArc(x + width - radius * 2, y + height - radius * 2, radius * 2, radius * 2, 0, 90);
        path.AddArc(x, y + height - radius * 2, radius * 2, radius * 2, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }
}

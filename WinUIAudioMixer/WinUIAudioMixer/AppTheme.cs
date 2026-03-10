using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace WinUIAudioMixer;

/// <summary>
/// Shared theme colours and fonts. Colours are non-readonly so they can be changed via
/// Apply* methods before the first control is created, then committed on restart.
/// </summary>
public static class AppTheme
{
    // ── Backgrounds ──────────────────────────────────────────────────────────
    public static Color BgDeep   = Color.FromArgb(13,  13,  26);
    public static Color BgMain   = Color.FromArgb(22,  22,  31);
    public static Color BgPanel  = Color.FromArgb(30,  30,  46);
    public static Color BgCard   = Color.FromArgb(37,  37,  54);

    // ── Accent ───────────────────────────────────────────────────────────────
    public static Color Accent      = Color.FromArgb(88,  101, 242);
    public static Color AccentHover = Color.FromArgb(71,   82, 196);

    // ── Text ─────────────────────────────────────────────────────────────────
    public static Color TextPrimary   = Color.FromArgb(220, 221, 222);
    public static Color TextSecondary = Color.FromArgb(142, 146, 151);
    public static Color TextMuted     = Color.FromArgb(114, 118, 125);

    // ── Status ───────────────────────────────────────────────────────────────
    public static Color Success  = Color.FromArgb(87,  242, 135);
    public static Color Danger   = Color.FromArgb(237,  66,  69);
    public static Color Warning  = Color.FromArgb(254, 231,  92);
    public static Color Muted    = Color.FromArgb(180, 180,  60);

    // ── Structural ───────────────────────────────────────────────────────────
    public static Color Border   = Color.FromArgb(46,   47,  62);
    public static Color Speaking = Color.FromArgb(87,  242, 135);

    // ── Fonts (stable across themes) ─────────────────────────────────────────
    public static readonly Font FontSectionHeader = new("Segoe UI", 7.5f, FontStyle.Bold);
    public static readonly Font FontPanelHeader   = new("Segoe UI", 12f,  FontStyle.Bold);
    public static readonly Font FontPanelSub      = new("Segoe UI", 11f,  FontStyle.Bold);
    public static readonly Font FontLabel         = new("Segoe UI", 9f,   FontStyle.Regular);
    public static readonly Font FontBold          = new("Segoe UI", 9f,   FontStyle.Bold);
    public static readonly Font FontSmall         = new("Segoe UI", 8f,   FontStyle.Regular);
    public static readonly Font FontButton        = new("Segoe UI", 9f,   FontStyle.Bold);
    public static readonly Font FontMedia         = new("Segoe UI", 10f,  FontStyle.Bold);
    public static readonly Font FontMediaSub      = new("Segoe UI", 9f,   FontStyle.Regular);
    public static readonly Font FontClock         = new("Segoe UI Light", 46f, FontStyle.Regular);
    public static readonly Font FontClockDate     = new("Segoe UI",  13f, FontStyle.Regular);

    // ── Background image ─────────────────────────────────────────────────────
    /// <summary>Optional wallpaper rendered behind all panels.</summary>
    public static Image?  BgImage        = null;
    /// <summary>stretch / fill / fit / tile / center</summary>
    public static string  BgImageMode    = "stretch";
    /// <summary>
    /// How opaque the solid panel-colour overlay is painted on top of the image.
    /// 0 = raw image, 255 = solid (no image visible). Default 190 (~75% opaque).
    /// </summary>
    public static int     BgOverlayAlpha = 190;

    // ── Theme presets ─────────────────────────────────────────────────────────

    public static readonly Color DefaultAccent      = Color.FromArgb(88, 101, 242);
    public static readonly Color DefaultAccentHover = Color.FromArgb(71,  82, 196);

    public static void ApplyDark()
    {
        BgDeep         = Color.FromArgb(13,  13,  26);
        BgMain         = Color.FromArgb(22,  22,  31);
        BgPanel        = Color.FromArgb(30,  30,  46);
        BgCard         = Color.FromArgb(37,  37,  54);
        TextPrimary    = Color.FromArgb(220, 221, 222);
        TextSecondary  = Color.FromArgb(142, 146, 151);
        TextMuted      = Color.FromArgb(114, 118, 125);
        Border         = Color.FromArgb(46,   47,  62);
        Accent         = DefaultAccent;
        AccentHover    = DefaultAccentHover;
    }

    public static void ApplyLight()
    {
        BgDeep         = Color.FromArgb(225, 225, 235);
        BgMain         = Color.FromArgb(235, 235, 245);
        BgPanel        = Color.FromArgb(245, 245, 255);
        BgCard         = Color.FromArgb(212, 212, 228);
        TextPrimary    = Color.FromArgb(20,   20,  35);
        TextSecondary  = Color.FromArgb(80,   84,  96);
        TextMuted      = Color.FromArgb(120, 124, 135);
        Border         = Color.FromArgb(195, 195, 215);
        Accent         = DefaultAccent;
        AccentHover    = DefaultAccentHover;
    }

    /// <summary>Dark theme with a custom accent colour.</summary>
    public static void ApplyCustom(Color accent)
    {
        ApplyDark();
        Accent      = accent;
        AccentHover = ControlPaint.Dark(accent, 0.12f);
    }

    /// <summary>Apply a theme from string key; optionally override accent from a hex string.</summary>
    public static void Apply(string theme, string accentHex = "")
    {
        switch (theme.ToLowerInvariant())
        {
            case "light":
                ApplyLight();
                if (ParseHex(accentHex) is Color la) { Accent = la; AccentHover = ControlPaint.Dark(la, 0.12f); }
                break;
            case "custom":
                ApplyCustom(ParseHex(accentHex) ?? Color.FromArgb(88, 101, 242));
                break;
            default: // "dark"
                ApplyDark();
                if (ParseHex(accentHex) is Color da) { Accent = da; AccentHover = ControlPaint.Dark(da, 0.12f); }
                break;
        }
    }

    public static Color? ParseHex(string? hex)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            hex = hex.TrimStart('#');
            if (hex.Length == 6)
                return Color.FromArgb(
                    Convert.ToInt32(hex[0..2], 16),
                    Convert.ToInt32(hex[2..4], 16),
                    Convert.ToInt32(hex[4..6], 16));
        }
        catch { }
        return null;
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    // ── Live theme-change notification ────────────────────────────────────────
    /// <summary>Raised after Apply* is called so subscribers can refresh their UI.</summary>
    public static event Action? ThemeChanged;
    public static void RaiseThemeChanged() => ThemeChanged?.Invoke();

    // ── Background painting helper ────────────────────────────────────────────

    /// <summary>
    /// Paint the control background. If a BgImage is configured the image is rendered
    /// as a shared "wallpaper" (each panel sees its proportional slice of the image),
    /// then a semi-transparent BgOverlayAlpha-opaque fill of <paramref name="panelColor"/>
    /// is drawn on top so text remains legible. Falls back to a solid fill if no image.
    /// Call this from each panel's OnPaintBackground override.
    /// </summary>
    public static void PaintBackground(Graphics g, Control ctrl, Color panelColor)
    {
        int cw = ctrl.Width, ch = ctrl.Height;

        if (BgImage == null || ctrl.FindForm() is not Form form)
        {
            using var solid = new SolidBrush(panelColor);
            g.FillRectangle(solid, 0, 0, cw, ch);
            return;
        }

        try
        {
            var screenPt   = ctrl.PointToScreen(Point.Empty);
            var ctrlOnForm = form.PointToClient(screenPt); // control origin in form client coords
            int fw = form.ClientSize.Width,  fh = form.ClientSize.Height;
            int iw = BgImage.Width,          ih = BgImage.Height;

            if (BgImageMode == "tile")
            {
                using var tb = new TextureBrush(BgImage, WrapMode.Tile);
                tb.TranslateTransform(-ctrlOnForm.X, -ctrlOnForm.Y);
                g.FillRectangle(tb, 0, 0, cw, ch);
            }
            else
            {
                // Compute the "canvas": where the image sits in form-client space
                Rectangle canvas = BgImageMode switch
                {
                    "fill"   => ScaleCanvas(iw, ih, fw, fh, cover: true),
                    "fit"    => ScaleCanvas(iw, ih, fw, fh, cover: false),
                    "center" => new Rectangle((fw - iw) / 2, (fh - ih) / 2, iw, ih),
                    _        => new Rectangle(0, 0, fw, fh), // stretch
                };

                // Fill with BgDeep first (fills empty areas for "fit" / "center")
                using var deepBr = new SolidBrush(BgDeep);
                g.FillRectangle(deepBr, 0, 0, cw, ch);

                // Find the visible intersection of the control with the image canvas
                var ctrlRect = new Rectangle(ctrlOnForm.X, ctrlOnForm.Y, cw, ch);
                var vis      = Rectangle.Intersect(ctrlRect, canvas);
                if (!vis.IsEmpty)
                {
                    float sx = (float)iw / canvas.Width;
                    float sy = (float)ih / canvas.Height;
                    var srcRect  = new RectangleF(
                        (vis.X - canvas.X) * sx, (vis.Y - canvas.Y) * sy,
                        vis.Width * sx,           vis.Height * sy);
                    var destRect = new Rectangle(
                        vis.X - ctrlOnForm.X, vis.Y - ctrlOnForm.Y,
                        vis.Width,            vis.Height);
                    g.DrawImage(BgImage, destRect, srcRect, GraphicsUnit.Pixel);
                }
            }

            // Semi-transparent panel-colour overlay so content stays readable
            using var overlay = new SolidBrush(Color.FromArgb(BgOverlayAlpha, panelColor));
            g.FillRectangle(overlay, 0, 0, cw, ch);
        }
        catch
        {
            using var solid = new SolidBrush(panelColor);
            g.FillRectangle(solid, 0, 0, cw, ch);
        }
    }

    // ── Dark title bar (DWM) ─────────────────────────────────────────────────

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    /// <summary>
    /// Applies a dark title bar and sets the caption background to <see cref="BgDeep"/>
    /// so dialogs don't inherit the Windows system accent colour.
    /// Safe to call in Form.Load or OnHandleCreated.
    /// </summary>
    public static void ApplyDarkTitleBar(IntPtr hwnd)
    {
        try
        {
            // Dark text/buttons in the caption area
            int dark = 1;
            DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, 4);

            // Override caption background colour (Windows 11 21H2+, attr 35)
            // COLORREF is 0x00BBGGRR
            int colorRef = BgDeep.R | (BgDeep.G << 8) | (BgDeep.B << 16);
            DwmSetWindowAttribute(hwnd, 35 /* DWMWA_CAPTION_COLOR */, ref colorRef, 4);
        }
        catch { /* Pre-Win11 — ignore */ }
    }

    // ── Dark scrollbar ────────────────────────────────────────────────────────

    [DllImport("uxtheme.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string? pszSubIdList);

    /// <summary>
    /// Applies the Windows dark-mode scrollbar style to a scrollable control.
    /// Safe to call before or after the control's handle is created.
    /// </summary>
    public static void ApplyDarkScrollBar(Control control)
    {
        void Apply(IntPtr hwnd) => SetWindowTheme(hwnd, "DarkMode_Explorer", null);

        if (control.IsHandleCreated)
            Apply(control.Handle);
        else
            control.HandleCreated += (s, _) => Apply(((Control)s!).Handle);
    }

    private static Rectangle ScaleCanvas(int iw, int ih, int fw, int fh, bool cover)
    {
        float scale = cover
            ? Math.Max((float)fw / iw, (float)fh / ih)
            : Math.Min((float)fw / iw, (float)fh / ih);
        int dw = (int)(iw * scale), dh = (int)(ih * scale);
        return new Rectangle((fw - dw) / 2, (fh - dh) / 2, dw, dh);
    }
}

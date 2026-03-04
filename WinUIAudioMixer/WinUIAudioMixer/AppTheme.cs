namespace WinUIAudioMixer;

/// <summary>Shared dark-theme colours and fonts for the entire app.</summary>
public static class AppTheme
{
    // Backgrounds
    public static readonly Color BgDeep   = Color.FromArgb(13,  13,  26);
    public static readonly Color BgMain   = Color.FromArgb(22,  22,  31);
    public static readonly Color BgPanel  = Color.FromArgb(30,  30,  46);
    public static readonly Color BgCard   = Color.FromArgb(37,  37,  54);

    // Accent
    public static readonly Color Accent      = Color.FromArgb(88,  101, 242);
    public static readonly Color AccentHover = Color.FromArgb(71,  82,  196);

    // Text
    public static readonly Color TextPrimary   = Color.FromArgb(220, 221, 222);
    public static readonly Color TextSecondary = Color.FromArgb(142, 146, 151);
    public static readonly Color TextMuted     = Color.FromArgb(114, 118, 125);

    // Status
    public static readonly Color Success = Color.FromArgb(87,  242, 135);
    public static readonly Color Danger  = Color.FromArgb(237, 66,  69);
    public static readonly Color Warning = Color.FromArgb(254, 231, 92);
    public static readonly Color Muted   = Color.FromArgb(180, 180, 60);

    // Structural
    public static readonly Color Border  = Color.FromArgb(46,  47,  62);
    public static readonly Color Speaking = Color.FromArgb(87, 242, 135);

    // Fonts
    public static readonly Font FontSectionHeader = new("Segoe UI", 7.5f, FontStyle.Bold);
    public static readonly Font FontLabel         = new("Segoe UI", 9f,   FontStyle.Regular);
    public static readonly Font FontBold          = new("Segoe UI", 9f,   FontStyle.Bold);
    public static readonly Font FontSmall         = new("Segoe UI", 8f,   FontStyle.Regular);
    public static readonly Font FontButton        = new("Segoe UI", 9f,   FontStyle.Bold);
    public static readonly Font FontMedia         = new("Segoe UI", 10f,  FontStyle.Bold);
    public static readonly Font FontMediaSub      = new("Segoe UI", 9f,   FontStyle.Regular);
    public static readonly Font FontClock         = new("Segoe UI Light", 46f, FontStyle.Regular);
    public static readonly Font FontClockDate     = new("Segoe UI",  13f, FontStyle.Regular);
}

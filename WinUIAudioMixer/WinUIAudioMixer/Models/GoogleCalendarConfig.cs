namespace WinUIAudioMixer.Models;

public sealed class CalendarEntry
{
    public string Name    { get; set; } = "";
    public string ICalUrl { get; set; } = "";
}

public sealed class GoogleCalendarConfig
{
    /// <summary>Legacy single-URL field — migrated to Calendars on first load.</summary>
    public string? ICalUrl { get; set; }

    public List<CalendarEntry> Calendars { get; set; } = new();
}

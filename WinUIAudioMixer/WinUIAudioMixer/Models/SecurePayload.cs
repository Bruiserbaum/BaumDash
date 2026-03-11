namespace WinUIAudioMixer.Models;

/// <summary>All user secrets — stored encrypted via Windows DPAPI, never in plaintext files.</summary>
public sealed class SecurePayload
{
    public string DiscordClientId     { get; set; } = "";
    public string DiscordClientSecret { get; set; } = "";
    public string ChatGptApiKey       { get; set; } = "";
    public string AnythingLLMApiKey   { get; set; } = "";
    public string HaToken             { get; set; } = "";
    /// <summary>iCal calendar URLs — contain embedded auth tokens so treated as secrets.</summary>
    public List<CalendarEntry> Calendars { get; set; } = new();
}

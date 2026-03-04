namespace WinUIAudioMixer.Models;

public sealed class AnythingLLMConfig
{
    public string Url       { get; set; } = "http://localhost:3001";
    public string ApiKey    { get; set; } = "";
    public string Workspace { get; set; } = "";
}

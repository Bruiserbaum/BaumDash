namespace WinUIAudioMixer.Models;

public sealed class HaConfig
{
    public string Url    { get; set; } = "";
    public string Token  { get; set; } = "";
    public List<HaEntity> Lights   { get; set; } = new();
    public List<HaEntity> Sensors  { get; set; } = new();
    public List<HaEntity> Switches { get; set; } = new();
}

public sealed class HaEntity
{
    public string Id   { get; set; } = "";
    public string Name { get; set; } = "";
}

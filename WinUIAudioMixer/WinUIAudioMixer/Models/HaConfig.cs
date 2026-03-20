namespace WinUIAudioMixer.Models;

public sealed class HaConfig
{
    public string Url    { get; set; } = "";
    public string Token  { get; set; } = "";
    public List<HaEntity>           Lights            { get; set; } = new();
    public List<HaEntity>           Sensors           { get; set; } = new();
    public List<HaEntity>           Switches          { get; set; } = new();
    public List<HaSensorThreshold>  SensorThresholds  { get; set; } = new();
}

public sealed class HaEntity
{
    public string Id   { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>
/// Colour rule for a sensor.  When the parsed numeric value falls between
/// GreenMin and GreenMax (inclusive, nulls mean no limit) the label is green;
/// outside that range it turns red.
/// </summary>
public sealed class HaSensorThreshold
{
    /// <summary>Case-insensitive substring of the sensor's display name.</summary>
    public string  NameContains { get; set; } = "";
    public double? GreenMin     { get; set; }
    public double? GreenMax     { get; set; }
}

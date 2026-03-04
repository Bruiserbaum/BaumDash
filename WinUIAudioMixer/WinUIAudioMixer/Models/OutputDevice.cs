namespace WinUIAudioMixer.Models;

public sealed class OutputDevice
{
    public OutputDevice(string id, string name, bool isDefault)
    {
        Id = id;
        Name = name;
        IsDefault = isDefault;
    }

    public string Id { get; }
    public string Name { get; }
    public bool IsDefault { get; }
}

namespace WinUIAudioMixer.Models;

public sealed class GeneralConfig
{
    public bool   CloseToTray     { get; set; } = true;
    /// <summary>dark / light / custom</summary>
    public string Theme           { get; set; } = "dark";
    /// <summary>Hex accent colour for the "custom" theme, e.g. #4A90E2</summary>
    public string CustomAccentHex { get; set; } = "";
    /// <summary>Absolute path to the background image file.</summary>
    public string BgImagePath     { get; set; } = "";
    /// <summary>stretch / fill / fit / tile / center</summary>
    public string BgImageMode     { get; set; } = "stretch";
    /// <summary>Panel overlay opacity: 0 = raw image visible, 255 = fully opaque (no image).</summary>
    public int    BgOverlayAlpha  { get; set; } = 190;
    /// <summary>amd / nvidia — controls which Instant Replay shortcut the replay button sends.</summary>
    public string GpuPlatform     { get; set; } = "amd";
    /// <summary>auto / 1920 / 2560 — panel column widths and target form width.</summary>
    public string LayoutProfile   { get; set; } = "auto";
    /// <summary>Tab names hidden in the Discord panel (e.g. ["AI Chat","PC Perf"]).</summary>
    public List<string> HiddenDiscordTabs { get; set; } = new();
    /// <summary>stable = production releases only; dev = include pre-release / Dev-branch builds.</summary>
    public string ReleaseChannel { get; set; } = "stable";
}

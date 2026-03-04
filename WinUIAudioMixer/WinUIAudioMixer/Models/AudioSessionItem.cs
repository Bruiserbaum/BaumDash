using System.Diagnostics;
using WinUIAudioMixer.Interop;

namespace WinUIAudioMixer.Models;

public sealed class AudioSessionItem : IDisposable
{
    private readonly ISimpleAudioVolume _simpleVolume;
    private float _volume;
    private bool _isMuted;
    private bool _disposed;

    public AudioSessionItem(string displayName, uint processId, ISimpleAudioVolume simpleVolume, float volume, bool isMuted)
    {
        DisplayName = displayName;
        ProcessId = processId;
        _simpleVolume = simpleVolume;
        _volume = volume;
        _isMuted = isMuted;
    }

    public string DisplayName { get; }
    public uint ProcessId { get; }

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            if (!_disposed) _simpleVolume.SetMasterVolume(_volume, Guid.Empty);
        }
    }

    public int VolumePercent => (int)Math.Round(_volume * 100);

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            _isMuted = value;
            if (!_disposed) _simpleVolume.SetMute(value, Guid.Empty);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        MarshalHelpers.ReleaseComObject(_simpleVolume);
    }

    public static string? ResolveDisplayName(string? displayName, uint processId)
    {
        // Filter out system audio (processId 0 = Windows audio engine)
        if (processId == 0)
            return null;

        // Resource-string references (@%SystemRoot%\..., @{...}) — skip them
        if (!string.IsNullOrWhiteSpace(displayName) &&
            (displayName.StartsWith('@') || displayName.StartsWith('{')))
            return null;

        if (!string.IsNullOrWhiteSpace(displayName))
            return displayName;

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return null; // dead process, no point showing it
        }
    }
}

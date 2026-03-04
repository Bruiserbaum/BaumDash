using WinUIAudioMixer.Interop;
using WinUIAudioMixer.Models;

namespace WinUIAudioMixer.Services;

public sealed class AudioSessionService
{
    public IReadOnlyList<AudioSessionItem> GetSessionsForDefaultDevice()
    {
        var sessions = new List<AudioSessionItem>();
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();

        try
        {
            enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var device);
            try
            {
                var iid = typeof(IAudioSessionManager2).GUID;
                device.Activate(ref iid, CLSCTX.InprocServer, IntPtr.Zero, out var managerObj);
                var manager = (IAudioSessionManager2)managerObj;

                manager.GetSessionEnumerator(out var sessionEnumerator);
                try
                {
                    sessionEnumerator.GetCount(out var count);
                    for (var i = 0; i < count; i++)
                    {
                        sessionEnumerator.GetSession(i, out var sessionControl);
                        // sessionControl, sessionControl2, and simpleVolume are all the same
                        // RCW (same underlying COM object). Only one ReleaseComObject call is
                        // needed, and only on the failure path — AudioSessionItem.Dispose()
                        // owns the lifetime on the success path.
                        bool sessionAdded = false;
                        try
                        {
                            var sessionControl2 = (IAudioSessionControl2)sessionControl;
                            sessionControl2.GetProcessId(out var processId);
                            sessionControl2.GetDisplayName(out var displayName);

                            var simpleVolume = (ISimpleAudioVolume)sessionControl;
                            simpleVolume.GetMasterVolume(out var volume);
                            simpleVolume.GetMute(out var isMuted);

                            var resolvedName = AudioSessionItem.ResolveDisplayName(displayName, processId);
                            if (resolvedName == null) continue; // skip system/resource-string sessions
                            sessions.Add(new AudioSessionItem(resolvedName, processId, simpleVolume, volume, isMuted));
                            sessionAdded = true;
                        }
                        finally
                        {
                            // Only release here if we failed before AudioSessionItem could take ownership
                            if (!sessionAdded) MarshalHelpers.ReleaseComObject(sessionControl);
                        }
                    }
                }
                finally
                {
                    MarshalHelpers.ReleaseComObject(sessionEnumerator);
                    MarshalHelpers.ReleaseComObject(manager);
                }
            }
            finally
            {
                MarshalHelpers.ReleaseComObject(device);
            }
        }
        finally
        {
            MarshalHelpers.ReleaseComObject(enumerator);
        }

        return sessions
            .DistinctBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item.DisplayName)
            .ToList();
    }
}

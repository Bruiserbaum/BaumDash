using System.Runtime.InteropServices;
using WinUIAudioMixer.Interop;

namespace WinUIAudioMixer.Services;

/// <summary>
/// Listens for audio device and session changes, then notifies the UI via
/// the captured SynchronizationContext (WinForms main thread).
/// </summary>
public sealed class AudioNotificationService : IMMNotificationClient, IAudioSessionNotification, IDisposable
{
    private readonly SynchronizationContext _syncCtx;
    private readonly Action _onChange;
    private IMMDeviceEnumerator? _deviceEnumerator;
    private IMMDevice? _defaultDevice;
    private IAudioSessionManager2? _sessionManager;
    private bool _started;

    public AudioNotificationService(SynchronizationContext syncCtx, Action onChange)
    {
        _syncCtx = syncCtx;
        _onChange = onChange;
    }

    public void Start()
    {
        if (_started) return;
        _started = true;

        _deviceEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        var ptr = Marshal.GetComInterfaceForObject(this, typeof(IMMNotificationClient));
        try { _deviceEnumerator.RegisterEndpointNotificationCallback(ptr); }
        finally { Marshal.Release(ptr); }

        RegisterSessionNotificationsForDefaultDevice();
    }

    private void RegisterSessionNotificationsForDefaultDevice()
    {
        try
        {
            CleanupSessionManager();
            if (_deviceEnumerator is null) return;

            _deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var device);
            _defaultDevice = device;

            var iid = typeof(IAudioSessionManager2).GUID;
            device.Activate(ref iid, CLSCTX.InprocServer, IntPtr.Zero, out var managerObj);
            _sessionManager = (IAudioSessionManager2)managerObj;

            var ptr = Marshal.GetComInterfaceForObject(this, typeof(IAudioSessionNotification));
            try { _sessionManager.RegisterSessionNotification(ptr); }
            finally { Marshal.Release(ptr); }
        }
        catch (Exception ex)
        {
            CrashLogger.Error("RegisterSessionNotificationsForDefaultDevice failed", ex);
        }
    }

    private void CleanupSessionManager()
    {
        if (_sessionManager is not null)
        {
            var ptr = Marshal.GetComInterfaceForObject(this, typeof(IAudioSessionNotification));
            try { _sessionManager.UnregisterSessionNotification(ptr); }
            finally { Marshal.Release(ptr); }
            MarshalHelpers.ReleaseComObject(_sessionManager);
            _sessionManager = null;
        }
        if (_defaultDevice is not null)
        {
            MarshalHelpers.ReleaseComObject(_defaultDevice);
            _defaultDevice = null;
        }
    }

    private void NotifyChanged() => _syncCtx.Post(_ => _onChange(), null);

    // IMMNotificationClient
    public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string defaultDeviceId)
    {
        // Only react to the Multimedia role — Windows fires this for Console/Multimedia/Communications
        // separately, so three MTA callbacks would race to re-register COM objects that were created
        // on the STA thread. Post to the SynchronizationContext so all COM work stays on the UI thread.
        if (flow == EDataFlow.Render && role == ERole.Multimedia)
        {
            CrashLogger.Info($"Default audio device changed → {defaultDeviceId}");
            _syncCtx.Post(_ =>
            {
                try
                {
                    RegisterSessionNotificationsForDefaultDevice();
                    _onChange();
                }
                catch (Exception ex)
                {
                    CrashLogger.Error("Exception re-registering sessions after device change", ex);
                }
            }, null);
        }
        return 0;
    }
    public int OnDeviceAdded(string id) { CrashLogger.Info($"Audio device added: {id}"); NotifyChanged(); return 0; }
    public int OnDeviceRemoved(string id) { CrashLogger.Info($"Audio device removed: {id}"); NotifyChanged(); return 0; }
    public int OnDeviceStateChanged(string id, DeviceState state) { CrashLogger.Info($"Audio device state changed: {id} → {state}"); NotifyChanged(); return 0; }
    public int OnPropertyValueChanged(string id, PROPERTYKEY key) => 0;

    // IAudioSessionNotification
    public int OnSessionCreated(IAudioSessionControl session)
    {
        NotifyChanged();
        MarshalHelpers.ReleaseComObject(session);
        return 0;
    }

    public void Dispose()
    {
        if (_deviceEnumerator is not null)
        {
            var ptr = Marshal.GetComInterfaceForObject(this, typeof(IMMNotificationClient));
            try { _deviceEnumerator.UnregisterEndpointNotificationCallback(ptr); }
            finally { Marshal.Release(ptr); }
        }
        CleanupSessionManager();
        MarshalHelpers.ReleaseComObject(_deviceEnumerator);
        _deviceEnumerator = null;
    }
}

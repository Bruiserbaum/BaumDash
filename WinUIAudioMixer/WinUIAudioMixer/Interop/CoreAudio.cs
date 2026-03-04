using System.Runtime.InteropServices;

namespace WinUIAudioMixer.Interop;

public enum EDataFlow
{
    Render = 0,
    Capture = 1,
    All = 2
}

public enum ERole
{
    Console = 0,
    Multimedia = 1,
    Communications = 2
}

[Flags]
public enum DeviceState
{
    Active = 0x00000001,
    Disabled = 0x00000002,
    NotPresent = 0x00000004,
    Unplugged = 0x00000008,
    All = 0x0000000F
}

[Flags]
internal enum CLSCTX
{
    InprocServer = 0x1,
    InprocHandler = 0x2,
    LocalServer = 0x4,
    InprocServer16 = 0x8,
    RemoteServer = 0x10,
    InprocHandler16 = 0x20,
    Reserved1 = 0x40,
    Reserved2 = 0x80,
    Reserved3 = 0x100,
    Reserved4 = 0x200,
    NoCodeDownload = 0x400,
    Reserved5 = 0x800,
    NoCustomMarshal = 0x1000,
    EnableCodeDownload = 0x2000,
    NoFailureLog = 0x4000,
    DisableAaa = 0x8000,
    EnableAaa = 0x10000,
    FromDefaultContext = 0x20000,
    Activate32BitServer = 0x40000,
    Activate64BitServer = 0x80000,
    EnableCloaking = 0x100000,
    AppContainer = 0x400000,
    ActivateAaaAsIu = 0x800000,
    ActivateNativeServer = 0x1000000,
    ActivateArm32Server = 0x2000000,
    ActivateArm64Server = 0x4000000,
    EnableSplitProxy = 0x8000000,
    PsDll = unchecked((int)0x80000000)
}

[StructLayout(LayoutKind.Sequential)]
public struct PROPERTYKEY
{
    public Guid fmtid;
    public uint pid;
}

[StructLayout(LayoutKind.Explicit)]
internal struct PropVariant : IDisposable
{
    [FieldOffset(0)]
    public ushort vt;

    [FieldOffset(8)]
    public IntPtr pointerValue;

    public string? GetString()
    {
        return vt switch
        {
            31 => Marshal.PtrToStringUni(pointerValue),
            8 => Marshal.PtrToStringBSTR(pointerValue),
            _ => null
        };
    }

    public void Dispose()
    {
        PropVariantClear(ref this);
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);
}

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumerator
{
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    int EnumAudioEndpoints(EDataFlow dataFlow, DeviceState stateMask, out IntPtr devices);
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out IMMDevice device);
    int RegisterEndpointNotificationCallback(IntPtr client);
    int UnregisterEndpointNotificationCallback(IntPtr client);
}

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-C0A3893660B1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    int GetCount(out uint count);
    int Item(uint index, out IMMDevice device);
}

[ComImport]
[Guid("7991eec9-7e89-4d85-8390-6c703cec60c0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMNotificationClient
{
    int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, DeviceState dwNewState);
    int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);
    int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);
    int OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string pwstrDefaultDeviceId);
    int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, PROPERTYKEY key);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    int Activate(ref Guid iid, CLSCTX clsctx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
    int OpenPropertyStore(int stgmAccess, out IPropertyStore properties);
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    int GetState(out DeviceState state);
}

[ComImport]
[Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    int GetCount(out uint propertyCount);
    int GetAt(uint propertyIndex, out PROPERTYKEY key);
    int GetValue(ref PROPERTYKEY key, out PropVariant value);
    int SetValue(ref PROPERTYKEY key, ref PropVariant value);
    int Commit();
}

[ComImport]
[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    int GetAudioSessionControl(ref Guid audioSessionGuid, uint streamFlags, out IAudioSessionControl sessionControl);
    int GetSimpleAudioVolume(ref Guid audioSessionGuid, uint streamFlags, out ISimpleAudioVolume audioVolume);
    int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
    int RegisterSessionNotification(IntPtr sessionNotification);
    int UnregisterSessionNotification(IntPtr sessionNotification);
    int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, IntPtr duckNotification);
    int UnregisterDuckNotification(IntPtr duckNotification);
}

[ComImport]
[Guid("641DD20B-4D41-49CC-ABA3-174B9477BB08")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioSessionNotification
{
    int OnSessionCreated(IAudioSessionControl newSession);
}

[ComImport]
[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    int GetCount(out int sessionCount);
    int GetSession(int sessionIndex, out IAudioSessionControl sessionControl);
}

[ComImport]
[Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioSessionControl
{
    int GetState(out int state);
    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);
    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, ref Guid eventContext);
    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);
    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string iconPath, ref Guid eventContext);
    int GetGroupingParam(out Guid groupingId);
    int SetGroupingParam(ref Guid groupingId, ref Guid eventContext);
    int RegisterAudioSessionNotification(IntPtr sessionEvents);
    int UnregisterAudioSessionNotification(IntPtr sessionEvents);
}

[ComImport]
[Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    int GetState(out int state);
    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);
    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, ref Guid eventContext);
    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);
    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string iconPath, ref Guid eventContext);
    int GetGroupingParam(out Guid groupingId);
    int SetGroupingParam(ref Guid groupingId, ref Guid eventContext);
    int RegisterAudioSessionNotification(IntPtr sessionEvents);
    int UnregisterAudioSessionNotification(IntPtr sessionEvents);
    int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string sessionId);
    int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string sessionInstanceId);
    int GetProcessId(out uint processId);
    int IsSystemSoundsSession(out int isSystemSounds);
    int SetDuckingPreference(bool optOut);
}

[ComImport]
[Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ISimpleAudioVolume
{
    int SetMasterVolume(float level, Guid eventContext);
    int GetMasterVolume(out float level);
    int SetMute(bool mute, Guid eventContext);
    int GetMute(out bool mute);
}

[ComImport]
[Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr format);
    int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int isDefault, IntPtr format);
    // Windows 10/11 added ResetDeviceFormat here, shifting all subsequent methods by one slot.
    // Without this placeholder the vtable is misaligned and SetDefaultEndpoint hits the wrong method.
    int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
    int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr format, IntPtr mix);
    int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int isDefault, IntPtr defaultPeriod, IntPtr minPeriod);
    int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr defaultPeriod, IntPtr minPeriod);
    int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
    int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
    int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref PROPERTYKEY key, out PropVariant value);
    int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref PROPERTYKEY key, ref PropVariant value);
    int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
    int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int visible);
}

[ComImport]
[Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
internal class PolicyConfigClient
{
}

[ComImport]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioEndpointVolume
{
    int RegisterControlChangeNotify(IntPtr pNotify);
    int UnregisterControlChangeNotify(IntPtr pNotify);
    int GetChannelCount(out uint pnChannelCount);
    int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
    int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
    int GetMasterVolumeLevel(out float pfLevelDB);
    int GetMasterVolumeLevelScalar(out float pfLevel);
    int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
    int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
    int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
    int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
    int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);
    int VolumeStepUp(ref Guid pguidEventContext);
    int VolumeStepDown(ref Guid pguidEventContext);
    int QueryHardwareSupport(out uint pdwHardwareSupportMask);
    int GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB, out float pflVolumeIncrementdB);
}

internal static class PropertyKeys
{
    public static readonly PROPERTYKEY PkeyDeviceFriendlyName = new()
    {
        fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        pid = 14
    };
}

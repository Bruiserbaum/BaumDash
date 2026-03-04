using System.Runtime.InteropServices;
using WinUIAudioMixer.Interop;
using WinUIAudioMixer.Models;

namespace WinUIAudioMixer.Services;

public sealed class AudioDeviceService
{
    // ── Vtable delegates – bypass COM QI entirely ─────────────────────────────
    // IMMDeviceCollection vtable (after IUnknown's QI/AddRef/Release at [0-2]):
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CollGetCountDelegate(IntPtr self, out uint count);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CollItemDelegate(IntPtr self, uint index, out IntPtr ppDevice);

    // IMMDevice vtable entries we need ([3]=Activate, [4]=OpenPropertyStore, [5]=GetId, [6]=GetState):
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int DevGetIdDelegate(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] out string id);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int DevGetStateDelegate(IntPtr self, out DeviceState state);

    private static T VtableDelegate<T>(IntPtr comPtr, int methodIndex) where T : Delegate
    {
        IntPtr vtable = Marshal.ReadIntPtr(comPtr);
        IntPtr fn    = Marshal.ReadIntPtr(vtable + methodIndex * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<T>(fn);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public IReadOnlyList<OutputDevice> GetOutputDevices()
    {
        var devices = new List<OutputDevice>();

        System.Diagnostics.Trace.WriteLine(
            $"[AudioDeviceService] GetOutputDevices thread={Thread.CurrentThread.ManagedThreadId} apt={Thread.CurrentThread.GetApartmentState()}");

        IMMDeviceEnumerator? enumerator = null;
        IntPtr collectionPtr = IntPtr.Zero;
        IMMDevice? defaultDevice = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();

            // EnumAudioEndpoints returns IMMDeviceCollection* via raw IntPtr –
            // no QI happens here, so no InvalidCastException possible.
            try
            {
                int hr = enumerator.EnumAudioEndpoints(EDataFlow.Render, DeviceState.All, out collectionPtr);
                System.Diagnostics.Trace.WriteLine($"[AudioDeviceService] EnumAudioEndpoints hr=0x{hr:X8} ptr=0x{collectionPtr:X}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[AudioDeviceService] EnumAudioEndpoints threw: {ex.GetType().Name}: {ex.Message}");
            }

            if (collectionPtr == IntPtr.Zero)
            {
                System.Diagnostics.Trace.WriteLine("[AudioDeviceService] collection pointer is zero");
                return devices;
            }

            // Get the default device ID for IsDefault flag
            try { enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out defaultDevice); }
            catch { }

            string? defaultId = null;
            if (defaultDevice != null)
                try { defaultDevice.GetId(out defaultId); } catch { }

            // ── Call IMMDeviceCollection vtable directly (no QI) ───────────────
            try
            {
                var getCount = VtableDelegate<CollGetCountDelegate>(collectionPtr, 3);
                int hr = getCount(collectionPtr, out uint count);
                System.Diagnostics.Trace.WriteLine($"[AudioDeviceService] GetCount hr=0x{hr:X8} count={count}");
                if (hr < 0) return devices;

                var getItem = VtableDelegate<CollItemDelegate>(collectionPtr, 4);
                for (uint i = 0; i < count; i++)
                {
                    IntPtr devicePtr = IntPtr.Zero;
                    try
                    {
                        hr = getItem(collectionPtr, i, out devicePtr);
                        if (hr < 0 || devicePtr == IntPtr.Zero) continue;

                        // ── IMMDevice vtable directly (no QI) ─────────────────
                        var getState = VtableDelegate<DevGetStateDelegate>(devicePtr, 6);
                        getState(devicePtr, out var state);
                        if (state != DeviceState.Active) continue;

                        var getId = VtableDelegate<DevGetIdDelegate>(devicePtr, 5);
                        getId(devicePtr, out var id);

                        var name = GetFriendlyName(devicePtr) ?? id;
                        devices.Add(new OutputDevice(id, name, id == defaultId));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Trace.WriteLine($"[AudioDeviceService] device[{i}] error: {ex.GetType().Name}: {ex.Message}");
                    }
                    finally
                    {
                        if (devicePtr != IntPtr.Zero) Marshal.Release(devicePtr);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[AudioDeviceService] enumeration failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[AudioDeviceService] outer error: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            MarshalHelpers.ReleaseComObject(defaultDevice);
            if (collectionPtr != IntPtr.Zero) Marshal.Release(collectionPtr);
            MarshalHelpers.ReleaseComObject(enumerator);
        }

        return devices
            .OrderByDescending(d => d.IsDefault)
            .ThenBy(d => d.Name)
            .ToList();
    }

    public void SetDefaultDevice(string deviceId)
    {
        var policyConfig = (IPolicyConfig)new PolicyConfigClient();
        try
        {
            policyConfig.SetDefaultEndpoint(deviceId, ERole.Console);
            policyConfig.SetDefaultEndpoint(deviceId, ERole.Multimedia);
            policyConfig.SetDefaultEndpoint(deviceId, ERole.Communications);
        }
        finally
        {
            MarshalHelpers.ReleaseComObject(policyConfig);
        }
    }

    // ── Friendly name via typed COM interfaces ────────────────────────────────

    private static string? GetFriendlyName(IntPtr devicePtr)
    {
        IMMDevice? device = null;
        IPropertyStore? store = null;
        try
        {
            device = (IMMDevice)Marshal.GetObjectForIUnknown(devicePtr);
            device.OpenPropertyStore(0 /*STGM_READ*/, out store);
            var key = PropertyKeys.PkeyDeviceFriendlyName;
            store.GetValue(ref key, out var prop);
            using (prop) return prop.GetString();
        }
        catch { return null; }
        finally
        {
            MarshalHelpers.ReleaseComObject(store);
            MarshalHelpers.ReleaseComObject(device);
        }
    }
}

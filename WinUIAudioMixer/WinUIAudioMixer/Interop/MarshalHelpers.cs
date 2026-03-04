using System.Runtime.InteropServices;

namespace WinUIAudioMixer.Interop;

internal static class MarshalHelpers
{
    public static void ReleaseComObject(object? comObject)
    {
        if (comObject is null)
        {
            return;
        }

        if (Marshal.IsComObject(comObject))
        {
            Marshal.ReleaseComObject(comObject);
        }
    }
}

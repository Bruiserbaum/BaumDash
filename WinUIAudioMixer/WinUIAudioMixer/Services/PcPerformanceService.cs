using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinUIAudioMixer.Services;

public sealed record DriveSnapshot(string Name, double UsedGb, double TotalGb, int Percent);

public sealed record PcSnapshot(
    double                      CpuPercent,
    double                      RamUsedGb,
    double                      RamTotalGb,
    int                         RamPercent,
    IReadOnlyList<DriveSnapshot> Drives,
    int                         ProcessCount,
    TimeSpan                    Uptime);

public sealed class PcPerformanceService
{
    private long _prevIdle, _prevKernel, _prevUser;
    private bool _firstSample = true;

    public PcSnapshot GetSnapshot()
    {
        // CPU — delta between two calls to GetSystemTimes
        GetSystemTimes(out var ftIdle, out var ftKernel, out var ftUser);
        long curIdle   = ToLong(ftIdle);
        long curKernel = ToLong(ftKernel);
        long curUser   = ToLong(ftUser);

        double cpuPct = 0;
        if (!_firstSample)
        {
            long dIdle   = curIdle   - _prevIdle;
            long dKernel = curKernel - _prevKernel;
            long dUser   = curUser   - _prevUser;
            long dTotal  = dKernel + dUser;   // kernel includes idle
            if (dTotal > 0)
                cpuPct = Math.Clamp((1.0 - (double)dIdle / dTotal) * 100.0, 0, 100);
        }
        _prevIdle    = curIdle;
        _prevKernel  = curKernel;
        _prevUser    = curUser;
        _firstSample = false;

        // RAM
        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref mem);
        double ramTotal = mem.ullTotalPhys / (1024.0 * 1024 * 1024);
        double ramUsed  = (mem.ullTotalPhys - mem.ullAvailPhys) / (1024.0 * 1024 * 1024);

        // Fixed drives
        var drives = System.IO.DriveInfo.GetDrives()
            .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
            .Select(d =>
            {
                double tot  = d.TotalSize / (1024.0 * 1024 * 1024);
                double used = (d.TotalSize - d.AvailableFreeSpace) / (1024.0 * 1024 * 1024);
                int    pct  = tot > 0 ? (int)Math.Round(used / tot * 100) : 0;
                return new DriveSnapshot(d.Name.TrimEnd('\\'), used, tot, pct);
            })
            .ToList();

        // Process count (dispose handles to avoid handle leak)
        var procs = Process.GetProcesses();
        int procCount = procs.Length;
        foreach (var p in procs) p.Dispose();

        // System uptime
        var uptime = TimeSpan.FromMilliseconds(GetTickCount64());

        return new PcSnapshot(cpuPct, ramUsed, ramTotal, (int)mem.dwMemoryLoad,
                              drives, procCount, uptime);
    }

    private static long ToLong(FILETIME ft) => (long)((ulong)ft.High << 32 | ft.Low);

    [DllImport("kernel32.dll")] private static extern bool  GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);
    [DllImport("kernel32.dll")] private static extern bool  GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
    [DllImport("kernel32.dll")] private static extern ulong GetTickCount64();

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME { public uint Low, High; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint  dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys;
        public ulong ullTotalPageFile, ullAvailPageFile;
        public ulong ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }
}

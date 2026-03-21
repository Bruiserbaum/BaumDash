using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace WinUIAudioMixer.Services;

public sealed record DriveSnapshot(string Name, double UsedGb, double TotalGb, int Percent);

public sealed record BtBatteryInfo(string Name, int BatteryPercent);

public sealed record PcSnapshot(
    double                       CpuPercent,
    double                       RamUsedGb,
    double                       RamTotalGb,
    int                          RamPercent,
    IReadOnlyList<DriveSnapshot>  Drives,
    int                          ProcessCount,
    TimeSpan                     Uptime,
    double                       GpuPercent,
    double                       GpuMemUsedGb,
    double                       GpuMemTotalGb,
    double                       NetBytesPerSec,
    double                       DiskActivityPercent,
    /// <summary>CPU temperature in °C, or -1 if unavailable.</summary>
    double                       CpuTempC  = -1,
    /// <summary>GPU temperature in °C, or -1 if unavailable.</summary>
    double                       GpuTempC  = -1,
    /// <summary>Connected Bluetooth devices with battery levels (-1 = unknown).</summary>
    IReadOnlyList<BtBatteryInfo> BtDevices = null!,
    /// <summary>GPU model name reported by LibreHardwareMonitor, or empty string.</summary>
    string                       GpuName   = "");

public sealed class PcPerformanceService : IDisposable
{
    // ── CPU state ─────────────────────────────────────────────────────────────
    private long _prevIdle, _prevKernel, _prevUser;
    private bool _firstSample = true;

    // ── Network state ─────────────────────────────────────────────────────────
    private long     _prevNetBytes;
    private DateTime _prevNetTime = DateTime.MinValue;

    // ── PDH state (GPU + disk I/O) ────────────────────────────────────────────
    private IntPtr _pdhQuery    = IntPtr.Zero;
    private IntPtr _cGpuUtil;       // \GPU Engine(*engtype_3D)\Utilization Percentage
    private IntPtr _cGpuMemUsed;    // \GPU Adapter Memory(*)\Dedicated Usage
    private IntPtr _cGpuMemLimit;   // \GPU Adapter Memory(*)\Dedicated Limit
    private IntPtr _cDiskTime;      // \PhysicalDisk(_Total)\% Disk Time
    private IntPtr _cCpuTemp;       // \Thermal Zone Information(*)\Temperature (Kelvin)
    private bool   _pdhReady;
    private bool   _disposed;

    // ── Background-polled metrics ─────────────────────────────────────────────
    private double _cpuTempC = -1;
    private double _gpuTempC = -1;
    private string _gpuName  = "";
    private IReadOnlyList<BtBatteryInfo> _btDevices = Array.Empty<BtBatteryInfo>();
    private System.Threading.Timer? _tempTimer;
    private System.Threading.Timer? _btTimer;

    // ── LibreHardwareMonitor ──────────────────────────────────────────────────
    private LibreHardwareMonitor.Hardware.Computer? _lhm;

    public PcPerformanceService()
    {
        InitPdh();

        // LibreHardwareMonitor gives us GPU temp via ADL/NVAPI (no admin needed)
        // and CPU temp when running as admin.  Falls back to WMI if unavailable.
        try
        {
            _lhm = new LibreHardwareMonitor.Hardware.Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
            };
            _lhm.Open();

            // Cache GPU name at startup
            foreach (var hw in _lhm.Hardware)
            {
                if (hw.HardwareType is LibreHardwareMonitor.Hardware.HardwareType.GpuAmd
                                    or LibreHardwareMonitor.Hardware.HardwareType.GpuNvidia
                                    or LibreHardwareMonitor.Hardware.HardwareType.GpuIntel)
                {
                    _gpuName = hw.Name;
                    break;
                }
            }
        }
        catch { _lhm = null; }

        // CPU + GPU temps — every 5 s
        _tempTimer = new System.Threading.Timer(_ =>
        {
            if (!TryReadTempsViaLhm(out double cpu, out double gpu))
            {
                cpu = ReadCpuTempViaWmi();
                gpu = ReadGpuTempViaWmi();
            }
            _cpuTempC = cpu;
            _gpuTempC = gpu;
        }, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));

        // Bluetooth battery — every 30 s
        _btTimer = new System.Threading.Timer(
            _ => _ = StoreBtBatteriesAsync(),
            null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Reads CPU and GPU temperatures via LibreHardwareMonitor.
    /// Works without admin for GPU (ADL/NVAPI are user-space).
    /// CPU temp on AMD desktops requires admin to read MSR registers.
    /// Returns true if at least one temperature was obtained.
    /// </summary>
    private bool TryReadTempsViaLhm(out double cpuC, out double gpuC)
    {
        cpuC = gpuC = -1;
        if (_lhm == null) return false;
        bool got = false;
        try
        {
            foreach (var hw in _lhm.Hardware)
            {
                hw.Update();
                bool isCpu = hw.HardwareType == LibreHardwareMonitor.Hardware.HardwareType.Cpu;
                bool isGpu = hw.HardwareType is LibreHardwareMonitor.Hardware.HardwareType.GpuAmd
                                             or LibreHardwareMonitor.Hardware.HardwareType.GpuNvidia
                                             or LibreHardwareMonitor.Hardware.HardwareType.GpuIntel;
                if (!isCpu && !isGpu) continue;

                foreach (var s in hw.Sensors)
                {
                    if (s.SensorType != LibreHardwareMonitor.Hardware.SensorType.Temperature) continue;
                    double v = (double)(s.Value ?? -1);
                    if (v <= 0 || v >= 150) continue;

                    if (isCpu && cpuC < 0)
                    {
                        // Prefer Package / Tdie (AMD junction) / Tctl over per-core temps
                        if (s.Name.Contains("Package",  StringComparison.OrdinalIgnoreCase) ||
                            s.Name.Contains("Tdie",     StringComparison.OrdinalIgnoreCase) ||
                            s.Name.Contains("Tctl",     StringComparison.OrdinalIgnoreCase) ||
                            s.Name.Equals  ("CPU",      StringComparison.OrdinalIgnoreCase))
                        { cpuC = Math.Round(v, 1); got = true; }
                    }
                    else if (isGpu && gpuC < 0)
                    {
                        // Take first valid core/junction temperature
                        if (s.Name.Contains("Core",     StringComparison.OrdinalIgnoreCase) ||
                            s.Name.Contains("Junction", StringComparison.OrdinalIgnoreCase) ||
                            s.Name.Equals  ("GPU",      StringComparison.OrdinalIgnoreCase))
                        { gpuC = Math.Round(v, 1); got = true; }
                    }
                }
            }
        }
        catch { }
        return got;
    }

    private async Task StoreBtBatteriesAsync()
    {
        try { _btDevices = await ReadBluetoothBatteriesAsync().ConfigureAwait(false); }
        catch { }
    }

    // ── Snapshot ──────────────────────────────────────────────────────────────

    public PcSnapshot GetSnapshot()
    {
        // CPU — delta between consecutive GetSystemTimes calls
        GetSystemTimes(out var ftIdle, out var ftKernel, out var ftUser);
        long curIdle = ToLong(ftIdle), curKernel = ToLong(ftKernel), curUser = ToLong(ftUser);
        double cpuPct = 0;
        if (!_firstSample)
        {
            long dIdle = curIdle - _prevIdle, dKernel = curKernel - _prevKernel, dUser = curUser - _prevUser;
            long dTotal = dKernel + dUser;
            if (dTotal > 0) cpuPct = Math.Clamp((1.0 - (double)dIdle / dTotal) * 100.0, 0, 100);
        }
        _prevIdle = curIdle; _prevKernel = curKernel; _prevUser = curUser; _firstSample = false;

        // RAM
        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref mem);
        double ramTotal = mem.ullTotalPhys / (1024.0 * 1024 * 1024);
        double ramUsed  = (mem.ullTotalPhys - mem.ullAvailPhys) / (1024.0 * 1024 * 1024);

        // Drive space (fixed drives)
        var drives = System.IO.DriveInfo.GetDrives()
            .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
            .Select(d =>
            {
                double tot  = d.TotalSize / (1024.0 * 1024 * 1024);
                double used = (d.TotalSize - d.AvailableFreeSpace) / (1024.0 * 1024 * 1024);
                return new DriveSnapshot(d.Name.TrimEnd('\\'), used, tot,
                                         tot > 0 ? (int)Math.Round(used / tot * 100) : 0);
            }).ToList();

        // Process count + uptime
        var procs = Process.GetProcesses();
        int procCount = procs.Length;
        foreach (var p in procs) p.Dispose();
        var uptime = TimeSpan.FromMilliseconds(GetTickCount64());

        // GPU util + GPU VRAM + Disk I/O + CPU temp via PDH
        double gpuPct = 0, gpuMemUsed = 0, gpuMemTotal = 0, diskPct = 0;
        if (_pdhReady && PdhCollectQueryData(_pdhQuery) == 0)
        {
            var gpuVals = GetCounterArray(_cGpuUtil);
            gpuPct = Math.Clamp(gpuVals.Sum(), 0, 100);

            var memUsedVals  = GetCounterArray(_cGpuMemUsed);
            var memLimitVals = GetCounterArray(_cGpuMemLimit);
            gpuMemUsed  = memUsedVals .Sum() / (1024.0 * 1024 * 1024);
            gpuMemTotal = memLimitVals.Sum() / (1024.0 * 1024 * 1024);

            if (_cDiskTime != IntPtr.Zero &&
                PdhGetFormattedCounterValue(_cDiskTime, PDH_FMT_DOUBLE, out _, out var dv) == 0 &&
                (dv.CStatus & 0x80000000u) == 0)
                diskPct = Math.Clamp(dv.DoubleValue, 0, 100);

            // CPU temperature is now read via WMI in the background (_cpuTempC field)
        }

        // Network delta
        double netBps = GetNetworkBytesPerSec();

        return new PcSnapshot(cpuPct, ramUsed, ramTotal, (int)mem.dwMemoryLoad,
                              drives, procCount, uptime,
                              gpuPct, gpuMemUsed, gpuMemTotal, netBps, diskPct,
                              CpuTempC: _cpuTempC, GpuTempC: _gpuTempC,
                              BtDevices: _btDevices, GpuName: _gpuName);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void InitPdh()
    {
        if (PdhOpenQuery(null, IntPtr.Zero, out _pdhQuery) != 0) return;

        PdhAddEnglishCounter(_pdhQuery, @"\GPU Engine(*engtype_3D)\Utilization Percentage", IntPtr.Zero, out _cGpuUtil);
        PdhAddEnglishCounter(_pdhQuery, @"\GPU Adapter Memory(*)\Dedicated Usage",          IntPtr.Zero, out _cGpuMemUsed);
        PdhAddEnglishCounter(_pdhQuery, @"\GPU Adapter Memory(*)\Dedicated Limit",          IntPtr.Zero, out _cGpuMemLimit);
        PdhAddEnglishCounter(_pdhQuery, @"\PhysicalDisk(_Total)\% Disk Time",               IntPtr.Zero, out _cDiskTime);
        PdhAddEnglishCounter(_pdhQuery, @"\Thermal Zone Information(*)\Temperature",        IntPtr.Zero, out _cCpuTemp);

        PdhCollectQueryData(_pdhQuery); // first collection — rate counters need ≥ 2 samples
        _pdhReady = true;
    }

    private double GetNetworkBytesPerSec()
    {
        try
        {
            var now = DateTime.UtcNow;
            long total = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up
                          && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback
                          && ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .Sum(ni => { var s = ni.GetIPStatistics(); return s.BytesReceived + s.BytesSent; });

            double bps = 0;
            if (_prevNetTime != DateTime.MinValue)
            {
                double elapsed = (now - _prevNetTime).TotalSeconds;
                if (elapsed > 0) bps = Math.Max(0, (total - _prevNetBytes) / elapsed);
            }
            _prevNetBytes = total;
            _prevNetTime  = now;
            return bps;
        }
        catch { return 0; }
    }

    private static double[] GetCounterArray(IntPtr counter)
    {
        if (counter == IntPtr.Zero) return Array.Empty<double>();
        uint bufSize = 0, count = 0;
        PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE, ref bufSize, ref count, IntPtr.Zero);
        if (bufSize == 0 || count == 0) return Array.Empty<double>();

        var buf = Marshal.AllocHGlobal((int)bufSize);
        try
        {
            if (PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE, ref bufSize, ref count, buf) != 0)
                return Array.Empty<double>();

            int sz = Marshal.SizeOf<PdhFmtItem>();
            var result = new List<double>((int)count);
            for (int i = 0; i < (int)count; i++)
            {
                var item = Marshal.PtrToStructure<PdhFmtItem>(buf + i * sz);
                if ((item.CStatus & 0x80000000u) == 0)
                    result.Add(item.DoubleValue);
            }
            return result.ToArray();
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _tempTimer?.Dispose(); _tempTimer = null;
        _btTimer?.Dispose();   _btTimer   = null;
        try { _lhm?.Close(); } catch { }
        _lhm = null;
        if (_pdhQuery != IntPtr.Zero) { PdhCloseQuery(_pdhQuery); _pdhQuery = IntPtr.Zero; }
    }

    /// <summary>
    /// Reads CPU temperature. Tries three sources in order:
    /// 1. ACPI thermal zones (root\wmi – works on most laptops)
    /// 2. Win32_PerfFormattedData_Counters_ThermalZoneInformation (root\cimv2)
    /// 3. OpenHardwareMonitor / LibreHardwareMonitor WMI provider
    /// Returns temperature in °C, or -1 if all sources are unavailable.
    /// </summary>
    private static double ReadCpuTempViaWmi()
    {
        // ── 1. ACPI thermal zones ─────────────────────────────────────────────
        try
        {
            var opts = new System.Management.ConnectionOptions
            {
                Impersonation    = System.Management.ImpersonationLevel.Impersonate,
                EnablePrivileges = true,
            };
            var scope   = new System.Management.ManagementScope(@"root\wmi", opts);
            var query   = new System.Management.ObjectQuery(
                "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            using var searcher = new System.Management.ManagementObjectSearcher(scope, query);
            double best = -1;
            foreach (System.Management.ManagementObject obj in searcher.Get())
            {
                if (obj["CurrentTemperature"] is uint raw)
                {
                    double tempC = raw / 10.0 - 273.15;
                    if (tempC >= 0 && tempC <= 120 && tempC > best)
                        best = Math.Round(tempC, 1);
                }
            }
            if (best >= 0) return best;
        }
        catch { }

        // ── 2. Performance counter WMI class (root\cimv2) ────────────────────
        try
        {
            var scope   = new System.Management.ManagementScope(@"root\cimv2");
            var query   = new System.Management.ObjectQuery(
                "SELECT Temperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation");
            using var searcher = new System.Management.ManagementObjectSearcher(scope, query);
            double best = -1;
            foreach (System.Management.ManagementObject obj in searcher.Get())
            {
                // Value is in Kelvin (e.g. 298 = 24.85 °C)
                double rawK = obj["Temperature"] switch
                {
                    ulong  u => (double)u,
                    uint   u => (double)u,
                    long   l => (double)l,
                    int    i => (double)i,
                    _        => -1,
                };
                if (rawK < 0) continue;
                double t = rawK - 273.15;
                if (t >= 0 && t <= 120 && t > best) best = Math.Round(t, 1);
            }
            if (best >= 0) return best;
        }
        catch { }

        // ── 3. OHM / LHM WMI provider ────────────────────────────────────────
        foreach (var ns in new[] { @"root\OpenHardwareMonitor", @"root\LibreHardwareMonitor" })
        {
            try
            {
                var scope   = new System.Management.ManagementScope(ns);
                var query   = new System.Management.ObjectQuery(
                    "SELECT Name, Value FROM Sensor WHERE SensorType='Temperature'");
                using var searcher = new System.Management.ManagementObjectSearcher(scope, query);
                double best = -1;
                foreach (System.Management.ManagementObject obj in searcher.Get())
                {
                    var name = obj["Name"]?.ToString() ?? "";
                    if (!name.Contains("CPU", StringComparison.OrdinalIgnoreCase)) continue;
                    if (obj["Value"] is not float v || v <= 0 || v >= 120) continue;
                    double t = Math.Round(v, 1);
                    // Prefer "CPU Package" or bare "CPU" over individual core temps
                    if (name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("CPU", StringComparison.OrdinalIgnoreCase))
                        return t;
                    if (t > best) best = t;
                }
                if (best >= 0) return best;
            }
            catch { }
        }

        return -1;
    }

    /// <summary>
    /// Reads GPU temperature.
    /// Priority: AMD ADL → NVIDIA NVML → OHM/LHM WMI.
    /// Returns temperature in °C, or -1 if unavailable.
    /// </summary>
    private static double ReadGpuTempViaWmi()
    {
        double t = ReadGpuTempViaAdl();
        if (t >= 0) return t;

        t = ReadGpuTempViaNvml();
        if (t >= 0) return t;

        foreach (var ns in new[] { @"root\OpenHardwareMonitor", @"root\LibreHardwareMonitor" })
        {
            try
            {
                var scope   = new System.Management.ManagementScope(ns);
                var query   = new System.Management.ObjectQuery(
                    "SELECT Name, Value FROM Sensor WHERE SensorType='Temperature'");
                using var searcher = new System.Management.ManagementObjectSearcher(scope, query);
                foreach (System.Management.ManagementObject obj in searcher.Get())
                {
                    var name = obj["Name"]?.ToString() ?? "";
                    if (!name.Contains("GPU", StringComparison.OrdinalIgnoreCase)) continue;
                    if (obj["Value"] is float v && v > 0 && v < 150)
                        return Math.Round(v, 1);
                }
            }
            catch { }
        }
        return -1;
    }

    // ── AMD Display Library (ADL) ─────────────────────────────────────────────

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr AdlMallocFn(int size);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int AdlCreateFn(IntPtr mallocCb, int connectedOnly, out IntPtr ctx);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int AdlDestroyFn(IntPtr ctx);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int AdlAdapterCountFn(IntPtr ctx, out int count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int AdlAdapterActiveFn(IntPtr ctx, int adapterIdx, out int status);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int AdlOdnTempFn(IntPtr ctx, int adapterIdx, int tempType, out int tempMilliC);

    [StructLayout(LayoutKind.Sequential)]
    private struct AdlTemperature { public int iSize, iTemperature; }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int AdlOd5TempFn(IntPtr ctx, int adapterIdx, int ctrlIdx, ref AdlTemperature temp);

    private static double ReadGpuTempViaAdl()
    {
        // Try explicit System32 path first, then standard search order
        string sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        IntPtr lib = LoadLibraryEx(Path.Combine(sys32, "atiadlxx.dll"), IntPtr.Zero, 0);
        if (lib == IntPtr.Zero)
            lib = LoadLibraryEx("atiadlxx.dll", IntPtr.Zero, 0);
        if (lib == IntPtr.Zero) return -1;

        try
        {
            IntPtr Fn(string n) => GetProcAddress(lib, n);

            var pfnCreate  = Fn("ADL2_Main_Control_Create");
            var pfnDestroy = Fn("ADL2_Main_Control_Destroy");
            var pfnCount   = Fn("ADL2_Adapter_NumberOfAdapters_Get");
            if (pfnCreate == IntPtr.Zero || pfnDestroy == IntPtr.Zero || pfnCount == IntPtr.Zero)
                return -1;

            var fnCreate  = Marshal.GetDelegateForFunctionPointer<AdlCreateFn>(pfnCreate);
            var fnDestroy = Marshal.GetDelegateForFunctionPointer<AdlDestroyFn>(pfnDestroy);
            var fnCount   = Marshal.GetDelegateForFunctionPointer<AdlAdapterCountFn>(pfnCount);

            AdlMallocFn mallocFn = size => Marshal.AllocHGlobal(size);
            var mallocPtr = Marshal.GetFunctionPointerForDelegate(mallocFn);

            // Try connectedOnly=1 first (active displays), fall back to 0 (all adapters)
            IntPtr ctx = IntPtr.Zero;
            if (fnCreate(mallocPtr, 1, out ctx) != 0 &&
                fnCreate(mallocPtr, 0, out ctx) != 0)
            {
                GC.KeepAlive(mallocFn);
                return -1;
            }

            try
            {
                if (fnCount(ctx, out int n) != 0 || n <= 0) return -1;

                var pfnActive = Fn("ADL2_Adapter_Active_Get");
                var pfnTempN  = Fn("ADL2_OverdriveN_Temperature_Get");
                var pfnTemp5  = Fn("ADL2_Overdrive5_Temperature_Get");

                for (int i = 0; i < n; i++)
                {
                    // Skip inactive (virtual) adapters when possible
                    if (pfnActive != IntPtr.Zero)
                    {
                        var fnActive = Marshal.GetDelegateForFunctionPointer<AdlAdapterActiveFn>(pfnActive);
                        if (fnActive(ctx, i, out int active) == 0 && active == 0) continue;
                    }

                    if (pfnTempN != IntPtr.Zero)
                    {
                        var fn = Marshal.GetDelegateForFunctionPointer<AdlOdnTempFn>(pfnTempN);
                        // Try edge (1), hotspot (3) — skip memory (2) as it often errors
                        foreach (int tt in new[] { 1, 3 })
                        {
                            if (fn(ctx, i, tt, out int tN) == 0 && tN > 0)
                            {
                                double c = tN > 1000 ? tN / 1000.0 : tN;
                                if (c is > 0 and < 150) return Math.Round(c, 1);
                            }
                        }
                    }

                    if (pfnTemp5 != IntPtr.Zero)
                    {
                        var fn = Marshal.GetDelegateForFunctionPointer<AdlOd5TempFn>(pfnTemp5);
                        var t5 = new AdlTemperature { iSize = Marshal.SizeOf<AdlTemperature>() };
                        if (fn(ctx, i, 0, ref t5) == 0 && t5.iTemperature > 0)
                        {
                            double c = t5.iTemperature > 1000 ? t5.iTemperature / 1000.0 : t5.iTemperature;
                            if (c is > 0 and < 150) return Math.Round(c, 1);
                        }
                    }
                }
            }
            finally
            {
                fnDestroy(ctx);
                GC.KeepAlive(mallocFn);
            }
        }
        catch { }
        finally { FreeLibrary(lib); }
        return -1;
    }

    // ── NVIDIA NVML GPU temperature ───────────────────────────────────────────

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlInitFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlShutdownFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlGetHandleFn(int index, out IntPtr device);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlGetTempFn(IntPtr device, int sensorType, out uint temp);

    private static double ReadGpuTempViaNvml()
    {
        string sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        IntPtr lib = LoadLibraryEx(Path.Combine(sys32, "nvml.dll"), IntPtr.Zero, 0);
        if (lib == IntPtr.Zero)
            lib = LoadLibraryEx("nvml.dll", IntPtr.Zero, 0);
        if (lib == IntPtr.Zero) return -1;

        try
        {
            IntPtr Fn(string n) => GetProcAddress(lib, n);

            var pfnInit     = Fn("nvmlInit_v2");
            var pfnShutdown = Fn("nvmlShutdown");
            var pfnHandle   = Fn("nvmlDeviceGetHandleByIndex_v2");
            var pfnTemp     = Fn("nvmlDeviceGetTemperature");
            if (pfnInit == IntPtr.Zero || pfnShutdown == IntPtr.Zero ||
                pfnHandle == IntPtr.Zero || pfnTemp == IntPtr.Zero) return -1;

            var fnInit     = Marshal.GetDelegateForFunctionPointer<NvmlInitFn>(pfnInit);
            var fnShutdown = Marshal.GetDelegateForFunctionPointer<NvmlShutdownFn>(pfnShutdown);
            var fnHandle   = Marshal.GetDelegateForFunctionPointer<NvmlGetHandleFn>(pfnHandle);
            var fnTemp     = Marshal.GetDelegateForFunctionPointer<NvmlGetTempFn>(pfnTemp);

            if (fnInit() != 0) return -1;
            try
            {
                if (fnHandle(0, out IntPtr device) != 0) return -1;
                if (fnTemp(device, 0 /*NVML_TEMPERATURE_GPU*/, out uint t) != 0) return -1;
                return (double)t;   // NVML returns Celsius directly
            }
            finally { fnShutdown(); }
        }
        catch { }
        finally { FreeLibrary(lib); }
        return -1;
    }

    /// <summary>
    /// Reads Bluetooth device batteries.
    ///
    /// Strategy:
    /// 1. Scan ALL device containers for System.Devices.BatteryPercent (permissive
    ///    type cast — drivers may return byte, ushort, uint, or int).
    /// 2. For each connected BT/BLE device, look up its ContainerId and call
    ///    CreateFromIdAsync to force battery property population for that specific
    ///    container — more reliable than the bulk scan for devices like WH-1000XM5.
    /// 3. Fall back to name-only entries for any connected device not yet seen.
    /// </summary>
    private static async Task<IReadOnlyList<BtBatteryInfo>> ReadBluetoothBatteriesAsync()
    {
        const string battName = "System.Devices.BatteryPercent";
        const string battGuid = "{104EA319-6EE2-3DF1-8778-A5B7ABF5AB51} 2";
        var battProps = new[] { battName, battGuid };

        var result = new List<BtBatteryInfo>();
        var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Permissive numeric cast — drivers vary in the exact type stored.
        static int TryReadBattery(object? v) => v switch
        {
            byte   b => b,
            sbyte  s => s >= 0 ? s : -1,
            ushort u => u <= 100 ? u : -1,
            short  s => s >= 0 && s <= 100 ? s : -1,
            uint   u => u <= 100 ? (int)u : -1,
            int    i => i is >= 0 and <= 100 ? i : -1,
            _        => -1,
        };

        // ── Step 1: bulk container scan ───────────────────────────────────────
        try
        {
            var containers = await Windows.Devices.Enumeration.DeviceInformation
                .FindAllAsync("", battProps,
                    Windows.Devices.Enumeration.DeviceInformationKind.DeviceContainer)
                .AsTask().ConfigureAwait(false);

            foreach (var c in containers)
            {
                int pct = -1;
                if (c.Properties.TryGetValue(battName, out var v1)) pct = TryReadBattery(v1);
                if (pct < 0 && c.Properties.TryGetValue(battGuid, out var v2)) pct = TryReadBattery(v2);

                if (pct >= 0 && seen.Add(c.Name))
                    result.Add(new BtBatteryInfo(c.Name, pct));
            }
        }
        catch { }

        // ── Step 2: targeted CreateFromIdAsync per connected device ───────────
        // FindAllAsync with an empty selector may skip populating battery properties
        // for some containers. Fetching by explicit container ID forces the runtime
        // to query the property store for that device specifically.
        async Task TryAddByContainerId(string selector)
        {
            try
            {
                const string cidProp = "System.Devices.ContainerId";
                var devices = await Windows.Devices.Enumeration.DeviceInformation
                    .FindAllAsync(selector, new[] { cidProp })
                    .AsTask().ConfigureAwait(false);

                foreach (var di in devices)
                {
                    if (!di.Properties.TryGetValue(cidProp, out var cidObj) || cidObj is not Guid cid)
                        continue;

                    var cidStr = cid.ToString("B").ToUpperInvariant(); // {XXXXXXXX-…}
                    try
                    {
                        var container = await Windows.Devices.Enumeration.DeviceInformation
                            .CreateFromIdAsync(cidStr, battProps,
                                Windows.Devices.Enumeration.DeviceInformationKind.DeviceContainer)
                            .AsTask().ConfigureAwait(false);

                        int pct = -1;
                        if (container.Properties.TryGetValue(battName, out var v1)) pct = TryReadBattery(v1);
                        if (pct < 0 && container.Properties.TryGetValue(battGuid, out var v2)) pct = TryReadBattery(v2);

                        var name = string.IsNullOrWhiteSpace(container.Name) ? di.Name : container.Name;
                        if (pct >= 0 && seen.Add(name))
                            result.Add(new BtBatteryInfo(name, pct));
                        else if (pct < 0 && seen.Add(name))
                            result.Add(new BtBatteryInfo(name, -1));
                    }
                    catch
                    {
                        if (seen.Add(di.Name))
                            result.Add(new BtBatteryInfo(di.Name, -1));
                    }
                }
            }
            catch { }
        }

        await TryAddByContainerId(Windows.Devices.Bluetooth.BluetoothDevice
            .GetDeviceSelectorFromConnectionStatus(
                Windows.Devices.Bluetooth.BluetoothConnectionStatus.Connected))
            .ConfigureAwait(false);

        await TryAddByContainerId(Windows.Devices.Bluetooth.BluetoothLEDevice
            .GetDeviceSelectorFromConnectionStatus(
                Windows.Devices.Bluetooth.BluetoothConnectionStatus.Connected))
            .ConfigureAwait(false);

        return result;
    }

    private static long ToLong(FILETIME ft) => (long)((ulong)ft.High << 32 | ft.Low);

    // ── PDH interop ───────────────────────────────────────────────────────────

    private const uint PDH_FMT_DOUBLE = 0x00000200;

    // PDH_FMT_COUNTERVALUE_ITEM on x64: ptr(8) + DWORD(4) + pad(4) + double(8) = 24 bytes
    [StructLayout(LayoutKind.Explicit)]
    private struct PdhFmtItem
    {
        [FieldOffset(0)]  public IntPtr Name;
        [FieldOffset(8)]  public uint   CStatus;
        [FieldOffset(16)] public double DoubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PDH_FMT_COUNTERVALUE
    {
        public uint   CStatus;
        private uint  _pad;
        public double DoubleValue;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(string? source, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PDH_FMT_COUNTERVALUE value);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, ref uint itemCount, IntPtr buffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);

    // ── Kernel32 interop ──────────────────────────────────────────────────────

    [DllImport("kernel32.dll")] private static extern bool  GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);
    [DllImport("kernel32.dll")] private static extern bool  GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
    [DllImport("kernel32.dll")] private static extern ulong GetTickCount64();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

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

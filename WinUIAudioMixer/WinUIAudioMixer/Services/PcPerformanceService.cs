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
    IReadOnlyList<BtBatteryInfo> BtDevices = null!);

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
    private double _cpuTempC = -1;   // WMI ACPI thermal zone
    private double _gpuTempC = -1;   // WMI OHM/LHM
    private IReadOnlyList<BtBatteryInfo> _btDevices = Array.Empty<BtBatteryInfo>();
    private System.Threading.Timer? _tempTimer;
    private System.Threading.Timer? _btTimer;

    public PcPerformanceService()
    {
        InitPdh();
        // CPU + GPU temps via WMI — every 5 s
        _tempTimer = new System.Threading.Timer(
            _ => { _cpuTempC = ReadCpuTempViaWmi(); _gpuTempC = ReadGpuTempViaWmi(); },
            null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        // Bluetooth battery — every 30 s (slower; involves GATT reads)
        _btTimer = new System.Threading.Timer(
            _ => _ = StoreBtBatteriesAsync(),
            null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
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
                              CpuTempC: _cpuTempC, GpuTempC: _gpuTempC, BtDevices: _btDevices);
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
        if (_pdhQuery != IntPtr.Zero) { PdhCloseQuery(_pdhQuery); _pdhQuery = IntPtr.Zero; }
    }

    /// <summary>
    /// Reads CPU temperature from the ACPI thermal zone WMI class.
    /// CurrentTemperature is in tenths of Kelvin (e.g. 2981 = 24.95 °C).
    /// Returns the highest plausible zone temp in °C, or -1 if unavailable.
    /// </summary>
    private static double ReadCpuTempViaWmi()
    {
        try
        {
            var scope   = new System.Management.ManagementScope(@"root\wmi");
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
            return best;
        }
        catch { return -1; }
    }

    /// <summary>
    /// Queries OpenHardwareMonitor or LibreHardwareMonitor WMI provider for
    /// GPU temperature.  Returns the temperature in °C, or -1 if unavailable.
    /// Requires OHM/LHM to be running with its WMI provider enabled.
    /// </summary>
    private static double ReadGpuTempViaWmi()
    {
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
                    if (obj["Value"] is float v && v > 0 && v < 120)
                        return Math.Round(v, 1);
                }
            }
            catch { }
        }
        return -1;
    }

    /// <summary>
    /// Enumerates connected Bluetooth devices and reads each one's battery level
    /// via the standard GATT Battery Service (UUID 0x180F).  Works for most
    /// modern BLE headphones, mice, and keyboards.  Classic BR/EDR-only devices
    /// that do not advertise BLE will show BatteryPercent = -1.
    /// </summary>
    private static async Task<IReadOnlyList<BtBatteryInfo>> ReadBluetoothBatteriesAsync()
    {
        var result = new List<BtBatteryInfo>();
        try
        {
            string selector = Windows.Devices.Bluetooth.BluetoothDevice
                .GetDeviceSelectorFromConnectionStatus(
                    Windows.Devices.Bluetooth.BluetoothConnectionStatus.Connected);
            var devices = await Windows.Devices.Enumeration.DeviceInformation
                .FindAllAsync(selector).AsTask().ConfigureAwait(false);

            foreach (var di in devices)
            {
                try
                {
                    using var bt = await Windows.Devices.Bluetooth.BluetoothDevice
                        .FromIdAsync(di.Id).AsTask().ConfigureAwait(false);
                    if (bt == null) continue;

                    int pct = -1;
                    try
                    {
                        using var le = await Windows.Devices.Bluetooth.BluetoothLEDevice
                            .FromBluetoothAddressAsync(bt.BluetoothAddress)
                            .AsTask().ConfigureAwait(false);
                        if (le != null)
                        {
                            var svcResult = await le.GetGattServicesForUuidAsync(
                                Windows.Devices.Bluetooth.GenericAttributeProfile.GattServiceUuids.Battery,
                                Windows.Devices.Bluetooth.BluetoothCacheMode.Cached)
                                .AsTask().ConfigureAwait(false);
                            if (svcResult.Status == Windows.Devices.Bluetooth.GenericAttributeProfile.GattCommunicationStatus.Success
                                && svcResult.Services.Count > 0)
                            {
                                using var svc = svcResult.Services[0];
                                var chars = await svc.GetCharacteristicsForUuidAsync(
                                    Windows.Devices.Bluetooth.GenericAttributeProfile.GattCharacteristicUuids.BatteryLevel)
                                    .AsTask().ConfigureAwait(false);
                                if (chars.Status == Windows.Devices.Bluetooth.GenericAttributeProfile.GattCommunicationStatus.Success
                                    && chars.Characteristics.Count > 0)
                                {
                                    var read = await chars.Characteristics[0]
                                        .ReadValueAsync(Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached)
                                        .AsTask().ConfigureAwait(false);
                                    if (read.Status == Windows.Devices.Bluetooth.GenericAttributeProfile.GattCommunicationStatus.Success)
                                        pct = Windows.Storage.Streams.DataReader.FromBuffer(read.Value).ReadByte();
                                }
                            }
                        }
                    }
                    catch { }

                    result.Add(new BtBatteryInfo(bt.Name, pct));
                }
                catch { }
            }
        }
        catch { }
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

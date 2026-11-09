using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Orbit.Core;

namespace Orbit.Services.SystemMonitor;

public sealed class SystemMonitorService : ISystemMonitorService
{
    private const string LogCategory = "SYSTEM_MONITOR";

    // Win32 FILETIME and GetSystemTimes for CPU utilization
    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
        public ulong Value => ((ulong)dwHighDateTime << 32) | dwLowDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

    // Win32 MEMORYSTATUSEX for RAM utilization
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    // PDH native imports for GPU utilization
    private const uint PDH_FMT_DOUBLE = 0x00000200;
    private const uint PDH_MORE_DATA = 0x800007D2;

    [DllImport("pdh.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(string? szDataSource, IntPtr dwUserData, out IntPtr phQuery);

    [DllImport("pdh.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(IntPtr hQuery, string szFullCounterPath, IntPtr dwUserData, out IntPtr phCounter);

    [DllImport("pdh.dll", SetLastError = true)]
    private static extern uint PdhCollectQueryData(IntPtr hQuery);

    [DllImport("pdh.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArray(IntPtr hCounter, uint dwFormat, ref uint lpdwBufferSize, ref uint lpdwItemCount, IntPtr ItemBuffer);

    [DllImport("pdh.dll", SetLastError = true)]
    private static extern uint PdhCloseQuery(IntPtr hQuery);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PDH_FMT_COUNTERVALUE_ITEM
    {
        public IntPtr szName;
        public uint CStatus;
        public double doubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    public event Action<SystemMetrics>? MetricsUpdated;

    private readonly object _lock = new();
    private System.Threading.Timer? _samplingTimer;
    private bool _isMonitoring = false;

    // Last sampled state
    private FILETIME _lastIdleTime;
    private FILETIME _lastKernelTime;
    private FILETIME _lastUserTime;
    private bool _hasCpuBaseline = false;
    private double _lastCpuPercent = 0.0;

    private double _lastRamUsedGb = 0.0;
    private double _lastRamTotalGb = 0.0;
    private int _lastRamPercent = 0;

    private long _lastRxBytes = 0;
    private long _lastTxBytes = 0;
    private DateTime _lastWifiSampleTime = DateTime.MinValue;
    private double _lastDownloadSpeedBps = 0.0;
    private double _lastUploadSpeedBps = 0.0;

    private IntPtr _pdhQuery = IntPtr.Zero;
    private IntPtr _pdhCounter = IntPtr.Zero;
    private double _lastGpuPercent = 0.0;

    public void StartMonitoring()
    {
        lock (_lock)
        {
            if (_isMonitoring) return;
            _isMonitoring = true;

            OrbitLogger.Info(LogCategory, "Starting lightweight local system metrics sampling (~1s cadence)...");

            // Baseline CPU
            if (GetSystemTimes(out _lastIdleTime, out _lastKernelTime, out _lastUserTime))
            {
                _hasCpuBaseline = true;
            }

            // Baseline Wi-Fi
            InitWifiBaseline();

            // Baseline GPU
            InitGpuPdh();

            // Initial immediate sample
            SampleMetrics();

            // Set up ~1-second background sampling timer
            _samplingTimer = new System.Threading.Timer(OnTimerTick, null, 1000, 1000);
        }
    }

    public void StopMonitoring()
    {
        lock (_lock)
        {
            if (!_isMonitoring) return;
            _isMonitoring = false;

            OrbitLogger.Info(LogCategory, "Stopping system metrics sampling and releasing handles.");

            _samplingTimer?.Dispose();
            _samplingTimer = null;

            CleanupGpuPdh();
            _hasCpuBaseline = false;
            _lastWifiSampleTime = DateTime.MinValue;
            _lastRxBytes = 0;
            _lastTxBytes = 0;
        }
    }

    public SystemMetrics GetCurrentMetrics()
    {
        lock (_lock)
        {
            if (_lastRamTotalGb <= 0)
            {
                SampleRam();
            }

            int batteryPercent = -1;
            bool isCharging = false;
            bool isOnBattery = false;

            if (GetSystemPowerStatus(out var power))
            {
                if (power.BatteryLifePercent <= 100)
                {
                    batteryPercent = power.BatteryLifePercent;
                }
                isCharging = (power.BatteryFlag & 8) != 0;
                isOnBattery = power.ACLineStatus == 0;
            }

            return new SystemMetrics(
                CpuUsagePercent: _lastCpuPercent,
                RamUsedGb: _lastRamUsedGb,
                RamTotalGb: _lastRamTotalGb,
                RamUsagePercent: _lastRamPercent,
                WifiDownloadSpeedBps: _lastDownloadSpeedBps,
                WifiUploadSpeedBps: _lastUploadSpeedBps,
                GpuUsagePercent: _lastGpuPercent,
                BatteryPercent: batteryPercent,
                IsCharging: isCharging,
                IsOnBattery: isOnBattery);
        }
    }

    private void OnTimerTick(object? state)
    {
        if (!_isMonitoring) return;
        SampleMetrics();
    }

    private void SampleMetrics()
    {
        try
        {
            SampleCpu();
            SampleRam();
            SampleWifi();
            SampleGpu();

            var metrics = GetCurrentMetrics();
            MetricsUpdated?.Invoke(metrics);
        }
        catch (Exception ex)
        {
            OrbitLogger.Warn(LogCategory, $"Metric sampling error: {ex.Message}");
        }
    }

    private void SampleCpu()
    {
        if (!GetSystemTimes(out var currentIdle, out var currentKernel, out var currentUser))
        {
            return;
        }

        if (_hasCpuBaseline)
        {
            ulong dIdle = currentIdle.Value - _lastIdleTime.Value;
            ulong dKernel = currentKernel.Value - _lastKernelTime.Value;
            ulong dUser = currentUser.Value - _lastUserTime.Value;
            ulong total = dKernel + dUser;

            if (total > 0 && dIdle <= total)
            {
                double idleFraction = (double)dIdle / total;
                double usage = (1.0 - idleFraction) * 100.0;
                _lastCpuPercent = Math.Clamp(Math.Round(usage, 1), 0.0, 100.0);
            }
        }
        else
        {
            _hasCpuBaseline = true;
        }

        _lastIdleTime = currentIdle;
        _lastKernelTime = currentKernel;
        _lastUserTime = currentUser;
    }

    private void SampleRam()
    {
        var mem = new MEMORYSTATUSEX();
        if (GlobalMemoryStatusEx(mem))
        {
            _lastRamTotalGb = Math.Round(mem.ullTotalPhys / (1024.0 * 1024.0 * 1024.0), 1);
            double usedBytes = (double)mem.ullTotalPhys - (double)mem.ullAvailPhys;
            _lastRamUsedGb = Math.Round(usedBytes / (1024.0 * 1024.0 * 1024.0), 1);

            if (_lastRamTotalGb > 0)
            {
                _lastRamPercent = (int)Math.Clamp(Math.Round((_lastRamUsedGb / _lastRamTotalGb) * 100.0), 0, 100);
            }
            else
            {
                _lastRamPercent = (int)mem.dwMemoryLoad;
            }
        }
    }

    private void InitWifiBaseline()
    {
        try
        {
            var nic = GetActiveNetworkInterface();
            if (nic != null)
            {
                var stats = nic.GetIPv4Statistics();
                _lastRxBytes = stats.BytesReceived;
                _lastTxBytes = stats.BytesSent;
                _lastWifiSampleTime = DateTime.UtcNow;
            }
        }
        catch { }
    }

    private void SampleWifi()
    {
        try
        {
            var nic = GetActiveNetworkInterface();
            if (nic == null)
            {
                _lastDownloadSpeedBps = 0.0;
                _lastUploadSpeedBps = 0.0;
                return;
            }

            var stats = nic.GetIPv4Statistics();
            long currentRx = stats.BytesReceived;
            long currentTx = stats.BytesSent;
            DateTime now = DateTime.UtcNow;

            if (_lastWifiSampleTime != DateTime.MinValue)
            {
                double dt = (now - _lastWifiSampleTime).TotalSeconds;
                if (dt >= 0.4)
                {
                    if (_lastRxBytes > 0 && currentRx >= _lastRxBytes)
                    {
                        _lastDownloadSpeedBps = (currentRx - _lastRxBytes) / dt;
                    }
                    if (_lastTxBytes > 0 && currentTx >= _lastTxBytes)
                    {
                        _lastUploadSpeedBps = (currentTx - _lastTxBytes) / dt;
                    }
                }
            }

            _lastRxBytes = currentRx;
            _lastTxBytes = currentTx;
            _lastWifiSampleTime = now;
        }
        catch
        {
            _lastDownloadSpeedBps = 0.0;
            _lastUploadSpeedBps = 0.0;
        }
    }

    private static NetworkInterface? GetActiveNetworkInterface()
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();

            // 1. Prefer active Wireless80211 Wi-Fi interface
            var wifi = interfaces.FirstOrDefault(n =>
                n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                n.OperationalStatus == OperationalStatus.Up);

            if (wifi != null) return wifi;

            // 2. Fallback to active Ethernet or any active non-loopback interface
            return interfaces.FirstOrDefault(n =>
                n.OperationalStatus == OperationalStatus.Up &&
                n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                n.NetworkInterfaceType != NetworkInterfaceType.Tunnel);
        }
        catch
        {
            return null;
        }
    }

    private void InitGpuPdh()
    {
        try
        {
            if (_pdhQuery != IntPtr.Zero) return;

            uint res = PdhOpenQuery(null, IntPtr.Zero, out _pdhQuery);
            if (res == 0)
            {
                res = PdhAddEnglishCounter(_pdhQuery, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out _pdhCounter);
                if (res == 0)
                {
                    PdhCollectQueryData(_pdhQuery);
                }
            }
        }
        catch (Exception ex)
        {
            OrbitLogger.Warn(LogCategory, $"PDH GPU initialization: {ex.Message}");
        }
    }

    private void SampleGpu()
    {
        if (_pdhQuery == IntPtr.Zero || _pdhCounter == IntPtr.Zero) return;

        try
        {
            uint res = PdhCollectQueryData(_pdhQuery);
            if (res != 0) return;

            uint bufferSize = 0;
            uint itemCount = 0;
            res = PdhGetFormattedCounterArray(_pdhCounter, PDH_FMT_DOUBLE, ref bufferSize, ref itemCount, IntPtr.Zero);
            if (res == PDH_MORE_DATA && bufferSize > 0)
            {
                IntPtr buffer = Marshal.AllocHGlobal((int)bufferSize);
                try
                {
                    res = PdhGetFormattedCounterArray(_pdhCounter, PDH_FMT_DOUBLE, ref bufferSize, ref itemCount, buffer);
                    if (res == 0)
                    {
                        double total = 0.0;
                        int itemSize = Marshal.SizeOf<PDH_FMT_COUNTERVALUE_ITEM>();
                        for (int i = 0; i < itemCount; i++)
                        {
                            IntPtr itemPtr = IntPtr.Add(buffer, i * itemSize);
                            var item = Marshal.PtrToStructure<PDH_FMT_COUNTERVALUE_ITEM>(itemPtr);
                            if (item.CStatus == 0 && item.doubleValue > 0)
                            {
                                total += item.doubleValue;
                            }
                        }
                        _lastGpuPercent = Math.Clamp(Math.Round(total, 1), 0.0, 100.0);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }
        catch
        {
            // Safeguard
        }
    }

    private void CleanupGpuPdh()
    {
        try
        {
            if (_pdhQuery != IntPtr.Zero)
            {
                PdhCloseQuery(_pdhQuery);
                _pdhQuery = IntPtr.Zero;
                _pdhCounter = IntPtr.Zero;
            }
        }
        catch { }
    }

    public void Dispose()
    {
        StopMonitoring();
    }
}


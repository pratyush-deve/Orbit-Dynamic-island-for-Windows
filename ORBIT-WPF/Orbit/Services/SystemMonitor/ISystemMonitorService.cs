namespace Orbit.Services.SystemMonitor;

/// <summary>
/// Snapshot of live system performance metrics.
/// </summary>
public record SystemMetrics(
    double CpuUsagePercent,
    double RamUsedGb,
    double RamTotalGb,
    int RamUsagePercent,
    double WifiDownloadSpeedBps,
    double WifiUploadSpeedBps,
    double GpuUsagePercent,
    int BatteryPercent = -1,
    bool IsCharging = false,
    bool IsOnBattery = false);

/// <summary>
/// Service providing high-performance, lightweight local system metrics (CPU, RAM, Wi-Fi, GPU).
/// </summary>
public interface ISystemMonitorService : IDisposable
{
    /// <summary>
    /// Raised approximately once per second when new metrics are sampled.
    /// </summary>
    event Action<SystemMetrics>? MetricsUpdated;

    /// <summary>
    /// Starts sampling system metrics approximately once per second.
    /// </summary>
    void StartMonitoring();

    /// <summary>
    /// Stops sampling system metrics and releases monitoring handles.
    /// </summary>
    void StopMonitoring();

    /// <summary>
    /// Returns the most recently sampled metrics snapshot.
    /// </summary>
    SystemMetrics GetCurrentMetrics();
}


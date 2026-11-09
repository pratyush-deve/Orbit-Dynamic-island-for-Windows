using System;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Core;
using Orbit.Services.SystemMonitor;

namespace Orbit.Services.Commands.Handlers;

public sealed class SystemMetricsHandler : ICommandHandler
{
    private const string LogCategory = "SYSTEM_METRICS_HANDLER";
    private readonly ISystemMonitorService _monitorService;

    public CommandType SupportedType => CommandType.SystemMetrics;

    public SystemMetricsHandler(ISystemMonitorService monitorService)
    {
        _monitorService = monitorService;
    }

    public Task<CommandResult> ExecuteAsync(CommandIntent intent, CancellationToken cancellationToken = default)
    {
        var m = _monitorService.GetCurrentMetrics();

        string msg;
        switch (intent.MetricsSubtype)
        {
            case SystemMetricsSubtype.Ram:
                msg = $"RAM Usage: {m.RamUsedGb:F1} GB used of {m.RamTotalGb:F1} GB ({m.RamUsagePercent}%).";
                break;

            case SystemMetricsSubtype.Battery:
                if (m.BatteryPercent >= 0)
                {
                    string status = m.IsCharging ? "Charging" : (m.IsOnBattery ? "On Battery" : "Plugged in");
                    msg = $"Battery: {m.BatteryPercent}% ({status}).";
                }
                else
                {
                    msg = "Battery status is unavailable on this device.";
                }
                break;

            case SystemMetricsSubtype.Cpu:
                msg = $"CPU Usage: {m.CpuUsagePercent:F0}%.";
                break;

            case SystemMetricsSubtype.Gpu:
                msg = $"GPU Usage: {m.GpuUsagePercent:F0}%.";
                break;

            case SystemMetricsSubtype.All:
            default:
                string battStr = m.BatteryPercent >= 0 ? $", Battery {m.BatteryPercent}%" : "";
                msg = $"Laptop Status: CPU {m.CpuUsagePercent:F0}%, RAM {m.RamUsedGb:F1}/{m.RamTotalGb:F1} GB ({m.RamUsagePercent}%), GPU {m.GpuUsagePercent:F0}%{battStr}. Everything running smoothly.";
                break;
        }

        OrbitLogger.Info(LogCategory, $"System metrics queried ({intent.MetricsSubtype}): {msg}");
        return Task.FromResult(new CommandResult(true, msg, intent));
    }
}


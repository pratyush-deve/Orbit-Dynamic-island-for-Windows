using System.Windows;
using Orbit.Core;
using Orbit.Services;

namespace Orbit.Platform.Windows;

public interface IWindowPositionService
{
    void CenterOnMonitor(Window targetWindow);
}

/// <summary>
/// Service managing deterministic top-centre positioning on the primary monitor.
/// Dragging has been completely removed.
/// </summary>
public sealed class WindowPositionService : IWindowPositionService
{
    private const string LogCategory = "POSITION";
    private readonly IMonitorService _monitorService;

    public WindowPositionService(IMonitorService monitorService)
    {
        _monitorService = monitorService;
    }

    public void CenterOnMonitor(Window targetWindow)
    {
        var monitor = _monitorService.GetPrimaryMonitor();
        var width = targetWindow.ActualWidth > 0 ? targetWindow.ActualWidth : targetWindow.Width;
        var (x, y) = _monitorService.GetTopCenterPosition(monitor, width, 14.0);

        targetWindow.Left = x;
        targetWindow.Top = y;

        OrbitLogger.Info(LogCategory, $"Reset window position to primary monitor top-center: [{x:0.#}, {y:0.#}]");
    }
}

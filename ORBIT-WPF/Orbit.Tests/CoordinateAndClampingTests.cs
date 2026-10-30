using Orbit.Models;
using Orbit.Platform.Windows;
using Xunit;

namespace Orbit.Tests;

public class CoordinateAndClampingTests
{
    private readonly MonitorService _monitorService = new();

    [Fact]
    public void TopCenterPosition_CalculatesCorrectCoordinates()
    {
        // Primary monitor 1920x1080 with 40px taskbar at bottom (WorkArea height = 1040)
        var monitor = new MonitorInfo
        {
            DeviceName = @"\\.\DISPLAY1",
            Bounds = new MonitorBounds(0, 0, 1920, 1080),
            WorkArea = new MonitorBounds(0, 0, 1920, 1040),
            IsPrimary = true
        };

        double windowWidth = 140.0;
        double topMargin = 14.0;

        var (x, y) = _monitorService.GetTopCenterPosition(monitor, windowWidth, topMargin);

        // Expected X: (1920 - 140) / 2 = 890.0
        // Expected Y: 0 + 14 = 14.0
        Assert.Equal(890.0, x);
        Assert.Equal(14.0, y);
    }

    [Fact]
    public void ClampToWorkArea_ClampsWithinMonitorBounds_WhenOutOfBounds()
    {
        var monitor = new MonitorInfo
        {
            DeviceName = @"\\.\DISPLAY1",
            Bounds = new MonitorBounds(0, 0, 1920, 1080),
            WorkArea = new MonitorBounds(0, 0, 1920, 1040),
            IsPrimary = true
        };

        double width = 140.0;
        double height = 56.0;

        // Test 1: Window dragged too far left (negative X)
        var (clampedLeft, clampedTest1Y) = _monitorService.ClampToWorkArea(-50, 100, width, height, monitor);
        Assert.Equal(0.0, clampedLeft);
        Assert.Equal(100.0, clampedTest1Y);

        // Test 2: Window dragged too far right (X > Right - Width)
        var (clampedRight, _) = _monitorService.ClampToWorkArea(2000, 100, width, height, monitor);
        Assert.Equal(1920.0 - width, clampedRight);

        // Test 3: Window dragged too far up (negative Y)
        var (_, clampedTop) = _monitorService.ClampToWorkArea(500, -20, width, height, monitor);
        Assert.Equal(0.0, clampedTop);

        // Test 4: Window dragged too far down (Y > Bottom - Height)
        var (_, clampedBottom) = _monitorService.ClampToWorkArea(500, 1050, width, height, monitor);
        Assert.Equal(1040.0 - height, clampedBottom);
    }

    [Fact]
    public void ClampToWorkArea_SupportsNegativeMonitorCoordinates()
    {
        // Multi-monitor scenario: Left monitor positioned at X = -1920
        var leftSecondaryMonitor = new MonitorInfo
        {
            DeviceName = @"\\.\DISPLAY2",
            Bounds = new MonitorBounds(-1920, 0, 1920, 1080),
            WorkArea = new MonitorBounds(-1920, 0, 1920, 1040),
            IsPrimary = false
        };

        double width = 140.0;
        double height = 56.0;

        // Window placed safely inside secondary monitor
        var (x1, y1) = _monitorService.ClampToWorkArea(-1000, 100, width, height, leftSecondaryMonitor);
        Assert.Equal(-1000.0, x1);
        Assert.Equal(100.0, y1);

        // Window dragged past far left edge of secondary monitor
        var (x2, _) = _monitorService.ClampToWorkArea(-2000, 100, width, height, leftSecondaryMonitor);
        Assert.Equal(-1920.0, x2);
    }

    [Fact]
    public void MonitorBounds_ContainsPoint_ReturnsAccurateResults()
    {
        var bounds = new MonitorBounds(100, 200, 800, 600);

        Assert.True(bounds.Contains(150, 250));
        Assert.True(bounds.Contains(100, 200)); // Inclusive top-left
        Assert.False(bounds.Contains(900, 250)); // Right edge exclusive
        Assert.False(bounds.Contains(50, 250));  // Out of bounds left
        Assert.False(bounds.Contains(150, 900)); // Out of bounds bottom
    }
}

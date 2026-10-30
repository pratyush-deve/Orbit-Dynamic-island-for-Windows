using Orbit.Core;
using Orbit.Models;
using Orbit.Platform.Windows;
using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

public class SettingsValidationTests
{
    private class MockMonitorService : IMonitorService
    {
        private readonly List<MonitorInfo> _monitors;

        public MockMonitorService(List<MonitorInfo> monitors)
        {
            _monitors = monitors;
        }

        public IReadOnlyList<MonitorInfo> GetAllMonitors() => _monitors;

        public MonitorInfo GetPrimaryMonitor() =>
            _monitors.FirstOrDefault(m => m.IsPrimary) ?? _monitors[0];

        public (double X, double Y) GetTopCenterPosition(MonitorInfo monitor, double windowWidth, double topMargin = 14)
        {
            var work = monitor.WorkArea;
            var centerX = work.Left + ((work.Width - windowWidth) / 2.0);
            return (centerX, work.Top + topMargin);
        }

        public (double X, double Y) ClampToWorkArea(double x, double y, double width, double height, MonitorInfo? preferredMonitor = null)
        {
            var mon = preferredMonitor ?? GetPrimaryMonitor();
            var work = mon.WorkArea;
            return (Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - width)),
                    Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - height)));
        }
    }

    [Fact]
    public void Settings_ClonesCorrectly()
    {
        var settings = new OrbitSettings
        {
            EyeGlowEnabled = false,
            StayOnTop = true
        };

        var clone = settings.Clone();

        Assert.Equal(settings.EyeGlowEnabled, clone.EyeGlowEnabled);
        Assert.Equal(settings.StayOnTop, clone.StayOnTop);
    }

    [Fact]
    public void GetTopCenterPosition_ReturnsConsistentTopCenter()
    {
        var mockMonitors = new List<MonitorInfo>
        {
            new MonitorInfo
            {
                DeviceName = "Display1",
                Bounds = new MonitorBounds(0, 0, 1920, 1080),
                WorkArea = new MonitorBounds(0, 0, 1920, 1040),
                IsPrimary = true
            }
        };

        var monitorService = new MockMonitorService(mockMonitors);
        var settingsService = new SettingsService(monitorService);

        var (x, y) = settingsService.GetTopCenterPosition(140.0);

        Assert.Equal(890.0, x);
        Assert.Equal(14.0, y);
    }

    [Fact]
    public void EyeGlow_CanBeToggledAndPersisted()
    {
        var mockMonitors = new List<MonitorInfo>
        {
            new MonitorInfo
            {
                DeviceName = "Display1",
                Bounds = new MonitorBounds(0, 0, 1920, 1080),
                WorkArea = new MonitorBounds(0, 0, 1920, 1040),
                IsPrimary = true
            }
        };

        var monitorService = new MockMonitorService(mockMonitors);
        var settingsService = new SettingsService(monitorService);

        settingsService.UpdateEyeGlow(false);
        Assert.False(settingsService.CurrentSettings.EyeGlowEnabled);

        settingsService.UpdateEyeGlow(true);
        Assert.True(settingsService.CurrentSettings.EyeGlowEnabled);
    }
}

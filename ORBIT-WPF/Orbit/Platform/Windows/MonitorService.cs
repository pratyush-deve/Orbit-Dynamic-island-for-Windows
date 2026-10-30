using System.Runtime.InteropServices;
using Orbit.Core;
using Orbit.Models;

namespace Orbit.Platform.Windows;

public interface IMonitorService
{
    IReadOnlyList<MonitorInfo> GetAllMonitors();
    MonitorInfo GetPrimaryMonitor();
    (double X, double Y) GetTopCenterPosition(MonitorInfo monitor, double windowWidth, double topMargin = 14.0);
    (double X, double Y) ClampToWorkArea(double x, double y, double width, double height, MonitorInfo? preferredMonitor = null);
}

/// <summary>
/// Service providing DPI-aware primary monitor detection, work-area dimensions in WPF DIPs,
/// and deterministic top-centre placement.
/// </summary>
public sealed class MonitorService : IMonitorService
{
    private const string LogCategory = "MONITOR";

    public IReadOnlyList<MonitorInfo> GetAllMonitors()
    {
        var monitors = new List<MonitorInfo>();

        NativeMethods.MonitorEnumProc callback = (IntPtr hMonitor, IntPtr hdc, ref NativeMethods.RECT rect, IntPtr data) =>
        {
            var mi = new NativeMethods.MONITORINFOEX();
            mi.cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>();

            if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
            {
                uint dpiX = 96, dpiY = 96;
                try
                {
                    NativeMethods.GetDpiForMonitor(hMonitor, 0, out dpiX, out dpiY);
                }
                catch
                {
                    dpiX = 96;
                    dpiY = 96;
                }
                if (dpiX == 0) dpiX = 96;
                if (dpiY == 0) dpiY = 96;

                var scaleX = dpiX / 96.0;
                var scaleY = dpiY / 96.0;

                var isPrimary = (mi.dwFlags & 1) != 0; // MONITORINFOF_PRIMARY

                // Convert physical Win32 rects to WPF Device-Independent Pixels (DIPs)
                var bounds = new MonitorBounds(
                    mi.rcMonitor.Left / scaleX,
                    mi.rcMonitor.Top / scaleY,
                    mi.rcMonitor.Width / scaleX,
                    mi.rcMonitor.Height / scaleY);

                var workArea = new MonitorBounds(
                    mi.rcWork.Left / scaleX,
                    mi.rcWork.Top / scaleY,
                    mi.rcWork.Width / scaleX,
                    mi.rcWork.Height / scaleY);

                monitors.Add(new MonitorInfo
                {
                    DeviceName = mi.szDevice ?? "Unknown",
                    Bounds = bounds,
                    WorkArea = workArea,
                    IsPrimary = isPrimary,
                    DpiScaleX = scaleX,
                    DpiScaleY = scaleY
                });
            }
            return true;
        };

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);

        if (monitors.Count == 0)
        {
            // Fallback to WinForms Screen if API failed
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                monitors.Add(new MonitorInfo
                {
                    DeviceName = screen.DeviceName,
                    Bounds = new MonitorBounds(screen.Bounds.Left, screen.Bounds.Top, screen.Bounds.Width, screen.Bounds.Height),
                    WorkArea = new MonitorBounds(screen.WorkingArea.Left, screen.WorkingArea.Top, screen.WorkingArea.Width, screen.WorkingArea.Height),
                    IsPrimary = screen.Primary,
                    DpiScaleX = 1.0,
                    DpiScaleY = 1.0
                });
            }
        }

        return monitors;
    }

    public MonitorInfo GetPrimaryMonitor()
    {
        var all = GetAllMonitors();
        var primary = all.FirstOrDefault(m => m.IsPrimary);
        if (primary != null) return primary;

        if (all.Count > 0) return all[0];

        // Fallback
        return new MonitorInfo
        {
            DeviceName = "FallbackPrimary",
            Bounds = new MonitorBounds(0, 0, 1920, 1080),
            WorkArea = new MonitorBounds(0, 0, 1920, 1040),
            IsPrimary = true,
            DpiScaleX = 1.0,
            DpiScaleY = 1.0
        };
    }

    public (double X, double Y) GetTopCenterPosition(MonitorInfo monitor, double windowWidth, double topMargin = 14.0)
    {
        var work = monitor.WorkArea;
        var centerX = work.Left + ((work.Width - windowWidth) / 2.0);
        var topY = work.Top + topMargin;
        return (centerX, topY);
    }

    public (double X, double Y) ClampToWorkArea(double x, double y, double width, double height, MonitorInfo? preferredMonitor = null)
    {
        var monitor = preferredMonitor ?? GetPrimaryMonitor();
        var work = monitor.WorkArea;

        var minX = work.Left;
        var maxX = Math.Max(minX, work.Right - width);

        var minY = work.Top;
        var maxY = Math.Max(minY, work.Bottom - height);

        var clampedX = Math.Clamp(x, minX, maxX);
        var clampedY = Math.Clamp(y, minY, maxY);

        return (clampedX, clampedY);
    }
}

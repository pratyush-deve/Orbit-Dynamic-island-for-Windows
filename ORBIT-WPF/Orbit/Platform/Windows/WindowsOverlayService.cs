using System.Runtime.InteropServices;
using Orbit.Core;

namespace Orbit.Platform.Windows;

public interface IWindowsOverlayService
{
    void InitializeWindowHandle(IntPtr hwnd);
    void SetTopmost(IntPtr hwnd, bool topmost);
    void ReapplyTopmost(IntPtr hwnd);
    void BringToFront(IntPtr hwnd);
}

/// <summary>
/// Service managing native window styles, topmost Z-order, and toolwindow presentation.
/// </summary>
public sealed class WindowsOverlayService : IWindowsOverlayService
{
    private const string LogCategory = "OVERLAY";

    public void InitializeWindowHandle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            OrbitLogger.Warn(LogCategory, "InitializeWindowHandle called with zero HWND.");
            return;
        }

        try
        {
            // Set WS_EX_TOOLWINDOW so the window does not appear in Alt-Tab or Taskbar,
            // while explicitly keeping mouse clicks and activation working.
            var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            exStyle |= NativeMethods.WS_EX_TOOLWINDOW;
            exStyle &= ~NativeMethods.WS_EX_APPWINDOW;

            // Explicitly ensure WS_EX_TRANSPARENT is NOT set so mouse clicks are received
            exStyle &= ~NativeMethods.WS_EX_TRANSPARENT;

            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle));

            // Set topmost without activating immediately
            SetTopmost(hwnd, true);

            OrbitLogger.Info(LogCategory, $"Initialized HWND 0x{hwnd:X} with ToolWindow and Topmost flags.");
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, "Failed to apply window overlay styles.", ex);
        }
    }

    public void SetTopmost(IntPtr hwnd, bool topmost)
    {
        if (hwnd == IntPtr.Zero) return;

        var targetHwnd = topmost ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_NOTOPMOST;
        const uint flags = NativeMethods.SWP_NOMOVE |
                           NativeMethods.SWP_NOSIZE |
                           NativeMethods.SWP_NOACTIVATE |
                           NativeMethods.SWP_FRAMECHANGED;

        var success = NativeMethods.SetWindowPos(hwnd, targetHwnd, 0, 0, 0, 0, flags);
        if (!success)
        {
            var err = Marshal.GetLastWin32Error();
            OrbitLogger.Warn(LogCategory, $"SetWindowPos failed with Win32 error: {err}");
        }
        else
        {
            OrbitLogger.Debug(LogCategory, $"SetTopmost({topmost}) applied successfully to HWND 0x{hwnd:X}.");
        }
    }

    public void ReapplyTopmost(IntPtr hwnd)
    {
        SetTopmost(hwnd, true);
    }

    public void BringToFront(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWNORMAL);
        NativeMethods.SetForegroundWindow(hwnd);
        ReapplyTopmost(hwnd);
        OrbitLogger.Info(LogCategory, $"Brought HWND 0x{hwnd:X} to foreground.");
    }
}

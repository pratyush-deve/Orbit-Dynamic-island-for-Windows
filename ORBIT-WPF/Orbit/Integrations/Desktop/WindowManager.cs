using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Orbit.Core;
using Orbit.Models;
using Orbit.Platform.Windows;

namespace Orbit.Integrations.Desktop;

/// <summary>
/// High-performance Windows application and desktop window manager.
/// Uses native Win32 window enumeration, DWM cloaked detection, SHGetFileInfo icon extraction,
/// graceful WM_CLOSE dispatch, thread-attached foreground activation, and debounced WinEvent hooks.
/// </summary>
public sealed class WindowManager : IWindowManager
{
    private const string LogCategory = "WINDOW_MGR";

    public event Action? WindowsChanged { add { } remove { } }

    private readonly ConcurrentDictionary<string, ImageSource> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ignoredClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd"
    };

    private bool _disposed;

    public WindowManager()
    {
    }

    public void StartMonitoring()
    {
        // Continuous live background monitoring has been disabled in favor of on-demand refresh on open.
    }

    public void StopMonitoring()
    {
    }

    public Task<IReadOnlyList<WindowInfo>> GetOpenWindowsAsync()
    {
        return Task.Run<IReadOnlyList<WindowInfo>>(() =>
        {
            var windows = new List<WindowInfo>();
            var seenHandles = new HashSet<IntPtr>();
            int currentPid = Environment.ProcessId;

            bool EnumProc(IntPtr hWnd, IntPtr lParam)
            {
                if (seenHandles.Contains(hWnd)) return true;

                // 1. Must be visible
                if (!NativeMethods.IsWindowVisible(hWnd)) return true;

                // 2. Ignore 0-sized windows unless minimized (minimized windows have iconic/offscreen bounds)
                bool isMinimized = NativeMethods.IsIconic(hWnd);
                NativeMethods.RECT rect;
                if (!isMinimized)
                {
                    if (!NativeMethods.GetWindowRect(hWnd, out rect) || rect.Width <= 0 || rect.Height <= 0)
                    {
                        return true;
                    }
                }
                else
                {
                    NativeMethods.GetWindowRect(hWnd, out rect);
                }

                // 3. Ignore tool windows unless WS_EX_APPWINDOW is explicitly present
                int exStyle = (int)NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE);
                if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0 && (exStyle & NativeMethods.WS_EX_APPWINDOW) == 0)
                {
                    return true;
                }

                // 4. Ignore child/owned windows unless WS_EX_APPWINDOW is set
                IntPtr owner = NativeMethods.GetWindow(hWnd, NativeMethods.GW_OWNER);
                if (owner != IntPtr.Zero && (exStyle & NativeMethods.WS_EX_APPWINDOW) == 0)
                {
                    return true;
                }

                // 5. Must have a non-empty window title
                int titleLength = NativeMethods.GetWindowTextLength(hWnd);
                if (titleLength <= 0) return true;

                var titleSb = new StringBuilder(titleLength + 1);
                NativeMethods.GetWindowText(hWnd, titleSb, titleSb.Capacity);
                string title = titleSb.ToString().Trim();
                if (string.IsNullOrWhiteSpace(title)) return true;

                // 6. Check window class name to filter shell & desktop artifacts
                var classSb = new StringBuilder(256);
                NativeMethods.GetClassName(hWnd, classSb, classSb.Capacity);
                string className = classSb.ToString();
                if (_ignoredClasses.Contains(className)) return true;

                // 7. Check DWM cloaked state (filters inactive virtual desktop & suspended UWP apps)
                if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                {
                    return true;
                }

                // 8. Filter out ORBIT's own process
                NativeMethods.GetWindowThreadProcessId(hWnd, out uint processId);
                if (processId == 0 || processId == currentPid) return true;

                seenHandles.Add(hWnd);

                // 9. Resolve process name, app name and executable path
                string processName = string.Empty;
                string executablePath = string.Empty;
                string appName = string.Empty;

                try
                {
                    IntPtr hProcess = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
                    if (hProcess != IntPtr.Zero)
                    {
                        try
                        {
                            var exeSb = new StringBuilder(1024);
                            int size = exeSb.Capacity;
                            if (NativeMethods.QueryFullProcessImageName(hProcess, 0, exeSb, ref size))
                            {
                                executablePath = exeSb.ToString();
                            }
                        }
                        finally
                        {
                            NativeMethods.CloseHandle(hProcess);
                        }
                    }

                    if (!string.IsNullOrEmpty(executablePath) && File.Exists(executablePath))
                    {
                        var versionInfo = FileVersionInfo.GetVersionInfo(executablePath);
                        appName = !string.IsNullOrWhiteSpace(versionInfo.FileDescription)
                            ? versionInfo.FileDescription
                            : Path.GetFileNameWithoutExtension(executablePath);

                        processName = Path.GetFileNameWithoutExtension(executablePath);
                    }
                    else
                    {
                        using var proc = Process.GetProcessById((int)processId);
                        processName = proc.ProcessName;
                        appName = processName;
                    }
                }
                catch
                {
                    processName = $"Process {processId}";
                    appName = processName;
                }

                if (string.IsNullOrWhiteSpace(appName))
                {
                    appName = processName;
                }

                // 10. Extract icon
                ImageSource? icon = GetOrExtractIcon(executablePath);

                var windowInfo = new WindowInfo
                {
                    Handle = hWnd,
                    Title = title,
                    ProcessName = processName,
                    ProcessId = (int)processId,
                    AppName = appName,
                    ExecutablePath = executablePath,
                    Icon = icon,
                    IsVisible = true,
                    IsMinimized = isMinimized,
                    Bounds = new MonitorBounds(rect.Left, rect.Top, rect.Width, rect.Height)
                };

                windows.Add(windowInfo);
                OrbitLogger.Info(LogCategory, $"Detected window: '{appName}' - '{title}' [PID {processId}, HWND 0x{hWnd:X}, Icon: {(icon != null ? "Yes" : "Fallback")}]");

                return true;
            }

            // Attempt enumeration on user interactive desktop, falling back to current thread desktop
            IntPtr hDesktop = NativeMethods.OpenDesktop("default", 0, false, NativeMethods.DESKTOP_READOBJECTS);
            if (hDesktop == IntPtr.Zero)
            {
                hDesktop = NativeMethods.OpenInputDesktop(0, false, NativeMethods.DESKTOP_READOBJECTS);
            }

            if (hDesktop != IntPtr.Zero)
            {
                try
                {
                    NativeMethods.EnumDesktopWindows(hDesktop, EnumProc, IntPtr.Zero);
                }
                finally
                {
                    NativeMethods.CloseDesktop(hDesktop);
                }
            }
            else
            {
                NativeMethods.EnumWindows(EnumProc, IntPtr.Zero);
            }

            return windows;
        });
    }

    public Task<WindowInfo?> GetActiveWindowAsync()
    {
        IntPtr fgWnd = NativeMethods.GetForegroundWindow();
        if (fgWnd == IntPtr.Zero) return Task.FromResult<WindowInfo?>(null);

        var titleSb = new StringBuilder(256);
        NativeMethods.GetWindowText(fgWnd, titleSb, 256);
        NativeMethods.GetWindowThreadProcessId(fgWnd, out uint pid);

        var info = new WindowInfo
        {
            Handle = fgWnd,
            Title = titleSb.ToString(),
            ProcessName = $"PID_{pid}",
            ProcessId = (int)pid,
            IsVisible = true,
            IsMinimized = NativeMethods.IsIconic(fgWnd)
        };

        return Task.FromResult<WindowInfo?>(info);
    }

    public Task<bool> ActivateWindowAsync(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return Task.FromResult(false);

        return Task.Run(() =>
        {
            try
            {
                // If minimized, restore it
                if (NativeMethods.IsIconic(hwnd))
                {
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
                }
                else
                {
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
                }

                // Attach thread input to overcome Windows SetForegroundWindow restrictions
                IntPtr foregroundWnd = NativeMethods.GetForegroundWindow();
                uint foregroundThreadId = NativeMethods.GetWindowThreadProcessId(foregroundWnd, out _);
                uint currentThreadId = NativeMethods.GetCurrentThreadId();

                bool attached = false;
                if (foregroundThreadId != currentThreadId && foregroundThreadId != 0)
                {
                    attached = NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, true);
                }

                NativeMethods.BringWindowToTop(hwnd);
                bool success = NativeMethods.SetForegroundWindow(hwnd);

                if (attached)
                {
                    NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, false);
                }

                OrbitLogger.Info(LogCategory, $"Activated window 0x{hwnd:X}. Success: {success}");
                return success;
            }
            catch (Exception ex)
            {
                OrbitLogger.Error(LogCategory, $"Error activating window 0x{hwnd:X}", ex);
                return false;
            }
        });
    }

    public Task<bool> MinimizeWindowAsync(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return Task.FromResult(false);

        return Task.Run(() =>
        {
            NativeMethods.ShowWindow(hwnd, 6 /* SW_MINIMIZE */);
            return true;
        });
    }

    public Task<bool> CloseWindowAsync(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return Task.FromResult(false);

        return Task.Run(() =>
        {
            try
            {
                // Post graceful WM_CLOSE - allows unsaved work confirmation dialogs
                bool posted = NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                OrbitLogger.Info(LogCategory, $"Sent WM_CLOSE to window 0x{hwnd:X}. Posted: {posted}");
                return posted;
            }
            catch (Exception ex)
            {
                OrbitLogger.Error(LogCategory, $"Error closing window 0x{hwnd:X}", ex);
                return false;
            }
        });
    }

    private ImageSource? GetOrExtractIcon(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return null;

        if (_iconCache.TryGetValue(executablePath, out var cached))
        {
            return cached;
        }

        try
        {
            var shinfo = new NativeMethods.SHFILEINFO();
            IntPtr result = NativeMethods.SHGetFileInfo(
                executablePath,
                0,
                ref shinfo,
                (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
                NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON);

            if (result != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
            {
                try
                {
                    var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                        shinfo.hIcon,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    // Freeze so it can be safely used across threads in WPF
                    bitmapSource.Freeze();

                    _iconCache[executablePath] = bitmapSource;
                    return bitmapSource;
                }
                finally
                {
                    NativeMethods.DestroyIcon(shinfo.hIcon);
                }
            }
        }
        catch (Exception ex)
        {
            OrbitLogger.Debug(LogCategory, $"Icon extraction failed for '{executablePath}': {ex.Message}");
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopMonitoring();
    }
}

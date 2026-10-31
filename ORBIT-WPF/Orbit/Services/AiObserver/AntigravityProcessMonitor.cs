using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Orbit.Core;
using Orbit.Models;
using Orbit.Platform.Windows;

namespace Orbit.Services.AiObserver;

public interface IAntigravityProcessMonitor : IDisposable
{
    bool IsAntigravityRunning { get; }
    bool IsAntigravityIdeRunning { get; }
    IReadOnlyList<string> IdeWorkspaceDirectories { get; }
    event Action<bool, bool>? ProcessesChanged;
    void CheckProcesses();
    bool ActivateApplication(AntigravityAppType appType);
}

public sealed class AntigravityProcessMonitor : IAntigravityProcessMonitor
{
    private const string LogCategory = "PROCESS_MONITOR";
    private readonly System.Threading.Timer _timer;
    private readonly object _lock = new();

    private bool _isAntigravityRunning;
    private bool _isAntigravityIdeRunning;
    private List<string> _ideWorkspaces = [];
    private bool _disposed;

    public bool IsAntigravityRunning
    {
        get { lock (_lock) return _isAntigravityRunning; }
        private set { lock (_lock) _isAntigravityRunning = value; }
    }

    public bool IsAntigravityIdeRunning
    {
        get { lock (_lock) return _isAntigravityIdeRunning; }
        private set { lock (_lock) _isAntigravityIdeRunning = value; }
    }

    public IReadOnlyList<string> IdeWorkspaceDirectories
    {
        get { lock (_lock) return _ideWorkspaces.ToList(); }
    }

    public event Action<bool, bool>? ProcessesChanged;

    public AntigravityProcessMonitor()
    {
        // Initial scan
        CheckProcesses();

        // Background poll every 2 seconds
        _timer = new System.Threading.Timer(_ => CheckProcesses(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    public void CheckProcesses()
    {
        if (_disposed) return;

        bool prevAg, prevIde;
        bool currAg = false, currIde = false;

        try
        {
            var allProcs = Process.GetProcesses();
            foreach (var p in allProcs)
            {
                try
                {
                    string name = p.ProcessName;
                    if (string.Equals(name, "Antigravity IDE", StringComparison.OrdinalIgnoreCase))
                    {
                        currIde = true;
                    }
                    else if (string.Equals(name, "Antigravity", StringComparison.OrdinalIgnoreCase))
                    {
                        currAg = true;
                    }
                    else if (string.Equals(name, "agy", StringComparison.OrdinalIgnoreCase))
                    {
                        currIde = true;
                    }
                    else if (string.Equals(name, "language_server", StringComparison.OrdinalIgnoreCase))
                    {
                        currAg = true;
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            OrbitLogger.Debug(LogCategory, $"Process check error: {ex.Message}");
        }

        bool changed = false;
        lock (_lock)
        {
            prevAg = _isAntigravityRunning;
            prevIde = _isAntigravityIdeRunning;
            _isAntigravityRunning = currAg;
            _isAntigravityIdeRunning = currIde;

            if (prevAg != currAg || prevIde != currIde)
            {
                changed = true;
            }
        }

        if (changed)
        {
            OrbitLogger.Info(LogCategory, $"Antigravity processes updated: Regular={currAg}, IDE={currIde}");
            ProcessesChanged?.Invoke(currAg, currIde);
        }
    }

    public bool ActivateApplication(AntigravityAppType appType)
    {
        string targetProcName = appType == AntigravityAppType.AntigravityIde ? "Antigravity IDE" : "Antigravity";
        var procs = Process.GetProcessesByName(targetProcName);
        if (procs.Length == 0)
        {
            OrbitLogger.Warn(LogCategory, $"No running processes found for {targetProcName}");
            CheckProcesses();
            return false;
        }

        IntPtr targetHwnd = IntPtr.Zero;

        // 1. Try MainWindowHandle
        foreach (var p in procs)
        {
            try
            {
                if (p.MainWindowHandle != IntPtr.Zero && NativeMethods.IsWindowVisible(p.MainWindowHandle))
                {
                    targetHwnd = p.MainWindowHandle;
                    break;
                }
            }
            catch { }
        }

        // 2. If MainWindowHandle is 0, enumerate windows belonging to these PIDs
        if (targetHwnd == IntPtr.Zero)
        {
            var pids = procs.Select(p => (uint)p.Id).ToHashSet();
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                if (NativeMethods.IsWindowVisible(hwnd))
                {
                    NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                    if (pids.Contains(pid))
                    {
                        int len = NativeMethods.GetWindowTextLength(hwnd);
                        if (len > 0)
                        {
                            targetHwnd = hwnd;
                            return false; // Stop enumeration
                        }
                    }
                }
                return true;
            }, IntPtr.Zero);
        }

        if (targetHwnd != IntPtr.Zero)
        {
            try
            {
                if (NativeMethods.IsIconic(targetHwnd))
                {
                    NativeMethods.ShowWindow(targetHwnd, NativeMethods.SW_RESTORE);
                }
                else
                {
                    NativeMethods.ShowWindow(targetHwnd, NativeMethods.SW_SHOW);
                }

                // Attach thread input to bypass Windows focus restrictions
                IntPtr foregroundWnd = NativeMethods.GetForegroundWindow();
                uint foregroundThreadId = NativeMethods.GetWindowThreadProcessId(foregroundWnd, out _);
                uint currentThreadId = NativeMethods.GetCurrentThreadId();

                bool attached = false;
                if (foregroundThreadId != currentThreadId && foregroundThreadId != 0)
                {
                    attached = NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, true);
                }

                NativeMethods.BringWindowToTop(targetHwnd);
                bool success = NativeMethods.SetForegroundWindow(targetHwnd);

                if (attached)
                {
                    NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, false);
                }

                OrbitLogger.Info(LogCategory, $"Activated window 0x{targetHwnd:X} for {targetProcName}. Success: {success}");
                return success;
            }
            catch (Exception ex)
            {
                OrbitLogger.Error(LogCategory, $"Failed to activate window for {targetProcName}", ex);
                return false;
            }
        }

        OrbitLogger.Warn(LogCategory, $"Could not find a valid visible window handle for {targetProcName}");
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Dispose();
    }
}

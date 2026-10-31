using System.Diagnostics;
using System.IO;
using System.Management;
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

/// <summary>
/// Monitors running processes for Regular Antigravity and Antigravity IDE.
/// Distinguishes actual desktop applications from background headless sidecars
/// (e.g. device-hub, compose-preview) using executable paths and command-line arguments.
/// </summary>
public sealed class AntigravityProcessMonitor : IAntigravityProcessMonitor
{
    private const string LogCategory = "PROCESS_MONITOR";

    private static readonly string ExpectedAgExePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        @"Programs\antigravity\Antigravity.exe");

    private static readonly string ExpectedIdeExePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        @"Programs\Antigravity IDE\Antigravity IDE.exe");

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

        // 1-second periodic reconciliation timer for near-instant UI response when apps start or exit
        _timer = new System.Threading.Timer(_ => CheckProcesses(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public void CheckProcesses()
    {
        if (_disposed) return;

        bool prevAg, prevIde;
        bool currAg = false, currIde = false;

        try
        {
            // 1. Detect Antigravity IDE (Antigravity IDE.exe)
            var ideProcs = Process.GetProcessesByName("Antigravity IDE");
            if (ideProcs.Length > 0)
            {
                foreach (var p in ideProcs)
                {
                    try
                    {
                        string? path = p.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(path) &&
                            (string.Equals(path, ExpectedIdeExePath, StringComparison.OrdinalIgnoreCase) ||
                             path.Contains(@"Programs\Antigravity IDE\Antigravity IDE.exe", StringComparison.OrdinalIgnoreCase)))
                        {
                            currIde = true;
                            break;
                        }
                    }
                    catch
                    {
                        // Fallback: If access denied to MainModule, the process name Antigravity IDE is specific
                        currIde = true;
                        break;
                    }
                }
            }

            // 2. Detect Regular Antigravity (Antigravity.exe)
            // CRITICAL: We must filter out background headless plugin sidecars (e.g. device-hub, compose-preview)
            // that run using Antigravity.exe with '--import=...sidecar_sdk' or 'launch.mjs'.
            var agProcs = Process.GetProcessesByName("Antigravity");
            if (agProcs.Length > 0)
            {
                var candidatePids = new List<int>();
                foreach (var p in agProcs)
                {
                    try
                    {
                        // If it has a visible MainWindowHandle, it is definitely the GUI application
                        if (p.MainWindowHandle != IntPtr.Zero && NativeMethods.IsWindowVisible(p.MainWindowHandle))
                        {
                            currAg = true;
                            break;
                        }

                        string? path = p.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(path) &&
                            (string.Equals(path, ExpectedAgExePath, StringComparison.OrdinalIgnoreCase) ||
                             path.Contains(@"Programs\antigravity\Antigravity.exe", StringComparison.OrdinalIgnoreCase)))
                        {
                            candidatePids.Add(p.Id);
                        }
                    }
                    catch
                    {
                        candidatePids.Add(p.Id);
                    }
                }

                // If no visible window was immediately detected on MainWindowHandle, check WMI CommandLines
                // to distinguish the actual Electron GUI app from headless sidecar workers.
                if (!currAg && candidatePids.Count > 0)
                {
                    try
                    {
                        using var searcher = new ManagementObjectSearcher(
                            "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='Antigravity.exe'");

                        foreach (ManagementObject obj in searcher.Get())
                        {
                            string cmd = obj["CommandLine"]?.ToString() ?? string.Empty;

                            // Headless sidecars always contain sidecar_sdk, launch.mjs, or --import=
                            bool isSidecar = cmd.Contains("sidecar_sdk", StringComparison.OrdinalIgnoreCase) ||
                                             cmd.Contains("launch.mjs", StringComparison.OrdinalIgnoreCase) ||
                                             cmd.Contains("--import=", StringComparison.OrdinalIgnoreCase);

                            if (!isSidecar && !string.IsNullOrWhiteSpace(cmd))
                            {
                                // Confirmed real Antigravity GUI application or renderer/utility process
                                currAg = true;
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        OrbitLogger.Debug(LogCategory, $"WMI process inspect error: {ex.Message}");
                    }
                }
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

        // For regular Antigravity, filter out sidecar PIDs so we don't attempt to activate headless processes
        var validPids = new HashSet<uint>();
        if (appType == AntigravityAppType.Antigravity)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='Antigravity.exe'");

                foreach (ManagementObject obj in searcher.Get())
                {
                    uint pid = Convert.ToUInt32(obj["ProcessId"]);
                    string cmd = obj["CommandLine"]?.ToString() ?? string.Empty;
                    bool isSidecar = cmd.Contains("sidecar_sdk", StringComparison.OrdinalIgnoreCase) ||
                                     cmd.Contains("launch.mjs", StringComparison.OrdinalIgnoreCase) ||
                                     cmd.Contains("--import=", StringComparison.OrdinalIgnoreCase);

                    if (!isSidecar)
                    {
                        validPids.Add(pid);
                    }
                }
            }
            catch { }
        }
        else
        {
            validPids = procs.Select(p => (uint)p.Id).ToHashSet();
        }

        if (validPids.Count == 0)
        {
            CheckProcesses();
            return false;
        }

        IntPtr targetHwnd = IntPtr.Zero;

        // 1. Try MainWindowHandle of valid processes
        foreach (var p in procs)
        {
            try
            {
                if (validPids.Contains((uint)p.Id) && p.MainWindowHandle != IntPtr.Zero && NativeMethods.IsWindowVisible(p.MainWindowHandle))
                {
                    targetHwnd = p.MainWindowHandle;
                    break;
                }
            }
            catch { }
        }

        // 2. If MainWindowHandle is 0, enumerate visible windows belonging to valid PIDs
        if (targetHwnd == IntPtr.Zero)
        {
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                if (NativeMethods.IsWindowVisible(hwnd))
                {
                    NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                    if (validPids.Contains(pid))
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

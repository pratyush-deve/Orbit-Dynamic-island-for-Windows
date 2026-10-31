using System.Diagnostics;
using System.IO;
using System.Management;
using Orbit.Core;
using Orbit.Models;

namespace Orbit.Services.AiObserver;

public interface IAntigravityProcessMonitor : IDisposable
{
    bool IsAntigravityRunning { get; }
    bool IsAntigravityIdeRunning { get; }
    IReadOnlyList<string> IdeWorkspaceDirectories { get; }
    event Action<bool, bool>? ProcessesChanged;
    void CheckProcesses();
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

        // Background poll every 2.5 seconds as a lightweight watchdog
        _timer = new System.Threading.Timer(_ => CheckProcesses(), null, TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(2.5));
    }

    public void CheckProcesses()
    {
        if (_disposed) return;

        bool prevAg, prevIde;
        bool currAg = false, currIde = false;
        var workspaces = new List<string>();

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
                        // agy daemon is the engine for Antigravity IDE
                        currIde = true;
                    }
                    else if (string.Equals(name, "language_server", StringComparison.OrdinalIgnoreCase))
                    {
                        // language_server inside Antigravity resources
                        currAg = true;
                    }
                }
                catch { }
            }

            // Extract active IDE workspaces from agy daemon command line arguments (e.g. --add-dir=...)
            if (currIde)
            {
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT ProcessId, Name, CommandLine FROM Win32_Process WHERE Name='agy.exe' OR Name='Antigravity IDE.exe'");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string cmd = obj["CommandLine"]?.ToString() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(cmd))
                        {
                            // Look for --add-dir=<path>
                            int idx = 0;
                            while ((idx = cmd.IndexOf("--add-dir=", idx, StringComparison.OrdinalIgnoreCase)) != -1)
                            {
                                int start = idx + 10;
                                string path;
                                if (start < cmd.Length && cmd[start] == '"')
                                {
                                    int end = cmd.IndexOf('"', start + 1);
                                    path = end != -1 ? cmd.Substring(start + 1, end - start - 1) : cmd.Substring(start + 1);
                                    idx = end != -1 ? end + 1 : cmd.Length;
                                }
                                else
                                {
                                    int end = cmd.IndexOf(' ', start);
                                    path = end != -1 ? cmd.Substring(start, end - start) : cmd.Substring(start);
                                    idx = end != -1 ? end + 1 : cmd.Length;
                                }

                                if (!string.IsNullOrWhiteSpace(path) && !workspaces.Contains(path, StringComparer.OrdinalIgnoreCase))
                                {
                                    workspaces.Add(path.Trim());
                                }
                            }
                        }
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
            _ideWorkspaces = workspaces;

            if (prevAg != currAg || prevIde != currIde)
            {
                changed = true;
            }
        }

        if (changed)
        {
            OrbitLogger.Info(LogCategory, $"Antigravity processes updated: Regular={currAg}, IDE={currIde}, IDE Workspaces={workspaces.Count}");
            ProcessesChanged?.Invoke(currAg, currIde);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Dispose();
    }
}

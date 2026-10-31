using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Orbit.Core;
using Orbit.Models;
using Orbit.Platform.Windows;

namespace Orbit.Services.DevWorkspace;

public sealed class DevWorkspaceService : IDevWorkspaceService
{
    private const string LogCategory = "DEV_WORKSPACE";
    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private readonly IAppDiscoveryService _discoveryService;
    private readonly string _configFilePath;
    private readonly object _lock = new();

    private List<DevApp> _allApps = [];
    private bool _initialized;
    private bool _isInitializing;
    private bool _disposed;

    public event Action? WorkspaceChanged;

    public bool IsInitializing
    {
        get
        {
            lock (_lock) return _isInitializing;
        }
        private set
        {
            lock (_lock) _isInitializing = value;
        }
    }

    public IReadOnlyList<DevApp> SelectedApps
    {
        get
        {
            lock (_lock)
            {
                return _allApps.Where(a => a.IsSelected).ToList();
            }
        }
    }

    public IReadOnlyList<DevApp> AllDiscoveredApps
    {
        get
        {
            lock (_lock)
            {
                return _allApps.ToList();
            }
        }
    }

    public DevWorkspaceService(IAppDiscoveryService discoveryService)
    {
        _discoveryService = discoveryService;

        string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ORBIT");
        Directory.CreateDirectory(appData);
        _configFilePath = Path.Combine(appData, "dev_workspace.json");
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        IsInitializing = true;
        try
        {
            OrbitLogger.Info(LogCategory, "Initializing DEV Workspace Service...");

            // 1. Discover applications on device
            var discovered = await _discoveryService.DiscoverDeveloperAppsAsync();

            // 2. Load persisted selections
            var savedIds = LoadSavedSelectionIds();

            lock (_lock)
            {
                _allApps = discovered;

                if (savedIds != null && savedIds.Count > 0)
                {
                    var idSet = new HashSet<string>(savedIds, StringComparer.OrdinalIgnoreCase);
                    // Seamless migration: if legacy config selected the old merged antigravity-ide, include antigravity
                    if (idSet.Contains("antigravity-ide") && !idSet.Contains("antigravity"))
                    {
                        idSet.Add("antigravity");
                    }

                    // Restore saved selections
                    foreach (var app in _allApps)
                    {
                        app.IsSelected = idSet.Contains(app.Id);
                    }
                }
                else
                {
                    // Initial defaults: Android Studio, Antigravity IDE, Antigravity, Google Chrome
                    var defaults = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "android-studio",
                        "antigravity-ide",
                        "antigravity",
                        "google-chrome"
                    };

                    foreach (var app in _allApps)
                    {
                        app.IsSelected = defaults.Contains(app.Id);
                    }

                    SaveConfigurationInternal();
                }

                _initialized = true;
            }

            OrbitLogger.Info(LogCategory, $"DEV Workspace initialized. Total discovered: {_allApps.Count}, Selected: {SelectedApps.Count}");
            NotifyWorkspaceChanged();
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, "Failed to initialize DEV Workspace Service.", ex);
        }
        finally
        {
            IsInitializing = false;
        }
    }

    public async Task RefreshDiscoveryAsync()
    {
        try
        {
            OrbitLogger.Info(LogCategory, "Refreshing developer application discovery...");
            var freshDiscovered = await _discoveryService.DiscoverDeveloperAppsAsync();

            lock (_lock)
            {
                var currentlySelected = new HashSet<string>(_allApps.Where(a => a.IsSelected).Select(a => a.Id), StringComparer.OrdinalIgnoreCase);

                foreach (var app in freshDiscovered)
                {
                    if (currentlySelected.Contains(app.Id))
                    {
                        app.IsSelected = true;
                    }
                }

                _allApps = freshDiscovered;
                SaveConfigurationInternal();
            }

            NotifyWorkspaceChanged();
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, "Failed to refresh application discovery.", ex);
        }
    }

    public void ToggleAppSelection(string appId, bool isSelected)
    {
        lock (_lock)
        {
            var app = _allApps.FirstOrDefault(a => string.Equals(a.Id, appId, StringComparison.OrdinalIgnoreCase));
            if (app != null)
            {
                app.IsSelected = isSelected;
                SaveConfigurationInternal();
                OrbitLogger.Info(LogCategory, $"App '{app.Name}' selection set to {isSelected}.");
            }
        }

        NotifyWorkspaceChanged();
    }

    public async Task<LaunchWorkspaceResult> LaunchWorkspaceAsync()
    {
        List<DevApp> targets;
        lock (_lock)
        {
            targets = _allApps.Where(a => a.IsSelected).ToList();
        }

        int launchedCount = 0;
        int activatedCount = 0;
        var failed = new List<string>();

        OrbitLogger.Info(LogCategory, $"Activating DEV Workspace with {targets.Count} application(s)...");

        await Task.Run(() =>
        {
            foreach (var app in targets)
            {
                try
                {
                    bool handledExisting = false;

                    // 1. Check if the process is already running (excluding Chrome which opens a new tab via argument)
                    if (!string.IsNullOrWhiteSpace(app.ProcessName) && app.Id != "google-chrome")
                    {
                        var runningProcs = Process.GetProcessesByName(app.ProcessName);
                        if (runningProcs.Length > 0)
                        {
                            var procWithWindow = runningProcs.FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
                            if (procWithWindow != null)
                            {
                                IntPtr hWnd = procWithWindow.MainWindowHandle;
                                if (NativeMethods.IsIconic(hWnd))
                                {
                                    ShowWindow(hWnd, SwRestore);
                                }
                                NativeMethods.SetForegroundWindow(hWnd);
                                OrbitLogger.Info(LogCategory, $"App '{app.Name}' is already running (PID {procWithWindow.Id}). Brought to foreground.");
                                activatedCount++;
                                handledExisting = true;
                            }
                        }
                    }

                    if (handledExisting) continue;

                    // 2. Launch application
                    if (string.IsNullOrWhiteSpace(app.ExecutablePath))
                    {
                        failed.Add(app.Name);
                        OrbitLogger.Warn(LogCategory, $"Cannot launch '{app.Name}': missing executable path.");
                        continue;
                    }

                    var psi = new ProcessStartInfo
                    {
                        FileName = app.ExecutablePath,
                        Arguments = app.Arguments ?? string.Empty,
                        UseShellExecute = true
                    };

                    if (!string.IsNullOrWhiteSpace(app.WorkingDirectory) && Directory.Exists(app.WorkingDirectory))
                    {
                        psi.WorkingDirectory = app.WorkingDirectory;
                    }

                    var startedProc = Process.Start(psi);
                    launchedCount++;
                    OrbitLogger.Info(LogCategory, $"Launched '{app.Name}' (PID {startedProc?.Id.ToString() ?? "unknown"}). Args: '{app.Arguments}'");
                }
                catch (Exception ex)
                {
                    failed.Add(app.Name);
                    OrbitLogger.Error(LogCategory, $"Failed to launch '{app.Name}' ({app.ExecutablePath}).", ex);
                }
            }
        });

        return new LaunchWorkspaceResult(targets.Count, launchedCount, activatedCount, failed);
    }

    private List<string>? LoadSavedSelectionIds()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                string json = File.ReadAllText(_configFilePath);
                var config = JsonSerializer.Deserialize<WorkspaceConfigFile>(json);
                return config?.SelectedAppIds;
            }
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, $"Failed to load DEV workspace config from '{_configFilePath}'.", ex);
        }

        return null;
    }

    private void SaveConfigurationInternal()
    {
        try
        {
            var config = new WorkspaceConfigFile
            {
                SelectedAppIds = _allApps.Where(a => a.IsSelected).Select(a => a.Id).ToList(),
                LastSavedUtc = DateTime.UtcNow
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(config, options);
            File.WriteAllText(_configFilePath, json);
            OrbitLogger.Debug(LogCategory, "DEV workspace configuration successfully saved.");
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, "Failed to save DEV workspace configuration.", ex);
        }
    }

    private void NotifyWorkspaceChanged()
    {
        try
        {
            WorkspaceChanged?.Invoke();
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, "Error notifying WorkspaceChanged.", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }

    private sealed class WorkspaceConfigFile
    {
        public List<string> SelectedAppIds { get; set; } = [];
        public DateTime LastSavedUtc { get; set; }
    }
}


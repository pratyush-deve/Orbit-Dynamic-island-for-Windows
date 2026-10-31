using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Orbit.Core;
using Orbit.Integrations.Desktop;
using Orbit.Models;
using Orbit.Services.DevWorkspace;

namespace Orbit.Services.Hubs;

/// <summary>
/// Base service for Orbit application hubs (Social Hub, Work Hub).
/// Manages application discovery, user visibility preferences persistence, and window launching/activation.
/// </summary>
public abstract class HubService : IHubService
{
    private readonly string _logCategory;
    private readonly IAppDiscoveryService _discoveryService;
    private readonly IWindowManager _windowManager;
    private readonly string _configFilePath;
    private readonly object _lock = new();

    private List<OrbitApp> _allApps = [];
    private bool _initialized;
    private bool _isInitializing;
    private bool _disposed;

    public event Action? HubChanged;

    public AppCategory Category { get; }

    public bool IsInitializing
    {
        get
        {
            lock (_lock) return _isInitializing;
        }
        protected set
        {
            lock (_lock) _isInitializing = value;
        }
    }

    public IReadOnlyList<OrbitApp> SelectedApps
    {
        get
        {
            lock (_lock)
            {
                return _allApps.Where(a => a.IsSelected && a.IsInstalled).ToList();
            }
        }
    }

    public IReadOnlyList<OrbitApp> AllDiscoveredApps
    {
        get
        {
            lock (_lock)
            {
                return _allApps.ToList();
            }
        }
    }

    protected HubService(
        IAppDiscoveryService discoveryService,
        IWindowManager windowManager,
        AppCategory category,
        string configFileName)
    {
        Category = category;
        _logCategory = $"{category.ToString().ToUpperInvariant()}_HUB";
        _discoveryService = discoveryService;
        _windowManager = windowManager;

        string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ORBIT");
        Directory.CreateDirectory(appData);
        _configFilePath = Path.Combine(appData, configFileName);
    }

    public virtual async Task InitializeAsync()
    {
        if (_initialized) return;

        IsInitializing = true;
        try
        {
            OrbitLogger.Info(_logCategory, $"Initializing {Category} Hub Service...");

            var discovered = await _discoveryService.DiscoverAppsByCategoryAsync(Category);
            var savedIds = LoadSavedSelectionIds();

            lock (_lock)
            {
                _allApps = discovered;

                if (savedIds != null && savedIds.Count > 0)
                {
                    var idSet = new HashSet<string>(savedIds, StringComparer.OrdinalIgnoreCase);
                    foreach (var app in _allApps)
                    {
                        app.IsSelected = idSet.Contains(app.Id);
                    }
                }
                else
                {
                    // By default, select all discovered applications
                    foreach (var app in _allApps)
                    {
                        app.IsSelected = true;
                    }
                    SaveConfigurationInternal();
                }

                _initialized = true;
            }

            OrbitLogger.Info(_logCategory, $"{Category} Hub initialized. Discovered: {_allApps.Count}, Selected: {SelectedApps.Count}");
            NotifyHubChanged();
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(_logCategory, $"Failed to initialize {Category} Hub Service.", ex);
        }
        finally
        {
            IsInitializing = false;
        }
    }

    public virtual async Task RefreshDiscoveryAsync()
    {
        try
        {
            OrbitLogger.Info(_logCategory, $"Refreshing {Category} application discovery...");
            var freshDiscovered = await _discoveryService.DiscoverAppsByCategoryAsync(Category);

            lock (_lock)
            {
                var currentlySelected = new HashSet<string>(_allApps.Where(a => a.IsSelected).Select(a => a.Id), StringComparer.OrdinalIgnoreCase);

                foreach (var app in freshDiscovered)
                {
                    if (currentlySelected.Contains(app.Id))
                    {
                        app.IsSelected = true;
                    }
                    else if (_allApps.Count == 0)
                    {
                        // New installation initial selection
                        app.IsSelected = true;
                    }
                }

                _allApps = freshDiscovered;
                SaveConfigurationInternal();
            }

            NotifyHubChanged();
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(_logCategory, $"Failed to refresh {Category} application discovery.", ex);
        }
    }

    public void ToggleAppSelection(string appId, bool isSelected)
    {
        lock (_lock)
        {
            var app = _allApps.FirstOrDefault(a => a.Id.Equals(appId, StringComparison.OrdinalIgnoreCase));
            if (app != null)
            {
                app.IsSelected = isSelected;
                SaveConfigurationInternal();
                OrbitLogger.Info(_logCategory, $"Toggled app '{app.Name}' selection -> {isSelected}");
            }
        }

        NotifyHubChanged();
    }

    public virtual async Task<bool> LaunchAppAsync(OrbitApp app)
    {
        if (app == null) return false;

        try
        {
            OrbitLogger.Info(_logCategory, $"Launching/Activating application: '{app.Name}' (ID: {app.Id})");

            // 1. Check if application is already running and bring existing window to foreground
            var openWindows = await _windowManager.GetOpenWindowsAsync();

            WindowInfo? matchingWindow = null;

            if (app.Id == "google-meet")
            {
                // For Google Meet, look for open Chrome/Edge window with "Meet" in title
                matchingWindow = openWindows.FirstOrDefault(w =>
                    w.Title.Contains("Meet", StringComparison.OrdinalIgnoreCase) &&
                    (w.ProcessName.Equals("chrome", StringComparison.OrdinalIgnoreCase) ||
                     w.ProcessName.Equals("msedge", StringComparison.OrdinalIgnoreCase)));
            }
            else
            {
                matchingWindow = openWindows.FirstOrDefault(w =>
                    (!string.IsNullOrWhiteSpace(app.ProcessName) && w.ProcessName.Equals(app.ProcessName, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(app.Name) && w.Title.Contains(app.Name, StringComparison.OrdinalIgnoreCase)));
            }

            if (matchingWindow != null && matchingWindow.Handle != IntPtr.Zero)
            {
                OrbitLogger.Info(_logCategory, $"Found running window for '{app.Name}' (HWND: 0x{matchingWindow.Handle:X}). Activating...");
                bool activated = await _windowManager.ActivateWindowAsync(matchingWindow.Handle);
                if (activated) return true;
            }

            // 2. Launch target executable or shell folder target
            if (app.ExecutablePath.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
            {
                var psi = new ProcessStartInfo("explorer.exe", app.ExecutablePath)
                {
                    UseShellExecute = true
                };
                Process.Start(psi);
                return true;
            }

            if (File.Exists(app.ExecutablePath))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = app.ExecutablePath,
                    Arguments = app.Arguments ?? string.Empty,
                    WorkingDirectory = !string.IsNullOrWhiteSpace(app.WorkingDirectory) && Directory.Exists(app.WorkingDirectory)
                        ? app.WorkingDirectory
                        : Path.GetDirectoryName(app.ExecutablePath) ?? string.Empty,
                    UseShellExecute = true
                };
                Process.Start(psi);
                return true;
            }

            // 3. Fallback to default shell execution (e.g. system PATH command)
            var fallbackPsi = new ProcessStartInfo
            {
                FileName = app.ExecutablePath,
                Arguments = app.Arguments ?? string.Empty,
                UseShellExecute = true
            };
            Process.Start(fallbackPsi);
            return true;
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(_logCategory, $"Failed to launch '{app.Name}': {ex.Message}", ex);
            return false;
        }
    }

    protected void NotifyHubChanged()
    {
        HubChanged?.Invoke();
    }

    private List<string>? LoadSavedSelectionIds()
    {
        try
        {
            if (!File.Exists(_configFilePath)) return null;

            string json = File.ReadAllText(_configFilePath);
            var model = JsonSerializer.Deserialize<HubConfigModel>(json);
            return model?.SelectedAppIds;
        }
        catch (Exception ex)
        {
            OrbitLogger.Warn(_logCategory, $"Failed to read configuration from '{_configFilePath}': {ex.Message}");
            return null;
        }
    }

    private void SaveConfigurationInternal()
    {
        try
        {
            var model = new HubConfigModel
            {
                SelectedAppIds = _allApps.Where(a => a.IsSelected).Select(a => a.Id).ToList()
            };

            string json = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configFilePath, json);
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(_logCategory, $"Failed to save configuration to '{_configFilePath}': {ex.Message}");
        }
    }

    public virtual void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }

    private sealed class HubConfigModel
    {
        public List<string> SelectedAppIds { get; set; } = [];
    }
}


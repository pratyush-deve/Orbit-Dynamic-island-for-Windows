using System.IO;
using System.Text.Json;
using Orbit.Core;
using Orbit.Platform.Windows;

namespace Orbit.Services;

public interface ISettingsService
{
    OrbitSettings CurrentSettings { get; }
    void LoadSettings();
    void SaveSettings();
    void UpdateEyeGlow(bool enabled);
    (double X, double Y) GetTopCenterPosition(double windowWidth);
}

/// <summary>
/// Service managing local persistence of OrbitSettings in %LOCALAPPDATA%\ORBIT\settings.json.
/// Window is always deterministically positioned at top-centre of primary monitor.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private const string LogCategory = "SETTINGS";
    private readonly IMonitorService _monitorService;
    private readonly string _settingsFilePath;
    private readonly object _fileLock = new();

    public OrbitSettings CurrentSettings { get; private set; } = new();

    public SettingsService(IMonitorService monitorService)
    {
        _monitorService = monitorService;

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(localAppData, "ORBIT");
        Directory.CreateDirectory(dir);
        _settingsFilePath = Path.Combine(dir, "settings.json");

        LoadSettings();
    }

    public void LoadSettings()
    {
        lock (_fileLock)
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    var loaded = JsonSerializer.Deserialize<OrbitSettings>(json);
                    if (loaded != null)
                    {
                        CurrentSettings = loaded;
                        OrbitLogger.Info(LogCategory, $"Loaded settings from {_settingsFilePath}. EyeGlow: {CurrentSettings.EyeGlowEnabled}");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                OrbitLogger.Error(LogCategory, "Failed to load settings file, using defaults.", ex);
            }

            CurrentSettings = new OrbitSettings();
        }
    }

    public void SaveSettings()
    {
        lock (_fileLock)
        {
            try
            {
                CurrentSettings.LastSavedUtc = DateTime.UtcNow;
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(CurrentSettings, options);
                File.WriteAllText(_settingsFilePath, json);
                OrbitLogger.Debug(LogCategory, "Settings successfully saved.");
            }
            catch (Exception ex)
            {
                OrbitLogger.Error(LogCategory, "Failed to save settings to disk.", ex);
            }
        }
    }

    public void UpdateEyeGlow(bool enabled)
    {
        CurrentSettings.EyeGlowEnabled = enabled;
        SaveSettings();
        OrbitLogger.Info(LogCategory, $"Eye glow preference updated: {enabled}");
    }

    public (double X, double Y) GetTopCenterPosition(double windowWidth)
    {
        var primaryMonitor = _monitorService.GetPrimaryMonitor();
        var (x, y) = _monitorService.GetTopCenterPosition(primaryMonitor, windowWidth, 14.0);
        OrbitLogger.Info(LogCategory, $"Computed top-center on {primaryMonitor.DeviceName}: ({x:0.#}, {y:0.#})");
        return (x, y);
    }
}

using System.Text.Json.Serialization;

namespace Orbit.Core;

/// <summary>
/// User and application preferences persisted locally in %LOCALAPPDATA%\ORBIT\settings.json.
/// </summary>
public sealed class OrbitSettings
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "2.0.0";

    [JsonPropertyName("windowPositionX")]
    public double? WindowPositionX { get; set; }

    [JsonPropertyName("windowPositionY")]
    public double? WindowPositionY { get; set; }

    [JsonPropertyName("hasCustomPosition")]
    public bool HasCustomPosition { get; set; } = false;

    [JsonPropertyName("eyeGlowEnabled")]
    public bool EyeGlowEnabled { get; set; } = true;

    [JsonPropertyName("stayOnTop")]
    public bool StayOnTop { get; set; } = true;

    [JsonPropertyName("blinkIntervalSeconds")]
    public double BlinkIntervalSeconds { get; set; } = 4.0;

    [JsonPropertyName("lastMonitorDeviceName")]
    public string? LastMonitorDeviceName { get; set; }

    [JsonPropertyName("lastSavedUtc")]
    public DateTime? LastSavedUtc { get; set; }

    /// <summary>
    /// Clones the settings object.
    /// </summary>
    public OrbitSettings Clone()
    {
        return new OrbitSettings
        {
            Version = Version,
            WindowPositionX = WindowPositionX,
            WindowPositionY = WindowPositionY,
            HasCustomPosition = HasCustomPosition,
            EyeGlowEnabled = EyeGlowEnabled,
            StayOnTop = StayOnTop,
            BlinkIntervalSeconds = BlinkIntervalSeconds,
            LastMonitorDeviceName = LastMonitorDeviceName,
            LastSavedUtc = LastSavedUtc
        };
    }
}

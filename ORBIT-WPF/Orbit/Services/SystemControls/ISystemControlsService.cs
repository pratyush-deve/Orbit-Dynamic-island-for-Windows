namespace Orbit.Services.SystemControls;

public enum SystemControlType
{
    Microphone,
    WiFi,
    Bluetooth,
    SystemVolume,
    Settings
}

public record SystemControlStatus(
    SystemControlType ControlType,
    bool IsAvailable,
    bool IsEnabled,
    string StatusLabel,
    string? Detail = null);

public interface ISystemControlsService : IDisposable
{
    event Action<SystemControlStatus>? StatusChanged;

    Task InitializeAsync();
    SystemControlStatus GetStatus(SystemControlType type);
    Task<bool> ToggleControlAsync(SystemControlType type);
    Task RefreshAllStatesAsync();
}


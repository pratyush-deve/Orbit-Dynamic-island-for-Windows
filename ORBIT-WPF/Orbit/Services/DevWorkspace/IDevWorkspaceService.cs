using Orbit.Models;

namespace Orbit.Services.DevWorkspace;

public interface IDevWorkspaceService : IDisposable
{
    event Action? WorkspaceChanged;

    IReadOnlyList<DevApp> SelectedApps { get; }
    IReadOnlyList<DevApp> AllDiscoveredApps { get; }
    bool IsInitializing { get; }

    Task InitializeAsync();
    Task RefreshDiscoveryAsync();
    void ToggleAppSelection(string appId, bool isSelected);
    Task<LaunchWorkspaceResult> LaunchWorkspaceAsync();
}

public record LaunchWorkspaceResult(
    int TotalCount,
    int LaunchedCount,
    int ActivatedCount,
    List<string> FailedAppNames);


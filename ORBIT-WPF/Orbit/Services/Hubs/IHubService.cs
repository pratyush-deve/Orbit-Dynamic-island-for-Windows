using Orbit.Models;

namespace Orbit.Services.Hubs;

public interface IHubService : IDisposable
{
    event Action? HubChanged;

    AppCategory Category { get; }
    IReadOnlyList<OrbitApp> SelectedApps { get; }
    IReadOnlyList<OrbitApp> AllDiscoveredApps { get; }
    bool IsInitializing { get; }

    Task InitializeAsync();
    Task RefreshDiscoveryAsync();
    void ToggleAppSelection(string appId, bool isSelected);
    Task<bool> LaunchAppAsync(OrbitApp app);
}

public interface ISocialHubService : IHubService
{
    Task RefreshNotificationsAsync();
}

public interface IWorkHubService : IHubService
{
}


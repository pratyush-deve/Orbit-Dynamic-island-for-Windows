using Orbit.Models;

namespace Orbit.Services.DevWorkspace;

/// <summary>
/// Unified application discovery service across all Orbit application categories.
/// Discovers Developer, Social, and Work applications from Windows shortcuts, registry, and App Paths.
/// </summary>
public interface IAppDiscoveryService
{
    Task<List<DevApp>> DiscoverDeveloperAppsAsync();
    List<DevApp> GetDefaultApplications();

    Task<List<OrbitApp>> DiscoverSocialAppsAsync();
    Task<List<OrbitApp>> DiscoverWorkAppsAsync();
    Task<List<OrbitApp>> DiscoverAppsByCategoryAsync(AppCategory category);
}

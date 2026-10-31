using Orbit.Models;

namespace Orbit.Services.DevWorkspace;

public interface IAppDiscoveryService
{
    Task<List<DevApp>> DiscoverDeveloperAppsAsync();
    List<DevApp> GetDefaultApplications();
}


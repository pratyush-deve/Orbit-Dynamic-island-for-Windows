using Orbit.Integrations.Desktop;
using Orbit.Models;
using Orbit.Services.DevWorkspace;

namespace Orbit.Services.Hubs;

public sealed class WorkHubService : HubService, IWorkHubService
{
    public WorkHubService(
        IAppDiscoveryService discoveryService,
        IWindowManager windowManager)
        : base(discoveryService, windowManager, AppCategory.Work, "work_hub.json")
    {
    }
}


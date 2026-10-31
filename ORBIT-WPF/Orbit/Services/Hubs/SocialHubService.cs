using Orbit.Core;
using Orbit.Integrations.Desktop;
using Orbit.Models;
using Orbit.Services.DevWorkspace;

namespace Orbit.Services.Hubs;

public sealed class SocialHubService : HubService, ISocialHubService
{
    private const string LogCategory = "SOCIAL_HUB";
    private readonly INotificationService _notificationService;
    private readonly IWindowManager _windowManager;

    public SocialHubService(
        IAppDiscoveryService discoveryService,
        IWindowManager windowManager,
        INotificationService notificationService)
        : base(discoveryService, windowManager, AppCategory.Social, "social_hub.json")
    {
        _notificationService = notificationService;
        _windowManager = windowManager;

        _windowManager.WindowsChanged += OnWindowsChanged;
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await RefreshNotificationsAsync();
    }

    public override async Task RefreshDiscoveryAsync()
    {
        await base.RefreshDiscoveryAsync();
        await RefreshNotificationsAsync();
    }

    public async Task RefreshNotificationsAsync()
    {
        try
        {
            var apps = AllDiscoveredApps;
            bool anyChanged = false;

            foreach (var app in apps)
            {
                int? count = await _notificationService.GetNotificationCountAsync(app);
                if (app.NotificationCount != count)
                {
                    app.NotificationCount = count;
                    anyChanged = true;
                }
            }

            if (anyChanged)
            {
                NotifyHubChanged();
            }
        }
        catch (Exception ex)
        {
            OrbitLogger.Debug(LogCategory, $"Failed to refresh notification counts: {ex.Message}");
        }
    }

    private void OnWindowsChanged()
    {
        _ = RefreshNotificationsAsync();
    }

    public override void Dispose()
    {
        _windowManager.WindowsChanged -= OnWindowsChanged;
        base.Dispose();
    }
}


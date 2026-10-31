using System.Text.RegularExpressions;
using Orbit.Core;
using Orbit.Integrations.Desktop;
using Orbit.Models;

namespace Orbit.Services.Hubs;

/// <summary>
/// Retrieves actual notification count information using reliable Windows sources:
/// 1. Window title notification badges (e.g., "(3) WhatsApp", "(5) Discord") from open desktop windows.
/// 2. Windows 10/11 UserNotificationListener WinRT API where permitted by the user/system.
/// Never invents counts and never silently bypasses Windows permissions.
/// </summary>
public sealed class NotificationService : INotificationService
{
    private const string LogCategory = "NOTIFICATION_SERVICE";
    private readonly IWindowManager _windowManager;
    private bool _checkedWinRtAccess;
    private bool _winRtAccessAllowed;

    public NotificationService(IWindowManager windowManager)
    {
        _windowManager = windowManager;
    }

    public async Task<int?> GetNotificationCountAsync(OrbitApp app)
    {
        if (app == null) return null;

        // 1. Check open desktop window titles for unread indicators
        try
        {
            var openWindows = await _windowManager.GetOpenWindowsAsync();
            var matchingWindows = openWindows.Where(w =>
                (!string.IsNullOrWhiteSpace(app.ProcessName) && w.ProcessName.Equals(app.ProcessName, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(app.Name) && w.Title.Contains(app.Name, StringComparison.OrdinalIgnoreCase))
            ).ToList();

            foreach (var win in matchingWindows)
            {
                // Match common notification patterns in window titles:
                // e.g. "(3) WhatsApp", "WhatsApp (3)", "(5) Discord", "Slack (1)"
                var match = Regex.Match(win.Title, @"\(([0-9]+)\)|\[([0-9]+)\]");
                if (match.Success)
                {
                    string countStr = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                    if (int.TryParse(countStr, out int count) && count > 0)
                    {
                        return count;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            OrbitLogger.Debug(LogCategory, $"Window title badge check error for {app.Name}: {ex.Message}");
        }

        // 2. Query Windows 10/11 UserNotificationListener if supported and permitted
        try
        {
            if (Windows.Foundation.Metadata.ApiInformation.IsTypePresent("Windows.UI.Notifications.Management.UserNotificationListener"))
            {
                if (!_checkedWinRtAccess)
                {
                    _checkedWinRtAccess = true;
                    var listener = Windows.UI.Notifications.Management.UserNotificationListener.Current;
                    var status = await listener.RequestAccessAsync();
                    _winRtAccessAllowed = (status == Windows.UI.Notifications.Management.UserNotificationListenerAccessStatus.Allowed);
                    OrbitLogger.Info(LogCategory, $"Windows UserNotificationListener access status: {status}");
                }

                if (_winRtAccessAllowed)
                {
                    var listener = Windows.UI.Notifications.Management.UserNotificationListener.Current;
                    var notifications = await listener.GetNotificationsAsync(Windows.UI.Notifications.NotificationKinds.Toast);

                    int matchCount = 0;
                    foreach (var n in notifications)
                    {
                        string aumid = n.AppInfo?.AppUserModelId ?? string.Empty;
                        string display = n.AppInfo?.DisplayInfo?.DisplayName ?? string.Empty;

                        bool match = false;
                        if (!string.IsNullOrWhiteSpace(app.PackageId) && aumid.Contains(app.PackageId, StringComparison.OrdinalIgnoreCase))
                        {
                            match = true;
                        }
                        else if (!string.IsNullOrWhiteSpace(app.Name) &&
                                 (display.Contains(app.Name, StringComparison.OrdinalIgnoreCase) || aumid.Contains(app.Name, StringComparison.OrdinalIgnoreCase)))
                        {
                            match = true;
                        }

                        if (match) matchCount++;
                    }

                    if (matchCount > 0)
                    {
                        return matchCount;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            OrbitLogger.Debug(LogCategory, $"WinRT notification query exception: {ex.Message}");
        }

        // If count cannot be retrieved reliably, return null (no badge shown)
        return null;
    }
}


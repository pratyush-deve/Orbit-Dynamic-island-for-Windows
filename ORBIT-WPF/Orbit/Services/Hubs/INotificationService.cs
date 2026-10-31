using Orbit.Models;

namespace Orbit.Services.Hubs;

/// <summary>
/// Service for retrieving unread notification counts for installed applications.
/// Adheres strictly to Windows privacy permissions and official OS sources.
/// </summary>
public interface INotificationService
{
    Task<int?> GetNotificationCountAsync(OrbitApp app);
}


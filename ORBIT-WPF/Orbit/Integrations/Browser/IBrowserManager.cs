namespace Orbit.Integrations.Browser;

public sealed record BrowserTabInfo(string TabId, string Title, string Url, bool IsActive, string BrowserName);

/// <summary>
/// Foundation interface for browser extension and tab automation (Chrome, Edge).
/// </summary>
public interface IBrowserManager
{
    Task<IReadOnlyList<BrowserTabInfo>> GetOpenTabsAsync();
    Task<bool> ActivateTabAsync(string tabId);
    Task<bool> CloseTabAsync(string tabId);
}

using Orbit.Models;

namespace Orbit.Integrations.Desktop;

/// <summary>
/// Foundation interface for managing OS windows, window focus, and desktop state.
/// </summary>
public interface IWindowManager : IDisposable
{
    event Action? WindowsChanged;

    void StartMonitoring();
    void StopMonitoring();

    Task<IReadOnlyList<WindowInfo>> GetOpenWindowsAsync();
    Task<WindowInfo?> GetActiveWindowAsync();
    Task<bool> ActivateWindowAsync(IntPtr hwnd);
    Task<bool> MinimizeWindowAsync(IntPtr hwnd);
    Task<bool> CloseWindowAsync(IntPtr hwnd);
}

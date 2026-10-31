using Orbit.Core;

namespace Orbit.Platform.Windows;

public interface IGlobalHotkeyService : IDisposable
{
    bool Register(IntPtr hwnd, uint modifiers, uint vkKey, Action onHotkeyPressed);
    void Unregister();
}

/// <summary>
/// Global hotkey provider disabled per user requirement.
/// Operates strictly through mouse-based interactions; does not register Win32 hooks or hotkeys.
/// </summary>
public sealed class GlobalHotkeyService : IGlobalHotkeyService
{
    public bool Register(IntPtr hwnd, uint modifiers, uint vkKey, Action onHotkeyPressed)
    {
        // Global keyboard hook registration intentionally disabled.
        return false;
    }

    public void Unregister()
    {
    }

    public void Dispose()
    {
    }
}

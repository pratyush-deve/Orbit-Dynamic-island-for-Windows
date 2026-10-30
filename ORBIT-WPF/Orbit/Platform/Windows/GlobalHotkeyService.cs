using System.Runtime.InteropServices;
using System.Windows.Interop;
using Orbit.Core;

namespace Orbit.Platform.Windows;

public interface IGlobalHotkeyService : IDisposable
{
    bool Register(IntPtr hwnd, uint modifiers, uint vkKey, Action onHotkeyPressed);
    void Unregister();
}

/// <summary>
/// Registers system-wide hotkeys via Win32 RegisterHotKey to toggle or summon ORBIT from anywhere.
/// </summary>
public sealed class GlobalHotkeyService : IGlobalHotkeyService
{
    private const string LogCategory = "HOTKEY";
    private const int HotkeyId = 0x504F; // "PO" Orbit

    private IntPtr _hwnd = IntPtr.Zero;
    private HwndSource? _hwndSource;
    private Action? _callback;
    private bool _isRegistered = false;

    public bool Register(IntPtr hwnd, uint modifiers, uint vkKey, Action onHotkeyPressed)
    {
        if (hwnd == IntPtr.Zero) return false;

        Unregister();

        _hwnd = hwnd;
        _callback = onHotkeyPressed;

        var registered = NativeMethods.RegisterHotKey(_hwnd, HotkeyId, modifiers | NativeMethods.MOD_NOREPEAT, vkKey);
        if (!registered)
        {
            var err = Marshal.GetLastWin32Error();
            OrbitLogger.Warn(LogCategory, $"RegisterHotKey failed with error {err}. Hotkey may already be registered by another app.");
            return false;
        }

        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(HwndHook);
        _isRegistered = true;

        OrbitLogger.Info(LogCategory, $"Global hotkey registered successfully (Modifiers: 0x{modifiers:X}, VK: 0x{vkKey:X}).");
        return true;
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            OrbitLogger.Info(LogCategory, "Global hotkey triggered.");
            _callback?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Unregister()
    {
        if (_isRegistered && _hwnd != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(_hwnd, HotkeyId);
            _hwndSource?.RemoveHook(HwndHook);
            _hwndSource = null;
            _isRegistered = false;
            OrbitLogger.Debug(LogCategory, "Global hotkey unregistered.");
        }
    }

    public void Dispose()
    {
        Unregister();
    }
}

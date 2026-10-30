using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace Orbit.Models;

/// <summary>
/// Information about an open OS desktop window with observable property updates.
/// </summary>
public sealed class WindowInfo : INotifyPropertyChanged, IEquatable<WindowInfo>
{
    public required IntPtr Handle { get; init; }
    public required string ProcessName { get; init; }
    public required int ProcessId { get; init; }
    public string ExecutablePath { get; init; } = string.Empty;

    private string _title = string.Empty;
    public string Title
    {
        get => _title;
        set
        {
            if (_title != value)
            {
                _title = value;
                OnPropertyChanged();
            }
        }
    }

    private string _appName = string.Empty;
    public string AppName
    {
        get => _appName;
        set
        {
            if (_appName != value)
            {
                _appName = value;
                OnPropertyChanged();
            }
        }
    }

    private ImageSource? _icon;
    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (_icon != value)
            {
                _icon = value;
                OnPropertyChanged();
            }
        }
    }

    private MonitorBounds _bounds;
    public MonitorBounds Bounds
    {
        get => _bounds;
        set
        {
            _bounds = value;
            OnPropertyChanged();
        }
    }

    private bool _isVisible;
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible != value)
            {
                _isVisible = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _isMinimized;
    public bool IsMinimized
    {
        get => _isMinimized;
        set
        {
            if (_isMinimized != value)
            {
                _isMinimized = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void UpdateFrom(WindowInfo fresh)
    {
        if (!string.IsNullOrWhiteSpace(fresh.Title) && Title != fresh.Title)
        {
            Title = fresh.Title;
        }

        if (!string.IsNullOrWhiteSpace(fresh.AppName) && AppName != fresh.AppName)
        {
            AppName = fresh.AppName;
        }

        if (fresh.Icon != null && Icon != fresh.Icon)
        {
            Icon = fresh.Icon;
        }

        IsMinimized = fresh.IsMinimized;
        IsVisible = fresh.IsVisible;
        Bounds = fresh.Bounds;
    }

    public bool Equals(WindowInfo? other) => other != null && Handle == other.Handle;
    public override bool Equals(object? obj) => obj is WindowInfo other && Equals(other);
    public override int GetHashCode() => Handle.GetHashCode();

    public override string ToString() => $"[HWND: 0x{Handle:X}] '{Title}' ({ProcessName})";
}

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace Orbit.Models;

public enum AppCategory
{
    Developer,
    Social,
    Work
}

/// <summary>
/// Universal application descriptor across Orbit hubs (Developer, Social, Work).
/// Supports executable launching, packaged Windows Store apps, Web apps, notification badges, and active state tracking.
/// </summary>
public class OrbitApp : INotifyPropertyChanged
{
    private bool _isSelected;
    private ImageSource? _icon;
    private string _executablePath = string.Empty;
    private bool _isInstalled = true;
    private int? _notificationCount;
    private bool _isRunning;

    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public AppCategory Category { get; init; } = AppCategory.Developer;

    public string ExecutablePath
    {
        get => _executablePath;
        set
        {
            if (_executablePath != value)
            {
                _executablePath = value;
                OnPropertyChanged();
            }
        }
    }

    public string? Arguments { get; init; }
    public string? WorkingDirectory { get; init; }
    public string? ProcessName { get; init; }
    public string? IconPath { get; init; }
    public string? PackageId { get; init; }
    public string? WebUrl { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsInstalled
    {
        get => _isInstalled;
        set
        {
            if (_isInstalled != value)
            {
                _isInstalled = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (_isRunning != value)
            {
                _isRunning = value;
                OnPropertyChanged();
            }
        }
    }

    public int? NotificationCount
    {
        get => _notificationCount;
        set
        {
            if (_notificationCount != value)
            {
                _notificationCount = value;
                OnPropertyChanged();
            }
        }
    }

    [JsonIgnore]
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

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}


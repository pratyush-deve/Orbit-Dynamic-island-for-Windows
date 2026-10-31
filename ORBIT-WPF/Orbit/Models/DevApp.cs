using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace Orbit.Models;

/// <summary>
/// Represents a developer application in the DEV Mode workspace.
/// </summary>
public sealed class DevApp : INotifyPropertyChanged
{
    private bool _isSelected;
    private ImageSource? _icon;
    private string _executablePath = string.Empty;
    private bool _isInstalled = true;

    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;

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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}


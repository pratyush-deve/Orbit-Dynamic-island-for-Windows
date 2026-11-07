using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Orbit.Core;
using Orbit.Models;
using Orbit.Services.DevWorkspace;

namespace Orbit.Services.FileDock;

/// <summary>
/// Thread-safe in-memory session implementation of the ORBIT File Dock service.
/// References are stored exclusively in RAM and cleared when ORBIT restarts or Windows shuts down.
/// </summary>
public sealed class FileDockService : IFileDockService
{
    private const string LogCategory = "FILE_DOCK";

    private readonly IIconExtractorService? _iconExtractor;
    private readonly ObservableCollection<FileDockItem> _items = new();
    private FileDockMode _currentMode = FileDockMode.Closed;
    private readonly object _syncLock = new();

    public ObservableCollection<FileDockItem> Items => _items;

    public FileDockMode CurrentMode
    {
        get => _currentMode;
        set
        {
            if (_currentMode != value)
            {
                var prev = _currentMode;
                _currentMode = value;
                OrbitLogger.Info(LogCategory, $"File Dock mode changed: {prev} -> {_currentMode}");
                RunOnDispatcher(() => ModeChanged?.Invoke(_currentMode));
            }
        }
    }

    public event Action<FileDockMode>? ModeChanged;
    public event Action<FileDockItem>? ItemAdded;
    public event Action<FileDockItem>? ItemRemoved;
    public event Action? CollectionCleared;

    public FileDockService(IIconExtractorService? iconExtractor = null)
    {
        _iconExtractor = iconExtractor;
        OrbitLogger.Info(LogCategory, "File Dock service initialized with empty in-memory session collection.");
    }

    public void AddFiles(IEnumerable<string> paths)
    {
        if (paths == null) return;

        foreach (var rawPath in paths)
        {
            if (string.IsNullOrWhiteSpace(rawPath)) continue;

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(rawPath);
            }
            catch
            {
                fullPath = rawPath;
            }

            if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            {
                OrbitLogger.Warn(LogCategory, $"Skipped non-existent file path: '{fullPath}'");
                continue;
            }

            lock (_syncLock)
            {
                // Check for duplicate reference
                var existing = _items.FirstOrDefault(x => string.Equals(x.FilePath, fullPath, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    OrbitLogger.Debug(LogCategory, $"File reference already staged in File Dock: '{fullPath}'");
                    continue;
                }

                ImageSource? icon = null;
                try
                {
                    icon = _iconExtractor?.GetOrExtractIcon(fullPath);
                }
                catch (Exception ex)
                {
                    OrbitLogger.Debug(LogCategory, $"Failed to extract icon for '{fullPath}': {ex.Message}");
                }

                var item = new FileDockItem(fullPath, icon);

                RunOnDispatcher(() =>
                {
                    _items.Add(item);
                    ItemAdded?.Invoke(item);
                    OrbitLogger.Info(LogCategory, $"Staged file into File Dock: '{item.FileName}' ({item.FileSizeFormatted}). Total staged: {_items.Count}");
                });
            }
        }
    }

    public bool RemoveFile(FileDockItem item)
    {
        if (item == null) return false;

        bool removed = false;
        RunOnDispatcher(() =>
        {
            removed = _items.Remove(item);
            if (removed)
            {
                ItemRemoved?.Invoke(item);
                OrbitLogger.Info(LogCategory, $"Removed file from dock: '{item.FileName}'. Total staged: {_items.Count}");
            }
        });
        return removed;
    }

    public void Clear()
    {
        RunOnDispatcher(() =>
        {
            int count = _items.Count;
            _items.Clear();
            CollectionCleared?.Invoke();
            OrbitLogger.Info(LogCategory, $"Cleared File Dock session collection (was {count} items).");
        });
    }

    private static void RunOnDispatcher(Action action)
    {
        var app = Application.Current;
        if (app != null && app.Dispatcher != null && app.Dispatcher.Thread.IsAlive && !app.Dispatcher.HasShutdownStarted && !app.Dispatcher.HasShutdownFinished)
        {
            if (!app.Dispatcher.CheckAccess())
            {
                try
                {
                    app.Dispatcher.Invoke(action);
                    return;
                }
                catch
                {
                    // Fall back to direct execution if dispatcher invocation fails
                }
            }
        }
        action();
    }
}

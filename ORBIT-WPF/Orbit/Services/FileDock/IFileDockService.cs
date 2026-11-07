using System.Collections.ObjectModel;
using Orbit.Models;

namespace Orbit.Services.FileDock;

/// <summary>
/// Provides session-only in-memory storage and lifecycle management for the ORBIT File Dock.
/// Staged items are retained in memory during the active session and are never saved to disk or database.
/// </summary>
public interface IFileDockService
{
    ObservableCollection<FileDockItem> Items { get; }
    FileDockMode CurrentMode { get; set; }

    event Action<FileDockMode>? ModeChanged;
    event Action<FileDockItem>? ItemAdded;
    event Action<FileDockItem>? ItemRemoved;
    event Action? CollectionCleared;

    void AddFiles(IEnumerable<string> paths);
    bool RemoveFile(FileDockItem item);
    void Clear();
}


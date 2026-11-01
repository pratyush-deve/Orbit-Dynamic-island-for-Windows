namespace Orbit.Services.Notes;

/// <summary>
/// Service interface for ORBIT built-in quick notes persistence and file export.
/// </summary>
public interface INotesService
{
    /// <summary>
    /// Loads saved notes content from local persistent storage.
    /// </summary>
    string LoadNotes();

    /// <summary>
    /// Saves notes content synchronously to local persistent storage.
    /// </summary>
    void SaveNotes(string content);

    /// <summary>
    /// Saves notes content asynchronously without blocking the UI thread.
    /// </summary>
    Task SaveNotesAsync(string content);

    /// <summary>
    /// Exports notes content to an external file (.txt or .md) chosen by the user.
    /// </summary>
    bool ExportNotes(string destinationFilePath, string content);

    /// <summary>
    /// Clears the saved notes from persistent storage.
    /// </summary>
    void ClearNotes();
}


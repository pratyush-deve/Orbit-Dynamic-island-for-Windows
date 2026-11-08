using System.IO;
using System.Text;
using Orbit.Core;

namespace Orbit.Services.Notes;

/// <summary>
/// Thread-safe local file persistence service for ORBIT Notes mode.
/// Stores notes in %LOCALAPPDATA%\ORBIT\orbit_notes.txt independently of AI sessions or cloud connections.
/// </summary>
public sealed class NotesService : INotesService
{
    private const string LogCategory = "NOTES_SERVICE";
    private readonly string _notesFilePath;
    private readonly object _lock = new();

    public event Action<string>? NotesUpdated;

    public NotesService()
    {
        string orbitDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ORBIT");
        Directory.CreateDirectory(orbitDir);
        _notesFilePath = Path.Combine(orbitDir, "orbit_notes.txt");
    }

    public string LoadNotes()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_notesFilePath))
                {
                    return File.ReadAllText(_notesFilePath, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                OrbitLogger.Error(LogCategory, $"Failed to load notes from '{_notesFilePath}': {ex.Message}");
            }

            return string.Empty;
        }
    }

    public void SaveNotes(string content)
    {
        lock (_lock)
        {
            try
            {
                File.WriteAllText(_notesFilePath, content ?? string.Empty, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                OrbitLogger.Error(LogCategory, $"Failed to save notes to '{_notesFilePath}': {ex.Message}");
            }
        }
        NotesUpdated?.Invoke(content ?? string.Empty);
    }

    public async Task SaveNotesAsync(string content)
    {
        try
        {
            await Task.Run(() => SaveNotes(content));
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, $"Failed to save notes asynchronously: {ex.Message}");
        }
    }

    public bool ExportNotes(string destinationFilePath, string content)
    {
        try
        {
            string? dir = Path.GetDirectoryName(destinationFilePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(destinationFilePath, content ?? string.Empty, Encoding.UTF8);
            OrbitLogger.Info(LogCategory, $"Successfully exported notes to '{destinationFilePath}'");
            return true;
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, $"Failed to export notes to '{destinationFilePath}': {ex.Message}");
            return false;
        }
    }

    public void ClearNotes()
    {
        lock (_lock)
        {
            try
            {
                File.WriteAllText(_notesFilePath, string.Empty, Encoding.UTF8);
                OrbitLogger.Info(LogCategory, "Cleared persistent notes.");
            }
            catch (Exception ex)
            {
                OrbitLogger.Error(LogCategory, $"Failed to clear notes: {ex.Message}");
            }
        }
        NotesUpdated?.Invoke(string.Empty);
    }
}


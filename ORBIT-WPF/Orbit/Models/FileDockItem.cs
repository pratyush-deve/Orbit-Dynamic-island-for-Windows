using System.IO;
using System.Windows.Media;

namespace Orbit.Models;

/// <summary>
/// Represents a file reference staged in the in-memory session File Dock.
/// Staging or removing this item never alters or deletes the underlying physical file on disk.
/// </summary>
public class FileDockItem
{
    public string FilePath { get; }
    public string FileName { get; }
    public string Extension { get; }
    public long FileSizeBytes { get; }
    public string FileSizeFormatted { get; }
    public ImageSource? Icon { get; set; }
    public DateTime AddedTime { get; }
    public bool IsDirectory { get; }

    public FileDockItem(string filePath, ImageSource? icon = null)
    {
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        AddedTime = DateTime.UtcNow;
        Icon = icon;

        bool isDir = Directory.Exists(filePath);
        IsDirectory = isDir;

        if (isDir)
        {
            var dirInfo = new DirectoryInfo(filePath);
            FileName = string.IsNullOrEmpty(dirInfo.Name) ? filePath : dirInfo.Name;
            Extension = "Folder";
            FileSizeBytes = 0;
            FileSizeFormatted = "Folder";
        }
        else
        {
            var fileInfo = new FileInfo(filePath);
            FileName = fileInfo.Name;
            Extension = fileInfo.Extension.ToLowerInvariant();
            FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0;
            FileSizeFormatted = FormatBytes(FileSizeBytes);
        }
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "0 B";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{(bytes / 1024.0):F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{(bytes / (1024.0 * 1024.0)):F1} MB";
        return $"{(bytes / (1024.0 * 1024.0 * 1024.0)):F1} GB";
    }

    public override bool Equals(object? obj)
    {
        if (obj is FileDockItem other)
        {
            return string.Equals(FilePath, other.FilePath, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    public override int GetHashCode()
    {
        return StringComparer.OrdinalIgnoreCase.GetHashCode(FilePath);
    }
}


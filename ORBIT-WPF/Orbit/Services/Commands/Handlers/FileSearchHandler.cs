using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Core;

namespace Orbit.Services.Commands.Handlers;

public sealed class FileSearchHandler : ICommandHandler
{
    private const string LogCategory = "FILE_SEARCH_HANDLER";

    public CommandType SupportedType => CommandType.FileSearch;

    public async Task<CommandResult> ExecuteAsync(CommandIntent intent, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(intent.Target))
        {
            return new CommandResult(false, "No search term provided", intent);
        }

        string query = intent.Target.Trim();
        string? scope = intent.Scope?.Trim();

        OrbitLogger.Info(LogCategory, $"Executing local file search for: '{query}' in scope: '{scope ?? "All"}'");

        try
        {
            // 1. If user targeted a specific folder (e.g. Desktop, Downloads, Documents)
            if (!string.IsNullOrWhiteSpace(scope))
            {
                var (folderPath, folderDisplayName) = ResolveFolder(scope);

                if (Directory.Exists(folderPath))
                {
                    var matches = await Task.Run(() => SearchInDirectory(folderPath, query, maxResults: 10), cancellationToken);

                    if (matches.Count > 0)
                    {
                        string firstMatch = matches[0];
                        OrbitLogger.Info(LogCategory, $"Found {matches.Count} match(es) in {folderDisplayName}. Selecting: {firstMatch}");

                        // Open Explorer and select the first matching file
                        Process.Start("explorer.exe", $"/select,\"{firstMatch}\"");

                        return new CommandResult(true, $"Found {matches.Count} file(s) in {folderDisplayName}", intent, firstMatch);
                    }
                    else
                    {
                        OrbitLogger.Info(LogCategory, $"No matches found in {folderDisplayName}. Opening folder and informing user.");

                        // As requested: Open the folder so user can browse, and report unable to find before closing voice
                        Process.Start("explorer.exe", folderPath);

                        return new CommandResult(false, $"Can't find \"{query}\" in {folderDisplayName}", intent);
                    }
                }
            }

            // 2. General file search across primary user directories
            var userFolders = GetPrimaryUserFolders();
            var generalMatches = await Task.Run(() =>
            {
                var list = new List<string>();
                foreach (var folder in userFolders)
                {
                    if (Directory.Exists(folder))
                    {
                        list.AddRange(SearchInDirectory(folder, query, maxResults: 5));
                        if (list.Count >= 5) break;
                    }
                }
                return list;
            }, cancellationToken);

            string searchUrl = $"search-ms:query={Uri.EscapeDataString(query)}";
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = searchUrl,
                UseShellExecute = true
            });

            if (generalMatches.Count > 0)
            {
                return new CommandResult(true, $"Found matching files for \"{query}\"", intent, searchUrl);
            }
            else
            {
                return new CommandResult(false, $"Can't find files for \"{query}\"", intent, searchUrl);
            }
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, $"Failed to execute local file search for '{query}'", ex);
            return new CommandResult(false, "Failed to start file search", intent, ex.Message);
        }
    }

    private static (string Path, string DisplayName) ResolveFolder(string scope)
    {
        string clean = scope.ToLowerInvariant();

        if (clean.Contains("desktop"))
        {
            string oneDriveDesktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive", "Desktop");
            if (Directory.Exists(oneDriveDesktop)) return (oneDriveDesktop, "Desktop");
            return (Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Desktop");
        }

        if (clean.Contains("download"))
        {
            return (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), "Downloads");
        }

        if (clean.Contains("document"))
        {
            string oneDriveDocs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive", "Documents");
            if (Directory.Exists(oneDriveDocs)) return (oneDriveDocs, "Documents");
            return (Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Documents");
        }

        if (clean.Contains("picture") || clean.Contains("photo"))
        {
            return (Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Pictures");
        }

        if (clean.Contains("music"))
        {
            return (Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Music");
        }

        if (clean.Contains("video"))
        {
            return (Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Videos");
        }

        if (clean.Contains("c drive") || clean == "c")
        {
            return (@"C:\", "C Drive");
        }

        if (clean.Contains("d drive") || clean == "d")
        {
            return (@"D:\", "D Drive");
        }

        // Check user profile folder or current directory as fallback
        string userFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), scope);
        if (Directory.Exists(userFolder))
        {
            return (userFolder, scope);
        }

        return (Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Desktop");
    }

    private static List<string> GetPrimaryUserFolders()
    {
        var folders = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        string oneDriveDesktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive", "Desktop");
        if (Directory.Exists(oneDriveDesktop) && !folders.Contains(oneDriveDesktop))
        {
            folders.Add(oneDriveDesktop);
        }

        return folders;
    }

    private static List<string> SearchInDirectory(string dir, string query, int maxResults)
    {
        var matches = new List<string>();
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = 2,
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false
            };

            // Tokenize query words (e.g. "random", "college")
            var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                              .Where(t => t.Length > 2)
                              .ToArray();

            foreach (var file in Directory.EnumerateFiles(dir, "*", options))
            {
                string fileName = Path.GetFileName(file);

                // Exact phrase match
                if (fileName.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(file);
                    if (matches.Count >= maxResults) break;
                    continue;
                }

                // Any keyword match
                if (tokens.Length > 0 && tokens.Any(t => fileName.Contains(t, StringComparison.OrdinalIgnoreCase)))
                {
                    matches.Add(file);
                    if (matches.Count >= maxResults) break;
                }
            }
        }
        catch (Exception ex)
        {
            OrbitLogger.Warn(LogCategory, $"Error scanning directory '{dir}': {ex.Message}");
        }

        return matches;
    }
}

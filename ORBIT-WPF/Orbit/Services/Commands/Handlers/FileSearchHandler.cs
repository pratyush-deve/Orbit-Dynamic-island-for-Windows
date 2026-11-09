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
            // 1. If user targeted a specific folder (e.g. Desktop, Downloads, Documents, D drive)
            if (!string.IsNullOrWhiteSpace(scope))
            {
                var (folderPath, folderDisplayName) = ResolveFolder(scope);

                if (Directory.Exists(folderPath))
                {
                    var matches = await Task.Run(() => SearchInDirectory(folderPath, query, maxResults: 10), cancellationToken);

                    if (matches.Count > 0)
                    {
                        string firstMatch = matches[0];
                        bool isDir = Directory.Exists(firstMatch);
                        OrbitLogger.Info(LogCategory, $"Found {matches.Count} match(es) in {folderDisplayName} (isDir={isDir}). Target: {firstMatch}");

                        if (isDir)
                        {
                            // Open Explorer directly inside or at the matched directory
                            Process.Start("explorer.exe", firstMatch);
                        }
                        else
                        {
                            // Open Explorer and select the first matching file
                            Process.Start("explorer.exe", $"/select,\"{firstMatch}\"");
                        }

                        string itemType = isDir ? "folder" : "file(s)";
                        return new CommandResult(true, $"Found {matches.Count} {itemType} in {folderDisplayName}", intent, firstMatch);
                    }
                    else
                    {
                        OrbitLogger.Info(LogCategory, $"No matches found in {folderDisplayName}. Opening folder and informing user.");

                        // Open the folder so user can browse, and report unable to find
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

            string cleanQuery = query.Trim();
            bool userWantsFolder = System.Text.RegularExpressions.Regex.IsMatch(cleanQuery, @"\b(?:folder|directory)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // Strip filler words like "folder", "files", "file" for actual name matching
            string strippedName = System.Text.RegularExpressions.Regex.Replace(cleanQuery, @"\b(?:folder|directory|files|file)\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            if (string.IsNullOrWhiteSpace(strippedName))
            {
                strippedName = cleanQuery;
            }

            // Extension filter mapping (e.g. "PDFs" -> .pdf, "images" -> .png, .jpg)
            string lowerQuery = strippedName.ToLowerInvariant();
            List<string> extensionFilters = new();
            if (lowerQuery == "pdf" || lowerQuery == "pdfs")
            {
                extensionFilters.Add(".pdf");
            }
            else if (lowerQuery == "word" || lowerQuery == "docs" || lowerQuery == "doc")
            {
                extensionFilters.AddRange([".docx", ".doc"]);
            }
            else if (lowerQuery == "excel" || lowerQuery == "sheets")
            {
                extensionFilters.AddRange([".xlsx", ".xls", ".csv"]);
            }
            else if (lowerQuery == "images" || lowerQuery == "photos" || lowerQuery == "pictures")
            {
                extensionFilters.AddRange([".png", ".jpg", ".jpeg"]);
            }

            var tokens = strippedName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                     .Where(t => t.Length > 2)
                                     .ToArray();

            // 1. If user explicitly asked for a folder, check directories first
            if (userWantsFolder)
            {
                foreach (var subDir in Directory.EnumerateDirectories(dir, "*", options))
                {
                    string dirName = Path.GetFileName(subDir);
                    if (dirName.Contains(strippedName, StringComparison.OrdinalIgnoreCase) ||
                        (tokens.Length > 0 && tokens.Any(t => dirName.Contains(t, StringComparison.OrdinalIgnoreCase))))
                    {
                        matches.Add(subDir);
                        if (matches.Count >= maxResults) return matches;
                    }
                }
            }

            // 2. Scan files
            foreach (var file in Directory.EnumerateFiles(dir, "*", options))
            {
                string fileName = Path.GetFileName(file);

                // Extension match
                if (extensionFilters.Count > 0)
                {
                    if (extensionFilters.Any(ext => fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                    {
                        matches.Add(file);
                        if (matches.Count >= maxResults) break;
                    }
                    continue;
                }

                // Exact phrase match
                if (fileName.Contains(strippedName, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(file);
                    if (matches.Count >= maxResults) break;
                    continue;
                }

                // Token match
                if (tokens.Length > 0 && tokens.Any(t => fileName.Contains(t, StringComparison.OrdinalIgnoreCase)))
                {
                    matches.Add(file);
                    if (matches.Count >= maxResults) break;
                }
            }

            // 3. If no files matched and folder wasn't checked yet, check directories as fallback
            if (matches.Count == 0 && !userWantsFolder)
            {
                foreach (var subDir in Directory.EnumerateDirectories(dir, "*", options))
                {
                    string dirName = Path.GetFileName(subDir);
                    if (dirName.Contains(strippedName, StringComparison.OrdinalIgnoreCase) ||
                        (tokens.Length > 0 && tokens.Any(t => dirName.Contains(t, StringComparison.OrdinalIgnoreCase))))
                    {
                        matches.Add(subDir);
                        if (matches.Count >= maxResults) break;
                    }
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

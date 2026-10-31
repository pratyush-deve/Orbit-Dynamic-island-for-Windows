using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Orbit.Core;
using Orbit.Models;

namespace Orbit.Services.AiObserver;

public interface IAntigravitySessionResolver
{
    AntigravityAppType ResolveOriginatingApp(RawConversationSummary summary, IAntigravityProcessMonitor processMonitor);
}

/// <summary>
/// Deterministic session attribution service for Antigravity vs Antigravity IDE.
/// Inspects the application-specific storage metadata (app_storage.json vs state.vscdb)
/// to establish proven application ownership without guessing or heuristics.
/// </summary>
public sealed class AntigravitySessionResolver : IAntigravitySessionResolver
{
    private const string LogCategory = "SESSION_RESOLVER";

    private readonly string _agAppStoragePath;
    private readonly string _ideGlobalStorageDbPath;
    private readonly string _ideWorkspaceStorageRoot;

    private readonly object _lock = new();
    private DateTime _lastCacheRefresh = DateTime.MinValue;
    private HashSet<string> _knownAntigravityCids = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _knownIdeCids = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _knownIdeWorkspaces = [];

    public AntigravitySessionResolver()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _agAppStoragePath = Path.Combine(appData, "Antigravity", "app_storage.json");
        _ideGlobalStorageDbPath = Path.Combine(appData, "Antigravity IDE", "User", "globalStorage", "state.vscdb");
        _ideWorkspaceStorageRoot = Path.Combine(appData, "Antigravity IDE", "User", "workspaceStorage");
    }

    public AntigravityAppType ResolveOriginatingApp(RawConversationSummary summary, IAntigravityProcessMonitor processMonitor)
    {
        if (summary == null || string.IsNullOrWhiteSpace(summary.ConversationId))
        {
            return AntigravityAppType.Unknown;
        }

        RefreshCachesIfNeeded();

        string cid = summary.ConversationId.Trim();

        lock (_lock)
        {
            bool inIde = _knownIdeCids.Contains(cid);
            bool inAg = _knownAntigravityCids.Contains(cid);

            // 1. Direct metadata attribution
            if (inIde && !inAg)
            {
                return AntigravityAppType.AntigravityIde;
            }
            if (inAg && !inIde)
            {
                return AntigravityAppType.Antigravity;
            }

            // 2. Workspace path match with Antigravity IDE known workspace folders
            if (summary.WorkspacePaths.Count > 0 && _knownIdeWorkspaces.Count > 0)
            {
                foreach (var wp in summary.WorkspacePaths)
                {
                    string normWp = NormalizePath(wp);
                    foreach (var ideWp in _knownIdeWorkspaces)
                    {
                        string normIdeWp = NormalizePath(ideWp);
                        if (string.Equals(normWp, normIdeWp, StringComparison.OrdinalIgnoreCase) ||
                            normWp.StartsWith(normIdeWp, StringComparison.OrdinalIgnoreCase))
                        {
                            return AntigravityAppType.AntigravityIde;
                        }
                    }
                }
            }
        }

        // 3. If ownership cannot be established reliably, return Unknown
        return AntigravityAppType.Unknown;
    }

    private void RefreshCachesIfNeeded()
    {
        lock (_lock)
        {
            if ((DateTime.UtcNow - _lastCacheRefresh).TotalSeconds < 2.0)
            {
                return;
            }
            _lastCacheRefresh = DateTime.UtcNow;
        }

        var agCids = LoadAntigravityConversationIds();
        var (ideCids, ideWorkspaces) = LoadAntigravityIdeConversationIdsAndWorkspaces();

        lock (_lock)
        {
            _knownAntigravityCids = agCids;
            _knownIdeCids = ideCids;
            _knownIdeWorkspaces = ideWorkspaces;
        }
    }

    private HashSet<string> LoadAntigravityConversationIds()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(_agAppStoragePath)) return result;

        try
        {
            using var stream = new FileStream(_agAppStoragePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            string content = reader.ReadToEnd();

            var matches = Regex.Matches(content, @"antigravity-multi-conversation-layout-v3-([0-9a-fA-F-]{36})");
            foreach (Match m in matches)
            {
                if (m.Groups.Count > 1)
                {
                    result.Add(m.Groups[1].Value);
                }
            }
        }
        catch (Exception ex)
        {
            OrbitLogger.Debug(LogCategory, $"Failed to read Antigravity app_storage: {ex.Message}");
        }

        return result;
    }

    private (HashSet<string> Cids, List<string> Workspaces) LoadAntigravityIdeConversationIdsAndWorkspaces()
    {
        var cids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var workspaces = new List<string>();

        // 1. Read globalStorage/state.vscdb
        if (File.Exists(_ideGlobalStorageDbPath))
        {
            try
            {
                var builder = new SqliteConnectionStringBuilder
                {
                    DataSource = _ideGlobalStorageDbPath,
                    Mode = SqliteOpenMode.ReadOnly,
                    Cache = SqliteCacheMode.Shared
                };

                using var conn = new SqliteConnection(builder.ConnectionString);
                conn.Open();

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT value FROM ItemTable WHERE key = 'google.google-antigravity';";
                var val = cmd.ExecuteScalar()?.ToString();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    var matches = Regex.Matches(val, @"antigravity-multi-conversation-layout-v3-([0-9a-fA-F-]{36})");
                    foreach (Match m in matches)
                    {
                        if (m.Groups.Count > 1)
                        {
                            cids.Add(m.Groups[1].Value);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                OrbitLogger.Debug(LogCategory, $"Failed to read IDE global state.vscdb: {ex.Message}");
            }
        }

        // 2. Read workspaceStorage folders
        if (Directory.Exists(_ideWorkspaceStorageRoot))
        {
            try
            {
                foreach (var folder in Directory.GetDirectories(_ideWorkspaceStorageRoot))
                {
                    string wsFile = Path.Combine(folder, "workspace.json");
                    if (File.Exists(wsFile))
                    {
                        try
                        {
                            string jsonText = File.ReadAllText(wsFile);
                            using var doc = JsonDocument.Parse(jsonText);
                            if (doc.RootElement.TryGetProperty("folder", out var folderProp))
                            {
                                string uriStr = folderProp.GetString() ?? string.Empty;
                                if (Uri.TryCreate(uriStr, UriKind.Absolute, out var uri))
                                {
                                    workspaces.Add(uri.LocalPath);
                                }
                            }
                        }
                        catch { }
                    }

                    string dbFile = Path.Combine(folder, "state.vscdb");
                    if (File.Exists(dbFile))
                    {
                        try
                        {
                            var builder = new SqliteConnectionStringBuilder
                            {
                                DataSource = dbFile,
                                Mode = SqliteOpenMode.ReadOnly,
                                Cache = SqliteCacheMode.Shared
                            };

                            using var conn = new SqliteConnection(builder.ConnectionString);
                            conn.Open();

                            using var cmd = conn.CreateCommand();
                            cmd.CommandText = "SELECT value FROM ItemTable WHERE key = 'google.google-antigravity';";
                            var val = cmd.ExecuteScalar()?.ToString();
                            if (!string.IsNullOrWhiteSpace(val))
                            {
                                using var doc = JsonDocument.Parse(val);
                                if (doc.RootElement.TryGetProperty("lastConversationId", out var lastCidProp))
                                {
                                    string? lastCid = lastCidProp.GetString();
                                    if (!string.IsNullOrWhiteSpace(lastCid))
                                    {
                                        cids.Add(lastCid);
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                OrbitLogger.Debug(LogCategory, $"Failed to scan IDE workspaceStorage: {ex.Message}");
            }
        }

        return (cids, workspaces);
    }

    private static string NormalizePath(string path)
    {
        return path.Trim().TrimEnd('\\', '/').ToLowerInvariant();
    }
}

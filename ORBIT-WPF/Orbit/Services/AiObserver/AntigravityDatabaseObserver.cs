using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Orbit.Core;
using Orbit.Models;

namespace Orbit.Services.AiObserver;

public interface IAntigravityDatabaseObserver : IDisposable
{
    event Action<IReadOnlyList<RawConversationSummary>>? DatabaseUpdated;
    IReadOnlyList<RawConversationSummary> GetLatestSummaries();
    void RefreshNow();
}

public sealed class AntigravityDatabaseObserver : IAntigravityDatabaseObserver
{
    private const string LogCategory = "DB_OBSERVER";

    private readonly string _dbPath;
    private readonly FileSystemWatcher? _watcher;
    private readonly System.Threading.Timer _fallbackTimer;
    private readonly object _lock = new();

    private DateTime _lastTriggerTime = DateTime.MinValue;
    private List<RawConversationSummary> _cachedSummaries = [];
    private bool _disposed;

    public event Action<IReadOnlyList<RawConversationSummary>>? DatabaseUpdated;

    public AntigravityDatabaseObserver()
    {
        string baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity");
        _dbPath = Path.Combine(baseDir, "conversation_summaries.db");

        // Initial fetch
        ReadSummariesInternal();

        // 1. FileSystemWatcher on the DB directory
        try
        {
            if (Directory.Exists(baseDir))
            {
                _watcher = new FileSystemWatcher(baseDir)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                    Filter = "conversation_summaries.db*",
                    EnableRaisingEvents = true
                };

                _watcher.Changed += OnFileChanged;
                _watcher.Created += OnFileChanged;
            }
        }
        catch (Exception ex)
        {
            OrbitLogger.Warn(LogCategory, $"Failed to initialize FileSystemWatcher: {ex.Message}");
        }

        // 2. Periodic reconciliation fallback timer (every 3 seconds)
        _fallbackTimer = new System.Threading.Timer(_ => CheckForUpdates(), null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
    }

    public IReadOnlyList<RawConversationSummary> GetLatestSummaries()
    {
        lock (_lock)
        {
            return _cachedSummaries.ToList();
        }
    }

    public void RefreshNow()
    {
        ReadSummariesInternal();
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Debounce within 100ms
        lock (_lock)
        {
            if ((DateTime.UtcNow - _lastTriggerTime).TotalMilliseconds < 100) return;
            _lastTriggerTime = DateTime.UtcNow;
        }

        Task.Delay(50).ContinueWith(_ => ReadSummariesInternal());
    }

    private void CheckForUpdates()
    {
        if (_disposed) return;
        ReadSummariesInternal();
    }

    private void ReadSummariesInternal()
    {
        if (_disposed || !File.Exists(_dbPath)) return;

        var results = new List<RawConversationSummary>();

        try
        {
            // Open read-only with SQLite WAL mode support
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = _dbPath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Shared
            };

            using var connection = new SqliteConnection(builder.ConnectionString);
            connection.Open();

            // Explicitly enable query_only pragma to guarantee zero modifications
            using (var pragmaCmd = connection.CreateCommand())
            {
                pragmaCmd.CommandText = "PRAGMA query_only = ON;";
                pragmaCmd.ExecuteNonQuery();
            }

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT 
                    conversation_id, 
                    title, 
                    preview, 
                    step_count, 
                    last_modified_time, 
                    workspace_uris, 
                    status, 
                    agent_name, 
                    not_fully_idle, 
                    killed, 
                    last_user_input_time 
                FROM conversation_summaries 
                ORDER BY last_modified_time DESC 
                LIMIT 25;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var summary = new RawConversationSummary
                {
                    ConversationId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    Title = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    Preview = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    StepCount = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                    LastModifiedTime = reader.IsDBNull(4) ? DateTime.MinValue : ParseSqliteDate(reader.GetString(4)),
                    WorkspaceUrisJson = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                    Status = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                    AgentName = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                    NotFullyIdle = !reader.IsDBNull(8) && Convert.ToBoolean(reader.GetValue(8)),
                    Killed = !reader.IsDBNull(9) && Convert.ToBoolean(reader.GetValue(9)),
                    LastUserInputTime = reader.IsDBNull(10) ? DateTime.MinValue : ParseSqliteDate(reader.GetString(10))
                };

                // Parse workspace paths
                summary.WorkspacePaths = ParseWorkspaceUris(summary.WorkspaceUrisJson);
                results.Add(summary);
            }
        }
        catch (Exception ex)
        {
            OrbitLogger.Debug(LogCategory, $"Database read exception: {ex.Message}");
            return;
        }

        bool hasDifferences = false;
        lock (_lock)
        {
            if (_cachedSummaries.Count != results.Count)
            {
                hasDifferences = true;
            }
            else
            {
                for (int i = 0; i < results.Count; i++)
                {
                    var a = _cachedSummaries[i];
                    var b = results[i];
                    if (a.ConversationId != b.ConversationId ||
                        a.Status != b.Status ||
                        a.StepCount != b.StepCount ||
                        a.NotFullyIdle != b.NotFullyIdle ||
                        a.LastModifiedTime != b.LastModifiedTime)
                    {
                        hasDifferences = true;
                        break;
                    }
                }
            }

            _cachedSummaries = results;
        }

        if (hasDifferences)
        {
            OrbitLogger.Info(LogCategory, $"Database updated with {results.Count} summaries.");
            DatabaseUpdated?.Invoke(results);
        }
    }

    private static DateTime ParseSqliteDate(string str)
    {
        if (DateTime.TryParse(str, out var dt))
        {
            return dt.ToUniversalTime();
        }
        return DateTime.UtcNow;
    }

    private static List<string> ParseWorkspaceUris(string json)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(json)) return list;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    string uriStr = el.GetString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(uriStr))
                    {
                        if (Uri.TryCreate(uriStr, UriKind.Absolute, out var uri) && uri.IsFile)
                        {
                            list.Add(uri.LocalPath);
                        }
                        else
                        {
                            // Strip file:/// if present
                            string cleaned = uriStr.Replace("file:///", "").Replace('/', '\\');
                            list.Add(Uri.UnescapeDataString(cleaned));
                        }
                    }
                }
            }
        }
        catch { }

        return list;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _fallbackTimer.Dispose();
        _watcher?.Dispose();
    }
}

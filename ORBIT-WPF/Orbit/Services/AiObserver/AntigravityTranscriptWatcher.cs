using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using Orbit.Core;
using Orbit.Models;

namespace Orbit.Services.AiObserver;

public class TranscriptSnapshot
{
    public string ConversationId { get; set; } = string.Empty;
    public string LatestPrompt { get; set; } = string.Empty;
    public string LatestActivity { get; set; } = string.Empty;
    public List<AgentActivityItem> RecentActivities { get; set; } = [];
    public List<string> ModifiedFiles { get; set; } = [];
    public List<string> ExecutedCommands { get; set; } = [];
    public bool IsWaitingForApproval { get; set; }
    public bool HasError { get; set; }
    public string? ErrorMessage { get; set; }
}

public interface IAntigravityTranscriptWatcher : IDisposable
{
    TranscriptSnapshot GetOrUpdateSnapshot(string conversationId);
    void ClearConversation(string conversationId);
}

public sealed class AntigravityTranscriptWatcher : IAntigravityTranscriptWatcher
{
    private const string LogCategory = "TRANSCRIPT_WATCHER";
    private readonly string _brainBaseDir;
    private readonly ConcurrentDictionary<string, (long ByteOffset, TranscriptSnapshot Snapshot)> _stateCache = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public AntigravityTranscriptWatcher()
    {
        _brainBaseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity", "brain");
    }

    public TranscriptSnapshot GetOrUpdateSnapshot(string conversationId)
    {
        if (_disposed || string.IsNullOrWhiteSpace(conversationId))
        {
            return new TranscriptSnapshot();
        }

        string transcriptPath = Path.Combine(_brainBaseDir, conversationId, ".system_generated", "logs", "transcript.jsonl");
        if (!File.Exists(transcriptPath))
        {
            return _stateCache.TryGetValue(conversationId, out var existing) ? existing.Snapshot : new TranscriptSnapshot { ConversationId = conversationId };
        }

        long currentOffset = 0;
        TranscriptSnapshot snapshot;

        if (_stateCache.TryGetValue(conversationId, out var cached))
        {
            currentOffset = cached.ByteOffset;
            snapshot = cached.Snapshot;
        }
        else
        {
            snapshot = new TranscriptSnapshot { ConversationId = conversationId };
        }

        try
        {
            var fileInfo = new FileInfo(transcriptPath);
            if (fileInfo.Length < currentOffset)
            {
                // File was rotated or truncated, reset offset
                currentOffset = 0;
            }

            if (fileInfo.Length > currentOffset)
            {
                using var fs = new FileStream(transcriptPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                fs.Seek(currentOffset, SeekOrigin.Begin);

                using var reader = new StreamReader(fs, System.Text.Encoding.UTF8);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    ProcessTranscriptLine(line, snapshot);
                }

                currentOffset = fs.Position;
                _stateCache[conversationId] = (currentOffset, snapshot);
            }
        }
        catch (Exception ex)
        {
            OrbitLogger.Debug(LogCategory, $"Error reading transcript for {conversationId}: {ex.Message}");
        }

        return snapshot;
    }

    private static void ProcessTranscriptLine(string line, TranscriptSnapshot snapshot)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            string source = root.TryGetProperty("source", out var srcProp) ? srcProp.GetString() ?? "" : "";
            string type = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? "" : "";
            string status = root.TryGetProperty("status", out var statusProp) ? statusProp.GetString() ?? "" : "";

            // Check if this step is an error
            if (string.Equals(status, "ERROR", StringComparison.OrdinalIgnoreCase))
            {
                snapshot.HasError = true;
                string errContent = root.TryGetProperty("content", out var cProp) ? cProp.GetString() ?? "" : "";
                if (!string.IsNullOrWhiteSpace(errContent))
                {
                    snapshot.ErrorMessage = errContent.Length > 120 ? errContent.Substring(0, 120) + "..." : errContent;
                }
            }
            else if (string.Equals(status, "DONE", StringComparison.OrdinalIgnoreCase))
            {
                // Successful execution of a subsequent step or planner response clears prior intermediate tool errors
                snapshot.HasError = false;
                snapshot.ErrorMessage = null;
            }

            // 1. Explicit user prompt (Never read internal reasoning)
            if (string.Equals(source, "USER_EXPLICIT", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(type, "USER_INPUT", StringComparison.OrdinalIgnoreCase))
            {
                if (root.TryGetProperty("content", out var promptProp))
                {
                    string prompt = promptProp.GetString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(prompt))
                    {
                        snapshot.LatestPrompt = prompt.Trim();
                        snapshot.IsWaitingForApproval = false;
                        snapshot.HasError = false;
                    }
                }
            }

            // 2. Model Tool Calls & Planner Steps
            if (string.Equals(source, "MODEL", StringComparison.OrdinalIgnoreCase))
            {
                if (root.TryGetProperty("tool_calls", out var toolsProp) && toolsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tool in toolsProp.EnumerateArray())
                    {
                        string toolName = tool.TryGetProperty("name", out var tName) ? tName.GetString() ?? "" : "";
                        var args = tool.TryGetProperty("args", out var aProp) ? aProp : default;

                        var item = new AgentActivityItem
                        {
                            Timestamp = DateTime.UtcNow,
                            ActivityType = "Tool",
                            Description = $"Executing {toolName}"
                        };

                        if (string.Equals(toolName, "run_command", StringComparison.OrdinalIgnoreCase))
                        {
                            item.ActivityType = "Command";
                            string cmd = args.TryGetProperty("CommandLine", out var cl) ? cl.GetString() ?? "" : "";
                            string summary = args.TryGetProperty("toolSummary", out var ts) ? ts.GetString() ?? "" : "";
                            item.Command = cmd;
                            item.Description = !string.IsNullOrWhiteSpace(summary) ? summary : (cmd.Length > 60 ? cmd.Substring(0, 60) + "..." : cmd);

                            if (!string.IsNullOrWhiteSpace(cmd) && !snapshot.ExecutedCommands.Contains(cmd))
                            {
                                snapshot.ExecutedCommands.Insert(0, cmd);
                                if (snapshot.ExecutedCommands.Count > 15) snapshot.ExecutedCommands.RemoveAt(15);
                            }
                        }
                        else if (string.Equals(toolName, "write_file", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(toolName, "edit_file", StringComparison.OrdinalIgnoreCase))
                        {
                            item.ActivityType = "File";
                            string path = args.TryGetProperty("Path", out var p) ? p.GetString() ?? "" : "";
                            item.TargetPath = path;
                            string fileName = Path.GetFileName(path);
                            item.Description = $"{toolName}: {fileName}";

                            if (!string.IsNullOrWhiteSpace(path) && !snapshot.ModifiedFiles.Contains(path))
                            {
                                snapshot.ModifiedFiles.Insert(0, path);
                                if (snapshot.ModifiedFiles.Count > 15) snapshot.ModifiedFiles.RemoveAt(15);
                            }
                        }
                        else if (string.Equals(toolName, "ask_question", StringComparison.OrdinalIgnoreCase))
                        {
                            item.ActivityType = "Question";
                            snapshot.IsWaitingForApproval = true;
                            item.Description = "Awaiting user input / question response";
                        }
                        else if (toolName.Contains("mcp") || toolName.Contains("subagent"))
                        {
                            item.ActivityType = "Tool";
                            item.Description = toolName;
                        }

                        snapshot.LatestActivity = item.Description;
                        snapshot.RecentActivities.Insert(0, item);
                        if (snapshot.RecentActivities.Count > 20)
                        {
                            snapshot.RecentActivities.RemoveAt(20);
                        }
                    }
                }

                // If content is present without tool calls (e.g. text progress update or question)
                if (root.TryGetProperty("content", out var contentProp) && contentProp.ValueKind == JsonValueKind.String)
                {
                    string content = contentProp.GetString() ?? "";
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        // Check if content indicates asking for user input
                        if (content.Contains("?") || content.Contains("approve") || content.Contains("proceed"))
                        {
                            // Soft hint
                        }
                    }
                }
            }
        }
        catch { }
    }

    public void ClearConversation(string conversationId)
    {
        _stateCache.TryRemove(conversationId, out _);
    }

    public void Dispose()
    {
        _disposed = true;
        _stateCache.Clear();
    }
}

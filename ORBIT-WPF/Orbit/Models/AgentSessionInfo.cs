using System.Windows.Media;

namespace Orbit.Models;

public class AgentSessionInfo
{
    public AntigravityAppType AppType { get; set; } = AntigravityAppType.Unknown;

    public string AppName => AppType switch
    {
        AntigravityAppType.Antigravity => "Antigravity",
        AntigravityAppType.AntigravityIde => "Antigravity IDE",
        _ => "Unknown Antigravity"
    };

    public ImageSource? AppIcon { get; set; }
    public string ConversationId { get; set; } = string.Empty;
    public string TaskTitle { get; set; } = string.Empty;
    public string WorkspaceName { get; set; } = string.Empty;
    public string WorkspacePath { get; set; } = string.Empty;
    public string LatestPrompt { get; set; } = string.Empty;
    public AgentState State { get; set; } = AgentState.Idle;
    public string StatusMessage { get; set; } = "Idle";
    public string LatestActivity { get; set; } = string.Empty;
    public int StepCount { get; set; }
    public DateTime LastModifiedTime { get; set; }
    public List<AgentActivityItem> RecentActivities { get; set; } = [];
    public List<string> ModifiedFiles { get; set; } = [];
    public List<string> ExecutedCommands { get; set; } = [];
    public bool IsProcessRunning { get; set; }
}


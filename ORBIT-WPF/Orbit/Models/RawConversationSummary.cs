namespace Orbit.Models;

public class RawConversationSummary
{
    public string ConversationId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Preview { get; set; } = string.Empty;
    public int StepCount { get; set; }
    public DateTime LastModifiedTime { get; set; }
    public string WorkspaceUrisJson { get; set; } = string.Empty;
    public List<string> WorkspacePaths { get; set; } = [];
    public string Status { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public bool NotFullyIdle { get; set; }
    public bool Killed { get; set; }
    public DateTime LastUserInputTime { get; set; }
}

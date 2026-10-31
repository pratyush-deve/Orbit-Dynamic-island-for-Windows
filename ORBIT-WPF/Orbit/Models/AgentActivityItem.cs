namespace Orbit.Models;

public class AgentActivityItem
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string ActivityType { get; set; } = "Activity"; // "Tool", "File", "Command", "Message", "Question", "Error"
    public string Description { get; set; } = string.Empty;
    public string? TargetPath { get; set; }
    public string? Command { get; set; }
    public bool IsError { get; set; }
}

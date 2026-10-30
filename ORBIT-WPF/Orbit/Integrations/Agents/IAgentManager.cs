namespace Orbit.Integrations.Agents;

public sealed record AgentInfo(string Id, string Name, string Provider, string Status, DateTime LastActiveUtc);

/// <summary>
/// Foundation interface for AI agent orchestrators (Google Antigravity, OpenAI Codex, Claude Code).
/// </summary>
public interface IAgentManager
{
    Task<IReadOnlyList<AgentInfo>> GetActiveAgentsAsync();
    Task<string> GetAgentStatusAsync(string agentId);
}

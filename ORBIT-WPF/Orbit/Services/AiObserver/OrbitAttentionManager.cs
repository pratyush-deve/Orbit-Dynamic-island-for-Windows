using Orbit.Core;
using Orbit.Models;

namespace Orbit.Services.AiObserver;

public interface IOrbitAttentionManager : IDisposable
{
    AgentState CurrentMascotState { get; }
    string CurrentAttentionMessage { get; }
    event Action<AgentState, string>? MascotStateChanged;
}

public sealed class OrbitAttentionManager : IOrbitAttentionManager
{
    private const string LogCategory = "ATTENTION_MGR";
    private readonly IAgentStateManager _stateManager;
    private readonly object _lock = new();

    private AgentState _currentState = AgentState.Idle;
    private string _currentMessage = string.Empty;
    private bool _disposed;

    public AgentState CurrentMascotState
    {
        get { lock (_lock) return _currentState; }
    }

    public string CurrentAttentionMessage
    {
        get { lock (_lock) return _currentMessage; }
    }

    public event Action<AgentState, string>? MascotStateChanged;

    public OrbitAttentionManager(IAgentStateManager stateManager)
    {
        _stateManager = stateManager;
        _stateManager.StateUpdated += OnAgentStateUpdated;

        // Initial evaluation
        EvaluateAggregateState(_stateManager.AntigravitySession, _stateManager.AntigravityIdeSession);
    }

    private void OnAgentStateUpdated(AgentSessionInfo ag, AgentSessionInfo ide)
    {
        EvaluateAggregateState(ag, ide);
    }

    private void EvaluateAggregateState(AgentSessionInfo ag, AgentSessionInfo ide)
    {
        if (_disposed) return;

        AgentState nextState = AgentState.Idle;
        string nextMessage = string.Empty;

        // Hierarchy of attention urgency:
        // 1. Attention (User input or approval requested)
        // 2. Error
        // 3. Working
        // 4. Completed
        // 5. Idle

        if (ag.State == AgentState.Attention)
        {
            nextState = AgentState.Attention;
            nextMessage = $"Antigravity requires approval or input for '{ag.TaskTitle}'";
        }
        else if (ide.State == AgentState.Attention)
        {
            nextState = AgentState.Attention;
            nextMessage = $"Antigravity IDE requires approval or input for '{ide.TaskTitle}'";
        }
        else if (ag.State == AgentState.Error)
        {
            nextState = AgentState.Error;
            nextMessage = $"Antigravity: {ag.StatusMessage}";
        }
        else if (ide.State == AgentState.Error)
        {
            nextState = AgentState.Error;
            nextMessage = $"Antigravity IDE: {ide.StatusMessage}";
        }
        else if (ag.State == AgentState.Working || ide.State == AgentState.Working)
        {
            nextState = AgentState.Working;
            if (ag.State == AgentState.Working && ide.State == AgentState.Working)
            {
                nextMessage = "Both Antigravity agents actively working";
            }
            else if (ag.State == AgentState.Working)
            {
                nextMessage = $"Antigravity: {ag.LatestActivity}";
            }
            else
            {
                nextMessage = $"Antigravity IDE: {ide.LatestActivity}";
            }
        }
        else if (ag.State == AgentState.Completed || ide.State == AgentState.Completed)
        {
            nextState = AgentState.Completed;
            var completedApp = ag.State == AgentState.Completed ? ag : ide;
            nextMessage = $"{completedApp.AppName}: Task completed";
        }
        else
        {
            nextState = AgentState.Idle;
            nextMessage = "Standing by";
        }

        bool changed = false;
        lock (_lock)
        {
            if (_currentState != nextState || _currentMessage != nextMessage)
            {
                _currentState = nextState;
                _currentMessage = nextMessage;
                changed = true;
            }
        }

        if (changed)
        {
            OrbitLogger.Info(LogCategory, $"Mascot aggregate state changed -> {nextState} ('{nextMessage}')");
            MascotStateChanged?.Invoke(nextState, nextMessage);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stateManager.StateUpdated -= OnAgentStateUpdated;
    }
}

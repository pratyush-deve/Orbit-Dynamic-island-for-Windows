using Orbit.Core;

namespace Orbit.Services;

public interface IWindowLifecycleService
{
    CompanionState CurrentState { get; }

    event Action<CompanionState>? StateChanged;
    event Action? ResetPositionRequested;
    event Action? ExitRequested;

    void SetState(CompanionState newState);
    void RequestExpand();
    void RequestCollapse();
    void Show();
    void Hide();
    void ToggleVisibility();
    void ResetPosition();
    void RequestExit();
}

/// <summary>
/// Orchestrates the companion state machine, visibility, and application lifecycle.
/// </summary>
public sealed class WindowLifecycleService : IWindowLifecycleService
{
    private const string LogCategory = "LIFECYCLE";

    public CompanionState CurrentState { get; private set; } = CompanionState.Collapsed;

    public event Action<CompanionState>? StateChanged;
    public event Action? ResetPositionRequested;
    public event Action? ExitRequested;

    public void SetState(CompanionState newState)
    {
        if (CurrentState == newState) return;

        CurrentState = newState;
        OrbitLogger.Info(LogCategory, $"Companion state transitioned to: {newState}");
        StateChanged?.Invoke(CurrentState);
    }

    public void RequestExpand()
    {
        // Only allow expand if currently Collapsed
        if (CurrentState == CompanionState.Collapsed)
        {
            SetState(CompanionState.Expanding);
        }
        else
        {
            OrbitLogger.Debug(LogCategory, $"Ignored RequestExpand while in state: {CurrentState}");
        }
    }

    public void RequestCollapse()
    {
        // Only allow collapse if currently Expanded
        if (CurrentState == CompanionState.Expanded)
        {
            SetState(CompanionState.Collapsing);
        }
        else
        {
            OrbitLogger.Debug(LogCategory, $"Ignored RequestCollapse while in state: {CurrentState}");
        }
    }

    public void Show()
    {
        if (CurrentState == CompanionState.Hidden)
        {
            SetState(CompanionState.Collapsed);
        }
    }

    public void Hide()
    {
        if (CurrentState != CompanionState.Hidden)
        {
            SetState(CompanionState.Hidden);
        }
    }

    public void ToggleVisibility()
    {
        if (CurrentState == CompanionState.Hidden)
        {
            Show();
        }
        else
        {
            Hide();
        }
    }

    public void ResetPosition()
    {
        OrbitLogger.Info(LogCategory, "ResetPosition requested.");
        ResetPositionRequested?.Invoke();
    }

    public void RequestExit()
    {
        OrbitLogger.Info(LogCategory, "Application Exit requested.");
        ExitRequested?.Invoke();
    }
}

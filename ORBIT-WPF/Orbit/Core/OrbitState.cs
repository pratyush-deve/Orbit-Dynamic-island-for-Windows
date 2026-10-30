namespace Orbit.Core;

/// <summary>
/// Explicit state machine for ORBIT 2.0 lifecycle and transitions.
/// </summary>
public enum CompanionState
{
    Collapsed,
    Expanding,
    Expanded,
    Collapsing,
    Hidden
}

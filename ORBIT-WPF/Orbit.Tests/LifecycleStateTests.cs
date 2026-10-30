using Orbit.Core;
using Orbit.Models;
using Orbit.Platform.Windows;
using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

public class LifecycleStateTests
{
    [Fact]
    public void LifecycleService_InitializesInCollapsedState()
    {
        var lifecycle = new WindowLifecycleService();

        Assert.Equal(CompanionState.Collapsed, lifecycle.CurrentState);
    }

    [Fact]
    public void RequestExpand_And_Collapse_TransitionsCorrectly()
    {
        var lifecycle = new WindowLifecycleService();

        var stateHistory = new List<CompanionState>();
        lifecycle.StateChanged += s => stateHistory.Add(s);

        // When Collapsed, RequestExpand transitions to Expanding
        lifecycle.RequestExpand();
        Assert.Equal(CompanionState.Expanding, lifecycle.CurrentState);

        // Duplicate RequestExpand is ignored while Expanding
        lifecycle.RequestExpand();
        Assert.Equal(CompanionState.Expanding, lifecycle.CurrentState);

        // Transition to Expanded
        lifecycle.SetState(CompanionState.Expanded);
        Assert.Equal(CompanionState.Expanded, lifecycle.CurrentState);

        // When Expanded, RequestCollapse transitions to Collapsing
        lifecycle.RequestCollapse();
        Assert.Equal(CompanionState.Collapsing, lifecycle.CurrentState);

        // Transition back to Collapsed
        lifecycle.SetState(CompanionState.Collapsed);
        Assert.Equal(CompanionState.Collapsed, lifecycle.CurrentState);

        Assert.Equal(4, stateHistory.Count);
        Assert.Equal(CompanionState.Expanding, stateHistory[0]);
        Assert.Equal(CompanionState.Expanded, stateHistory[1]);
        Assert.Equal(CompanionState.Collapsing, stateHistory[2]);
        Assert.Equal(CompanionState.Collapsed, stateHistory[3]);
    }

    [Fact]
    public void Hide_And_Show_TransitionsCorrectly()
    {
        var lifecycle = new WindowLifecycleService();

        lifecycle.Hide();
        Assert.Equal(CompanionState.Hidden, lifecycle.CurrentState);

        lifecycle.Show();
        Assert.Equal(CompanionState.Collapsed, lifecycle.CurrentState);

        lifecycle.ToggleVisibility();
        Assert.Equal(CompanionState.Hidden, lifecycle.CurrentState);

        lifecycle.ToggleVisibility();
        Assert.Equal(CompanionState.Collapsed, lifecycle.CurrentState);
    }
}

using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Orbit.Core;
using Orbit.UI.Mascot;
using Xunit;

namespace Orbit.Tests;

[Collection("UITests")]
public class HoverQuickActionsTests
{
    private void RunOnSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current == null)
                {
                    try { _ = new System.Windows.Application(); } catch { }
                }

                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception != null)
        {
            throw new Exception("STA test execution failed: " + exception.ToString());
        }
    }

    [Fact]
    public void EyeMascot_ContainsClosedSleepingEyes_AndAnimatesGracefully()
    {
        RunOnSta(() =>
        {
            var mascot = new EyeMascot();
            Assert.NotNull(mascot);

            // Verify LeftClosedEye and RightClosedEye exist in the visual tree
            var leftClosed = mascot.FindName("LeftClosedEye") as FrameworkElement;
            var rightClosed = mascot.FindName("RightClosedEye") as FrameworkElement;

            Assert.NotNull(leftClosed);
            Assert.NotNull(rightClosed);
            Assert.Equal(0.0, leftClosed.Opacity);
            Assert.Equal(0.0, rightClosed.Opacity);

            // Test GracefullyCloseEyes and RestoreNormalEyes
            mascot.GracefullyCloseEyes(TimeSpan.FromMilliseconds(50));
            mascot.RestoreNormalEyes(TimeSpan.Zero);
            Assert.Equal(0.0, leftClosed.Opacity);
            Assert.Equal(0.0, rightClosed.Opacity);
        });
    }

    [Fact]
    public void MainWindow_ContainsQuickActionsBar_WithFourButtons()
    {
        RunOnSta(() =>
        {
            var app = new OrbitApplication();
            var window = app.GetService<MainWindow>();
            Assert.NotNull(window);

            Assert.Equal(160.0, MainWindow.CollapsedWidth);
            Assert.Equal(38.0, MainWindow.CollapsedHeight);
            Assert.Equal(310.0, MainWindow.QuickActionsWidth);
            Assert.Equal(44.0, MainWindow.QuickActionsHeight);
            Assert.Equal(2.0, MainWindow.HoverActivationSeconds);
            Assert.Equal(450.0, MainWindow.CollapseDelayMilliseconds);

            var quickBar = window.FindName("QuickActionsBar") as Grid;
            Assert.NotNull(quickBar);
            Assert.Equal(Visibility.Collapsed, quickBar.Visibility);

            var mascotGrid = window.FindName("MascotContainerGrid") as Grid;
            Assert.NotNull(mascotGrid);
            Assert.Equal(Visibility.Visible, mascotGrid.Visibility);

            var btnFileDock = window.FindName("BtnQuickFileDock") as Button;
            var btnVoice = window.FindName("BtnQuickVoiceAssistant") as Button;
            var btnAnnotation = window.FindName("BtnQuickAnnotation") as Button;
            var btnFullExpand = window.FindName("BtnQuickFullExpand") as Button;

            Assert.NotNull(btnFileDock);
            Assert.NotNull(btnVoice);
            Assert.NotNull(btnAnnotation);
            Assert.NotNull(btnFullExpand);

            Assert.Equal("File Dock", btnFileDock.ToolTip);
            Assert.Equal("Voice Assistant", btnVoice.ToolTip);
            Assert.Equal("Annotation", btnAnnotation.ToolTip);
            Assert.Equal("Full Expand", btnFullExpand.ToolTip);
        });
    }

    [Fact]
    public void HoverRecognition_OnlyAllowedInIdleState()
    {
        RunOnSta(() =>
        {
            var app = new OrbitApplication();
            var window = app.GetService<MainWindow>();
            Assert.NotNull(window);

            var timerField = typeof(MainWindow).GetField("_hoverTimer", BindingFlags.NonPublic | BindingFlags.Instance);
            var stateField = typeof(MainWindow).GetField("_currentAgentState", BindingFlags.NonPublic | BindingFlags.Instance);
            var startMethod = typeof(MainWindow).GetMethod("StartHoverTimer", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.NotNull(timerField);
            Assert.NotNull(stateField);
            Assert.NotNull(startMethod);

            var timer = timerField.GetValue(window) as System.Windows.Threading.DispatcherTimer;
            Assert.NotNull(timer);

            // In Working state -> hover timer must NOT start
            stateField.SetValue(window, Orbit.Models.AgentState.Working);
            startMethod.Invoke(window, null);
            Assert.False(timer.IsEnabled);

            // In Error state -> hover timer must NOT start
            stateField.SetValue(window, Orbit.Models.AgentState.Error);
            startMethod.Invoke(window, null);
            Assert.False(timer.IsEnabled);

            // In Completed/Done state -> hover timer must NOT start
            stateField.SetValue(window, Orbit.Models.AgentState.Completed);
            startMethod.Invoke(window, null);
            Assert.False(timer.IsEnabled);

            // In Attention state -> hover timer must NOT start
            stateField.SetValue(window, Orbit.Models.AgentState.Attention);
            startMethod.Invoke(window, null);
            Assert.False(timer.IsEnabled);

            // In Idle state -> hover timer MUST start with 2.0s interval
            stateField.SetValue(window, Orbit.Models.AgentState.Idle);
            startMethod.Invoke(window, null);
            Assert.True(timer.IsEnabled);
            Assert.Equal(TimeSpan.FromSeconds(2.0), timer.Interval);

            // Clean up
            timer.Stop();
        });
    }

    [Fact]
    public void WorkingState_NoWhitePupilArtifacts_OnRestoreOrCollapse()
    {
        RunOnSta(() =>
        {
            var mascot = new EyeMascot();
            Assert.NotNull(mascot);

            var leftPill = mascot.FindName("LeftEyePill") as FrameworkElement;
            var rightPill = mascot.FindName("RightEyePill") as FrameworkElement;
            var leftWorking = mascot.FindName("LeftWorkingEye") as FrameworkElement;
            var rightWorking = mascot.FindName("RightWorkingEye") as FrameworkElement;

            Assert.NotNull(leftPill);
            Assert.NotNull(rightPill);
            Assert.NotNull(leftWorking);
            Assert.NotNull(rightWorking);

            // Set state to Working: white capsule eyes must be hidden immediately
            mascot.SetAiState(Orbit.Models.AgentState.Working, "Thinking...");
            Assert.Equal(0.0, leftPill.Opacity);
            Assert.Equal(0.0, rightPill.Opacity);

            // Even if RestoreNormalEyes is called, white capsule eyes must NOT appear while in Working state
            mascot.RestoreNormalEyes(TimeSpan.Zero);
            Assert.Equal(0.0, leftPill.Opacity);
            Assert.Equal(0.0, rightPill.Opacity);
            Assert.Equal(1.0, leftWorking.Opacity);
            Assert.Equal(1.0, rightWorking.Opacity);

            mascot.RestoreNormalEyes(TimeSpan.FromMilliseconds(150));
            Assert.Equal(0.0, leftPill.Opacity);
            Assert.Equal(0.0, rightPill.Opacity);
            Assert.Equal(1.0, leftWorking.Opacity);
            Assert.Equal(1.0, rightWorking.Opacity);
        });
    }

    [Fact]
    public void QuickActions_SchedulesGracefulCollapseDelay_OnMouseLeave()
    {
        RunOnSta(() =>
        {
            var app = new OrbitApplication();
            var window = app.GetService<MainWindow>();
            Assert.NotNull(window);

            var delayTimerField = typeof(MainWindow).GetField("_collapseDelayTimer", BindingFlags.NonPublic | BindingFlags.Instance);
            var isQuickActionsOpenField = typeof(MainWindow).GetField("_isQuickActionsOpen", BindingFlags.NonPublic | BindingFlags.Instance);
            var scheduleMethod = typeof(MainWindow).GetMethod("ScheduleHoverCollapse", BindingFlags.NonPublic | BindingFlags.Instance);
            var cancelMethod = typeof(MainWindow).GetMethod("CancelCollapseDelay", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.NotNull(delayTimerField);
            Assert.NotNull(isQuickActionsOpenField);
            Assert.NotNull(scheduleMethod);
            Assert.NotNull(cancelMethod);

            var timer = delayTimerField.GetValue(window) as System.Windows.Threading.DispatcherTimer;
            Assert.NotNull(timer);

            // While quick actions is open, calling schedule starts the delay timer
            isQuickActionsOpenField.SetValue(window, true);
            scheduleMethod.Invoke(window, null);
            Assert.True(timer.IsEnabled);
            Assert.Equal(TimeSpan.FromMilliseconds(MainWindow.CollapseDelayMilliseconds), timer.Interval);

            // Returning to quick actions cancels the delay timer
            cancelMethod.Invoke(window, null);
            Assert.False(timer.IsEnabled);
        });
    }

    [Fact]
    public void EyeMascot_LeftEyeContainer_HasClickHitboxAndEvent()
    {
        RunOnSta(() =>
        {
            var mascot = new EyeMascot();
            Assert.NotNull(mascot);

            var leftContainer = mascot.FindName("LeftEyeContainer") as Grid;
            Assert.NotNull(leftContainer);
            Assert.NotNull(leftContainer.Background); // Background must not be null for hit-testing
            Assert.Equal(System.Windows.Input.Cursors.Hand, leftContainer.Cursor);

            bool eventFired = false;
            mascot.LeftEyeMouseDown += (s, e) => { eventFired = true; };

            var onLeftEyeMouseDown = typeof(EyeMascot).GetMethod("OnLeftEyeMouseDown", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(onLeftEyeMouseDown);

            var mouseDevice = System.Windows.Input.Mouse.PrimaryDevice;
            var eventArgs = new System.Windows.Input.MouseButtonEventArgs(mouseDevice, 0, System.Windows.Input.MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonDownEvent
            };

            onLeftEyeMouseDown.Invoke(mascot, new object[] { leftContainer, eventArgs });
            Assert.True(eventFired);
        });
    }

    [Fact]
    public void LeftEyeClick_OpensHoverTray_OnlyInIdleCollapsedState_AndMarksHandled()
    {
        RunOnSta(() =>
        {
            var app = new OrbitApplication();
            var window = app.GetService<MainWindow>();
            Assert.NotNull(window);

            var handler = typeof(MainWindow).GetMethod("OnPillMascotLeftEyeMouseDown", BindingFlags.NonPublic | BindingFlags.Instance);
            var isExpandingField = typeof(MainWindow).GetField("_isHoverExpanding", BindingFlags.NonPublic | BindingFlags.Instance);
            var stateField = typeof(MainWindow).GetField("_currentAgentState", BindingFlags.NonPublic | BindingFlags.Instance);
            var lifecycleService = app.GetService<Orbit.Services.IWindowLifecycleService>();

            Assert.NotNull(handler);
            Assert.NotNull(isExpandingField);
            Assert.NotNull(stateField);
            Assert.NotNull(lifecycleService);

            // 1. In Idle Collapsed state -> Click left eye should mark handled and trigger expansion
            stateField.SetValue(window, Orbit.Models.AgentState.Idle);
            lifecycleService.RequestCollapse(); // Ensure collapsed

            var mouseDevice = System.Windows.Input.Mouse.PrimaryDevice;
            var eventArgs = new System.Windows.Input.MouseButtonEventArgs(mouseDevice, 0, System.Windows.Input.MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonDownEvent
            };

            handler.Invoke(window, new object[] { window.PillMascot, eventArgs });
            Assert.True(eventArgs.Handled, "Left eye click must mark e.Handled = true so it never bubbles to normal expansion");
            Assert.True((bool)isExpandingField.GetValue(window)!, "Left eye click must trigger hover expansion in idle collapsed state");

            // Reset expanding flag
            isExpandingField.SetValue(window, false);

            // 2. In Working state -> Click left eye must NOT trigger expansion
            stateField.SetValue(window, Orbit.Models.AgentState.Working);
            var workingArgs = new System.Windows.Input.MouseButtonEventArgs(mouseDevice, 0, System.Windows.Input.MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonDownEvent
            };
            handler.Invoke(window, new object[] { window.PillMascot, workingArgs });
            Assert.True(workingArgs.Handled, "Must still mark handled to prevent bubbling");
            Assert.False((bool)isExpandingField.GetValue(window)!, "Left eye click must NOT expand tray during Working state");

            // 3. In Expanded state -> Click left eye must NOT trigger hover tray
            stateField.SetValue(window, Orbit.Models.AgentState.Idle);
            lifecycleService.RequestExpand(); // Change to expanded
            var expandedArgs = new System.Windows.Input.MouseButtonEventArgs(mouseDevice, 0, System.Windows.Input.MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonDownEvent
            };
            handler.Invoke(window, new object[] { window.PillMascot, expandedArgs });
            Assert.False((bool)isExpandingField.GetValue(window)!, "Left eye click must NOT trigger hover tray when fully expanded");
        });
    }

    [Fact]
    public void MascotMouseEnter_DoesNotStartHoverTimer()
    {
        RunOnSta(() =>
        {
            var app = new OrbitApplication();
            var window = app.GetService<MainWindow>();
            Assert.NotNull(window);

            var timerField = typeof(MainWindow).GetField("_hoverTimer", BindingFlags.NonPublic | BindingFlags.Instance);
            var mouseEnterMethod = typeof(MainWindow).GetMethod("OnMascotMouseEnter", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.NotNull(timerField);
            Assert.NotNull(mouseEnterMethod);

            var timer = timerField.GetValue(window) as System.Windows.Threading.DispatcherTimer;
            Assert.NotNull(timer);

            // Trigger mouse enter
            var mouseDevice = System.Windows.Input.Mouse.PrimaryDevice;
            var args = new System.Windows.Input.MouseEventArgs(mouseDevice, 0);
            mouseEnterMethod.Invoke(window, new object[] { window.MascotContainerGrid, args });

            // Hover timer must remain NOT started
            Assert.False(timer.IsEnabled, "Hovering over mascot must not start the hover timer");
        });
    }
}


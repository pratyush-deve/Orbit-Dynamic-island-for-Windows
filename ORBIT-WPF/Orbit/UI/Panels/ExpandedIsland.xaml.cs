using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Orbit.Core;
using Orbit.Integrations.Desktop;
using Orbit.UI.Mascot;

namespace Orbit.UI.Panels;

/// <summary>
/// Interaction logic for ExpandedIsland.xaml.
/// Controls island actions, body content transitions, Active Apps management view,
/// top-right hide command, task manager shortcut, and card navigation.
/// </summary>
public partial class ExpandedIsland : UserControl
{
    private const string LogCategory = "EXPANDED_ISLAND";

    public event Action? CollapseRequested;
    public event Action? HideRequested;
    public event Action<double>? DesiredHeightChanged;

    public EyeMascot Mascot => IslandMascot;
    public bool IsInActiveAppsView { get; private set; }

    private bool _glowEnabled = true;
    private DateTime _lastTaskManagerLaunch = DateTime.MinValue;

    public ExpandedIsland()
    {
        InitializeComponent();
        ActiveAppsPanel.DesiredHeightChanged += (height) =>
        {
            if (IsInActiveAppsView)
            {
                DesiredHeightChanged?.Invoke(height);
            }
        };

        ActiveAppsPanel.DismissAndCollapseRequested += OnActiveAppsDismissAndCollapseRequested;

        CardActiveApps.Click += OnCardActiveAppsClicked;
        CardActiveApps.MouseLeftButtonUp += (s, e) => OnCardActiveAppsClicked(s, e);
    }

    public void Initialize(IWindowManager windowManager)
    {
        ActiveAppsPanel.Initialize(windowManager);
    }

    public void SetGlowStatus(bool enabled)
    {
        _glowEnabled = enabled;
        IslandMascot.SetGlowEnabled(enabled);
    }

    public void SetBodyOpacity(double opacity)
    {
        IslandBodyContent.Opacity = opacity;
    }

    public void AnimateBodyOpacity(double targetOpacity, TimeSpan duration)
    {
        var anim = new DoubleAnimation(targetOpacity, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        IslandBodyContent.BeginAnimation(OpacityProperty, anim);
    }

    public void ResetToHomeView()
    {
        IsInActiveAppsView = false;
        ActiveAppsContainer.BeginAnimation(OpacityProperty, null);
        ActiveAppsContainer.Visibility = Visibility.Collapsed;
        ActiveAppsContainer.Opacity = 0.0;

        HomeContentView.BeginAnimation(OpacityProperty, null);
        HomeContentView.Visibility = Visibility.Visible;
        HomeContentView.Opacity = 1.0;

        DesiredHeightChanged?.Invoke(240.0);
    }

    private void OnActiveAppsDismissAndCollapseRequested()
    {
        OrbitLogger.Info(LogCategory, "Application action handled. Navigating back to Home and requesting collapse.");
        ResetToHomeView();
        CollapseRequested?.Invoke();
    }

    private void OnCardActiveAppsClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (IsInActiveAppsView) return;
        OrbitLogger.Info(LogCategory, "APPS card clicked. Switching to Active Apps view.");

        IsInActiveAppsView = true;

        var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fadeOut.Completed += (s, ev) =>
        {
            HomeContentView.BeginAnimation(OpacityProperty, null);
            HomeContentView.Visibility = Visibility.Collapsed;

            ActiveAppsContainer.Visibility = Visibility.Visible;
            _ = ActiveAppsPanel.LoadWindowsOnOpenAsync();

            var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            fadeIn.Completed += (s2, ev2) =>
            {
                ActiveAppsContainer.BeginAnimation(OpacityProperty, null);
                ActiveAppsContainer.Opacity = 1.0;
            };
            ActiveAppsContainer.BeginAnimation(OpacityProperty, fadeIn);
        };
        HomeContentView.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void OnActiveAppsBackRequested()
    {
        OrbitLogger.Info(LogCategory, "Back clicked in Active Apps view. Returning to Home.");

        IsInActiveAppsView = false;

        // Reset to standard expanded height (240 DIPs)
        DesiredHeightChanged?.Invoke(240.0);

        var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fadeOut.Completed += (s, ev) =>
        {
            ActiveAppsContainer.BeginAnimation(OpacityProperty, null);
            ActiveAppsContainer.Visibility = Visibility.Collapsed;
            ActiveAppsContainer.Opacity = 0.0;

            HomeContentView.Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            fadeIn.Completed += (s2, ev2) =>
            {
                HomeContentView.BeginAnimation(OpacityProperty, null);
                HomeContentView.Opacity = 1.0;
            };
            HomeContentView.BeginAnimation(OpacityProperty, fadeIn);
        };
        ActiveAppsContainer.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void OnMascotClicked(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            CollapseRequested?.Invoke();
        }
    }

    private void OnHideButtonClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        HideRequested?.Invoke();
    }

    private void OnTaskManagerClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;

        // Prevent duplicate launch attempts from rapid repeated clicks (1.5-second cooldown)
        if (DateTime.UtcNow - _lastTaskManagerLaunch < TimeSpan.FromSeconds(1.5))
        {
            OrbitLogger.Debug(LogCategory, "Ignored duplicate Task Manager launch request (cooldown active).");
            return;
        }

        _lastTaskManagerLaunch = DateTime.UtcNow;

        try
        {
            OrbitLogger.Info(LogCategory, "Launching native Windows Task Manager...");
            var startInfo = new ProcessStartInfo
            {
                FileName = "taskmgr.exe",
                UseShellExecute = true
            };
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, "Failed to launch Windows Task Manager.", ex);
        }
    }
}

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Orbit.Core;
using Orbit.Integrations.Desktop;
using Orbit.Services;
using Orbit.Services.AiObserver;
using Orbit.Services.DevWorkspace;
using Orbit.Services.Hubs;
using Orbit.Services.SystemControls;
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
    public bool IsInControlsView { get; private set; }
    public bool IsInDevModeView { get; private set; }
    public bool IsInManageAppsView { get; private set; }
    public bool IsInAiMonitorView { get; private set; }
    public bool IsInSocialHubView { get; private set; }
    public bool IsInManageSocialAppsView { get; private set; }
    public bool IsInWorkHubView { get; private set; }
    public bool IsInManageWorkAppsView { get; private set; }

    private IAgentStateManager? _agentStateManager;
    private ISocialHubService? _socialHubService;
    private IWorkHubService? _workHubService;
    private bool _glowEnabled = true;
    private DateTime _lastTaskManagerLaunch = DateTime.MinValue;
    private DateTime _lastFileExplorerLaunch = DateTime.MinValue;

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

        DevModePanel.DesiredHeightChanged += (height) =>
        {
            if (IsInDevModeView)
            {
                DesiredHeightChanged?.Invoke(height);
            }
        };

        ManageAppsPanel.DesiredHeightChanged += (height) =>
        {
            if (IsInManageAppsView)
            {
                DesiredHeightChanged?.Invoke(height);
            }
        };

        AiMonitorPanel.DesiredHeightChanged += (height) =>
        {
            if (IsInAiMonitorView)
            {
                DesiredHeightChanged?.Invoke(height);
            }
        };

        SocialHubPanel.DesiredHeightChanged += (height) =>
        {
            if (IsInSocialHubView)
            {
                DesiredHeightChanged?.Invoke(height);
            }
        };

        WorkHubPanel.DesiredHeightChanged += (height) =>
        {
            if (IsInWorkHubView)
            {
                DesiredHeightChanged?.Invoke(height);
            }
        };

        ManageSocialAppsPanel.DesiredHeightChanged += (height) =>
        {
            if (IsInManageSocialAppsView)
            {
                DesiredHeightChanged?.Invoke(height);
            }
        };

        ManageWorkAppsPanel.DesiredHeightChanged += (height) =>
        {
            if (IsInManageWorkAppsView)
            {
                DesiredHeightChanged?.Invoke(height);
            }
        };

        ActiveAppsPanel.DismissAndCollapseRequested += OnActiveAppsDismissAndCollapseRequested;

        CardActiveApps.Click += OnCardActiveAppsClicked;
        CardActiveApps.MouseLeftButtonUp += (s, e) => OnCardActiveAppsClicked(s, e);

        CardControls.Click += OnCardControlsClicked;
        CardControls.MouseLeftButtonUp += (s, e) => OnCardControlsClicked(s, e);

        CardDevMode.Click += OnCardDevModeClicked;
        CardDevMode.MouseLeftButtonUp += (s, e) => OnCardDevModeClicked(s, e);

        CardAiAgent.Click += OnCardAiAgentClicked;
        CardAiAgent.MouseLeftButtonUp += (s, e) => OnCardAiAgentClicked(s, e);

        CardSocial.Click += OnCardSocialClicked;
        CardSocial.MouseLeftButtonUp += (s, e) => OnCardSocialClicked(s, e);

        CardWork.Click += OnCardWorkClicked;
        CardWork.MouseLeftButtonUp += (s, e) => OnCardWorkClicked(s, e);
    }

    public void Initialize(
        IWindowManager windowManager,
        ISystemControlsService? controlsService = null,
        IDevWorkspaceService? workspaceService = null,
        IAgentStateManager? agentStateManager = null,
        IOrbitAttentionManager? attentionManager = null,
        ISocialHubService? socialHubService = null,
        IWorkHubService? workHubService = null)
    {
        ActiveAppsPanel.Initialize(windowManager);
        if (controlsService != null)
        {
            ControlsPanel.Initialize(controlsService);
        }
        if (workspaceService != null)
        {
            DevModePanel.Initialize(workspaceService);
            ManageAppsPanel.Initialize(workspaceService);
        }
        if (agentStateManager != null)
        {
            _agentStateManager = agentStateManager;
            AiMonitorPanel.Initialize(agentStateManager);
        }
        if (socialHubService != null)
        {
            _socialHubService = socialHubService;
            SocialHubPanel.Initialize(socialHubService);
            ManageSocialAppsPanel.Initialize(socialHubService, "MANAGE SOCIAL APPS", "SOCIAL");
        }
        if (workHubService != null)
        {
            _workHubService = workHubService;
            WorkHubPanel.Initialize(workHubService);
            ManageWorkAppsPanel.Initialize(workHubService, "MANAGE WORK APPS", "WORK");
        }
        if (attentionManager != null)
        {
            attentionManager.MascotStateChanged += (state, msg) =>
            {
                Dispatcher.Invoke(() => IslandMascot.SetAiState(state, msg));
            };
        }
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
        IsInControlsView = false;
        IsInDevModeView = false;
        IsInManageAppsView = false;
        IsInAiMonitorView = false;
        IsInSocialHubView = false;
        IsInManageSocialAppsView = false;
        IsInWorkHubView = false;
        IsInManageWorkAppsView = false;

        ActiveAppsContainer.BeginAnimation(OpacityProperty, null);
        ActiveAppsContainer.Visibility = Visibility.Collapsed;
        ActiveAppsContainer.Opacity = 0.0;

        ControlsContainer.BeginAnimation(OpacityProperty, null);
        ControlsContainer.Visibility = Visibility.Collapsed;
        ControlsContainer.Opacity = 0.0;

        DevModeContainer.BeginAnimation(OpacityProperty, null);
        DevModeContainer.Visibility = Visibility.Collapsed;
        DevModeContainer.Opacity = 0.0;

        ManageAppsContainer.BeginAnimation(OpacityProperty, null);
        ManageAppsContainer.Visibility = Visibility.Collapsed;
        ManageAppsContainer.Opacity = 0.0;

        AiMonitorContainer.BeginAnimation(OpacityProperty, null);
        AiMonitorContainer.Visibility = Visibility.Collapsed;
        AiMonitorContainer.Opacity = 0.0;

        SocialHubContainer.BeginAnimation(OpacityProperty, null);
        SocialHubContainer.Visibility = Visibility.Collapsed;
        SocialHubContainer.Opacity = 0.0;

        ManageSocialAppsContainer.BeginAnimation(OpacityProperty, null);
        ManageSocialAppsContainer.Visibility = Visibility.Collapsed;
        ManageSocialAppsContainer.Opacity = 0.0;

        WorkHubContainer.BeginAnimation(OpacityProperty, null);
        WorkHubContainer.Visibility = Visibility.Collapsed;
        WorkHubContainer.Opacity = 0.0;

        ManageWorkAppsContainer.BeginAnimation(OpacityProperty, null);
        ManageWorkAppsContainer.Visibility = Visibility.Collapsed;
        ManageWorkAppsContainer.Opacity = 0.0;

        HomeContentView.BeginAnimation(OpacityProperty, null);
        HomeContentView.Visibility = Visibility.Visible;
        HomeContentView.Opacity = 1.0;

        HomeCardsScrollViewer?.ScrollToHorizontalOffset(0);
        UpdateHomeCardsScrollProgress();

        DesiredHeightChanged?.Invoke(CalculateHomeDesiredHeight());
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

        // Reset to content-driven home expanded height
        DesiredHeightChanged?.Invoke(CalculateHomeDesiredHeight());

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

    private void OnCardControlsClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (IsInControlsView || IsInActiveAppsView) return;
        OrbitLogger.Info(LogCategory, "CTRL card clicked. Switching to System Controls view.");

        IsInControlsView = true;

        var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fadeOut.Completed += (s, ev) =>
        {
            HomeContentView.BeginAnimation(OpacityProperty, null);
            HomeContentView.Visibility = Visibility.Collapsed;

            ControlsContainer.Visibility = Visibility.Visible;
            _ = ControlsPanel.RefreshStateAsync();

            var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            fadeIn.Completed += (s2, ev2) =>
            {
                ControlsContainer.BeginAnimation(OpacityProperty, null);
                ControlsContainer.Opacity = 1.0;
            };
            ControlsContainer.BeginAnimation(OpacityProperty, fadeIn);
        };
        HomeContentView.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void OnControlsBackRequested()
    {
        OrbitLogger.Info(LogCategory, "Back clicked in System Controls view. Returning to Home.");

        IsInControlsView = false;
        DesiredHeightChanged?.Invoke(CalculateHomeDesiredHeight());

        var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fadeOut.Completed += (s, ev) =>
        {
            ControlsContainer.BeginAnimation(OpacityProperty, null);
            ControlsContainer.Visibility = Visibility.Collapsed;
            ControlsContainer.Opacity = 0.0;

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
        ControlsContainer.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void OnCardDevModeClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (IsInDevModeView || IsInControlsView || IsInActiveAppsView) return;
        OrbitLogger.Info(LogCategory, "DEV card clicked. Switching to Dev Mode view.");

        IsInDevModeView = true;
        DesiredHeightChanged?.Invoke(DevModePanel.CalculateDesiredHeight());

        var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fadeOut.Completed += (s, ev) =>
        {
            HomeContentView.BeginAnimation(OpacityProperty, null);
            HomeContentView.Visibility = Visibility.Collapsed;

            DevModeContainer.Visibility = Visibility.Visible;
            DevModePanel.UpdateWorkspaceDisplay();

            var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            fadeIn.Completed += (s2, ev2) =>
            {
                DevModeContainer.BeginAnimation(OpacityProperty, null);
                DevModeContainer.Opacity = 1.0;
            };
            DevModeContainer.BeginAnimation(OpacityProperty, fadeIn);
        };
        HomeContentView.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void OnDevModeBackRequested()
    {
        OrbitLogger.Info(LogCategory, "Back clicked in Dev Mode view. Returning to Home.");

        IsInDevModeView = false;
        DesiredHeightChanged?.Invoke(CalculateHomeDesiredHeight());

        var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fadeOut.Completed += (s, ev) =>
        {
            DevModeContainer.BeginAnimation(OpacityProperty, null);
            DevModeContainer.Visibility = Visibility.Collapsed;
            DevModeContainer.Opacity = 0.0;

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
        DevModeContainer.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void OnDevModeManageAppsRequested()
    {
        OrbitLogger.Info(LogCategory, "Manage Apps clicked in Dev Mode view. Switching to Manage Apps view.");

        IsInDevModeView = false;
        IsInManageAppsView = true;
        DesiredHeightChanged?.Invoke(ManageAppsPanel.GetDesiredHeight());

        var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fadeOut.Completed += (s, ev) =>
        {
            DevModeContainer.BeginAnimation(OpacityProperty, null);
            DevModeContainer.Visibility = Visibility.Collapsed;
            DevModeContainer.Opacity = 0.0;

            ManageAppsContainer.Visibility = Visibility.Visible;
            ManageAppsPanel.UpdateAppsList();

            var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            fadeIn.Completed += (s2, ev2) =>
            {
                ManageAppsContainer.BeginAnimation(OpacityProperty, null);
                ManageAppsContainer.Opacity = 1.0;
            };
            ManageAppsContainer.BeginAnimation(OpacityProperty, fadeIn);
        };
        DevModeContainer.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void OnManageAppsBackRequested()
    {
        OrbitLogger.Info(LogCategory, "Back clicked in Manage Apps view. Returning to Dev Mode view.");

        IsInManageAppsView = false;
        IsInDevModeView = true;
        DesiredHeightChanged?.Invoke(DevModePanel.CalculateDesiredHeight());

        var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fadeOut.Completed += (s, ev) =>
        {
            ManageAppsContainer.BeginAnimation(OpacityProperty, null);
            ManageAppsContainer.Visibility = Visibility.Collapsed;
            ManageAppsContainer.Opacity = 0.0;

            DevModeContainer.Visibility = Visibility.Visible;
            DevModePanel.UpdateWorkspaceDisplay();

            var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            fadeIn.Completed += (s2, ev2) =>
            {
                DevModeContainer.BeginAnimation(OpacityProperty, null);
                DevModeContainer.Opacity = 1.0;
            };
            DevModeContainer.BeginAnimation(OpacityProperty, fadeIn);
        };
        ManageAppsContainer.BeginAnimation(OpacityProperty, fadeOut);
    }

    public void NavigateToAiMonitor()
    {
        if (IsInAiMonitorView) return;
        ResetToHomeView();
        OnCardAiAgentClicked(this, new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
    }

    private void OnCardAiAgentClicked(object sender, RoutedEventArgs e)
    {
        if (e?.RoutedEvent != null)
        {
            e.Handled = true;
        }
        if (IsInAiMonitorView || IsInDevModeView || IsInControlsView || IsInActiveAppsView) return;
        OrbitLogger.Info(LogCategory, "AI card clicked. Switching to AI Agent Monitor view.");

        IsInAiMonitorView = true;
        _agentStateManager?.Refresh();
        DesiredHeightChanged?.Invoke(AiMonitorPanel.GetDesiredHeight());

        var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fadeOut.Completed += (s, ev) =>
        {
            HomeContentView.BeginAnimation(OpacityProperty, null);
            HomeContentView.Visibility = Visibility.Collapsed;

            AiMonitorContainer.Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            fadeIn.Completed += (s2, ev2) =>
            {
                AiMonitorContainer.BeginAnimation(OpacityProperty, null);
                AiMonitorContainer.Opacity = 1.0;
            };
            AiMonitorContainer.BeginAnimation(OpacityProperty, fadeIn);
        };
        HomeContentView.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void OnAiMonitorBackRequested()
    {
        OrbitLogger.Info(LogCategory, "Back clicked in AI Agent Monitor view. Returning to Home.");

        IsInAiMonitorView = false;
        DesiredHeightChanged?.Invoke(CalculateHomeDesiredHeight());

        var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fadeOut.Completed += (s, ev) =>
        {
            AiMonitorContainer.BeginAnimation(OpacityProperty, null);
            AiMonitorContainer.Visibility = Visibility.Collapsed;
            AiMonitorContainer.Opacity = 0.0;

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
        AiMonitorContainer.BeginAnimation(OpacityProperty, fadeOut);
    }

    /// <summary>
    /// Calculates the dynamic content-driven height required for the Home view,
    /// measuring the actual rendered visual tree (mascot, branding, cards carousel, framing).
    /// </summary>
    public double CalculateHomeDesiredHeight()
    {
        try
        {
            var previousVisibility = HomeContentView.Visibility;
            HomeContentView.Visibility = Visibility.Visible;

            // Measure RootIsland against the standard expanded width (440 DIPs) with unconstrained vertical space
            RootIsland.Measure(new System.Windows.Size(MainWindow.ExpandedWidth, double.PositiveInfinity));
            double measuredHeight = RootIsland.DesiredSize.Height;

            HomeContentView.Visibility = previousVisibility;

            if (measuredHeight > 150)
            {
                // Round up with a 4-DIP subpixel breathing buffer
                return Math.Max(264.0, Math.Ceiling(measuredHeight + 4.0));
            }
        }
        catch (Exception ex)
        {
            OrbitLogger.Warn(LogCategory, $"Failed to measure home content height: {ex.Message}");
        }

        // Reliable calculated fallback based on measured visual hierarchy:
        // Framing & margins (40) + Mascot (57) + Branding (54) + Card Row (106) + Padding = 266 DIPs
        return 266.0;
    }

    private void OnHomeCardsScrollViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer sv && sv.ScrollableWidth > 0)
        {
            // Standard vertical mouse wheel delta is typically +/-120 per notch.
            // Scale by 0.75 (~90 DIPs per notch) to scroll sideways by approximately one card width.
            double scrollDelta = -e.Delta * 0.75;
            double targetOffset = Math.Clamp(sv.HorizontalOffset + scrollDelta, 0, sv.ScrollableWidth);
            sv.ScrollToHorizontalOffset(targetOffset);
            e.Handled = true;
        }
    }

    private void OnHomeCardsScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        UpdateHomeCardsScrollProgress();
    }

    private void UpdateHomeCardsScrollProgress()
    {
        if (HomeCardsScrollViewer == null || HomeCardsScrollTrack == null || HomeCardsScrollThumb == null || HomeCardsScrollThumbTransform == null) return;

        double scrollable = HomeCardsScrollViewer.ScrollableWidth;
        double extent = HomeCardsScrollViewer.ExtentWidth;
        double viewport = HomeCardsScrollViewer.ViewportWidth;

        if (scrollable <= 0 || extent <= viewport)
        {
            HomeCardsScrollTrack.Visibility = Visibility.Collapsed;
            return;
        }

        HomeCardsScrollTrack.Visibility = Visibility.Visible;

        double trackWidth = HomeCardsScrollTrack.ActualWidth > 0 ? HomeCardsScrollTrack.ActualWidth : 120.0;
        double viewportRatio = Math.Clamp(viewport / extent, 0.15, 0.85);
        double thumbWidth = Math.Max(24.0, trackWidth * viewportRatio);
        double scrollRatio = Math.Clamp(HomeCardsScrollViewer.HorizontalOffset / scrollable, 0.0, 1.0);
        double thumbOffset = scrollRatio * (trackWidth - thumbWidth);

        HomeCardsScrollThumb.Width = thumbWidth;
        HomeCardsScrollThumbTransform.X = thumbOffset;
    }

    private void OnCardSocialClicked(object sender, RoutedEventArgs e)
    {
        if (e?.RoutedEvent != null)
        {
            e.Handled = true;
        }
        if (IsInSocialHubView) return;
        OrbitLogger.Info(LogCategory, "SOCIAL card clicked. Switching to Social Hub view.");

        IsInSocialHubView = true;
        SocialHubPanel.UpdateAppsDisplay();
        DesiredHeightChanged?.Invoke(SocialHubPanel.CalculateDesiredHeight());

        var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fadeOut.Completed += (s, ev) =>
        {
            HomeContentView.BeginAnimation(OpacityProperty, null);
            HomeContentView.Visibility = Visibility.Collapsed;

            SocialHubContainer.Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            fadeIn.Completed += (s2, ev2) =>
            {
                SocialHubContainer.BeginAnimation(OpacityProperty, null);
                SocialHubContainer.Opacity = 1.0;
            };
            SocialHubContainer.BeginAnimation(OpacityProperty, fadeIn);
        };
        HomeContentView.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void OnSocialHubBackRequested()
    {
        OrbitLogger.Info(LogCategory, "Back clicked in Social Hub view. Returning to Home.");

        IsInSocialHubView = false;
        DesiredHeightChanged?.Invoke(CalculateHomeDesiredHeight());

        var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fadeOut.Completed += (s, ev) =>
        {
            SocialHubContainer.BeginAnimation(OpacityProperty, null);
            SocialHubContainer.Visibility = Visibility.Collapsed;
            SocialHubContainer.Opacity = 0.0;

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
        SocialHubContainer.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void OnSocialHubManageAppsRequested()
    {
        OrbitLogger.Info(LogCategory, "Manage Apps clicked in Social Hub. Switching to Manage Social Apps view.");

        IsInSocialHubView = false;
        IsInManageSocialAppsView = true;
        ManageSocialAppsPanel.UpdateAppsList();
        DesiredHeightChanged?.Invoke(ManageSocialAppsPanel.GetDesiredHeight());

        var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fadeOut.Completed += (s, ev) =>
        {
            SocialHubContainer.BeginAnimation(OpacityProperty, null);
            SocialHubContainer.Visibility = Visibility.Collapsed;
            SocialHubContainer.Opacity = 0.0;

            ManageSocialAppsContainer.Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            fadeIn.Completed += (s2, ev2) =>
            {
                ManageSocialAppsContainer.BeginAnimation(OpacityProperty, null);
                ManageSocialAppsContainer.Opacity = 1.0;
            };
            ManageSocialAppsContainer.BeginAnimation(OpacityProperty, fadeIn);
        };
        SocialHubContainer.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void OnManageSocialAppsBackRequested()
    {
        OrbitLogger.Info(LogCategory, "Back clicked in Manage Social Apps. Returning to Social Hub view.");

        IsInManageSocialAppsView = false;
        IsInSocialHubView = true;
        SocialHubPanel.UpdateAppsDisplay();
        DesiredHeightChanged?.Invoke(SocialHubPanel.CalculateDesiredHeight());

        var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fadeOut.Completed += (s, ev) =>
        {
            ManageSocialAppsContainer.BeginAnimation(OpacityProperty, null);
            ManageSocialAppsContainer.Visibility = Visibility.Collapsed;
            ManageSocialAppsContainer.Opacity = 0.0;

            SocialHubContainer.Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            fadeIn.Completed += (s2, ev2) =>
            {
                SocialHubContainer.BeginAnimation(OpacityProperty, null);
                SocialHubContainer.Opacity = 1.0;
            };
            SocialHubContainer.BeginAnimation(OpacityProperty, fadeIn);
        };
        ManageSocialAppsContainer.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void OnCardWorkClicked(object sender, RoutedEventArgs e)
    {
        if (e?.RoutedEvent != null)
        {
            e.Handled = true;
        }
        if (IsInWorkHubView) return;
        OrbitLogger.Info(LogCategory, "WORK card clicked. Switching to Work Hub view.");

        IsInWorkHubView = true;
        WorkHubPanel.UpdateAppsDisplay();
        DesiredHeightChanged?.Invoke(WorkHubPanel.CalculateDesiredHeight());

        var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fadeOut.Completed += (s, ev) =>
        {
            HomeContentView.BeginAnimation(OpacityProperty, null);
            HomeContentView.Visibility = Visibility.Collapsed;

            WorkHubContainer.Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            fadeIn.Completed += (s2, ev2) =>
            {
                WorkHubContainer.BeginAnimation(OpacityProperty, null);
                WorkHubContainer.Opacity = 1.0;
            };
            WorkHubContainer.BeginAnimation(OpacityProperty, fadeIn);
        };
        HomeContentView.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void OnWorkHubBackRequested()
    {
        OrbitLogger.Info(LogCategory, "Back clicked in Work Hub view. Returning to Home.");

        IsInWorkHubView = false;
        DesiredHeightChanged?.Invoke(CalculateHomeDesiredHeight());

        var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fadeOut.Completed += (s, ev) =>
        {
            WorkHubContainer.BeginAnimation(OpacityProperty, null);
            WorkHubContainer.Visibility = Visibility.Collapsed;
            WorkHubContainer.Opacity = 0.0;

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
        WorkHubContainer.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void OnWorkHubManageAppsRequested()
    {
        OrbitLogger.Info(LogCategory, "Manage Apps clicked in Work Hub. Switching to Manage Work Apps view.");

        IsInWorkHubView = false;
        IsInManageWorkAppsView = true;
        ManageWorkAppsPanel.UpdateAppsList();
        DesiredHeightChanged?.Invoke(ManageWorkAppsPanel.GetDesiredHeight());

        var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fadeOut.Completed += (s, ev) =>
        {
            WorkHubContainer.BeginAnimation(OpacityProperty, null);
            WorkHubContainer.Visibility = Visibility.Collapsed;
            WorkHubContainer.Opacity = 0.0;

            ManageWorkAppsContainer.Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            fadeIn.Completed += (s2, ev2) =>
            {
                ManageWorkAppsContainer.BeginAnimation(OpacityProperty, null);
                ManageWorkAppsContainer.Opacity = 1.0;
            };
            ManageWorkAppsContainer.BeginAnimation(OpacityProperty, fadeIn);
        };
        WorkHubContainer.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void OnManageWorkAppsBackRequested()
    {
        OrbitLogger.Info(LogCategory, "Back clicked in Manage Work Apps. Returning to Work Hub view.");

        IsInManageWorkAppsView = false;
        IsInWorkHubView = true;
        WorkHubPanel.UpdateAppsDisplay();
        DesiredHeightChanged?.Invoke(WorkHubPanel.CalculateDesiredHeight());

        var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fadeOut.Completed += (s, ev) =>
        {
            ManageWorkAppsContainer.BeginAnimation(OpacityProperty, null);
            ManageWorkAppsContainer.Visibility = Visibility.Collapsed;
            ManageWorkAppsContainer.Opacity = 0.0;

            WorkHubContainer.Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            fadeIn.Completed += (s2, ev2) =>
            {
                WorkHubContainer.BeginAnimation(OpacityProperty, null);
                WorkHubContainer.Opacity = 1.0;
            };
            WorkHubContainer.BeginAnimation(OpacityProperty, fadeIn);
        };
        ManageWorkAppsContainer.BeginAnimation(OpacityProperty, fadeOut);
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

    private void OnFileExplorerClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (DateTime.UtcNow - _lastFileExplorerLaunch < TimeSpan.FromSeconds(1.5))
        {
            OrbitLogger.Debug(LogCategory, "Ignored duplicate File Explorer launch request (cooldown active).");
            return;
        }

        _lastFileExplorerLaunch = DateTime.UtcNow;

        try
        {
            OrbitLogger.Info(LogCategory, "Launching native Windows File Explorer...");
            var startInfo = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                UseShellExecute = true
            };
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, "Failed to launch Windows File Explorer.", ex);
        }
    }
}

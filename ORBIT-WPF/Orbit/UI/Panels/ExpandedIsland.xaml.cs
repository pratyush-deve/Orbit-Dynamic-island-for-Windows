using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Orbit.Core;
using Orbit.Integrations.Desktop;
using Orbit.Services;
using Orbit.Services.AiObserver;
using Orbit.Services.DevWorkspace;
using Orbit.Services.Hubs;
using Orbit.Services.Notes;
using Orbit.Services.SystemControls;
using Orbit.Services.SystemMonitor;
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
    public bool IsInNotesView { get; private set; }
    public bool IsNotesModeActive => IsInNotesView;
    public bool IsInSystemMonitorView { get; private set; }

    public event Action<bool>? NotesModeChanged;

    private IAgentStateManager? _agentStateManager;
    private ISocialHubService? _socialHubService;
    private IWorkHubService? _workHubService;
    private ISystemMonitorService? _systemMonitorService;
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

        Loaded += (s, e) =>
        {
            IslandMascot.IsEmbeddedInNotch = true;
            UpdateIslandFrameGeometry(RootIsland.ActualWidth, RootIsland.ActualHeight);
            UpdateParentIslandGlowState(Orbit.Models.AgentState.Idle);
        };
        RootIsland.SizeChanged += (s, e) => UpdateIslandFrameGeometry(e.NewSize.Width, e.NewSize.Height);
    }

    public void Initialize(
        IWindowManager windowManager,
        ISystemControlsService? controlsService = null,
        IDevWorkspaceService? workspaceService = null,
        IAgentStateManager? agentStateManager = null,
        IOrbitAttentionManager? attentionManager = null,
        ISocialHubService? socialHubService = null,
        IWorkHubService? workHubService = null,
        INotesService? notesService = null,
        ISystemMonitorService? systemMonitorService = null,
        Orbit.Services.Ai.IOllamaServerManager? ollamaServerManager = null)
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
            AiMonitorPanel.Initialize(agentStateManager, ollamaServerManager);
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
        NotesPanel.Initialize(notesService);
        if (systemMonitorService != null)
        {
            _systemMonitorService = systemMonitorService;
            SystemMonitorPanel.Initialize(systemMonitorService);
        }
        if (attentionManager != null)
        {
            attentionManager.MascotStateChanged += (state, msg) =>
            {
                Dispatcher.Invoke(() =>
                {
                    IslandMascot.SetAiState(state, msg);
                    UpdateParentIslandGlowState(state);
                });
            };
        }
    }

    public void SetGlowStatus(bool enabled)
    {
        _glowEnabled = enabled;
        IslandMascot.SetGlowEnabled(enabled);
        UpdateParentIslandGlowState(Orbit.Models.AgentState.Idle);
    }

    /// <summary>
    /// Generates dynamic StreamGeometry for the screen-top Dynamic Island notch parent window contour.
    /// Perfectly traces the left shoulder, central notch cradle holding the mascot, right shoulder,
    /// and rounded perimeter corners with hardware-accelerated direct rendering.
    /// </summary>
    private void UpdateIslandFrameGeometry(double width, double height)
    {
        if (width < 60 || height < 60) return;

        const double marginX = 8.0;
        const double marginTop = 2.0;
        const double marginBottom = 8.0;

        double left = marginX;
        double top = marginTop;
        double right = width - marginX;
        double bottom = height - marginBottom;
        double cardW = right - left;
        double cardH = bottom - top;

        double r = Math.Min(22.0, Math.Min(cardW / 4.0, cardH / 4.0));
        double cx = left + (cardW / 2.0);

        // Dynamic Island notch parameters
        double notchHalfW = Math.Min(88.0, (cardW / 2.0) - r - 20.0);
        double notchDepth = Math.Min(36.0, cardH / 4.0);
        double curveW = Math.Min(42.0, notchHalfW * 0.48);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            // Start at top edge just past top-left corner fillet
            ctx.BeginFigure(new Point(left + r, top), isFilled: true, isClosed: true);

            // Left shoulder (horizontal line towards center)
            ctx.LineTo(new Point(cx - notchHalfW, top), isStroked: true, isSmoothJoin: true);

            // Concave S-curve dipping smoothly down into the central notch
            ctx.BezierTo(
                new Point(cx - notchHalfW + (curveW * 0.42), top),
                new Point(cx - notchHalfW + (curveW * 0.58), top + notchDepth),
                new Point(cx - notchHalfW + curveW, top + notchDepth),
                isStroked: true, isSmoothJoin: true);

            // Notch cradle floor (supporting the mascot eyes)
            ctx.LineTo(new Point(cx + notchHalfW - curveW, top + notchDepth), isStroked: true, isSmoothJoin: true);

            // Concave S-curve rising smoothly up to the right shoulder
            ctx.BezierTo(
                new Point(cx + notchHalfW - (curveW * 0.58), top + notchDepth),
                new Point(cx + notchHalfW - (curveW * 0.42), top),
                new Point(cx + notchHalfW, top),
                isStroked: true, isSmoothJoin: true);

            // Right shoulder (horizontal line towards top-right corner)
            ctx.LineTo(new Point(right - r, top), isStroked: true, isSmoothJoin: true);

            // Top-right corner arc
            ctx.ArcTo(new Point(right, top + r), new System.Windows.Size(r, r), 0, false, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: true);

            // Right perimeter edge
            ctx.LineTo(new Point(right, bottom - r), isStroked: true, isSmoothJoin: true);

            // Bottom-right corner arc
            ctx.ArcTo(new Point(right - r, bottom), new System.Windows.Size(r, r), 0, false, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: true);

            // Bottom perimeter edge
            ctx.LineTo(new Point(left + r, bottom), isStroked: true, isSmoothJoin: true);

            // Bottom-left corner arc
            ctx.ArcTo(new Point(left, bottom - r), new System.Windows.Size(r, r), 0, false, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: true);

            // Left perimeter edge
            ctx.LineTo(new Point(left, top + r), isStroked: true, isSmoothJoin: true);

            // Top-left corner arc
            ctx.ArcTo(new Point(left + r, top), new System.Windows.Size(r, r), 0, false, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: true);
        }
        geometry.Freeze();
        IslandFramePath.Data = geometry;

        // Dynamic Island notch accent shelf (subtle glowing rim highlight under the notch cradle)
        var accentGeom = new StreamGeometry();
        using (var actx = accentGeom.Open())
        {
            actx.BeginFigure(new Point(cx - notchHalfW + curveW + 4.0, top + notchDepth + 2.0), isFilled: false, isClosed: false);
            actx.QuadraticBezierTo(
                new Point(cx, top + notchDepth + 4.0),
                new Point(cx + notchHalfW - curveW - 4.0, top + notchDepth + 2.0),
                isStroked: true, isSmoothJoin: true);
        }
        accentGeom.Freeze();
        NotchAccentPath.Data = accentGeom;
    }

    private void UpdateParentIslandGlowState(Orbit.Models.AgentState state)
    {
        if (IslandGlow == null) return;
        var colorEase = new SineEase { EasingMode = EasingMode.EaseInOut };

        switch (state)
        {
            case Orbit.Models.AgentState.Working:
                IslandGlow.Color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#C084FC");
                IslandGlow.BlurRadius = 32.0;
                if (_glowEnabled)
                {
                    var workAnim = new DoubleAnimation(0.5, 0.85, TimeSpan.FromSeconds(1.3))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = colorEase
                    };
                    IslandGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, workAnim);
                }
                break;

            case Orbit.Models.AgentState.Completed:
                IslandGlow.Color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#10B981");
                IslandGlow.BlurRadius = 32.0;
                if (_glowEnabled)
                {
                    var doneAnim = new DoubleAnimation(0.55, 0.85, TimeSpan.FromSeconds(1.5))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = colorEase
                    };
                    IslandGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, doneAnim);
                }
                break;

            case Orbit.Models.AgentState.Attention:
            case Orbit.Models.AgentState.Error:
                IslandGlow.Color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#EF4444");
                IslandGlow.BlurRadius = 26.0;
                if (_glowEnabled)
                {
                    var attnAnim = new DoubleAnimation(0.4, 0.75, TimeSpan.FromSeconds(1.0))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = colorEase
                    };
                    IslandGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, attnAnim);
                }
                break;

            default:
                IslandGlow.Color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#A855F7");
                IslandGlow.BlurRadius = 28.0;
                if (_glowEnabled)
                {
                    var idleAnim = new DoubleAnimation(0.35, 0.55, TimeSpan.FromSeconds(2.5))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = colorEase
                    };
                    IslandGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, idleAnim);
                }
                else
                {
                    IslandGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, null);
                    IslandGlow.Opacity = 0.0;
                }
                break;
        }
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
        bool wasInNotes = IsInNotesView;
        if (IsInNotesView)
        {
            NotesPanel.SaveCurrentNotesSynchronously();
        }

        IsInActiveAppsView = false;
        IsInControlsView = false;
        IsInDevModeView = false;
        IsInManageAppsView = false;
        IsInAiMonitorView = false;
        IsInSocialHubView = false;
        IsInManageSocialAppsView = false;
        IsInWorkHubView = false;
        IsInManageWorkAppsView = false;
        IsInNotesView = false;

        if (IsInSystemMonitorView)
        {
            SystemMonitorPanel.StopMonitoring();
            IsInSystemMonitorView = false;
        }

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

        NotesContainer.BeginAnimation(OpacityProperty, null);
        NotesContainer.Visibility = Visibility.Collapsed;
        NotesContainer.Opacity = 0.0;

        SystemMonitorContainer.BeginAnimation(OpacityProperty, null);
        SystemMonitorContainer.Visibility = Visibility.Collapsed;
        SystemMonitorContainer.Opacity = 0.0;

        HomeContentView.BeginAnimation(OpacityProperty, null);
        HomeContentView.Visibility = Visibility.Visible;
        HomeContentView.Opacity = 1.0;

        TopLeftShortcutContainer?.BeginAnimation(OpacityProperty, null);
        if (TopLeftShortcutContainer != null)
        {
            TopLeftShortcutContainer.Opacity = 1.0;
            TopLeftShortcutContainer.Visibility = Visibility.Visible;
        }

        HomeCardsScrollViewer?.ScrollToHorizontalOffset(0);
        UpdateHomeCardsScrollProgress();

        if (wasInNotes)
        {
            NotesModeChanged?.Invoke(false);
        }
        else
        {
            DesiredHeightChanged?.Invoke(CalculateHomeDesiredHeight());
        }
    }

    private void OnActiveAppsDismissAndCollapseRequested()
    {
        OrbitLogger.Info(LogCategory, "Application action handled. Navigating back to Home and requesting collapse.");
        ResetToHomeView();
        CollapseRequested?.Invoke();
    }

    private void SetTopLeftShortcutsVisible(bool visible)
    {
        if (TopLeftShortcutContainer == null) return;

        if (visible)
        {
            TopLeftShortcutContainer.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            fadeIn.Completed += (s, e) =>
            {
                TopLeftShortcutContainer.BeginAnimation(OpacityProperty, null);
                TopLeftShortcutContainer.Opacity = 1.0;
            };
            TopLeftShortcutContainer.BeginAnimation(OpacityProperty, fadeIn);
        }
        else
        {
            var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            fadeOut.Completed += (s, e) =>
            {
                TopLeftShortcutContainer.BeginAnimation(OpacityProperty, null);
                TopLeftShortcutContainer.Opacity = 0.0;
                TopLeftShortcutContainer.Visibility = Visibility.Collapsed;
            };
            TopLeftShortcutContainer.BeginAnimation(OpacityProperty, fadeOut);
        }
    }

    private void OnCardActiveAppsClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (IsInActiveAppsView) return;
        OrbitLogger.Info(LogCategory, "APPS card clicked. Switching to Active Apps view.");

        IsInActiveAppsView = true;
        SetTopLeftShortcutsVisible(false);

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
        SetTopLeftShortcutsVisible(true);

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
        SetTopLeftShortcutsVisible(false);

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
        SetTopLeftShortcutsVisible(true);
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
        SetTopLeftShortcutsVisible(false);
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
        SetTopLeftShortcutsVisible(true);
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
        SetTopLeftShortcutsVisible(false);
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
        SetTopLeftShortcutsVisible(true);
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
    /// Calculates the standard height required for the Home view (Mascot header + cards carousel).
    /// </summary>
    public double CalculateHomeDesiredHeight()
    {
        return MainWindow.ExpandedHeight;
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
        SetTopLeftShortcutsVisible(false);
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
        SetTopLeftShortcutsVisible(true);
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
        SetTopLeftShortcutsVisible(false);
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
        SetTopLeftShortcutsVisible(false);
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
        SetTopLeftShortcutsVisible(true);
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
        SetTopLeftShortcutsVisible(false);
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
            if (IsInNotesView)
            {
                OnNotesBackRequested();
                return;
            }
            CollapseRequested?.Invoke();
        }
    }

    private void OnNotesButtonClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (IsInNotesView)
        {
            OnNotesBackRequested();
            return;
        }

        SwitchToNotesView();
    }

    public void SwitchToNotesView()
    {
        if (IsInNotesView) return;
        OrbitLogger.Info(LogCategory, "Notes button clicked. Switching to Notes mode view.");

        // Cleanly exit any active workspace or hub before entering Notes mode
        if (IsInActiveAppsView || IsInControlsView || IsInDevModeView || IsInManageAppsView ||
            IsInAiMonitorView || IsInSocialHubView || IsInManageSocialAppsView ||
            IsInWorkHubView || IsInManageWorkAppsView || IsInSystemMonitorView)
        {
            ResetToHomeView();
        }

        IsInNotesView = true;
        NotesPanel.RefreshNotes();
        SetTopLeftShortcutsVisible(false);
        NotesModeChanged?.Invoke(true);

        var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fadeOut.Completed += (s, ev) =>
        {
            HomeContentView.BeginAnimation(OpacityProperty, null);
            HomeContentView.Visibility = Visibility.Collapsed;

            NotesContainer.Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            fadeIn.Completed += (s2, ev2) =>
            {
                NotesContainer.BeginAnimation(OpacityProperty, null);
                NotesContainer.Opacity = 1.0;
            };
            NotesContainer.BeginAnimation(OpacityProperty, fadeIn);
        };
        HomeContentView.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void OnNotesBackRequested()
    {
        OrbitLogger.Info(LogCategory, "Back clicked in Notes view. Returning to Home.");

        NotesPanel.SaveCurrentNotesSynchronously();
        IsInNotesView = false;
        SetTopLeftShortcutsVisible(true);

        // Ensure Home view is visible and reset scroll before transition begins
        HomeContentView.Visibility = Visibility.Visible;
        HomeContentView.Opacity = 0.0;
        HomeCardsScrollViewer?.ScrollToHorizontalOffset(0);
        UpdateHomeCardsScrollProgress();
        HomeContentView.InvalidateMeasure();

        // Immediately notify window to animate back to normal Home dimensions (440x266)
        NotesModeChanged?.Invoke(false);

        var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fadeOut.Completed += (s, ev) =>
        {
            NotesContainer.BeginAnimation(OpacityProperty, null);
            NotesContainer.Visibility = Visibility.Collapsed;
            NotesContainer.Opacity = 0.0;
        };
        NotesContainer.BeginAnimation(OpacityProperty, fadeOut);

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
    }

    private void OnHideButtonClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        HideRequested?.Invoke();
    }

    private void OnTaskManagerClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (IsInSystemMonitorView) return;
        OrbitLogger.Info(LogCategory, "Task Manager button clicked. Navigating to native System Monitor view.");

        SwitchToSystemMonitorView();
    }

    public void SwitchToSystemMonitorView()
    {
        if (IsInSystemMonitorView) return;
        OrbitLogger.Info(LogCategory, "Switching to System Monitor view.");

        // Cleanly exit any active workspace, hub, or notes mode
        if (IsInActiveAppsView || IsInControlsView || IsInDevModeView || IsInManageAppsView ||
            IsInAiMonitorView || IsInSocialHubView || IsInManageSocialAppsView ||
            IsInWorkHubView || IsInManageWorkAppsView || IsInNotesView)
        {
            ResetToHomeView();
        }

        IsInSystemMonitorView = true;
        SetTopLeftShortcutsVisible(false);

        DesiredHeightChanged?.Invoke(CalculateHomeDesiredHeight());

        var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fadeOut.Completed += (s, ev) =>
        {
            HomeContentView.BeginAnimation(OpacityProperty, null);
            HomeContentView.Visibility = Visibility.Collapsed;
            HomeContentView.Opacity = 0.0;

            SystemMonitorContainer.Visibility = Visibility.Visible;
            SystemMonitorPanel.StartMonitoring();

            var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            fadeIn.Completed += (s2, ev2) =>
            {
                SystemMonitorContainer.BeginAnimation(OpacityProperty, null);
                SystemMonitorContainer.Opacity = 1.0;
            };
            SystemMonitorContainer.BeginAnimation(OpacityProperty, fadeIn);
        };
        HomeContentView.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void OnSystemMonitorBackRequested()
    {
        OrbitLogger.Info(LogCategory, "Back clicked in System Monitor view. Returning to Home.");

        IsInSystemMonitorView = false;
        SystemMonitorPanel.StopMonitoring();
        SetTopLeftShortcutsVisible(true);

        DesiredHeightChanged?.Invoke(CalculateHomeDesiredHeight());

        var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fadeOut.Completed += (s, ev) =>
        {
            SystemMonitorContainer.BeginAnimation(OpacityProperty, null);
            SystemMonitorContainer.Visibility = Visibility.Collapsed;
            SystemMonitorContainer.Opacity = 0.0;

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
        SystemMonitorContainer.BeginAnimation(OpacityProperty, fadeOut);
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

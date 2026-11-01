using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using Orbit.Core;
using Orbit.Integrations.Desktop;
using Orbit.Platform.Windows;
using Orbit.Services;
using Orbit.Services.AiObserver;
using Orbit.Services.DevWorkspace;
using Orbit.Services.Hubs;
using Orbit.Services.Notes;
using Orbit.Services.SystemControls;

namespace Orbit;

/// <summary>
/// Main floating companion window for ORBIT 2.0.
/// Anchored top-centre on primary monitor with clean two-stage expansion and focus collapse.
/// Dragging has been completely removed for maximum stability.
/// </summary>
public partial class MainWindow : Window
{
    private const string LogCategory = "MAIN_WINDOW";

    // Standard dimensions (DIPs)
    public const double CollapsedWidth = 140.0;
    public const double CollapsedHeight = 56.0;
    public const double ExpandedWidth = 440.0;
    public const double ExpandedHeight = 266.0;
    public const double NotesWidth = 500.0;
    public const double NotesHeight = 380.0;
    public const double TopMargin = 14.0;

    private readonly IMonitorService _monitorService;
    private readonly ISettingsService _settingsService;
    private readonly IWindowsOverlayService _overlayService;
    private readonly IWindowPositionService _positionService;
    private readonly IWindowLifecycleService _lifecycleService;
    private readonly IGlobalHotkeyService _hotkeyService;
    private readonly IWindowManager _windowManager;
    private readonly ISystemControlsService? _systemControlsService;
    private readonly IDevWorkspaceService? _devWorkspaceService;
    private readonly IAgentStateManager? _agentStateManager;
    private readonly IOrbitAttentionManager? _attentionManager;
    private readonly ISocialHubService? _socialHubService;
    private readonly IWorkHubService? _workHubService;
    private readonly INotesService? _notesService;

    private IntPtr _hwnd = IntPtr.Zero;

    public MainWindow(
        IMonitorService monitorService,
        ISettingsService settingsService,
        IWindowsOverlayService overlayService,
        IWindowPositionService positionService,
        IWindowLifecycleService lifecycleService,
        IGlobalHotkeyService hotkeyService,
        IWindowManager windowManager,
        ISystemControlsService? systemControlsService = null,
        IDevWorkspaceService? devWorkspaceService = null,
        IAgentStateManager? agentStateManager = null,
        IOrbitAttentionManager? attentionManager = null,
        ISocialHubService? socialHubService = null,
        IWorkHubService? workHubService = null,
        INotesService? notesService = null)
    {
        InitializeComponent();

        _monitorService = monitorService;
        _settingsService = settingsService;
        _overlayService = overlayService;
        _positionService = positionService;
        _lifecycleService = lifecycleService;
        _hotkeyService = hotkeyService;
        _windowManager = windowManager;
        _systemControlsService = systemControlsService;
        _devWorkspaceService = devWorkspaceService;
        _agentStateManager = agentStateManager;
        _attentionManager = attentionManager;
        _socialHubService = socialHubService;
        _workHubService = workHubService;
        _notesService = notesService;

        // Wire lifecycle service events
        _lifecycleService.StateChanged += OnStateChanged;
        _lifecycleService.ResetPositionRequested += OnResetPositionRequested;
        _lifecycleService.ExitRequested += OnExitRequested;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Deactivated += OnWindowDeactivated;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        OrbitLogger.Info(LogCategory, $"HWND initialized: 0x{_hwnd:X}");

        _overlayService.InitializeWindowHandle(_hwnd);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Anchor window to top-center of primary monitor
        PositionAtTopCenter(CollapsedWidth);

        // Apply saved eye glow preference
        var glow = _settingsService.CurrentSettings.EyeGlowEnabled;
        PillMascot.SetGlowEnabled(glow);
        IslandPanel.SetGlowStatus(glow);

        // Initialize active apps manager, system controls, dev workspace, AI agent monitor, social/work hubs, and responsive height changes
        IslandPanel.Initialize(_windowManager, _systemControlsService, _devWorkspaceService, _agentStateManager, _attentionManager, _socialHubService, _workHubService, _notesService);
        IslandPanel.DesiredHeightChanged += OnIslandDesiredHeightChanged;
        IslandPanel.NotesModeChanged += OnNotesModeChanged;

        if (_attentionManager != null)
        {
            _attentionManager.MascotStateChanged += (state, msg) =>
            {
                Dispatcher.Invoke(() => PillMascot.SetAiState(state, msg));
            };
        }

        if (_systemControlsService != null)
        {
            _ = _systemControlsService.InitializeAsync();
        }

        if (_devWorkspaceService != null)
        {
            _ = _devWorkspaceService.InitializeAsync();
        }

        if (_socialHubService != null)
        {
            _ = _socialHubService.InitializeAsync();
        }

        if (_workHubService != null)
        {
            _ = _workHubService.InitializeAsync();
        }

        OrbitLogger.Info(LogCategory, $"MainWindow loaded at top-centre [{Left:0.#}, {Top:0.#}]. State: {_lifecycleService.CurrentState}");
    }

    private void PositionAtTopCenter(double currentWidth)
    {
        var primary = _monitorService.GetPrimaryMonitor();
        var (centerX, topY) = _monitorService.GetTopCenterPosition(primary, currentWidth, TopMargin);
        Left = centerX;
        Top = topY;
    }

    private void OnIslandDesiredHeightChanged(double targetHeight)
    {
        if (_lifecycleService.CurrentState != CompanionState.Expanded) return;
        if (IslandPanel.IsNotesModeActive) return;

        var primary = _monitorService.GetPrimaryMonitor();
        double maxAvailable = Math.Max(200, primary.WorkArea.Height - Top - 20);
        double clampedHeight = Math.Min(targetHeight, maxAvailable);

        var anim = new DoubleAnimation(clampedHeight, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(HeightProperty, anim);
    }

    private void OnNotesModeChanged(bool isActive)
    {
        if (_lifecycleService.CurrentState != CompanionState.Expanded) return;

        var primary = _monitorService.GetPrimaryMonitor();
        double targetWidth = isActive ? NotesWidth : ExpandedWidth;
        double targetHeight = isActive ? NotesHeight : IslandPanel.CalculateHomeDesiredHeight();

        var (targetLeft, topY) = _monitorService.GetTopCenterPosition(primary, targetWidth, TopMargin);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(240);

        var widthAnim = new DoubleAnimation(targetWidth, duration) { EasingFunction = ease };
        var leftAnim = new DoubleAnimation(targetLeft, duration) { EasingFunction = ease };
        var heightAnim = new DoubleAnimation(targetHeight, duration) { EasingFunction = ease };

        widthAnim.Completed += (s, e) =>
        {
            BeginAnimation(WidthProperty, null);
            BeginAnimation(LeftProperty, null);
            BeginAnimation(HeightProperty, null);

            Width = targetWidth;
            Left = targetLeft;
            Height = targetHeight;
            _overlayService.ReapplyTopmost(_hwnd);
        };

        BeginAnimation(WidthProperty, widthAnim);
        BeginAnimation(LeftProperty, leftAnim);
        BeginAnimation(HeightProperty, heightAnim);
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        OrbitLogger.Debug(LogCategory, $"Window deactivated. CurrentState = {_lifecycleService.CurrentState}");

        // CRITICAL: Do NOT collapse or exit if Notes mode is active!
        if (IslandPanel.IsNotesModeActive)
        {
            _overlayService.ReapplyTopmost(_hwnd);
            return;
        }

        // Only collapse if currently EXPANDED
        // Do NOT collapse if Expanding, Collapsing, Collapsed, or Hidden!
        // Prevents activation changes during expansion from collapsing prematurely.
        if (_lifecycleService.CurrentState == CompanionState.Expanded)
        {
            IslandPanel.ResetToHomeView();
            _lifecycleService.RequestCollapse();
        }
    }

    private void OnStateChanged(CompanionState newState)
    {
        Dispatcher.Invoke(() =>
        {
            switch (newState)
            {
                case CompanionState.Expanding:
                    ExecuteTwoStageExpansion();
                    break;

                case CompanionState.Collapsing:
                    ExecuteTwoStageCollapse();
                    break;

                case CompanionState.Hidden:
                    Hide();
                    // Reset window geometry so next Show() restores directly as the top-centre pill
                    BeginAnimation(WidthProperty, null);
                    BeginAnimation(HeightProperty, null);
                    BeginAnimation(LeftProperty, null);
                    Width = CollapsedWidth;
                    Height = CollapsedHeight;
                    PositionAtTopCenter(CollapsedWidth);
                    IslandPanel.ResetToHomeView();
                    CollapsedView.Visibility = Visibility.Visible;
                    ExpandedView.Visibility = Visibility.Collapsed;
                    break;

                case CompanionState.Collapsed:
                    Show();
                    IslandPanel.ResetToHomeView();
                    _overlayService.BringToFront(_hwnd);
                    break;
            }
        });
    }

    #region Mouse Tracking and Click Handling

    private void OnWindowMouseMove(object sender, MouseEventArgs e)
    {
        if (_lifecycleService.CurrentState == CompanionState.Collapsed)
        {
            var pos = e.GetPosition(PillMascot);
            PillMascot.TrackCursor(pos);
        }
        else if (_lifecycleService.CurrentState == CompanionState.Expanded)
        {
            var pos = e.GetPosition(IslandPanel.Mascot);
            IslandPanel.Mascot.TrackCursor(pos);
        }
    }

    private void OnWindowMouseLeave(object sender, MouseEventArgs e)
    {
        PillMascot.OnWindowMouseLeave();
        IslandPanel.Mascot.OnWindowMouseLeave();
    }

    private void OnMascotClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            OrbitLogger.Info(LogCategory, "Collapsed pill clicked. Initiating expansion.");
            _lifecycleService.RequestExpand();
        }
    }

    private void OnIslandCollapseRequested()
    {
        OrbitLogger.Info(LogCategory, "Expanded island eyes clicked. Initiating collapse.");
        _lifecycleService.RequestCollapse();
    }

    #endregion

    #region Two-Stage Expansion and Collapse

    private void ExecuteTwoStageExpansion()
    {
        OrbitLogger.Info(LogCategory, "Starting two-stage expansion.");

        var primary = _monitorService.GetPrimaryMonitor();
        var (collapsedLeft, topY) = _monitorService.GetTopCenterPosition(primary, CollapsedWidth, TopMargin);
        var (expandedLeft, _) = _monitorService.GetTopCenterPosition(primary, ExpandedWidth, TopMargin);

        // Ensure window starts aligned to top-center
        Left = collapsedLeft;
        Top = topY;
        Width = CollapsedWidth;
        Height = CollapsedHeight;

        // Switch to expanded view with hidden body content for Stage 1
        CollapsedView.Visibility = Visibility.Collapsed;
        ExpandedView.Visibility = Visibility.Visible;
        IslandPanel.SetBodyOpacity(0.0);

        var stage1Duration = TimeSpan.FromMilliseconds(300);
        var stage2Duration = TimeSpan.FromMilliseconds(300);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        // STAGE 1: Horizontal Expansion (Width expands 140 -> 330, Left shifts to maintain center)
        var widthAnim = new DoubleAnimation(ExpandedWidth, stage1Duration) { EasingFunction = ease };
        var leftAnim = new DoubleAnimation(expandedLeft, stage1Duration) { EasingFunction = ease };

        widthAnim.Completed += (s, e) =>
        {
            double targetExpandedHeight = IslandPanel.CalculateHomeDesiredHeight();

            // STAGE 2: Vertical Expansion (Height expands 56 -> content-driven height downwards)
            var heightAnim = new DoubleAnimation(targetExpandedHeight, stage2Duration) { EasingFunction = ease };

            // Content gently fades in during Stage 2
            IslandPanel.AnimateBodyOpacity(1.0, TimeSpan.FromMilliseconds(200));

            heightAnim.Completed += (s2, e2) =>
            {
                // Release animation clocks so properties aren't held
                BeginAnimation(WidthProperty, null);
                BeginAnimation(HeightProperty, null);
                BeginAnimation(LeftProperty, null);

                Width = ExpandedWidth;
                Height = targetExpandedHeight;
                Left = expandedLeft;
                Top = topY;

                _lifecycleService.SetState(CompanionState.Expanded);
                Activate();
                _overlayService.ReapplyTopmost(_hwnd);
                OrbitLogger.Info(LogCategory, "Two-stage expansion completed.");
            };

            BeginAnimation(HeightProperty, heightAnim);
        };

        BeginAnimation(WidthProperty, widthAnim);
        BeginAnimation(LeftProperty, leftAnim);
    }

    private void ExecuteTwoStageCollapse()
    {
        OrbitLogger.Info(LogCategory, "Starting two-stage collapse.");

        // Immediately ensure island returns to Home view
        IslandPanel.ResetToHomeView();

        var primary = _monitorService.GetPrimaryMonitor();
        var (collapsedLeft, topY) = _monitorService.GetTopCenterPosition(primary, CollapsedWidth, TopMargin);
        var (expandedLeft, _) = _monitorService.GetTopCenterPosition(primary, ExpandedWidth, TopMargin);

        var stage1Duration = TimeSpan.FromMilliseconds(250);
        var stage2Duration = TimeSpan.FromMilliseconds(250);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        // STAGE 1: Vertical Collapse (Height reduces 260 -> 56, body content fades out)
        IslandPanel.AnimateBodyOpacity(0.0, TimeSpan.FromMilliseconds(120));

        var heightAnim = new DoubleAnimation(CollapsedHeight, stage1Duration) { EasingFunction = ease };

        heightAnim.Completed += (s, e) =>
        {
            // STAGE 2: Horizontal Collapse (Width reduces 330 -> 140, Left shifts back to collapsed center)
            var widthAnim = new DoubleAnimation(CollapsedWidth, stage2Duration) { EasingFunction = ease };
            var leftAnim = new DoubleAnimation(collapsedLeft, stage2Duration) { EasingFunction = ease };

            widthAnim.Completed += (s2, e2) =>
            {
                // Release animation clocks so properties aren't held
                BeginAnimation(WidthProperty, null);
                BeginAnimation(HeightProperty, null);
                BeginAnimation(LeftProperty, null);

                Width = CollapsedWidth;
                Height = CollapsedHeight;
                Left = collapsedLeft;
                Top = topY;

                CollapsedView.Visibility = Visibility.Visible;
                ExpandedView.Visibility = Visibility.Collapsed;
                IslandPanel.ResetToHomeView();

                _lifecycleService.SetState(CompanionState.Collapsed);
                _overlayService.ReapplyTopmost(_hwnd);
                OrbitLogger.Info(LogCategory, "Two-stage collapse completed.");
            };

            BeginAnimation(WidthProperty, widthAnim);
            BeginAnimation(LeftProperty, leftAnim);
        };

        BeginAnimation(HeightProperty, heightAnim);
    }

    #endregion

    #region Action Callbacks

    private void OnResetPositionRequested()
    {
        PositionAtTopCenter(Width);
        _overlayService.BringToFront(_hwnd);
    }

    private void OnGlowToggleRequested(bool enabled)
    {
        PillMascot.SetGlowEnabled(enabled);
        _settingsService.UpdateEyeGlow(enabled);
    }

    private void OnHideRequested()
    {
        _lifecycleService.Hide();
    }

    private void OnExitRequested()
    {
        Close();
        Application.Current.Shutdown();
    }

    #endregion
}
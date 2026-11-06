using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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
using Orbit.Services.SystemMonitor;

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
    public const double CollapsedWidth = 160.0;
    public const double CollapsedHeight = 38.0;
    public const double ExpandedWidth = 480.0;
    public const double ExpandedHeight = 168.0;
    public const double NotesWidth = 500.0;
    public const double NotesHeight = 280.0;
    public const double TopMargin = 0.0;

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
    private readonly ISystemMonitorService? _systemMonitorService;

    private IntPtr _hwnd = IntPtr.Zero;
    private Orbit.Models.AgentState _currentAgentState = Orbit.Models.AgentState.Idle;
    private string _currentAgentMessage = string.Empty;

    // Notch outline & shelf brushes for synchronized state transitions (Idle/Working = Purple, Completed = Emerald Green, Attention/Error = Red)
    private static readonly LinearGradientBrush BrushPurpleNotchOutline = CreatePurpleOutlineBrush();
    private static readonly LinearGradientBrush BrushPurpleNotchShelf = CreatePurpleShelfBrush();
    private static readonly LinearGradientBrush BrushGreenNotchOutline = CreateGreenOutlineBrush();
    private static readonly LinearGradientBrush BrushGreenNotchShelf = CreateGreenShelfBrush();
    private static readonly LinearGradientBrush BrushRedNotchOutline = CreateRedOutlineBrush();
    private static readonly LinearGradientBrush BrushRedNotchShelf = CreateRedShelfBrush();

    private static LinearGradientBrush CreatePurpleOutlineBrush()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x00, 0xA8, 0x55, 0xF7), 0.0));
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x00, 0xA8, 0x55, 0xF7), 0.10));
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x25, 0xA8, 0x55, 0xF7), 0.45));
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x60, 0xA8, 0x55, 0xF7), 0.80));
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x90, 0xC0, 0x84, 0xFC), 1.0));
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush CreatePurpleShelfBrush()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x00, 0xA8, 0x55, 0xF7), 0.0));
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x15, 0xA8, 0x55, 0xF7), 0.25));
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x32, 0xA8, 0x55, 0xF7), 0.65));
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x4F, 0xC0, 0x84, 0xFC), 1.0));
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush CreateGreenOutlineBrush()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x00, 0x10, 0xB9, 0x81), 0.0));
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x00, 0x10, 0xB9, 0x81), 0.10));
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x35, 0x10, 0xB9, 0x81), 0.45));
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x80, 0x10, 0xB9, 0x81), 0.80));
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0xC0, 0x34, 0xD3, 0x99), 1.0));
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush CreateGreenShelfBrush()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x00, 0x10, 0xB9, 0x81), 0.0));
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x20, 0x10, 0xB9, 0x81), 0.25));
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x55, 0x10, 0xB9, 0x81), 0.65));
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x90, 0x34, 0xD3, 0x99), 1.0));
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush CreateRedOutlineBrush()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x00, 0xEF, 0x44, 0x44), 0.0));
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x00, 0xEF, 0x44, 0x44), 0.10));
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x35, 0xEF, 0x44, 0x44), 0.45));
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x75, 0xEF, 0x44, 0x44), 0.80));
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0xA5, 0xF8, 0x71, 0x71), 1.0));
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush CreateRedShelfBrush()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x00, 0xEF, 0x44, 0x44), 0.0));
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x20, 0xEF, 0x44, 0x44), 0.25));
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x50, 0xEF, 0x44, 0x44), 0.65));
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x85, 0xEF, 0x44, 0x44), 1.0));
        brush.Freeze();
        return brush;
    }

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
        INotesService? notesService = null,
        ISystemMonitorService? systemMonitorService = null)
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
        _systemMonitorService = systemMonitorService;

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

        try
        {
            var iconUri = new Uri("pack://application:,,,/Assets/ORBIT.png", UriKind.Absolute);
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(iconUri);
        }
        catch (Exception ex)
        {
            OrbitLogger.Warn(LogCategory, $"Could not assign window icon: {ex.Message}");
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Embed compact mascot eyes seamlessly inside the carved notch cutout
        PillMascot.IsEmbeddedInNotch = true;

        // Anchor window to top-center of primary monitor
        PositionAtTopCenter(CollapsedWidth);
        UpdateUnifiedIslandGeometry(CollapsedWidth, CollapsedHeight);

        // Apply saved eye glow preference
        var glow = _settingsService.CurrentSettings.EyeGlowEnabled;
        PillMascot.SetGlowEnabled(glow);
        IslandPanel.SetGlowStatus(glow);
        UpdateCompactNotchGlow(Orbit.Models.AgentState.Idle);

        // Keep notch perimeter outline & glow synchronized with Mascot visual state changes (e.g. 18s DONE hold)
        PillMascot.VisualStateChanged += state =>
        {
            Dispatcher.Invoke(() =>
            {
                _currentAgentState = state;
                UpdateCompactNotchGlow(state);
            });
        };

        // Initialize active apps manager, system controls, dev workspace, AI agent monitor, social/work hubs, and responsive height changes
        IslandPanel.Initialize(_windowManager, _systemControlsService, _devWorkspaceService, _agentStateManager, _attentionManager, _socialHubService, _workHubService, _notesService, _systemMonitorService);
        IslandPanel.DesiredHeightChanged += OnIslandDesiredHeightChanged;
        IslandPanel.NotesModeChanged += OnNotesModeChanged;

        if (_attentionManager != null)
        {
            _attentionManager.MascotStateChanged += (state, msg) =>
            {
                Dispatcher.Invoke(() =>
                {
                    _currentAgentMessage = msg;
                    // If PillMascot is currently holding the Done state and incoming state is Idle,
                    // keep Done active until the Mascot's hold timer finishes.
                    if (state == Orbit.Models.AgentState.Idle && PillMascot.IsHoldingDoneState)
                    {
                        return;
                    }

                    _currentAgentState = state;
                    PillMascot.SetAiState(state, msg);
                    UpdateCompactNotchGlow(state);
                });
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
        double targetHeight = isActive ? NotesHeight : ExpandedHeight;

        var (targetLeft, topY) = _monitorService.GetTopCenterPosition(primary, targetWidth, TopMargin);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(240);

        var widthAnim = new DoubleAnimation(targetWidth, duration) { EasingFunction = ease };
        var leftAnim = new DoubleAnimation(targetLeft, duration) { EasingFunction = ease };
        var heightAnim = new DoubleAnimation(targetHeight, duration) { EasingFunction = ease };

        widthAnim.Completed += (s, e) =>
        {
            // Assign target values before detaching animations to prevent base-value snap-back
            Width = targetWidth;
            Left = targetLeft;
            Height = targetHeight;

            BeginAnimation(WidthProperty, null);
            BeginAnimation(LeftProperty, null);
            BeginAnimation(HeightProperty, null);

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

    private DateTime _lastToggleRequestTime = DateTime.MinValue;

    /// <summary>
    /// Handles an IPC toggle request from a secondary launch (e.g. HP Programmable Key).
    /// Alternates visibility between hidden and visible on screen (matching the mascot Hide button).
    /// </summary>
    public void HandleToggleRequest()
    {
        // Debounce rapid keyboard key bounces (150ms)
        if (DateTime.UtcNow - _lastToggleRequestTime < TimeSpan.FromMilliseconds(150))
        {
            OrbitLogger.Debug(LogCategory, "Ignored rapid duplicate toggle request (debounce 150ms).");
            return;
        }
        _lastToggleRequestTime = DateTime.UtcNow;

        OrbitLogger.Info(LogCategory, $"IPC Toggle requested. CurrentState={_lifecycleService.CurrentState}, IsVisible={IsVisible}, WindowVis={Visibility}");

        // 1. Safeguard against overlapping animations: safely ignore while transitioning
        if (_lifecycleService.CurrentState == CompanionState.Expanding ||
            _lifecycleService.CurrentState == CompanionState.Collapsing)
        {
            OrbitLogger.Debug(LogCategory, "Ignored toggle request while companion is transitioning.");
            return;
        }

        // 2. If the mascot is currently hidden (via Hide button, tray, or window state), bring it to focus on screen
        if (_lifecycleService.CurrentState == CompanionState.Hidden || !IsVisible || Visibility != Visibility.Visible)
        {
            OrbitLogger.Info(LogCategory, "Mascot is hidden. Bringing mascot to focus on screen.");
            _lifecycleService.Show();
            Show();
            Activate();
            _overlayService.BringToFront(_hwnd);
            _overlayService.ReapplyTopmost(_hwnd);
            return;
        }

        // 3. If the mascot is currently visible on screen (Collapsed or Expanded), hide it (matching the Hide button)
        OrbitLogger.Info(LogCategory, "Mascot is visible on screen. Hiding mascot.");
        _lifecycleService.Hide();
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
                    PillMascot.IsExpanded = false;
                    PillMascot.IsEmbeddedInNotch = true;
                    UpdateCompactNotchGlow(_currentAgentState);
                    IslandPanel.ResetToHomeView();
                    _overlayService.BringToFront(_hwnd);
                    break;
            }
        });
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateUnifiedIslandGeometry(sizeInfo.NewSize.Width, sizeInfo.NewSize.Height);
    }

    #region Mouse Tracking and Click Handling

    private void OnWindowMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(PillMascot);
        PillMascot.TrackCursor(pos);
    }

    private void OnWindowMouseLeave(object sender, MouseEventArgs e)
    {
        PillMascot.OnWindowMouseLeave();
    }

    private void OnMascotClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            if (_lifecycleService.CurrentState == CompanionState.Collapsed)
            {
                OrbitLogger.Info(LogCategory, "Collapsed pill clicked. Initiating expansion.");
                _lifecycleService.RequestExpand();
            }
            else if (_lifecycleService.CurrentState == CompanionState.Expanded)
            {
                OrbitLogger.Info(LogCategory, "Expanded mascot clicked. Initiating collapse.");
                _lifecycleService.RequestCollapse();
            }
        }
    }

    private void OnIslandCollapseRequested()
    {
        OrbitLogger.Info(LogCategory, "Expanded island collapse requested. Initiating collapse.");
        _lifecycleService.RequestCollapse();
    }

    #endregion

    #region Two-Stage Expansion and Collapse

    private void ExecuteTwoStageExpansion()
    {
        OrbitLogger.Info(LogCategory, "Starting continuous two-stage expansion.");

        var primary = _monitorService.GetPrimaryMonitor();
        var (collapsedLeft, topY) = _monitorService.GetTopCenterPosition(primary, CollapsedWidth, TopMargin);
        var (expandedLeft, _) = _monitorService.GetTopCenterPosition(primary, ExpandedWidth, TopMargin);

        // Ensure window starts aligned to top-center
        Left = collapsedLeft;
        Top = topY;
        Width = CollapsedWidth;
        Height = CollapsedHeight;

        // ExpandedView becomes visible with 0 opacity ready for Stage 2
        PillMascot.IsExpanded = true;
        ExpandedView.Visibility = Visibility.Visible;
        ExpandedView.Opacity = 0.0;
        IslandPanel.SetBodyOpacity(0.0);

        if (_currentAgentState == Orbit.Models.AgentState.Working)
        {
            StartBinaryStreamAnimation();
        }

        var stage1Duration = TimeSpan.FromMilliseconds(220);
        var stage2Duration = TimeSpan.FromMilliseconds(240);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        // STAGE 1: Horizontal Expansion (Width expands 160 -> 480, Left shifts to maintain center)
        var widthAnim = new DoubleAnimation(ExpandedWidth, stage1Duration) { EasingFunction = ease };
        var leftAnim = new DoubleAnimation(expandedLeft, stage1Duration) { EasingFunction = ease };

        widthAnim.Completed += (s, e) =>
        {
            double targetExpandedHeight = IslandPanel.CalculateHomeDesiredHeight();

            // STAGE 2: Vertical Expansion (Height expands 38 -> content height downwards)
            var heightAnim = new DoubleAnimation(targetExpandedHeight, stage2Duration) { EasingFunction = ease };

            // Content smoothly fades in during Stage 2
            var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease };
            ExpandedView.BeginAnimation(OpacityProperty, fadeIn);
            IslandPanel.AnimateBodyOpacity(1.0, TimeSpan.FromMilliseconds(200));

            heightAnim.Completed += (s2, e2) =>
            {
                Width = ExpandedWidth;
                Height = targetExpandedHeight;
                Left = expandedLeft;
                Top = topY;

                BeginAnimation(WidthProperty, null);
                BeginAnimation(HeightProperty, null);
                BeginAnimation(LeftProperty, null);

                UpdateUnifiedIslandGeometry(ExpandedWidth, targetExpandedHeight);

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
        OrbitLogger.Info(LogCategory, "Starting continuous two-stage collapse.");

        PillMascot.IsExpanded = false;
        IslandPanel.ResetToHomeView();

        var primary = _monitorService.GetPrimaryMonitor();
        var (collapsedLeft, topY) = _monitorService.GetTopCenterPosition(primary, CollapsedWidth, TopMargin);
        var (expandedLeft, _) = _monitorService.GetTopCenterPosition(primary, ExpandedWidth, TopMargin);

        var stage1Duration = TimeSpan.FromMilliseconds(180);
        var stage2Duration = TimeSpan.FromMilliseconds(200);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        // STAGE 1: Vertical Collapse (Height reduces from expanded -> 38, cards fade out)
        var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(120)) { EasingFunction = ease };
        ExpandedView.BeginAnimation(OpacityProperty, fadeOut);
        IslandPanel.AnimateBodyOpacity(0.0, TimeSpan.FromMilliseconds(120));

        var heightAnim = new DoubleAnimation(CollapsedHeight, stage1Duration) { EasingFunction = ease };

        heightAnim.Completed += (s, e) =>
        {
            ExpandedView.Visibility = Visibility.Collapsed;

            // STAGE 2: Horizontal Collapse (Width reduces 480 -> 160, Left shifts back to collapsed center)
            var widthAnim = new DoubleAnimation(CollapsedWidth, stage2Duration) { EasingFunction = ease };
            var leftAnim = new DoubleAnimation(collapsedLeft, stage2Duration) { EasingFunction = ease };

            widthAnim.Completed += (s2, e2) =>
            {
                Width = CollapsedWidth;
                Height = CollapsedHeight;
                Left = collapsedLeft;
                Top = topY;

                BeginAnimation(WidthProperty, null);
                BeginAnimation(HeightProperty, null);
                BeginAnimation(LeftProperty, null);

                UpdateUnifiedIslandGeometry(CollapsedWidth, CollapsedHeight);

                PillMascot.IsEmbeddedInNotch = true;
                UpdateCompactNotchGlow(_currentAgentState);

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

    /// <summary>
    /// Dynamically regenerates StreamGeometry for the continuous morphing island background and perimeter outline.
    /// Handles every stage of animation seamlessly from the compact notch to the expanded dashboard.
    /// </summary>
    private void UpdateUnifiedIslandGeometry(double width, double height)
    {
        if (width < 60 || height < 20) return;

        var (bgGeom, outlineGeom, notchShelfGeom) = BuildContourGeometries(width, height);
        if (CollapsedNotchBg != null)
        {
            CollapsedNotchBg.Data = bgGeom;
        }
        if (CollapsedNotchOutline != null)
        {
            CollapsedNotchOutline.Data = outlineGeom;
        }
        if (MascotNotchBoundaryIndicator != null)
        {
            MascotNotchBoundaryIndicator.Data = notchShelfGeom;
        }
    }

    private (Geometry bg, Geometry outline, Geometry notchShelf) BuildContourGeometries(double width, double height)
    {
        double cx = width / 2.0;
        double x1 = cx - 62.0;
        double x2 = cx + 62.0;

        // Dedicated notch shelf line: faint indication of the mascot notch boundary
        var notchShelf = new StreamGeometry();
        using (var ctx = notchShelf.Open())
        {
            ctx.BeginFigure(new Point(x1, 0), isFilled: false, isClosed: false);
            ctx.BezierTo(new Point(cx - 55, 0), new Point(cx - 47, 7), new Point(cx - 44, 14), true, true);
            ctx.BezierTo(new Point(cx - 41, 21), new Point(cx - 34, 30), new Point(cx - 26, 30), true, true);
            ctx.LineTo(new Point(cx + 26, 30), true, true);
            ctx.BezierTo(new Point(cx + 34, 30), new Point(cx + 41, 21), new Point(cx + 44, 14), true, true);
            ctx.BezierTo(new Point(cx + 47, 7), new Point(cx + 55, 0), new Point(x2, 0), true, true);
        }
        notchShelf.Freeze();

        if (width <= 165.0 && height <= 42.0)
        {
            // Exact approved compact notch coordinates centered at cx = 80
            var bg = new StreamGeometry();
            using (var ctx = bg.Open())
            {
                ctx.BeginFigure(new Point(0, 0), isFilled: true, isClosed: true);
                ctx.LineTo(new Point(x1, 0), true, true);
                ctx.BezierTo(new Point(cx - 55, 0), new Point(cx - 47, 7), new Point(cx - 44, 14), true, true);
                ctx.BezierTo(new Point(cx - 41, 21), new Point(cx - 34, 30), new Point(cx - 26, 30), true, true);
                ctx.LineTo(new Point(cx + 26, 30), true, true);
                ctx.BezierTo(new Point(cx + 34, 30), new Point(cx + 41, 21), new Point(cx + 44, 14), true, true);
                ctx.BezierTo(new Point(cx + 47, 7), new Point(cx + 55, 0), new Point(x2, 0), true, true);
                ctx.LineTo(new Point(width, 0), true, true);
            }
            bg.Freeze();

            var outline = new StreamGeometry();
            using (var ctx = outline.Open())
            {
                ctx.BeginFigure(new Point(0, 0), isFilled: false, isClosed: false);
                ctx.LineTo(new Point(x1, 0), true, true);
                ctx.BezierTo(new Point(cx - 55, 0), new Point(cx - 47, 7), new Point(cx - 44, 14), true, true);
                ctx.BezierTo(new Point(cx - 41, 21), new Point(cx - 34, 30), new Point(cx - 26, 30), true, true);
                ctx.LineTo(new Point(cx + 26, 30), true, true);
                ctx.BezierTo(new Point(cx + 34, 30), new Point(cx + 41, 21), new Point(cx + 44, 14), true, true);
                ctx.BezierTo(new Point(cx + 47, 7), new Point(cx + 55, 0), new Point(x2, 0), true, true);
                ctx.LineTo(new Point(width, 0), true, true);
            }
            outline.Freeze();

            return (bg, outline, notchShelf);
        }
        else
        {
            // Morphing / Expanded island card contour
            double r = Math.Min(18.0, Math.Min(width / 6.0, height / 3.0));

            var bg = new StreamGeometry();
            using (var ctx = bg.Open())
            {
                // Solid unified card background across the entire width and height, retaining signature black
                ctx.BeginFigure(new Point(width - r, 0), isFilled: true, isClosed: true);

                // Right edge to bottom-right corner
                ctx.ArcTo(new Point(width, r), new System.Windows.Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
                ctx.LineTo(new Point(width, height - r), true, true);
                ctx.ArcTo(new Point(width - r, height), new System.Windows.Size(r, r), 0, false, SweepDirection.Clockwise, true, true);

                // Bottom edge to bottom-left corner
                ctx.LineTo(new Point(r, height), true, true);
                ctx.ArcTo(new Point(0, height - r), new System.Windows.Size(r, r), 0, false, SweepDirection.Clockwise, true, true);

                // Left edge to top-left corner
                ctx.LineTo(new Point(0, r), true, true);
                ctx.ArcTo(new Point(r, 0), new System.Windows.Size(r, r), 0, false, SweepDirection.Clockwise, true, true);

                // Top edge back to top-right
                ctx.LineTo(new Point(width - r, 0), true, true);
            }
            bg.Freeze();

            var outline = new StreamGeometry();
            using (var ctx = outline.Open())
            {
                ctx.BeginFigure(new Point(width - r, 0), isFilled: false, isClosed: false);

                ctx.ArcTo(new Point(width, r), new System.Windows.Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
                ctx.LineTo(new Point(width, height - r), true, true);
                ctx.ArcTo(new Point(width - r, height), new System.Windows.Size(r, r), 0, false, SweepDirection.Clockwise, true, true);

                ctx.LineTo(new Point(r, height), true, true);
                ctx.ArcTo(new Point(0, height - r), new System.Windows.Size(r, r), 0, false, SweepDirection.Clockwise, true, true);

                ctx.LineTo(new Point(0, r), true, true);
                ctx.ArcTo(new Point(r, 0), new System.Windows.Size(r, r), 0, false, SweepDirection.Clockwise, true, true);

                ctx.LineTo(new Point(width - r, 0), true, true);
            }
            outline.Freeze();

            return (bg, outline, notchShelf);
        }
    }

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
        UpdateCompactNotchGlow(_currentAgentState);
    }

    /// <summary>
    /// Updates the compact screen-top carved notch perimeter glow according to real-time AI status.
    /// Perfectly synchronizes the glowing purple, emerald green, and alert red micro-interactions.
    /// In the Completed (DONE) state, turns the bottom border of the mascot notch green for the duration.
    /// </summary>
    private void UpdateCompactNotchGlow(Orbit.Models.AgentState state)
    {
        if (CollapsedNotchGlow == null) return;
        var colorEase = new SineEase { EasingMode = EasingMode.EaseInOut };
        bool glowEnabled = _settingsService.CurrentSettings.EyeGlowEnabled;

        switch (state)
        {
            case Orbit.Models.AgentState.Working:
                StartBinaryStreamAnimation();
                if (CollapsedNotchOutline != null) CollapsedNotchOutline.Stroke = BrushPurpleNotchOutline;
                if (MascotNotchBoundaryIndicator != null) MascotNotchBoundaryIndicator.Stroke = BrushPurpleNotchShelf;
                CollapsedNotchGlow.Color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#C084FC");
                CollapsedNotchGlow.BlurRadius = 10.0;
                if (MascotNotchBoundaryGlow != null)
                {
                    MascotNotchBoundaryGlow.Color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#C084FC");
                    MascotNotchBoundaryGlow.BlurRadius = 6.0;
                    MascotNotchBoundaryGlow.Opacity = 0.25;
                }
                if (glowEnabled)
                {
                    var workAnim = new DoubleAnimation(0.20, 0.40, TimeSpan.FromSeconds(1.3))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = colorEase
                    };
                    CollapsedNotchGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, workAnim);
                }
                break;

            case Orbit.Models.AgentState.Completed:
                StopBinaryStreamAnimation();
                // DONE State: Transition the entire bottom border of the mascot notch to emerald green (#10B981)
                if (CollapsedNotchOutline != null) CollapsedNotchOutline.Stroke = BrushGreenNotchOutline;
                if (MascotNotchBoundaryIndicator != null) MascotNotchBoundaryIndicator.Stroke = BrushGreenNotchShelf;
                CollapsedNotchGlow.Color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#10B981");
                CollapsedNotchGlow.BlurRadius = 10.0;
                if (MascotNotchBoundaryGlow != null)
                {
                    MascotNotchBoundaryGlow.Color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#10B981");
                    MascotNotchBoundaryGlow.BlurRadius = 8.0;
                    MascotNotchBoundaryGlow.Opacity = 0.35;
                }
                if (glowEnabled)
                {
                    var doneAnim = new DoubleAnimation(0.28, 0.50, TimeSpan.FromSeconds(1.5))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = colorEase
                    };
                    CollapsedNotchGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, doneAnim);
                }
                else
                {
                    CollapsedNotchGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, null);
                    CollapsedNotchGlow.Opacity = 0.25;
                }
                break;

            case Orbit.Models.AgentState.Attention:
            case Orbit.Models.AgentState.Error:
                StopBinaryStreamAnimation();
                if (CollapsedNotchOutline != null) CollapsedNotchOutline.Stroke = BrushRedNotchOutline;
                if (MascotNotchBoundaryIndicator != null) MascotNotchBoundaryIndicator.Stroke = BrushRedNotchShelf;
                CollapsedNotchGlow.Color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#EF4444");
                CollapsedNotchGlow.BlurRadius = 10.0;
                if (MascotNotchBoundaryGlow != null)
                {
                    MascotNotchBoundaryGlow.Color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#EF4444");
                    MascotNotchBoundaryGlow.BlurRadius = 6.0;
                    MascotNotchBoundaryGlow.Opacity = 0.30;
                }
                if (glowEnabled)
                {
                    var attnAnim = new DoubleAnimation(0.25, 0.45, TimeSpan.FromSeconds(1.0))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = colorEase
                    };
                    CollapsedNotchGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, attnAnim);
                }
                break;

            default:
                StopBinaryStreamAnimation();
                if (CollapsedNotchOutline != null) CollapsedNotchOutline.Stroke = BrushPurpleNotchOutline;
                if (MascotNotchBoundaryIndicator != null) MascotNotchBoundaryIndicator.Stroke = BrushPurpleNotchShelf;
                CollapsedNotchGlow.Color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#A855F7");
                CollapsedNotchGlow.BlurRadius = 6.0;
                if (MascotNotchBoundaryGlow != null)
                {
                    MascotNotchBoundaryGlow.Color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#A855F7");
                    MascotNotchBoundaryGlow.BlurRadius = 4.0;
                    MascotNotchBoundaryGlow.Opacity = 0.20;
                }
                if (glowEnabled)
                {
                    var idleAnim = new DoubleAnimation(0.12, 0.22, TimeSpan.FromSeconds(3.0))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = colorEase
                    };
                    CollapsedNotchGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, idleAnim);
                }
                else
                {
                    CollapsedNotchGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, null);
                    CollapsedNotchGlow.Opacity = 0.0;
                }
                break;
        }
    }

    private bool _isBinaryStreamActive = false;

    /// <summary>
    /// Starts the subtle vertical digital binary stream animation during the mascot's Working state.
    /// Hardware-accelerated and strictly clipped inside the screen-top notch cradle.
    /// </summary>
    private void StartBinaryStreamAnimation()
    {
        if (BinaryStreamOverlay == null) return;
        _isBinaryStreamActive = true;

        BinaryStreamOverlay.Visibility = Visibility.Visible;
        var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(220));
        BinaryStreamOverlay.BeginAnimation(OpacityProperty, fadeIn);

        // Hardware-accelerated 6-column vertical digital binary stream
        var animL1 = new DoubleAnimation(-54.0, 0.0, TimeSpan.FromSeconds(3.5)) { RepeatBehavior = RepeatBehavior.Forever };
        var animL2 = new DoubleAnimation(-54.0, 0.0, TimeSpan.FromSeconds(2.6)) { RepeatBehavior = RepeatBehavior.Forever };
        var animL3 = new DoubleAnimation(-54.0, 0.0, TimeSpan.FromSeconds(4.0)) { RepeatBehavior = RepeatBehavior.Forever };
        var animR3 = new DoubleAnimation(-54.0, 0.0, TimeSpan.FromSeconds(3.8)) { RepeatBehavior = RepeatBehavior.Forever };
        var animR2 = new DoubleAnimation(-54.0, 0.0, TimeSpan.FromSeconds(2.7)) { RepeatBehavior = RepeatBehavior.Forever };
        var animR1 = new DoubleAnimation(-54.0, 0.0, TimeSpan.FromSeconds(3.4)) { RepeatBehavior = RepeatBehavior.Forever };

        StreamTransformL1?.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, animL1);
        StreamTransformL2?.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, animL2);
        StreamTransformL3?.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, animL3);
        StreamTransformR3?.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, animR3);
        StreamTransformR2?.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, animR2);
        StreamTransformR1?.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, animR1);
    }

    /// <summary>
    /// Immediately halts binary stream animations and hides the overlay when Working state ends.
    /// Ensures zero background timer/rendering overhead while Idle.
    /// </summary>
    private void StopBinaryStreamAnimation()
    {
        if (!_isBinaryStreamActive && (BinaryStreamOverlay == null || BinaryStreamOverlay.Visibility == Visibility.Collapsed)) return;
        _isBinaryStreamActive = false;

        if (BinaryStreamOverlay != null)
        {
            var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(180));
            fadeOut.Completed += (s, e) =>
            {
                if (!_isBinaryStreamActive)
                {
                    BinaryStreamOverlay.Visibility = Visibility.Collapsed;
                    StreamTransformL1?.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
                    StreamTransformL2?.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
                    StreamTransformL3?.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
                    StreamTransformR3?.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
                    StreamTransformR2?.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
                    StreamTransformR1?.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
                }
            };
            BinaryStreamOverlay.BeginAnimation(OpacityProperty, fadeOut);
        }
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
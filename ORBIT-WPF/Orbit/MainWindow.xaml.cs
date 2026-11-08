using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Orbit.Core;
using Orbit.Integrations.Desktop;
using Orbit.Models;
using Orbit.Platform.Windows;
using Orbit.Services;
using Orbit.Services.AiObserver;
using Orbit.Services.Commands;
using Orbit.Services.DevWorkspace;
using Orbit.Services.FileDock;
using Orbit.Services.Hubs;
using Orbit.Services.Notes;
using Orbit.Services.SystemControls;
using Orbit.Services.SystemMonitor;
using Orbit.Services.Voice;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using DataObject = System.Windows.DataObject;
using DataFormats = System.Windows.DataFormats;
using Border = System.Windows.Controls.Border;
using Brushes = System.Windows.Media.Brushes;

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
    public const double QuickActionsWidth = 310.0;
    public const double QuickActionsHeight = 44.0;
    public const double FileDockTotalHeight = 150.0;
    public const double HoverActivationSeconds = 2.0;
    public const double CollapseDelayMilliseconds = 450.0;
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
    private readonly IFileDockService _fileDockService;
    private readonly IVoiceSessionManager _voiceSessionManager;
    private readonly ICommandParser? _commandParser;
    private readonly IVoiceCommandExecutor? _commandExecutor;

    private IntPtr _hwnd = IntPtr.Zero;
    private Orbit.Models.AgentState _currentAgentState = Orbit.Models.AgentState.Idle;
    private string _currentAgentMessage = string.Empty;

    // File Dock & Quick Actions state management
    private bool _isFileDockOpen = false;
    private bool _isFileDockTransitioning = false;
    private bool _isVoiceModeActive = false;
    public bool IsFileDockOpen => _isFileDockOpen;
    public bool IsVoiceModeActive => _isVoiceModeActive;
    public IFileDockService FileDockService => _fileDockService;
    public IVoiceSessionManager VoiceSessionManager => _voiceSessionManager;

    // Hover-based quick actions expansion state management
    private readonly DispatcherTimer _hoverTimer;
    private readonly DispatcherTimer _collapseDelayTimer;
    private bool _isHovering = false;
    private bool _isQuickActionsOpen = false;
    private bool _isHoverExpanding = false;
    private bool _isHoverCollapsing = false;
    private bool _isTransitioningToFullExpand = false;

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
        ISystemMonitorService? systemMonitorService = null,
        IFileDockService? fileDockService = null,
        IVoiceSessionManager? voiceSessionManager = null,
        ICommandParser? commandParser = null,
        IVoiceCommandExecutor? commandExecutor = null)
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
        _fileDockService = fileDockService ?? new FileDockService();
        _voiceSessionManager = voiceSessionManager ?? new VoiceSessionManager(new WasapiAudioCaptureService());
        _commandParser = commandParser;
        _commandExecutor = commandExecutor;

        // Wire voice session events
        _voiceSessionManager.AudioLevelUpdated += OnVoiceAudioLevelUpdated;
        _voiceSessionManager.StateChanged += OnVoiceSessionStateChanged;
        _voiceSessionManager.UtteranceCompleted += OnVoiceUtteranceCompleted;
        _voiceSessionManager.TranscriptionReceived += OnVoiceTranscriptionReceived;

        // Initialize 2-second continuous hover recognition timer
        _hoverTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(HoverActivationSeconds)
        };
        _hoverTimer.Tick += OnHoverTimerTick;

        // Initialize graceful collapse delay timer
        _collapseDelayTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(CollapseDelayMilliseconds)
        };
        _collapseDelayTimer.Tick += OnCollapseDelayTimerTick;

        // Wire lifecycle service events
        _lifecycleService.StateChanged += OnStateChanged;
        _lifecycleService.ResetPositionRequested += OnResetPositionRequested;
        _lifecycleService.ExitRequested += OnExitRequested;

        // Wire dedicated Left Eye click for Quick Actions tray activation
        PillMascot.LeftEyeMouseDown += OnPillMascotLeftEyeMouseDown;

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
                if (state != Orbit.Models.AgentState.Idle)
                {
                    CancelHoverTimer();
                    if (_isQuickActionsOpen && !_isTransitioningToFullExpand && !_isHoverCollapsing)
                    {
                        ExecuteHoverCollapse();
                    }
                }
                UpdateCompactNotchGlow(state);
            });
        };

        // Initialize active apps manager, system controls, dev workspace, AI agent monitor, social/work hubs, and responsive height changes
        IslandPanel.Initialize(_windowManager, _systemControlsService, _devWorkspaceService, _agentStateManager, _attentionManager, _socialHubService, _workHubService, _notesService, _systemMonitorService);
        IslandPanel.DesiredHeightChanged += OnIslandDesiredHeightChanged;
        IslandPanel.NotesModeChanged += OnNotesModeChanged;

        FileDockPanel.Initialize(_fileDockService);

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
                    if (state != Orbit.Models.AgentState.Idle)
                    {
                        CancelHoverTimer();
                        if (_isQuickActionsOpen && !_isTransitioningToFullExpand && !_isHoverCollapsing)
                        {
                            ExecuteHoverCollapse();
                        }
                    }
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

        if (_isFileDockOpen)
        {
            // If there are zero files, no need to pin the file dock, it can collapse when focus is lost!
            if (_fileDockService.Items.Count == 0)
            {
                OrbitLogger.Info(LogCategory, "File Dock has 0 files and lost focus. Collapsing to idle.");
                CloseFileDock(toQuickActions: false);
                return;
            }

            if (_fileDockService.CurrentMode == FileDockMode.Pinned)
            {
                // Pinned mode with files remains open while switching apps or dragging files elsewhere
                _overlayService.ReapplyTopmost(_hwnd);
                return;
            }
            if (_fileDockService.CurrentMode == FileDockMode.Temporary)
            {
                if (FileDockPanel != null && FileDockPanel.IsDraggingOut)
                {
                    _overlayService.ReapplyTopmost(_hwnd);
                    return;
                }
                CloseFileDock(toQuickActions: false);
                return;
            }
        }

        // CRITICAL: Do NOT collapse or exit if Voice Mode is actively listening!
        if (_isVoiceModeActive)
        {
            _overlayService.ReapplyTopmost(_hwnd);
            return;
        }

        if (_isQuickActionsOpen && !_isTransitioningToFullExpand)
        {
            ExecuteHoverCollapse();
            return;
        }

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
            _lifecycleService.CurrentState == CompanionState.Collapsing ||
            _isHoverExpanding || _isHoverCollapsing || _isTransitioningToFullExpand)
        {
            OrbitLogger.Debug(LogCategory, "Ignored toggle request while companion is transitioning.");
            return;
        }

        // 2. If the mascot is currently hidden (via Hide button, tray, or window state), bring it to focus on screen
        if (_lifecycleService.CurrentState == CompanionState.Hidden || !IsVisible || Visibility != Visibility.Visible)
        {
            OrbitLogger.Info(LogCategory, "Mascot is hidden. Bringing mascot to focus on screen.");
            CancelHoverTimer();
            CancelCollapseDelay();

            // Force close any open File Dock, Voice Assistant, or Quick Actions bar
            if (_isVoiceModeActive)
            {
                ExitVoiceMode(cancelCapture: true, returnToMascot: true, immediate: true);
            }

            if (_isFileDockOpen)
            {
                _isFileDockOpen = false;
                _fileDockService.CurrentMode = FileDockMode.Closed;
                FileDockPanel.Visibility = Visibility.Collapsed;
                FileDockPanel.Opacity = 0.0;
                FileDockPanel.IsHitTestVisible = false;
                FileDockPanel.SetDragPreviewActive(false);
                HighlightFileDockButton(false);
            }

            _isQuickActionsOpen = false;
            QuickActionsBar.Visibility = Visibility.Collapsed;
            QuickActionsBar.Opacity = 0.0;
            QuickActionsBar.IsHitTestVisible = false;

            // Reset geometry to collapsed notch
            BeginAnimation(WidthProperty, null);
            BeginAnimation(HeightProperty, null);
            BeginAnimation(LeftProperty, null);
            Width = CollapsedWidth;
            Height = CollapsedHeight;
            PositionAtTopCenter(CollapsedWidth);
            UpdateUnifiedIslandGeometry(CollapsedWidth, CollapsedHeight);

            // Clear all animation locks on MascotContainerGrid & restore eyes completely
            MascotContainerGrid.BeginAnimation(OpacityProperty, null);
            MascotContainerGrid.Visibility = Visibility.Visible;
            MascotContainerGrid.Opacity = 1.0;
            MascotContainerGrid.IsHitTestVisible = true;

            if (_currentAgentState == Orbit.Models.AgentState.Working)
            {
                PillMascot.SetAiState(Orbit.Models.AgentState.Working, _currentAgentMessage);
            }
            else if (_currentAgentState == Orbit.Models.AgentState.Completed || PillMascot.IsHoldingDoneState)
            {
                PillMascot.SetAiState(Orbit.Models.AgentState.Completed, _currentAgentMessage);
            }
            else if (_currentAgentState == Orbit.Models.AgentState.Attention || _currentAgentState == Orbit.Models.AgentState.Error)
            {
                PillMascot.SetAiState(_currentAgentState, _currentAgentMessage);
            }
            else
            {
                PillMascot.RestoreNormalEyes(TimeSpan.Zero);
            }

            _lifecycleService.Show();
            Show();
            Activate();
            _overlayService.BringToFront(_hwnd);
            _overlayService.ReapplyTopmost(_hwnd);
            return;
        }

        // 3. If the mascot is currently visible on screen (Collapsed or Expanded), hide it (matching the Hide button)
        OrbitLogger.Info(LogCategory, "Mascot is visible on screen. Hiding mascot.");
        CancelHoverTimer();
        CancelCollapseDelay();
        if (_isVoiceModeActive)
        {
            ExitVoiceMode(cancelCapture: true, returnToMascot: true, immediate: true);
        }
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
                    CancelHoverTimer();
                    CancelCollapseDelay();

                    if (_isVoiceModeActive)
                    {
                        ExitVoiceMode(cancelCapture: true, returnToMascot: true, immediate: true);
                    }

                    if (_isFileDockOpen)
                    {
                        _isFileDockOpen = false;
                        _fileDockService.CurrentMode = FileDockMode.Closed;
                        FileDockPanel.Visibility = Visibility.Collapsed;
                        FileDockPanel.Opacity = 0.0;
                        FileDockPanel.IsHitTestVisible = false;
                        FileDockPanel.SetDragPreviewActive(false);
                        HighlightFileDockButton(false);
                    }

                    if (_isQuickActionsOpen)
                    {
                        _isQuickActionsOpen = false;
                        QuickActionsBar.Visibility = Visibility.Collapsed;
                        QuickActionsBar.Opacity = 0.0;
                        QuickActionsBar.IsHitTestVisible = false;
                    }

                    // Reset window geometry so next Show() restores directly as the top-centre pill
                    BeginAnimation(WidthProperty, null);
                    BeginAnimation(HeightProperty, null);
                    BeginAnimation(LeftProperty, null);
                    Width = CollapsedWidth;
                    Height = CollapsedHeight;
                    PositionAtTopCenter(CollapsedWidth);
                    UpdateUnifiedIslandGeometry(CollapsedWidth, CollapsedHeight);

                    // Clear any animation locks on MascotContainerGrid & restore eyes
                    MascotContainerGrid.BeginAnimation(OpacityProperty, null);
                    MascotContainerGrid.Visibility = Visibility.Visible;
                    MascotContainerGrid.Opacity = 1.0;
                    MascotContainerGrid.IsHitTestVisible = true;
                    PillMascot.RestoreNormalEyes(TimeSpan.Zero);

                    Hide();
                    IslandPanel.ResetToHomeView();
                    CollapsedView.Visibility = Visibility.Visible;
                    ExpandedView.Visibility = Visibility.Collapsed;
                    break;

                case CompanionState.Collapsed:
                    CancelHoverTimer();
                    CancelCollapseDelay();
                    _isQuickActionsOpen = false;
                    _isHoverExpanding = false;
                    _isHoverCollapsing = false;
                    _isTransitioningToFullExpand = false;

                    if (_isVoiceModeActive)
                    {
                        ExitVoiceMode(cancelCapture: true, returnToMascot: true, immediate: true);
                    }

                    if (_isFileDockOpen)
                    {
                        _isFileDockOpen = false;
                        _fileDockService.CurrentMode = FileDockMode.Closed;
                        FileDockPanel.Visibility = Visibility.Collapsed;
                        FileDockPanel.Opacity = 0.0;
                        FileDockPanel.IsHitTestVisible = false;
                        FileDockPanel.SetDragPreviewActive(false);
                        HighlightFileDockButton(false);
                    }

                    QuickActionsBar.Visibility = Visibility.Collapsed;
                    QuickActionsBar.Opacity = 0.0;
                    QuickActionsBar.IsHitTestVisible = false;

                    // Reset window geometry so it is strictly the top-centre pill
                    BeginAnimation(WidthProperty, null);
                    BeginAnimation(HeightProperty, null);
                    BeginAnimation(LeftProperty, null);
                    Width = CollapsedWidth;
                    Height = CollapsedHeight;
                    PositionAtTopCenter(CollapsedWidth);
                    UpdateUnifiedIslandGeometry(CollapsedWidth, CollapsedHeight);

                    // Clear any animation locks on MascotContainerGrid & restore eyes
                    MascotContainerGrid.BeginAnimation(OpacityProperty, null);
                    MascotContainerGrid.Visibility = Visibility.Visible;
                    MascotContainerGrid.Opacity = 1.0;
                    MascotContainerGrid.IsHitTestVisible = true;

                    Show();
                    PillMascot.IsExpanded = false;
                    PillMascot.IsEmbeddedInNotch = true;

                    if (_currentAgentState == Orbit.Models.AgentState.Working)
                    {
                        PillMascot.SetAiState(Orbit.Models.AgentState.Working, _currentAgentMessage);
                    }
                    else if (_currentAgentState == Orbit.Models.AgentState.Completed || PillMascot.IsHoldingDoneState)
                    {
                        PillMascot.SetAiState(Orbit.Models.AgentState.Completed, _currentAgentMessage);
                    }
                    else if (_currentAgentState == Orbit.Models.AgentState.Attention || _currentAgentState == Orbit.Models.AgentState.Error)
                    {
                        PillMascot.SetAiState(_currentAgentState, _currentAgentMessage);
                    }
                    else
                    {
                        PillMascot.RestoreNormalEyes(TimeSpan.Zero);
                    }

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
        if (_isFileDockOpen || _isQuickActionsOpen)
        {
            CancelCollapseDelay();
        }
        else
        {
            var pos = e.GetPosition(PillMascot);
            PillMascot.TrackCursor(pos);
        }
    }

    private void OnWindowMouseLeave(object sender, MouseEventArgs e)
    {
        PillMascot.OnWindowMouseLeave();

        if (_isHovering && !_isQuickActionsOpen && !_isFileDockOpen)
        {
            CancelHoverTimer();
        }

        if (_isFileDockOpen)
        {
            if (FileDockPanel != null && FileDockPanel.IsDraggingOut)
            {
                return; // Don't collapse during active drag-out
            }

            // If there are zero files, no need to pin: collapse when cursor leaves
            if (_fileDockService.Items.Count == 0)
            {
                if (!IsMouseOver && FileDockPanel?.IsMouseOver != true && QuickActionsBar?.IsMouseOver != true)
                {
                    ScheduleHoverCollapse();
                }
                return;
            }

            if (_fileDockService.CurrentMode == FileDockMode.Pinned)
            {
                return; // Pinned mode with files never collapses on mouse leave
            }

            if (_fileDockService.CurrentMode == FileDockMode.Temporary)
            {
                if (!IsMouseOver && FileDockPanel?.IsMouseOver != true && QuickActionsBar?.IsMouseOver != true)
                {
                    CloseFileDock(toQuickActions: false);
                }
                return;
            }
        }

        // CRITICAL: While Voice Mode is actively listening, do NOT collapse on window mouse leave!
        if (_isVoiceModeActive)
        {
            return;
        }

        if (_isQuickActionsOpen && !_isTransitioningToFullExpand && !_isHoverCollapsing)
        {
            if (!IsMouseOver && !QuickActionsBar.IsMouseOver)
            {
                ScheduleHoverCollapse();
            }
        }
    }

    private void OnPillMascotLeftEyeMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        // Always mark handled so clicking the left eye never bubbles into general mascot expansion
        e.Handled = true;

        // Tray activation only permitted in idle collapsed mascot form
        if (_lifecycleService.CurrentState != CompanionState.Collapsed)
        {
            return;
        }

        if (_currentAgentState != Orbit.Models.AgentState.Idle || PillMascot.IsHoldingDoneState)
        {
            return;
        }

        if (_isQuickActionsOpen || _isHoverExpanding || _isHoverCollapsing || _isTransitioningToFullExpand || _isVoiceModeActive || _isFileDockOpen)
        {
            return;
        }

        OrbitLogger.Info(LogCategory, "Left eye clicked in collapsed idle state. Expanding Quick Actions tray.");
        ExecuteHoverExpansion();
    }

    private void OnMascotClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            if (_isFileDockOpen)
            {
                OrbitLogger.Info(LogCategory, "Mascot clicked while File Dock open. Closing dock and returning to normal ORBIT.");
                CloseFileDock(toQuickActions: false);
                return;
            }

            if (_lifecycleService.CurrentState == CompanionState.Collapsed)
            {
                CancelHoverTimer();
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

    #region Hover Recognition and Quick Actions

    private void StartHoverTimer()
    {
        // Allow hover quick actions ONLY in Idle state (ignore working, done, error, attention, etc.)
        if (_currentAgentState != Orbit.Models.AgentState.Idle || PillMascot.IsHoldingDoneState)
        {
            return;
        }

        if (_lifecycleService.CurrentState != CompanionState.Collapsed) return;
        if (_isQuickActionsOpen || _isHoverExpanding || _isHoverCollapsing || _isTransitioningToFullExpand || _isHovering) return;

        _isHovering = true;
        _hoverTimer.Stop();
        _hoverTimer.Interval = TimeSpan.FromSeconds(HoverActivationSeconds);
        _hoverTimer.Start();
        OrbitLogger.Info(LogCategory, $"Mascot hover recognized. Started {HoverActivationSeconds}-second delay timer.");
    }

    private void OnMascotMouseEnter(object sender, MouseEventArgs e)
    {
        // Hover-based activation removed; Quick Actions tray expansion is now triggered specifically by clicking the left eye.
    }

    private void OnMascotMouseLeave(object sender, MouseEventArgs e)
    {
        if (_isHovering && !_isQuickActionsOpen && !_isHoverExpanding)
        {
            var pos = e.GetPosition(this);
            if (pos.X < 0 || pos.X > ActualWidth || pos.Y < 0 || pos.Y > ActualHeight)
            {
                CancelHoverTimer();
            }
        }
    }

    private void OnHoverTimerTick(object? sender, EventArgs e)
    {
        _hoverTimer.Stop();
        _isHovering = false;

        OrbitLogger.Info(LogCategory, $"Hover timer expired! State={_lifecycleService.CurrentState}, AgentState={_currentAgentState}, QuickActionsOpen={_isQuickActionsOpen}");

        // Strictly allow hover expansion only in Idle state
        if (_currentAgentState != Orbit.Models.AgentState.Idle || PillMascot.IsHoldingDoneState) return;
        if (_lifecycleService.CurrentState != CompanionState.Collapsed) return;
        if (_isQuickActionsOpen || _isHoverExpanding || _isTransitioningToFullExpand) return;

        ExecuteHoverExpansion();
    }

    private void CancelHoverTimer()
    {
        if (_hoverTimer.IsEnabled)
        {
            _hoverTimer.Stop();
            OrbitLogger.Info(LogCategory, $"Mascot hover timer canceled (cursor left mascot before {HoverActivationSeconds} seconds).");
        }
        _isHovering = false;
        if (!_isQuickActionsOpen && !_isHoverExpanding && _currentAgentState == Orbit.Models.AgentState.Idle && !PillMascot.IsHoldingDoneState)
        {
            PillMascot.RestoreNormalEyes(TimeSpan.FromMilliseconds(150));
        }
    }

    private void ExecuteHoverExpansion()
    {
        _isHoverExpanding = true;
        OrbitLogger.Info(LogCategory, "Expanding Quick Actions tray horizontally.");

        // Guard: ensure StandardQuickActionsPanel is visible and VoiceModePanel is collapsed
        VoiceModePanel.BeginAnimation(OpacityProperty, null);
        VoiceModePanel.Visibility = Visibility.Collapsed;
        VoiceModePanel.Opacity = 0.0;
        VoiceModePanel.IsHitTestVisible = false;

        StandardQuickActionsPanel.BeginAnimation(OpacityProperty, null);
        StandardQuickActionsPanel.Visibility = Visibility.Visible;
        StandardQuickActionsPanel.Opacity = 1.0;
        StandardQuickActionsPanel.IsHitTestVisible = true;

        var primary = _monitorService.GetPrimaryMonitor();
        var (targetLeft, topY) = _monitorService.GetTopCenterPosition(primary, QuickActionsWidth, TopMargin);

        // 1. Gracefully close eyes during expansion
        PillMascot.GracefullyCloseEyes(TimeSpan.FromMilliseconds(220));

        var duration = TimeSpan.FromMilliseconds(260);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        // 2. Smoothly animate width expansion (160 -> 310), height (38 -> 42), and left position
        var widthAnim = new DoubleAnimation(QuickActionsWidth, duration) { EasingFunction = ease };
        var heightAnim = new DoubleAnimation(QuickActionsHeight, duration) { EasingFunction = ease };
        var leftAnim = new DoubleAnimation(targetLeft, duration) { EasingFunction = ease };

        // 3. Fade out mascot eyes completely once expansion finishes
        var mascotFadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(160))
        {
            BeginTime = TimeSpan.FromMilliseconds(100),
            EasingFunction = ease
        };

        // 4. Reveal QuickActionsBar
        QuickActionsBar.Visibility = Visibility.Visible;
        QuickActionsBar.Opacity = 0.0;
        QuickActionsBar.IsHitTestVisible = false;

        var actionsFadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(180))
        {
            BeginTime = TimeSpan.FromMilliseconds(120),
            EasingFunction = ease
        };

        widthAnim.Completed += (s, e) =>
        {
            Width = QuickActionsWidth;
            Height = QuickActionsHeight;
            Left = targetLeft;
            BeginAnimation(WidthProperty, null);
            BeginAnimation(HeightProperty, null);
            BeginAnimation(LeftProperty, null);

            UpdateUnifiedIslandGeometry(QuickActionsWidth, QuickActionsHeight);

            MascotContainerGrid.Visibility = Visibility.Collapsed;
            MascotContainerGrid.IsHitTestVisible = false;

            QuickActionsBar.IsHitTestVisible = true;
            QuickActionsBar.Opacity = 1.0;

            _isQuickActionsOpen = true;
            _isHoverExpanding = false;

            _overlayService.ReapplyTopmost(_hwnd);
            OrbitLogger.Info(LogCategory, "Quick Actions horizontal expansion completed.");
        };

        BeginAnimation(WidthProperty, widthAnim);
        BeginAnimation(HeightProperty, heightAnim);
        BeginAnimation(LeftProperty, leftAnim);
        MascotContainerGrid.BeginAnimation(OpacityProperty, mascotFadeOut);
        QuickActionsBar.BeginAnimation(OpacityProperty, actionsFadeIn);
    }

    private void OnQuickActionsMouseEnter(object sender, MouseEventArgs e)
    {
        CancelCollapseDelay();
    }

    private void OnQuickActionsMouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isQuickActionsOpen || _isTransitioningToFullExpand || _isHoverCollapsing) return;

        // CRITICAL: While Voice Mode is active, do not collapse when cursor leaves
        if (_isVoiceModeActive)
        {
            return;
        }

        // If dragging out, do not collapse
        if (FileDockPanel != null && FileDockPanel.IsDraggingOut)
        {
            return;
        }

        // If mouse is still anywhere over QuickActionsBar, FileDockPanel, or MainWindow, do not collapse
        if (QuickActionsBar.IsMouseOver || (FileDockPanel != null && FileDockPanel.IsMouseOver) || IsMouseOver)
        {
            return;
        }

        if (_isFileDockOpen)
        {
            // If there are zero files, allow it to collapse when cursor leaves
            if (_fileDockService.Items.Count == 0)
            {
                ScheduleHoverCollapse();
                return;
            }

            if (_fileDockService.CurrentMode == FileDockMode.Pinned)
            {
                return;
            }

            if (_fileDockService.CurrentMode == FileDockMode.Temporary)
            {
                CloseFileDock(toQuickActions: false);
                return;
            }
        }

        ScheduleHoverCollapse();
    }

    private void ScheduleHoverCollapse()
    {
        if (!_isQuickActionsOpen || _isTransitioningToFullExpand || _isHoverCollapsing) return;

        _collapseDelayTimer.Stop();
        _collapseDelayTimer.Interval = TimeSpan.FromMilliseconds(CollapseDelayMilliseconds);
        _collapseDelayTimer.Start();
        OrbitLogger.Debug(LogCategory, $"Cursor left quick actions island. Scheduled graceful collapse in {CollapseDelayMilliseconds}ms.");
    }

    private void CancelCollapseDelay()
    {
        if (_collapseDelayTimer.IsEnabled)
        {
            _collapseDelayTimer.Stop();
            OrbitLogger.Debug(LogCategory, "Cursor returned to quick actions island. Canceled scheduled collapse.");
        }
    }

    private void OnCollapseDelayTimerTick(object? sender, EventArgs e)
    {
        _collapseDelayTimer.Stop();

        // Double check mouse is not currently over window, quick actions bar, or file dock
        if (IsMouseOver || QuickActionsBar.IsMouseOver || (FileDockPanel != null && FileDockPanel.IsMouseOver))
        {
            return;
        }

        if (FileDockPanel != null && FileDockPanel.IsDraggingOut)
        {
            return;
        }

        if (_isFileDockOpen)
        {
            // If 0 files or temporary, collapse to idle!
            if (_fileDockService.Items.Count == 0 || _fileDockService.CurrentMode == FileDockMode.Temporary)
            {
                CloseFileDock(toQuickActions: false);
                return;
            }

            if (_fileDockService.CurrentMode == FileDockMode.Pinned)
            {
                return;
            }
        }

        if (_isQuickActionsOpen && !_isTransitioningToFullExpand && !_isHoverCollapsing)
        {
            ExecuteHoverCollapse();
        }
    }

    private void ExecuteHoverCollapse()
    {
        CancelCollapseDelay();
        if (_isTransitioningToFullExpand || _isHoverCollapsing || !_isQuickActionsOpen) return;

        // CRITICAL: While Voice Mode is active, do not collapse
        if (_isVoiceModeActive) return;

        if (_isFileDockOpen)
        {
            if (FileDockPanel != null && FileDockPanel.IsDraggingOut) return;

            // If items exist and pinned, keep open
            if (_fileDockService.CurrentMode == FileDockMode.Pinned && _fileDockService.Items.Count > 0)
            {
                return;
            }

            CloseFileDock(toQuickActions: false);
            return;
        }

        _isHoverCollapsing = true;
        _isQuickActionsOpen = false;
        OrbitLogger.Info(LogCategory, "Mouse left quick actions island. Collapsing to idle.");

        var primary = _monitorService.GetPrimaryMonitor();
        var (targetLeft, topY) = _monitorService.GetTopCenterPosition(primary, CollapsedWidth, TopMargin);

        // Hide quick actions
        QuickActionsBar.IsHitTestVisible = false;
        var actionsFadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(100));
        QuickActionsBar.BeginAnimation(OpacityProperty, actionsFadeOut);

        var duration = TimeSpan.FromMilliseconds(220);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        var widthAnim = new DoubleAnimation(CollapsedWidth, duration) { EasingFunction = ease };
        var heightAnim = new DoubleAnimation(CollapsedHeight, duration) { EasingFunction = ease };
        var leftAnim = new DoubleAnimation(targetLeft, duration) { EasingFunction = ease };

        // Restore mascot eyes & smile
        MascotContainerGrid.Visibility = Visibility.Visible;
        if (_currentAgentState == Orbit.Models.AgentState.Idle && !PillMascot.IsHoldingDoneState)
        {
            PillMascot.RestoreNormalEyes(TimeSpan.FromMilliseconds(180));
        }
        var mascotFadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(180))
        {
            BeginTime = TimeSpan.FromMilliseconds(60),
            EasingFunction = ease
        };
        MascotContainerGrid.BeginAnimation(OpacityProperty, mascotFadeIn);

        widthAnim.Completed += (s, e) =>
        {
            Width = CollapsedWidth;
            Height = CollapsedHeight;
            Left = targetLeft;
            BeginAnimation(WidthProperty, null);
            BeginAnimation(HeightProperty, null);
            BeginAnimation(LeftProperty, null);

            UpdateUnifiedIslandGeometry(CollapsedWidth, CollapsedHeight);

            QuickActionsBar.Visibility = Visibility.Collapsed;
            MascotContainerGrid.IsHitTestVisible = true;
            MascotContainerGrid.Opacity = 1.0;

            _isHoverCollapsing = false;
            _isHovering = false;

            _overlayService.ReapplyTopmost(_hwnd);
            OrbitLogger.Info(LogCategory, "Quick Actions collapsed back to idle.");
        };

        BeginAnimation(WidthProperty, widthAnim);
        BeginAnimation(HeightProperty, heightAnim);
        BeginAnimation(LeftProperty, leftAnim);
    }

    #region File Dock Operations & Drag-and-Drop

    public void OpenFileDock(FileDockMode mode)
    {
        if (_isFileDockTransitioning) return;
        _isFileDockTransitioning = true;
        _fileDockService.CurrentMode = mode;

        CancelHoverTimer();
        CancelCollapseDelay();
        OrbitLogger.Info(LogCategory, $"Opening File Dock in {mode} mode.");

        var primary = _monitorService.GetPrimaryMonitor();
        var (targetLeft, topY) = _monitorService.GetTopCenterPosition(primary, QuickActionsWidth, TopMargin);

        var duration = TimeSpan.FromMilliseconds(240);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        // 1. Ensure QuickActionsBar and FileDockPanel are visible
        QuickActionsBar.Visibility = Visibility.Visible;
        QuickActionsBar.IsHitTestVisible = true;
        QuickActionsBar.Opacity = 1.0;

        FileDockPanel.Visibility = Visibility.Visible;
        FileDockPanel.IsHitTestVisible = true;

        // Visual indicator on File Dock quick action button
        HighlightFileDockButton(true);

        // 2. Animate dimensions
        var widthAnim = new DoubleAnimation(QuickActionsWidth, duration) { EasingFunction = ease };
        var heightAnim = new DoubleAnimation(FileDockTotalHeight, duration) { EasingFunction = ease };
        var leftAnim = new DoubleAnimation(targetLeft, duration) { EasingFunction = ease };
        var dockFadeIn = new DoubleAnimation(1.0, duration) { EasingFunction = ease };

        if (MascotContainerGrid.Visibility == Visibility.Visible && MascotContainerGrid.Opacity > 0.0)
        {
            PillMascot.GracefullyCloseEyes(TimeSpan.FromMilliseconds(180));
            var mascotFadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease };
            MascotContainerGrid.BeginAnimation(OpacityProperty, mascotFadeOut);
        }

        heightAnim.Completed += (s, e) =>
        {
            Width = QuickActionsWidth;
            Height = FileDockTotalHeight;
            Left = targetLeft;
            BeginAnimation(WidthProperty, null);
            BeginAnimation(HeightProperty, null);
            BeginAnimation(LeftProperty, null);

            UpdateUnifiedIslandGeometry(QuickActionsWidth, FileDockTotalHeight);

            MascotContainerGrid.Visibility = Visibility.Collapsed;
            MascotContainerGrid.IsHitTestVisible = false;

            FileDockPanel.Opacity = 1.0;
            _isFileDockOpen = true;
            _isQuickActionsOpen = true;
            _isFileDockTransitioning = false;

            _overlayService.ReapplyTopmost(_hwnd);
            OrbitLogger.Info(LogCategory, $"File Dock opened successfully in {mode} mode.");
        };

        BeginAnimation(WidthProperty, widthAnim);
        BeginAnimation(HeightProperty, heightAnim);
        BeginAnimation(LeftProperty, leftAnim);
        FileDockPanel.BeginAnimation(OpacityProperty, dockFadeIn);
    }

    public void CloseFileDock(bool toQuickActions)
    {
        if (_isFileDockTransitioning) return;
        _isFileDockTransitioning = true;
        _fileDockService.CurrentMode = FileDockMode.Closed;

        HighlightFileDockButton(false);
        OrbitLogger.Info(LogCategory, $"Closing File Dock (toQuickActions: {toQuickActions}).");

        var duration = TimeSpan.FromMilliseconds(220);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        FileDockPanel.IsHitTestVisible = false;
        var dockFadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(130));
        FileDockPanel.BeginAnimation(OpacityProperty, dockFadeOut);

        if (toQuickActions)
        {
            var heightAnim = new DoubleAnimation(QuickActionsHeight, duration) { EasingFunction = ease };

            heightAnim.Completed += (s, e) =>
            {
                Height = QuickActionsHeight;
                BeginAnimation(HeightProperty, null);
                UpdateUnifiedIslandGeometry(QuickActionsWidth, QuickActionsHeight);

                FileDockPanel.Visibility = Visibility.Collapsed;
                _isFileDockOpen = false;
                _isFileDockTransitioning = false;
                _overlayService.ReapplyTopmost(_hwnd);
                OrbitLogger.Info(LogCategory, "File Dock closed back to Quick Actions bar.");
            };

            BeginAnimation(HeightProperty, heightAnim);
        }
        else
        {
            var primary = _monitorService.GetPrimaryMonitor();
            var (targetLeft, topY) = _monitorService.GetTopCenterPosition(primary, CollapsedWidth, TopMargin);

            QuickActionsBar.IsHitTestVisible = false;
            var actionsFadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(100));
            QuickActionsBar.BeginAnimation(OpacityProperty, actionsFadeOut);

            var widthAnim = new DoubleAnimation(CollapsedWidth, duration) { EasingFunction = ease };
            var heightAnim = new DoubleAnimation(CollapsedHeight, duration) { EasingFunction = ease };
            var leftAnim = new DoubleAnimation(targetLeft, duration) { EasingFunction = ease };

            MascotContainerGrid.Visibility = Visibility.Visible;
            if (_currentAgentState == Orbit.Models.AgentState.Idle && !PillMascot.IsHoldingDoneState)
            {
                PillMascot.RestoreNormalEyes(TimeSpan.FromMilliseconds(180));
            }
            var mascotFadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(180))
            {
                BeginTime = TimeSpan.FromMilliseconds(60),
                EasingFunction = ease
            };
            MascotContainerGrid.BeginAnimation(OpacityProperty, mascotFadeIn);

            widthAnim.Completed += (s, e) =>
            {
                Width = CollapsedWidth;
                Height = CollapsedHeight;
                Left = targetLeft;
                BeginAnimation(WidthProperty, null);
                BeginAnimation(HeightProperty, null);
                BeginAnimation(LeftProperty, null);

                UpdateUnifiedIslandGeometry(CollapsedWidth, CollapsedHeight);

                FileDockPanel.Visibility = Visibility.Collapsed;
                QuickActionsBar.Visibility = Visibility.Collapsed;
                MascotContainerGrid.IsHitTestVisible = true;
                MascotContainerGrid.Opacity = 1.0;

                _isFileDockOpen = false;
                _isQuickActionsOpen = false;
                _isHovering = false;
                _isFileDockTransitioning = false;

                _overlayService.ReapplyTopmost(_hwnd);
                OrbitLogger.Info(LogCategory, "File Dock closed completely to idle.");
            };

            BeginAnimation(WidthProperty, widthAnim);
            BeginAnimation(HeightProperty, heightAnim);
            BeginAnimation(LeftProperty, leftAnim);
        }
    }

    private void HighlightFileDockButton(bool active)
    {
        if (BtnQuickFileDock.Template?.FindName("Bg", BtnQuickFileDock) is Border border)
        {
            border.Background = active
                ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x35, 0xA8, 0x55, 0xF7))
                : Brushes.Transparent;
        }
    }

    private void OnFileDockButtonClicked(object sender, RoutedEventArgs e)
    {
        if (_isFileDockTransitioning) return;

        if (_isFileDockOpen && _fileDockService.CurrentMode == FileDockMode.Pinned)
        {
            CloseFileDock(toQuickActions: true);
        }
        else
        {
            OpenFileDock(FileDockMode.Pinned);
        }
    }

    private void OnFileDockPinToggleRequested()
    {
        if (_isFileDockTransitioning) return;

        if (_isFileDockOpen && _fileDockService.CurrentMode == FileDockMode.Pinned)
        {
            CloseFileDock(toQuickActions: true);
        }
        else
        {
            OpenFileDock(FileDockMode.Pinned);
        }
    }

    private void OnFileDockCloseRequested()
    {
        if (_isFileDockTransitioning) return;
        CloseFileDock(toQuickActions: true);
    }

    private void OnWindowPreviewDragEnter(object sender, DragEventArgs e)
    {
        if (FileDockPanel != null && FileDockPanel.IsDraggingOut)
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;

            if (!_isFileDockOpen)
            {
                OpenFileDock(FileDockMode.Temporary);
            }
            FileDockPanel?.SetDragPreviewActive(true);
        }
    }

    private void OnWindowPreviewDragOver(object sender, DragEventArgs e)
    {
        if (FileDockPanel != null && FileDockPanel.IsDraggingOut)
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            FileDockPanel?.SetDragPreviewActive(true);
        }
        else
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }
    }

    private void OnWindowPreviewDragLeave(object sender, DragEventArgs e)
    {
        if (FileDockPanel != null && FileDockPanel.IsDraggingOut)
        {
            return;
        }

        var pos = e.GetPosition(this);
        if (pos.X < 0 || pos.Y < 0 || pos.X > ActualWidth || pos.Y > ActualHeight)
        {
            FileDockPanel?.SetDragPreviewActive(false);

            if (_fileDockService.CurrentMode == FileDockMode.Temporary && FileDockPanel?.IsDraggingOut != true)
            {
                CloseFileDock(toQuickActions: false);
            }
        }
    }

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (FileDockPanel != null && FileDockPanel.IsDraggingOut)
        {
            return;
        }

        FileDockPanel?.SetDragPreviewActive(false);

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                _fileDockService.AddFiles(files);
                e.Handled = true;
                OrbitLogger.Info(LogCategory, $"Staged {files.Length} file(s) into File Dock from window drop.");
            }
        }
    }

    #endregion

    private void OnFullExpandClicked(object sender, RoutedEventArgs e)
    {
        if (_isTransitioningToFullExpand) return;
        _isTransitioningToFullExpand = true;

        CancelHoverTimer();
        CancelCollapseDelay();
        OrbitLogger.Info(LogCategory, "Full Expand clicked from Quick Actions.");

        if (_isFileDockOpen)
        {
            FileDockPanel.Visibility = Visibility.Collapsed;
            FileDockPanel.Opacity = 0.0;
            _isFileDockOpen = false;
            _fileDockService.CurrentMode = FileDockMode.Closed;
        }

        // 1. Immediately hide quick actions bar
        QuickActionsBar.IsHitTestVisible = false;
        QuickActionsBar.BeginAnimation(OpacityProperty, null);
        QuickActionsBar.Opacity = 0.0;
        QuickActionsBar.Visibility = Visibility.Collapsed;

        // 2. Restore mascot eyes and show mascot container
        MascotContainerGrid.Visibility = Visibility.Visible;
        MascotContainerGrid.IsHitTestVisible = true;
        MascotContainerGrid.BeginAnimation(OpacityProperty, null);
        MascotContainerGrid.Opacity = 1.0;
        if (_currentAgentState == Orbit.Models.AgentState.Idle && !PillMascot.IsHoldingDoneState)
        {
            PillMascot.RestoreNormalEyes(TimeSpan.FromMilliseconds(150));
        }
        PillMascot.IsExpanded = true;

        _isQuickActionsOpen = false;

        // 3. Smooth continuous transition into existing full ORBIT interface:
        // Window expands from (310, 38) -> Stage 1: Width 310 -> 480 (ExpandedWidth)
        // -> Stage 2: Height 38 -> targetExpandedHeight (CalculateHomeDesiredHeight)
        var primary = _monitorService.GetPrimaryMonitor();
        var (expandedLeft, topY) = _monitorService.GetTopCenterPosition(primary, ExpandedWidth, TopMargin);

        // Prep ExpandedView
        ExpandedView.Visibility = Visibility.Visible;
        ExpandedView.Opacity = 0.0;
        IslandPanel.SetBodyOpacity(0.0);

        if (_currentAgentState == Orbit.Models.AgentState.Working)
        {
            StartBinaryStreamAnimation();
            PillMascot.SetAiState(Orbit.Models.AgentState.Working, _currentAgentMessage);
        }

        var stage1Duration = TimeSpan.FromMilliseconds(180);
        var stage2Duration = TimeSpan.FromMilliseconds(240);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        // STAGE 1: Width expands 310 -> 480
        var widthAnim = new DoubleAnimation(ExpandedWidth, stage1Duration) { EasingFunction = ease };
        var leftAnim = new DoubleAnimation(expandedLeft, stage1Duration) { EasingFunction = ease };

        widthAnim.Completed += (s1, e1) =>
        {
            double targetExpandedHeight = IslandPanel.CalculateHomeDesiredHeight();

            // STAGE 2: Height expands 38 -> targetExpandedHeight downwards
            var heightAnim = new DoubleAnimation(targetExpandedHeight, stage2Duration) { EasingFunction = ease };

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
                _isTransitioningToFullExpand = false;
                Activate();
                _overlayService.ReapplyTopmost(_hwnd);
                OrbitLogger.Info(LogCategory, "Transition from Quick Actions to full ORBIT panel completed.");
            };

            BeginAnimation(HeightProperty, heightAnim);
        };

        BeginAnimation(WidthProperty, widthAnim);
        BeginAnimation(LeftProperty, leftAnim);
    }

    #endregion

    #region Voice Mode Operations & Waveform Integration

    private DateTime _lastVoiceButtonClickTime = DateTime.MinValue;

    private void OnVoiceAssistantButtonPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        HandleVoiceAssistantToggle();
    }

    private void OnVoiceAssistantButtonClicked(object sender, RoutedEventArgs e)
    {
        HandleVoiceAssistantToggle();
    }

    private void OnVoiceMicIconPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        HandleVoiceAssistantToggle();
    }

    private void HandleVoiceAssistantToggle()
    {
        if ((DateTime.UtcNow - _lastVoiceButtonClickTime).TotalMilliseconds < 250) return;
        _lastVoiceButtonClickTime = DateTime.UtcNow;

        if (_isVoiceModeActive)
        {
            OrbitLogger.Info(LogCategory, "Voice Assistant button clicked while voice mode active. Exiting voice mode.");
            ExitVoiceMode(cancelCapture: true, returnToMascot: true);
            return;
        }

        OrbitLogger.Info(LogCategory, "Voice Assistant button clicked. Activating Voice Mode.");
        EnterVoiceMode();
    }

    /// <summary>
    /// Enters In-Place Voice Mode inside the existing 310 DIP QuickActionsBar notch.
    /// Fades out the other quick actions and displays the glowing mic + real-time reactive waveform.
    /// </summary>
    public void EnterVoiceMode()
    {
        if (_isVoiceModeActive || _isHoverCollapsing || _isTransitioningToFullExpand) return;

        // If File Dock is open, close it cleanly first
        if (_isFileDockOpen)
        {
            CloseFileDock(toQuickActions: true);
        }

        _isVoiceModeActive = true;
        CancelCollapseDelay();

        // 1. Smoothly fade out standard quick action buttons
        var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(120));
        fadeOut.Completed += (s, e) =>
        {
            StandardQuickActionsPanel.Visibility = Visibility.Collapsed;
            StandardQuickActionsPanel.IsHitTestVisible = false;

            // 2. Reveal VoiceModePanel with animated waveform
            VoiceModePanel.Visibility = Visibility.Visible;
            VoiceModePanel.IsHitTestVisible = true;
            _voiceResultTimer?.Stop();
            StopProcessingAnimation();

            if (VoiceProcessingPanel != null)
            {
                VoiceProcessingPanel.Visibility = Visibility.Collapsed;
                VoiceProcessingPanel.Opacity = 0.0;
            }
            if (VoiceResultText != null)
            {
                VoiceResultText.BeginAnimation(OpacityProperty, null);
                VoiceResultText.Visibility = Visibility.Collapsed;
                VoiceResultText.Opacity = 0.0;
                VoiceResultText.Text = string.Empty;
            }

            VoiceWaveform.Visibility = Visibility.Visible;
            VoiceWaveform.Opacity = 1.0;
            VoiceWaveform.StartRendering();

            var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(140));
            VoiceModePanel.BeginAnimation(OpacityProperty, fadeIn);

            // 3. Start audio capture session
            bool started = _voiceSessionManager.StartSession();
            if (!started)
            {
                OrbitLogger.Warn(LogCategory, "Could not start voice session (hardware unavailable or access denied).");
            }
        };

        StandardQuickActionsPanel.BeginAnimation(OpacityProperty, fadeOut);
    }

    /// <summary>
    /// Exits Voice Mode and either restores the normal collapsed mascot (default when speech ends/times out)
    /// or transitions back to the standard hover toolbar (if explicitly requested).
    /// If immediate is true, resets all UI properties synchronously without starting animations (e.g. during Hide/Unhide).
    /// </summary>
    public void ExitVoiceMode(bool cancelCapture = false, bool returnToMascot = true, bool immediate = false)
    {
        if (!_isVoiceModeActive) return;
        _isVoiceModeActive = false;

        OrbitLogger.Info(LogCategory, $"Exiting Voice Mode (cancelCapture={cancelCapture}, returnToMascot={returnToMascot}, immediate={immediate}).");

        // 1. Stop or cancel audio capture
        if (cancelCapture)
        {
            _voiceSessionManager.CancelSession();
        }
        else
        {
            _voiceSessionManager.StopSession();
        }

        _voiceResultTimer?.Stop();
        StopProcessingAnimation();

        VoiceWaveform.StopRendering();
        VoiceWaveform.Visibility = Visibility.Collapsed;

        if (VoiceProcessingPanel != null)
        {
            VoiceProcessingPanel.Visibility = Visibility.Collapsed;
            VoiceProcessingPanel.Opacity = 0.0;
        }

        if (VoiceResultText != null)
        {
            VoiceResultText.BeginAnimation(OpacityProperty, null);
            VoiceResultText.Visibility = Visibility.Collapsed;
            VoiceResultText.Opacity = 0.0;
            VoiceResultText.Text = string.Empty;
        }

        // Always guarantee clean restoration of both panels
        VoiceModePanel.BeginAnimation(OpacityProperty, null);
        VoiceModePanel.Visibility = Visibility.Collapsed;
        VoiceModePanel.Opacity = 0.0;
        VoiceModePanel.IsHitTestVisible = false;

        StandardQuickActionsPanel.BeginAnimation(OpacityProperty, null);
        StandardQuickActionsPanel.Visibility = Visibility.Visible;
        StandardQuickActionsPanel.Opacity = 1.0;
        StandardQuickActionsPanel.IsHitTestVisible = true;

        if (immediate)
        {
            var primary = _monitorService.GetPrimaryMonitor();
            var (targetLeft, topY) = _monitorService.GetTopCenterPosition(primary, CollapsedWidth, TopMargin);
            BeginAnimation(WidthProperty, null);
            BeginAnimation(LeftProperty, null);
            Width = CollapsedWidth;
            Left = targetLeft;
            Top = topY;

            QuickActionsBar.BeginAnimation(OpacityProperty, null);
            QuickActionsBar.Visibility = Visibility.Collapsed;
            QuickActionsBar.Opacity = 0.0;
            QuickActionsBar.IsHitTestVisible = false;

            _isHoverCollapsing = false;
            _isHovering = false;
            _isQuickActionsOpen = false;

            MascotContainerGrid.BeginAnimation(OpacityProperty, null);
            MascotContainerGrid.Visibility = Visibility.Visible;
            MascotContainerGrid.Opacity = 1.0;
            MascotContainerGrid.IsHitTestVisible = true;
            if (_currentAgentState == Orbit.Models.AgentState.Idle && !PillMascot.IsHoldingDoneState)
            {
                PillMascot.RestoreNormalEyes(TimeSpan.Zero);
            }
            return;
        }

        if (returnToMascot)
        {
            // Transition directly from Voice Mode to the normal collapsed mascot pill
            _isHoverCollapsing = true;
            _isQuickActionsOpen = false;

            var primary = _monitorService.GetPrimaryMonitor();
            var (targetLeft, topY) = _monitorService.GetTopCenterPosition(primary, CollapsedWidth, TopMargin);

            // Hide quick actions & voice panel together
            QuickActionsBar.IsHitTestVisible = false;

            var actionsFadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(100));
            QuickActionsBar.BeginAnimation(OpacityProperty, actionsFadeOut);

            var duration = TimeSpan.FromMilliseconds(220);
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

            var widthAnim = new DoubleAnimation(CollapsedWidth, duration) { EasingFunction = ease };
            var heightAnim = new DoubleAnimation(CollapsedHeight, duration) { EasingFunction = ease };
            var leftAnim = new DoubleAnimation(targetLeft, duration) { EasingFunction = ease };

            // Restore mascot eyes & smile
            MascotContainerGrid.Visibility = Visibility.Visible;
            if (_currentAgentState == Orbit.Models.AgentState.Idle && !PillMascot.IsHoldingDoneState)
            {
                PillMascot.RestoreNormalEyes(TimeSpan.FromMilliseconds(180));
            }
            var mascotFadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(180))
            {
                BeginTime = TimeSpan.FromMilliseconds(60),
                EasingFunction = ease
            };
            MascotContainerGrid.BeginAnimation(OpacityProperty, mascotFadeIn);

            widthAnim.Completed += (s, e) =>
            {
                Width = CollapsedWidth;
                Height = CollapsedHeight;
                Left = targetLeft;
                BeginAnimation(WidthProperty, null);
                BeginAnimation(HeightProperty, null);
                BeginAnimation(LeftProperty, null);

                UpdateUnifiedIslandGeometry(CollapsedWidth, CollapsedHeight);

                QuickActionsBar.Visibility = Visibility.Collapsed;
                QuickActionsBar.Opacity = 0.0;
                VoiceModePanel.Visibility = Visibility.Collapsed;
                VoiceModePanel.Opacity = 0.0;
                StandardQuickActionsPanel.Visibility = Visibility.Visible;
                StandardQuickActionsPanel.Opacity = 1.0;

                MascotContainerGrid.IsHitTestVisible = true;
                MascotContainerGrid.Opacity = 1.0;

                _isHoverCollapsing = false;
                _isHovering = false;

                _overlayService.ReapplyTopmost(_hwnd);
                OrbitLogger.Info(LogCategory, "Voice Mode cleanly collapsed back to normal mascot.");
            };

            BeginAnimation(WidthProperty, widthAnim);
            BeginAnimation(HeightProperty, heightAnim);
            BeginAnimation(LeftProperty, leftAnim);
        }
        else
        {
            // 2. Cross-fade from VoiceModePanel back to StandardQuickActionsPanel
            var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(120));
            fadeOut.Completed += (s, e) =>
            {
                VoiceModePanel.Visibility = Visibility.Collapsed;
                VoiceModePanel.IsHitTestVisible = false;

                StandardQuickActionsPanel.Visibility = Visibility.Visible;
                StandardQuickActionsPanel.IsHitTestVisible = true;

                var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(140));
                StandardQuickActionsPanel.BeginAnimation(OpacityProperty, fadeIn);
            };

            VoiceModePanel.BeginAnimation(OpacityProperty, fadeOut);
        }
    }

    private DispatcherTimer? _voiceResultTimer;

    private void StartProcessingAnimation()
    {
        if (VoiceProcessingPanel == null) return;

        VoiceWaveform.StopRendering();
        VoiceWaveform.Visibility = Visibility.Collapsed;

        if (VoiceResultText != null)
        {
            VoiceResultText.BeginAnimation(OpacityProperty, null);
            VoiceResultText.Visibility = Visibility.Collapsed;
            VoiceResultText.Opacity = 0.0;
            VoiceResultText.Text = string.Empty;
        }

        VoiceProcessingPanel.Visibility = Visibility.Visible;
        VoiceProcessingPanel.Opacity = 1.0;

        var anim1 = new DoubleAnimation(0.25, 1.0, TimeSpan.FromMilliseconds(450))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };

        var anim2 = new DoubleAnimation(0.25, 1.0, TimeSpan.FromMilliseconds(450))
        {
            BeginTime = TimeSpan.FromMilliseconds(150),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };

        var anim3 = new DoubleAnimation(0.25, 1.0, TimeSpan.FromMilliseconds(450))
        {
            BeginTime = TimeSpan.FromMilliseconds(300),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };

        ProcessingDot1?.BeginAnimation(OpacityProperty, anim1);
        ProcessingDot2?.BeginAnimation(OpacityProperty, anim2);
        ProcessingDot3?.BeginAnimation(OpacityProperty, anim3);
    }

    private void StopProcessingAnimation()
    {
        if (VoiceProcessingPanel == null) return;

        ProcessingDot1?.BeginAnimation(OpacityProperty, null);
        ProcessingDot2?.BeginAnimation(OpacityProperty, null);
        ProcessingDot3?.BeginAnimation(OpacityProperty, null);

        VoiceProcessingPanel.Visibility = Visibility.Collapsed;
        VoiceProcessingPanel.Opacity = 0.0;
    }

    private void OnVoiceAudioLevelUpdated(object? sender, float intensity)
    {
        // Dispatch to UI thread to update waveform
        Dispatcher.BeginInvoke(() =>
        {
            if (_isVoiceModeActive)
            {
                VoiceWaveform.SetAudioIntensity(intensity);
            }
        }, DispatcherPriority.Render);
    }

    private void OnVoiceSessionStateChanged(object? sender, VoiceSessionState state)
    {
        Dispatcher.BeginInvoke(() =>
        {
            OrbitLogger.Info(LogCategory, $"MainWindow received VoiceSessionState: {state}");
            if (state == VoiceSessionState.Processing)
            {
                if (_isVoiceModeActive)
                {
                    StartProcessingAnimation();
                }
            }
            else if (state == VoiceSessionState.Cancelled || state == VoiceSessionState.Error)
            {
                if (_isVoiceModeActive)
                {
                    ExitVoiceMode(cancelCapture: false, returnToMascot: true);
                }
            }
        });
    }

    private void OnVoiceUtteranceCompleted(object? sender, byte[] pcmData)
    {
        Dispatcher.BeginInvoke(() =>
        {
            OrbitLogger.Info(LogCategory, $"Voice utterance completed: {pcmData.Length} bytes captured in RAM for Whisper.");
        });
    }

    private void OnVoiceTranscriptionReceived(object? sender, string text)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!_isVoiceModeActive) return;

            StopProcessingAnimation();

            if (string.IsNullOrWhiteSpace(text))
            {
                OrbitLogger.Info(LogCategory, "Voice transcription was empty. Displaying 'Didn't catch that' feedback.");
                VoiceResultText.Text = "Didn't catch that";
                VoiceResultText.FontStyle = FontStyles.Italic;
                VoiceResultText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC0, 0x84, 0xFC));
                VoiceResultText.Visibility = Visibility.Visible;

                var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(150));
                VoiceResultText.BeginAnimation(OpacityProperty, fadeIn);

                // Display gentle feedback for 2.5 seconds before smoothly returning to mascot
                _voiceResultTimer?.Stop();
                _voiceResultTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(2500)
                };
                _voiceResultTimer.Tick += (s, e) =>
                {
                    _voiceResultTimer.Stop();
                    if (_isVoiceModeActive)
                    {
                        ExitVoiceMode(cancelCapture: false, returnToMascot: true);
                    }
                };
                _voiceResultTimer.Start();
                return;
            }

            OrbitLogger.Info(LogCategory, $"Displaying recognized speech text in voice panel: '{text}'");
            VoiceResultText.Text = "\"" + text + "\"";
            VoiceResultText.FontStyle = FontStyles.Normal;
            VoiceResultText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF3, 0xE8, 0xFF));
            VoiceResultText.Visibility = Visibility.Visible;

            var textFadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(150));
            VoiceResultText.BeginAnimation(OpacityProperty, textFadeIn);

            // Execute offline command if parser and executor are available
            if (_commandParser != null && _commandExecutor != null)
            {
                var intent = _commandParser.Parse(text);
                if (intent.Type != CommandType.Unknown && intent.Confidence >= 0.60f)
                {
                    _voiceResultTimer?.Stop();
                    _ = Task.Run(async () =>
                    {
                        var result = await _commandExecutor.ExecuteAsync(intent);
                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (!_isVoiceModeActive) return;

                            OrbitLogger.Info(LogCategory, $"Command executed: success={result.Success}, message='{result.Message}'");
                            VoiceResultText.Text = result.Message;
                            VoiceResultText.Foreground = result.Success
                                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4A, 0xDE, 0x80))
                                : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF8, 0x71, 0x71));

                            // Display execution feedback comfortably for 3.5 seconds
                            _voiceResultTimer?.Stop();
                            _voiceResultTimer = new DispatcherTimer
                            {
                                Interval = TimeSpan.FromMilliseconds(3500)
                            };
                            _voiceResultTimer.Tick += (s, e) =>
                            {
                                _voiceResultTimer.Stop();
                                if (_isVoiceModeActive)
                                {
                                    ExitVoiceMode(cancelCapture: false, returnToMascot: true);
                                }
                            };
                            _voiceResultTimer.Start();
                        });
                    });
                    return;
                }
            }

            // Display recognized text comfortably for 3.5 seconds before smoothly returning to mascot
            _voiceResultTimer?.Stop();
            _voiceResultTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(3500)
            };
            _voiceResultTimer.Tick += (s, e) =>
            {
                _voiceResultTimer.Stop();
                if (_isVoiceModeActive)
                {
                    ExitVoiceMode(cancelCapture: false, returnToMascot: true);
                }
            };
            _voiceResultTimer.Start();
        });
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
            PillMascot.SetAiState(Orbit.Models.AgentState.Working, _currentAgentMessage);
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
        if (MascotNotchPermanentBg != null)
        {
            MascotNotchPermanentBg.Data = bgGeom;
        }
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

        // Dedicated notch shelf line: faint indication of the mascot notch boundary
        var notchShelf = new StreamGeometry();
        using (var ctx = notchShelf.Open())
        {
            if (height <= 46.0)
            {
                double shelfY = Math.Min(35.0, height - 8.0);
                double y0 = shelfY * (7.0 / 30.0);
                double y1 = shelfY * (14.0 / 30.0);
                double y2 = shelfY * (21.0 / 30.0);

                ctx.BeginFigure(new Point(18, 0), isFilled: false, isClosed: false);
                ctx.BezierTo(new Point(25, 0), new Point(33, y0), new Point(36, y1), true, true);
                ctx.BezierTo(new Point(39, y2), new Point(46, shelfY), new Point(54, shelfY), true, true);
                ctx.LineTo(new Point(width - 54, shelfY), true, true);
                ctx.BezierTo(new Point(width - 46, shelfY), new Point(width - 39, y2), new Point(width - 36, y1), true, true);
                ctx.BezierTo(new Point(width - 33, y0), new Point(width - 25, 0), new Point(width - 18, 0), true, true);
            }
            else
            {
                double x1 = cx - 62.0;
                double x2 = cx + 62.0;
                ctx.BeginFigure(new Point(x1, 0), isFilled: false, isClosed: false);
                ctx.BezierTo(new Point(cx - 55, 0), new Point(cx - 47, 7), new Point(cx - 44, 14), true, true);
                ctx.BezierTo(new Point(cx - 41, 21), new Point(cx - 34, 30), new Point(cx - 26, 30), true, true);
                ctx.LineTo(new Point(cx + 26, 30), true, true);
                ctx.BezierTo(new Point(cx + 34, 30), new Point(cx + 41, 21), new Point(cx + 44, 14), true, true);
                ctx.BezierTo(new Point(cx + 47, 7), new Point(cx + 55, 0), new Point(x2, 0), true, true);
            }
        }
        notchShelf.Freeze();

        if (height <= 46.0)
        {
            // Exact approved compact notch coordinates centered & horizontally expanded for Quick Actions (height <= 46)
            double shelfY = Math.Min(35.0, height - 8.0);
            double y0 = shelfY * (7.0 / 30.0);
            double y1 = shelfY * (14.0 / 30.0);
            double y2 = shelfY * (21.0 / 30.0);

            var bg = new StreamGeometry();
            using (var ctx = bg.Open())
            {
                ctx.BeginFigure(new Point(0, 0), isFilled: true, isClosed: true);
                ctx.LineTo(new Point(18, 0), true, true);
                ctx.BezierTo(new Point(25, 0), new Point(33, y0), new Point(36, y1), true, true);
                ctx.BezierTo(new Point(39, y2), new Point(46, shelfY), new Point(54, shelfY), true, true);
                ctx.LineTo(new Point(width - 54, shelfY), true, true);
                ctx.BezierTo(new Point(width - 46, shelfY), new Point(width - 39, y2), new Point(width - 36, y1), true, true);
                ctx.BezierTo(new Point(width - 33, y0), new Point(width - 25, 0), new Point(width - 18, 0), true, true);
                ctx.LineTo(new Point(width, 0), true, true);
            }
            bg.Freeze();

            var outline = new StreamGeometry();
            using (var ctx = outline.Open())
            {
                ctx.BeginFigure(new Point(0, 0), isFilled: false, isClosed: false);
                ctx.LineTo(new Point(18, 0), true, true);
                ctx.BezierTo(new Point(25, 0), new Point(33, y0), new Point(36, y1), true, true);
                ctx.BezierTo(new Point(39, y2), new Point(46, shelfY), new Point(54, shelfY), true, true);
                ctx.LineTo(new Point(width - 54, shelfY), true, true);
                ctx.BezierTo(new Point(width - 46, shelfY), new Point(width - 39, y2), new Point(width - 36, y1), true, true);
                ctx.BezierTo(new Point(width - 33, y0), new Point(width - 25, 0), new Point(width - 18, 0), true, true);
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
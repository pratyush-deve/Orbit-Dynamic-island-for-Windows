using System.IO;
using System.Runtime.InteropServices;
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
using Orbit.Services.Ai;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using DataObject = System.Windows.DataObject;
using DataFormats = System.Windows.DataFormats;
using Border = System.Windows.Controls.Border;
using Brushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;

namespace Orbit;

public enum AnnotationToolMode
{
    Cursor,
    Pen,
    Eraser
}

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
    public const double QuickActionsHeight = 48.0;
    public const double AnnotationToolbarWidth = 420.0;
    public const double TuckedToolbarWidth = 72.0;
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
    private readonly IAiAssistantService? _aiAssistantService;

    // AI Response Bubble & Generation state management
    public const double AiResponseBubbleWidth = 380.0;
    private bool _isAiGenerating = false;
    private bool _isAiResponseBubbleOpen = false;
    private bool _isClarificationActive = false;
    private DispatcherTimer? _aiDismissTimer;
    private DateTime _aiDismissTargetUtc;
    private int _aiDismissRemainingMs = 6000;
    public bool IsAiGenerating => _isAiGenerating;
    public bool IsAiResponseBubbleOpen => _isAiResponseBubbleOpen;

    // Smooth Typewriter delivery for AI bubble text
    private readonly DispatcherTimer _typewriterTimer = new(DispatcherPriority.Render);
    private readonly Queue<char> _typewriterQueue = new();
    private bool _typewriterCompleted = false;

    private IntPtr _hwnd = IntPtr.Zero;
    private Orbit.Models.AgentState _currentAgentState = Orbit.Models.AgentState.Idle;
    private string _currentAgentMessage = string.Empty;

    // File Dock & Quick Actions state management
    private bool _isFileDockOpen = false;
    private bool _isFileDockTransitioning = false;
    private bool _isVoiceModeActive = false;
    private bool _isAnnotationModeActive = false;
    private AnnotationToolMode _currentAnnotationTool = AnnotationToolMode.Cursor;
    private string _currentAnnotationColorHex = "#EF4444";
    private MediaColor _currentAnnotationColor = MediaColor.FromRgb(0xEF, 0x44, 0x44);
    private Orbit.UI.DesktopAnnotationOverlayWindow? _annotationOverlayWindow;
    public bool IsFileDockOpen => _isFileDockOpen;
    public bool IsVoiceModeActive => _isVoiceModeActive;
    public bool IsAnnotationModeActive => _isAnnotationModeActive;
    public AnnotationToolMode CurrentAnnotationTool => _currentAnnotationTool;
    public string CurrentAnnotationColorHex => _currentAnnotationColorHex;
    public MediaColor CurrentAnnotationColor => _currentAnnotationColor;
    public Orbit.UI.DesktopAnnotationOverlayWindow? AnnotationOverlayWindow => _annotationOverlayWindow;
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
        IVoiceCommandExecutor? commandExecutor = null,
        IAiAssistantService? aiAssistantService = null)
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
        _aiAssistantService = aiAssistantService;

        // Wire AI Assistant events
        if (_aiAssistantService != null)
        {
            _aiAssistantService.GenerationStarted += () => Dispatcher.BeginInvoke(OnAiGenerationStarted);
            _aiAssistantService.TokenReceived += chunk => Dispatcher.BeginInvoke(() => OnAiTokenReceived(chunk));
            _aiAssistantService.GenerationCompleted += full => Dispatcher.BeginInvoke(() => OnAiGenerationCompleted(full));
            _aiAssistantService.GenerationFailed += err => Dispatcher.BeginInvoke(() => OnAiGenerationFailed(err));
            _aiAssistantService.ClarificationRequested += intent => Dispatcher.BeginInvoke(() => OnAiClarificationRequested(intent));
            _aiAssistantService.ClarificationDismissed += () => Dispatcher.BeginInvoke(OnAiClarificationDismissed);
        }

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

        // Initialize fluid typewriter timer for silky smooth text delivery
        _typewriterTimer.Interval = TimeSpan.FromMilliseconds(16);
        _typewriterTimer.Tick += OnTypewriterTick;

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

        // CustomPopupPlacementCallback to precisely center the Color Palette Popup directly below the Dynamic Island
        AnnotationColorPalettePopup.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
        {
            double x = Math.Round((targetSize.Width - popupSize.Width) / 2.0);
            double y = Math.Round(targetSize.Height + 6.0);
            return new[] { new System.Windows.Controls.Primitives.CustomPopupPlacement(new Point(x, y), System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal) };
        };

        AnnotationColorPalettePopup.Opened += (s, e) =>
        {
            if (AnnotationColorPalettePopup.Child is UIElement child &&
                PresentationSource.FromVisual(child) is HwndSource source &&
                source.Handle != IntPtr.Zero)
            {
                NativeMethods.SetWindowPos(source.Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            }
        };

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

        if (double.IsNaN(Left)) Left = targetLeft;
        if (double.IsNaN(Width)) Width = targetWidth;
        if (double.IsNaN(Height)) Height = targetHeight;

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

        // Reapply topmost so ORBIT stays pinned to the top of the screen even when the user clicks into other applications
        _overlayService.ReapplyTopmost(_hwnd);

        // The topbar, AI response bubble, QuickActions, Voice mode, Annotation mode, and FileDock remain ACTIVE
        // even when focus moves to another window (e.g. IDE, browser, terminal).
        // Clicking outside ORBIT must NOT dismiss or collapse the topbar.
        if (_isAiGenerating || _isAiResponseBubbleOpen || _isVoiceModeActive || _isAnnotationModeActive || _isQuickActionsOpen || _isFileDockOpen)
        {
            return;
        }

        if (IslandPanel.IsNotesModeActive)
        {
            return;
        }

        // Only collapse full dashboard modal if explicitly in Expanded state
        if (_lifecycleService.CurrentState == CompanionState.Expanded)
        {
            IslandPanel.ResetToHomeView();
            _lifecycleService.RequestCollapse();
        }
    }

    private bool _isConfirmHideDialogOpen = false;
    private bool _isAnnotationToolbarTucked = false;
    public bool IsAnnotationToolbarTucked => _isAnnotationToolbarTucked;
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

        // 1b. Safeguard: If hide confirmation dialog is already open, ignore repeated HP key presses!
        if (_isConfirmHideDialogOpen)
        {
            OrbitLogger.Debug(LogCategory, "Ignored toggle request because hide confirmation dialog is already open.");
            return;
        }

        // 1c. If annotation mode is active and mascot is visible on screen:
        // Do NOT immediately hide ORBIT. Show confirmation dialog asking whether user wants to clear annotations and hide ORBIT.
        if (_isAnnotationModeActive && IsVisible && Visibility == Visibility.Visible && _lifecycleService.CurrentState != CompanionState.Hidden)
        {
            ShowAnnotationHideConfirmation();
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

            if (_isAnnotationModeActive)
            {
                ExitAnnotationToolbarMode(returnToQuickActions: false, immediate: true);
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
        if (_isAnnotationModeActive)
        {
            ExitAnnotationToolbarMode(returnToQuickActions: false, immediate: true);
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

                    if (_isAnnotationModeActive)
                    {
                        ExitAnnotationToolbarMode(returnToQuickActions: false, immediate: true);
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

                    if (_isAnnotationModeActive)
                    {
                        ExitAnnotationToolbarMode(returnToQuickActions: false, immediate: true);
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

        if (_isQuickActionsOpen || _isHoverExpanding || _isHoverCollapsing || _isTransitioningToFullExpand || _isVoiceModeActive || _isAnnotationModeActive || _isFileDockOpen || _isAiGenerating || _isAiResponseBubbleOpen)
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
            // Lock mascot interactions during active AI generation
            if (_isAiGenerating)
            {
                OrbitLogger.Debug(LogCategory, "Mascot click locked during AI generation.");
                return;
            }

            // If response bubble is open after generation, clicking mascot dismisses it immediately
            if (_isAiResponseBubbleOpen)
            {
                DismissAiResponseBubble();
                return;
            }

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
        // Suppress hover actions during AI generation or when AI Response Bubble is displayed
        if (_isAiGenerating || _isAiResponseBubbleOpen) return;

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

        // CRITICAL: While Voice Mode or Annotation Mode is active, do not collapse when cursor leaves
        if (_isVoiceModeActive || _isAnnotationModeActive)
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
        if (_isAiGenerating || _isAiResponseBubbleOpen) return;
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
        if (_isAiGenerating || _isAiResponseBubbleOpen) return;
        if (_isTransitioningToFullExpand || _isHoverCollapsing || !_isQuickActionsOpen) return;

        // CRITICAL: While Voice Mode or Annotation Mode is active, do not collapse
        if (_isVoiceModeActive || _isAnnotationModeActive) return;

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

            if (double.IsNaN(Left)) Left = targetLeft;
            if (double.IsNaN(Width)) Width = CollapsedWidth;
            if (double.IsNaN(Height)) Height = CollapsedHeight;

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
        if (_isVoiceModeActive || _isTransitioningToFullExpand) return;

        // Cancel any pending collapse delay or in-flight hover collapse immediately
        CancelHoverTimer();
        CancelCollapseDelay();
        _isHoverCollapsing = false;
        _isQuickActionsOpen = true;

        // If File Dock is open, close it cleanly first
        if (_isFileDockOpen)
        {
            CloseFileDock(toQuickActions: true);
        }

        _isVoiceModeActive = true;

        // 1. Immediately start audio capture session (0ms hardware lag!)
        bool started = _voiceSessionManager.StartSession();
        if (!started)
        {
            OrbitLogger.Warn(LogCategory, "Could not start voice session (hardware unavailable or access denied).");
        }

        // 2. Reveal VoiceModePanel with animated waveform immediately
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

        // 3. Smoothly cross-fade buttons and voice panel
        var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(100));
        fadeOut.Completed += (s, e) =>
        {
            StandardQuickActionsPanel.Visibility = Visibility.Collapsed;
            StandardQuickActionsPanel.IsHitTestVisible = false;
        };
        StandardQuickActionsPanel.BeginAnimation(OpacityProperty, fadeOut);

        var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(100));
        VoiceModePanel.BeginAnimation(OpacityProperty, fadeIn);
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
            BeginAnimation(HeightProperty, null);
            BeginAnimation(LeftProperty, null);
            Width = CollapsedWidth;
            Height = CollapsedHeight;
            Left = targetLeft;
            Top = topY;
            UpdateUnifiedIslandGeometry(CollapsedWidth, CollapsedHeight);

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

            if (double.IsNaN(Left)) Left = targetLeft;
            if (double.IsNaN(Width)) Width = CollapsedWidth;
            if (double.IsNaN(Height)) Height = CollapsedHeight;

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

            // When AI assistant is enabled, never display raw Whisper text in the UI
            if (_aiAssistantService != null)
            {
                ExitVoiceMode(cancelCapture: false, returnToMascot: true, immediate: true);

                if (string.IsNullOrWhiteSpace(text))
                {
                    OrbitLogger.Info(LogCategory, "Voice transcription was empty or silent.");
                    PillMascot.SetAiState(Orbit.Models.AgentState.Idle);
                    return;
                }

                // Check pending clarification voice response
                if (_aiAssistantService.PendingClarification != null)
                {
                    string cleanSpoken = text.Trim().ToLowerInvariant();
                    // 1. Clear affirmative
                    if (cleanSpoken == "yes" || cleanSpoken == "yeah" || cleanSpoken == "yep" || cleanSpoken == "correct" || cleanSpoken == "sure")
                    {
                        OrbitLogger.Info(LogCategory, $"User confirmed clarification via voice: '{text}'");
                        _isClarificationActive = false;
                        ClarificationActionPanel.Visibility = Visibility.Collapsed;
                        _ = Task.Run(async () =>
                        {
                            var result = await _aiAssistantService.ConfirmClarificationAsync().ConfigureAwait(false);
                            if (result != null)
                            {
                                _ = Dispatcher.BeginInvoke(() =>
                                {
                                    AiResponseText.Text = result.Message;
                                    StartAiDismissTimer(3500);
                                });
                            }
                        });
                        return;
                    }

                    // 2. Clear negative with correction (e.g. "no, open chrome instead", "actually open visual studio code")
                    var matchCorrection = System.Text.RegularExpressions.Regex.Match(text, @"^(?:no[, ]*|nope[, ]*|nah[, ]*|actually[, ]*)(?:i\s+meant\s+)?(.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (matchCorrection.Success && !string.IsNullOrWhiteSpace(matchCorrection.Groups[1].Value) &&
                        !cleanSpoken.Equals("no", StringComparison.OrdinalIgnoreCase) &&
                        !cleanSpoken.Equals("nope", StringComparison.OrdinalIgnoreCase))
                    {
                        string corrected = matchCorrection.Groups[1].Value.Trim();
                        OrbitLogger.Info(LogCategory, $"User corrected clarification via voice: '{text}' -> '{corrected}'");
                        _isClarificationActive = false;
                        ClarificationActionPanel.Visibility = Visibility.Collapsed;
                        _aiAssistantService.RejectClarification();

                        _ = Task.Run(async () =>
                        {
                            await _aiAssistantService.ProcessVoiceTranscriptionAsync(corrected).ConfigureAwait(false);
                        });
                        return;
                    }

                    // 3. Pure negative (e.g. "no", "nope", "cancel")
                    if (cleanSpoken == "no" || cleanSpoken == "nope" || cleanSpoken == "cancel")
                    {
                        OrbitLogger.Info(LogCategory, $"User rejected clarification via voice: '{text}'. Re-activating listening.");
                        _isClarificationActive = false;
                        ClarificationActionPanel.Visibility = Visibility.Collapsed;
                        _aiAssistantService.RejectClarification();
                        DismissAiResponseBubble();
                        EnterVoiceMode();
                        return;
                    }
                }

                OrbitLogger.Info(LogCategory, $"Routing voice transcription internally to AI engine: '{text}'");
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _aiAssistantService.ProcessVoiceTranscriptionAsync(text).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        OrbitLogger.Error(LogCategory, $"Failed processing voice transcription via AI: {ex.Message}", ex);
                    }
                });
                return;
            }

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

    #region AI Assistant & Response Bubble Management

    private void OnAiGenerationStarted()
    {
        _isAiGenerating = true;
        _isAiResponseBubbleOpen = false;
        _typewriterCompleted = false;
        _typewriterTimer.Stop();
        _typewriterQueue.Clear();

        // Lock window dimensions strictly to the compact 160x38 pill while Qwen is thinking
        var primary = _monitorService.GetPrimaryMonitor();
        var (targetLeft, topY) = _monitorService.GetTopCenterPosition(primary, CollapsedWidth, TopMargin);
        BeginAnimation(WidthProperty, null);
        BeginAnimation(HeightProperty, null);
        BeginAnimation(LeftProperty, null);
        Width = CollapsedWidth;
        Height = CollapsedHeight;
        Left = targetLeft;
        Top = topY;
        UpdateUnifiedIslandGeometry(CollapsedWidth, CollapsedHeight);

        // Recolor mascot and binary stream to green (#10B981)
        PillMascot.SetAiGenerating(true);
        SetBinaryStreamAiColor(true);
        StartBinaryStreamAnimation();

        // Update notch outline & shelf to green
        if (CollapsedNotchOutline != null) CollapsedNotchOutline.Stroke = BrushGreenNotchOutline;
        if (MascotNotchBoundaryIndicator != null) MascotNotchBoundaryIndicator.Stroke = BrushGreenNotchShelf;

        // Reset bubble text without opening dialog yet
        AiResponseText.Text = string.Empty;
        AiResponseBubble.Visibility = Visibility.Collapsed;
        AiResponseBubble.Opacity = 0.0;
        AiResponseBubble.IsHitTestVisible = false;
        if (AiScrollIndicatorTrack != null) AiScrollIndicatorTrack.Visibility = Visibility.Collapsed;
    }

    private void EnsureAiResponseBubbleOpen()
    {
        if (_isAiResponseBubbleOpen) return;

        _isAiResponseBubbleOpen = true;
        AiResponseBubble.Visibility = Visibility.Visible;
        AiResponseBubble.Opacity = 0.0;
        AiResponseBubble.IsHitTestVisible = true;

        var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(160));
        AiResponseBubble.BeginAnimation(OpacityProperty, fadeIn);

        AnimateAiWindowDimensions(targetWidth: 400.0, targetHeight: 140.0);
    }

    private void OnAiTokenReceived(string chunk)
    {
        if (!_isAiGenerating) return;

        EnsureAiResponseBubbleOpen();

        foreach (char c in chunk)
        {
            _typewriterQueue.Enqueue(c);
        }

        if (!_typewriterTimer.IsEnabled)
        {
            _typewriterTimer.Start();
        }
    }

    private void OnTypewriterTick(object? sender, EventArgs e)
    {
        if (_typewriterQueue.Count > 0)
        {
            // Adaptive typewriter cadence: 1 to 4 chars per 16ms tick
            int charsToDrain = _typewriterQueue.Count > 60 ? 4 : (_typewriterQueue.Count > 20 ? 2 : 1);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < charsToDrain && _typewriterQueue.Count > 0; i++)
            {
                sb.Append(_typewriterQueue.Dequeue());
            }

            AiResponseText.Text += sb.ToString();
            AiResponseScrollViewer.ScrollToEnd();

            // Dynamically adjust window height smoothly
            AiResponseBubble.UpdateLayout();
            double desiredHeight = Math.Min(260.0, Math.Max(140.0, AiResponseBubble.ActualHeight + 52.0));
            if (desiredHeight > Height + 6.0)
            {
                Height = desiredHeight;
                UpdateUnifiedIslandGeometry(Width, Height);
            }
        }
        else if (_typewriterCompleted)
        {
            _typewriterTimer.Stop();
            StartAiDismissTimer();
        }
    }

    private void FlushTypewriterQueue()
    {
        if (_typewriterQueue.Count > 0)
        {
            var sb = new System.Text.StringBuilder();
            while (_typewriterQueue.Count > 0)
            {
                sb.Append(_typewriterQueue.Dequeue());
            }
            AiResponseText.Text += sb.ToString();
            AiResponseScrollViewer.ScrollToEnd();
        }
        _typewriterTimer.Stop();
    }

    private void OnAiResponseScrollChanged(object sender, System.Windows.Controls.ScrollChangedEventArgs e)
    {
        if (AiResponseScrollViewer == null || AiScrollIndicatorTrack == null || AiScrollIndicatorThumb == null) return;

        double extent = AiResponseScrollViewer.ExtentHeight;
        double viewport = AiResponseScrollViewer.ViewportHeight;
        double offset = AiResponseScrollViewer.VerticalOffset;

        if (extent > viewport && viewport > 0)
        {
            AiScrollIndicatorTrack.Visibility = Visibility.Visible;
            double trackHeight = Math.Max(20.0, AiResponseScrollViewer.ActualHeight - 6.0);
            double thumbHeight = Math.Max(18.0, Math.Min(trackHeight, (viewport / extent) * trackHeight));
            AiScrollIndicatorThumb.Height = thumbHeight;

            double scrollable = extent - viewport;
            double thumbTravel = trackHeight - thumbHeight;
            double top = scrollable > 0 ? (offset / scrollable) * thumbTravel : 0;
            System.Windows.Controls.Canvas.SetTop(AiScrollIndicatorThumb, top);
        }
        else
        {
            AiScrollIndicatorTrack.Visibility = Visibility.Collapsed;
        }
    }

    private void OnAiGenerationCompleted(string fullResponse)
    {
        _isAiGenerating = false;
        StopBinaryStreamAnimation();
        SetBinaryStreamAiColor(false);

        // Ensure bubble is open
        EnsureAiResponseBubbleOpen();

        // If no tokens were streamed before completion, enqueue full response to type out
        if (string.IsNullOrWhiteSpace(AiResponseText.Text) && _typewriterQueue.Count == 0 && !string.IsNullOrWhiteSpace(fullResponse))
        {
            foreach (char c in fullResponse)
            {
                _typewriterQueue.Enqueue(c);
            }
            if (!_typewriterTimer.IsEnabled)
            {
                _typewriterTimer.Start();
            }
        }

        _typewriterCompleted = true;

        // Transition mascot to Completed state (emerald green #10B981, smiling eye arches ◠ ◠)
        PillMascot.SetAiGenerating(false);
        PillMascot.SetAiState(Orbit.Models.AgentState.Completed, "Complete");

        AiResponseScrollViewer.ScrollToEnd();
        AiResponseBubble.UpdateLayout();
        double desiredHeight = Math.Min(260.0, Math.Max(140.0, AiResponseBubble.ActualHeight + 52.0));
        Height = desiredHeight;
        UpdateUnifiedIslandGeometry(Width, Height);

        if (_typewriterQueue.Count == 0)
        {
            StartAiDismissTimer();
        }
    }

    private void OnAiGenerationFailed(string error)
    {
        _isAiGenerating = false;
        _typewriterCompleted = true;
        _typewriterTimer.Stop();
        _typewriterQueue.Clear();

        StopBinaryStreamAnimation();
        SetBinaryStreamAiColor(false);

        PillMascot.SetAiGenerating(false);
        PillMascot.SetAiState(Orbit.Models.AgentState.Error, "AI Error");

        // Open bubble to show error message
        EnsureAiResponseBubbleOpen();

        AiResponseText.Text = string.IsNullOrWhiteSpace(error)
            ? "Local AI engine is unavailable. Ensure Ollama is running."
            : error;

        AiResponseBubble.UpdateLayout();
        double desiredHeight = Math.Min(260.0, Math.Max(140.0, AiResponseBubble.ActualHeight + 52.0));
        Height = desiredHeight;
        UpdateUnifiedIslandGeometry(Width, Height);

        StartAiDismissTimer();
    }

    private void OnAiClarificationRequested(CommandIntent intent)
    {
        _isClarificationActive = true;
        _aiDismissTimer?.Stop();

        EnsureAiResponseBubbleOpen();
        FlushTypewriterQueue();

        string promptText = intent.ClarificationPrompt ?? $"Did you mean {intent.Target}?";
        AiResponseText.Text = promptText;
        ClarificationActionPanel.Visibility = Visibility.Visible;

        PillMascot.SetAiGenerating(false);
        PillMascot.SetAiState(Orbit.Models.AgentState.Attention, "Clarifying");

        AiResponseBubble.UpdateLayout();
        double desiredHeight = Math.Min(260.0, Math.Max(140.0, AiResponseBubble.ActualHeight + 52.0));
        Height = desiredHeight;
        UpdateUnifiedIslandGeometry(Width, Height);

        AnimateAiWindowDimensions(targetWidth: 400.0, targetHeight: 160.0);
    }

    private void OnAiClarificationDismissed()
    {
        _isClarificationActive = false;
        ClarificationActionPanel.Visibility = Visibility.Collapsed;
    }

    private async void OnClarificationYesClicked(object sender, RoutedEventArgs e)
    {
        if (_aiAssistantService == null) return;
        _isClarificationActive = false;
        ClarificationActionPanel.Visibility = Visibility.Collapsed;

        var result = await _aiAssistantService.ConfirmClarificationAsync();
        if (result != null)
        {
            AiResponseText.Text = result.Message;
            StartAiDismissTimer(3500);
        }
        else
        {
            DismissAiResponseBubble();
        }
    }

    private void OnClarificationNoClicked(object sender, RoutedEventArgs e)
    {
        if (_aiAssistantService == null) return;
        _isClarificationActive = false;
        ClarificationActionPanel.Visibility = Visibility.Collapsed;
        _aiAssistantService.RejectClarification();

        // Immediately reactivate microphone / listening mode for clean correction
        DismissAiResponseBubble();
        EnterVoiceMode();
    }

    private void StartAiDismissTimer(int durationMs = 6000)
    {
        if (_isClarificationActive) return;

        _aiDismissTimer?.Stop();
        _aiDismissRemainingMs = durationMs;
        _aiDismissTargetUtc = DateTime.UtcNow.AddMilliseconds(durationMs);

        _aiDismissTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(durationMs)
        };
        _aiDismissTimer.Tick += (s, e) =>
        {
            _aiDismissTimer.Stop();
            DismissAiResponseBubble();
        };
        _aiDismissTimer.Start();
    }

    private void OnAiResponseBubbleMouseEnter(object sender, MouseEventArgs e)
    {
        FlushTypewriterQueue();
        if (_aiDismissTimer != null && _aiDismissTimer.IsEnabled)
        {
            _aiDismissTimer.Stop();
            var remaining = (_aiDismissTargetUtc - DateTime.UtcNow).TotalMilliseconds;
            _aiDismissRemainingMs = Math.Max(1000, (int)remaining);
        }
    }

    private void OnAiResponseBubbleMouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isAiGenerating && _isAiResponseBubbleOpen && !_isClarificationActive)
        {
            StartAiDismissTimer(_aiDismissRemainingMs);
        }
    }

    public void DismissAiResponseBubble()
    {
        _aiDismissTimer?.Stop();
        _isClarificationActive = false;
        if (ClarificationActionPanel != null) ClarificationActionPanel.Visibility = Visibility.Collapsed;

        _isAiResponseBubbleOpen = false;
        _isAiGenerating = false;
        _typewriterTimer.Stop();
        _typewriterQueue.Clear();

        // 1. Fade out the AI bubble immediately
        var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(120));
        fadeOut.Completed += (s, e) =>
        {
            AiResponseBubble.Visibility = Visibility.Collapsed;
        };
        AiResponseBubble.BeginAnimation(OpacityProperty, fadeOut);

        // 2. Animate the main island dimensions smoothly back down to collapsed notch size
        var primary = _monitorService.GetPrimaryMonitor();
        var (targetLeft, topY) = _monitorService.GetTopCenterPosition(primary, CollapsedWidth, TopMargin);

        // If window is already at collapsed dimensions or not initialized, just reset idle state immediately
        if (double.IsNaN(Left) || double.IsNaN(Width) || double.IsNaN(Height) ||
            (Math.Abs(Width - CollapsedWidth) < 1.0 && Math.Abs(Height - CollapsedHeight) < 1.0))
        {
            Width = CollapsedWidth;
            Height = CollapsedHeight;
            if (!double.IsNaN(targetLeft)) Left = targetLeft;
            PillMascot.ResetToIdle();
            UpdateCompactNotchGlow(Orbit.Models.AgentState.Idle);
            if (CollapsedNotchOutline != null) CollapsedNotchOutline.Stroke = BrushPurpleNotchOutline;
            if (MascotNotchBoundaryIndicator != null) MascotNotchBoundaryIndicator.Stroke = BrushPurpleNotchShelf;
            return;
        }

        var duration = TimeSpan.FromMilliseconds(180);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var animW = new DoubleAnimation(CollapsedWidth, duration) { EasingFunction = ease };
        var animH = new DoubleAnimation(CollapsedHeight, duration) { EasingFunction = ease };
        var animL = new DoubleAnimation(targetLeft, duration) { EasingFunction = ease };

        animH.CurrentTimeInvalidated += (s, e) =>
        {
            UpdateUnifiedIslandGeometry(Width, Height);
        };

        animH.Completed += (s, e) =>
        {
            BeginAnimation(WidthProperty, null);
            BeginAnimation(HeightProperty, null);
            BeginAnimation(LeftProperty, null);

            Width = CollapsedWidth;
            Height = CollapsedHeight;
            Left = targetLeft;

            UpdateUnifiedIslandGeometry(CollapsedWidth, CollapsedHeight);

            PillMascot.ResetToIdle();
            UpdateCompactNotchGlow(Orbit.Models.AgentState.Idle);
            if (CollapsedNotchOutline != null) CollapsedNotchOutline.Stroke = BrushPurpleNotchOutline;
            if (MascotNotchBoundaryIndicator != null) MascotNotchBoundaryIndicator.Stroke = BrushPurpleNotchShelf;
            _overlayService.ReapplyTopmost(_hwnd);
        };

        BeginAnimation(WidthProperty, animW);
        BeginAnimation(HeightProperty, animH);
        BeginAnimation(LeftProperty, animL);
    }

    private void AnimateAiWindowDimensions(double targetWidth, double targetHeight)
    {
        var primary = _monitorService.GetPrimaryMonitor();
        var (targetLeft, topY) = _monitorService.GetTopCenterPosition(primary, targetWidth, TopMargin);

        var duration = TimeSpan.FromMilliseconds(200);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        if (double.IsNaN(Left)) Left = targetLeft;
        if (double.IsNaN(Width)) Width = targetWidth;
        if (double.IsNaN(Height)) Height = targetHeight;

        var widthAnim = new DoubleAnimation(targetWidth, duration) { EasingFunction = ease };
        var heightAnim = new DoubleAnimation(targetHeight, duration) { EasingFunction = ease };
        var leftAnim = new DoubleAnimation(targetLeft, duration) { EasingFunction = ease };

        heightAnim.Completed += (s, e) =>
        {
            Width = targetWidth;
            Height = targetHeight;
            Left = targetLeft;

            BeginAnimation(WidthProperty, null);
            BeginAnimation(HeightProperty, null);
            BeginAnimation(LeftProperty, null);

            UpdateUnifiedIslandGeometry(targetWidth, targetHeight);
            _overlayService.ReapplyTopmost(_hwnd);
        };

        BeginAnimation(WidthProperty, widthAnim);
        BeginAnimation(HeightProperty, heightAnim);
        BeginAnimation(LeftProperty, leftAnim);
    }

    private void SetBinaryStreamAiColor(bool isAiGreen)
    {
        if (BinaryStreamGlow != null)
        {
            BinaryStreamGlow.Color = isAiGreen
                ? System.Windows.Media.Color.FromRgb(0x10, 0xB9, 0x81)
                : System.Windows.Media.Color.FromRgb(0xA8, 0x55, 0xF7);
        }

        if (BinaryStreamOverlay == null) return;

        var greenBrushes = new[]
        {
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x10, 0xB9, 0x81)),
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x05, 0x96, 0x69)),
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x34, 0xD3, 0x99)),
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6E, 0xE7, 0xB7)),
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA7, 0xF3, 0xD0))
        };
        var purpleBrushes = new[]
        {
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x7C, 0x3A, 0xED)),
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA8, 0x55, 0xF7)),
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE9, 0xD5, 0xFF)),
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6B, 0x46, 0xC1)),
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC0, 0x84, 0xFC))
        };

        var palette = isAiGreen ? greenBrushes : purpleBrushes;
        int idx = 0;
        foreach (var child in BinaryStreamOverlay.Children)
        {
            if (child is System.Windows.Controls.TextBlock tb)
            {
                foreach (var inline in tb.Inlines)
                {
                    if (inline is System.Windows.Documents.Run run)
                    {
                        run.Foreground = palette[idx % palette.Length];
                        idx++;
                    }
                }
            }
        }
    }

    #endregion

    #region Desktop Annotation Toolbar Mode

    private void OnQuickAnnotationClicked(object sender, RoutedEventArgs e)
    {
        if (_isAnnotationModeActive || _isVoiceModeActive || _isFileDockTransitioning || _isHoverCollapsing || _isTransitioningToFullExpand)
        {
            return;
        }

        OrbitLogger.Info(LogCategory, "Quick Actions annotation button clicked. Activating Annotation Toolbar mode.");
        EnterAnnotationToolbarMode();
    }

    /// <summary>
    /// Enters In-Place Desktop Annotation Toolbar Mode inside the horizontally expanded dynamic island.
    /// Fades out standard quick actions and expands width to 385 DIP while keeping height at 44 DIP.
    /// </summary>
    public void EnterAnnotationToolbarMode()
    {
        if (_isAnnotationModeActive || _isHoverCollapsing || _isTransitioningToFullExpand) return;

        // If File Dock is open, close it cleanly first
        if (_isFileDockOpen)
        {
            CloseFileDock(toQuickActions: true);
        }

        _isAnnotationModeActive = true;
        _isAnnotationToolbarTucked = false;
        if (BtnTuckedAnnotationRestore != null)
        {
            BtnTuckedAnnotationRestore.Visibility = Visibility.Collapsed;
            BtnTuckedAnnotationRestore.Opacity = 0.0;
            BtnTuckedAnnotationRestore.IsHitTestVisible = false;
        }
        InstallAnnotationKeyboardHook();
        CancelCollapseDelay();
        CancelHoverTimer();

        // 1. Reset tool selection to Cursor by default
        SelectAnnotationTool(AnnotationToolMode.Cursor);

        // 1b. Launch fullscreen desktop annotation overlay window
        try
        {
            if (_annotationOverlayWindow == null)
            {
                _annotationOverlayWindow = new Orbit.UI.DesktopAnnotationOverlayWindow(_hwnd);
                _annotationOverlayWindow.IsOverToolbarOrPaletteFunc = IsPointOverOrbitUI;
                _annotationOverlayWindow.RequestCursorModeAction = () =>
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        if (_isAnnotationModeActive && _currentAnnotationTool != AnnotationToolMode.Cursor)
                        {
                            OrbitLogger.Info(LogCategory, "Right-click auto-switching to Cursor mode.");
                            SelectAnnotationTool(AnnotationToolMode.Cursor);
                        }
                    });
                };
                _annotationOverlayWindow.Show();
                _annotationOverlayWindow.SetTool(_currentAnnotationTool);
                _annotationOverlayWindow.SetPenColor(_currentAnnotationColor);

                _overlayService.ReapplyTopmost(_hwnd);
                if (_annotationOverlayWindow.Hwnd != IntPtr.Zero)
                {
                    NativeMethods.SetWindowPos(_annotationOverlayWindow.Hwnd, _hwnd, 0, 0, 0, 0,
                        NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
                }
            }
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, $"Failed to launch DesktopAnnotationOverlayWindow: {ex.Message}");
        }

        // 2. Hide mascot eyes
        PillMascot.GracefullyCloseEyes(TimeSpan.FromMilliseconds(180));
        if (MascotContainerGrid.Visibility == Visibility.Visible)
        {
            var mascotFadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(140));
            MascotContainerGrid.BeginAnimation(OpacityProperty, mascotFadeOut);
        }

        // 3. Smoothly fade out standard quick action buttons
        var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(100));
        fadeOut.Completed += (s, e) =>
        {
            StandardQuickActionsPanel.Visibility = Visibility.Collapsed;
            StandardQuickActionsPanel.IsHitTestVisible = false;

            // Reveal AnnotationToolbarPanel
            AnnotationToolbarPanel.Visibility = Visibility.Visible;
            AnnotationToolbarPanel.IsHitTestVisible = true;

            var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(140));
            AnnotationToolbarPanel.BeginAnimation(OpacityProperty, fadeIn);
        };
        StandardQuickActionsPanel.BeginAnimation(OpacityProperty, fadeOut);

        // 4. Smoothly animate window dimensions horizontally to AnnotationToolbarWidth (385 DIP)
        var primary = _monitorService.GetPrimaryMonitor();
        var (targetLeft, topY) = _monitorService.GetTopCenterPosition(primary, AnnotationToolbarWidth, TopMargin);

        var duration = TimeSpan.FromMilliseconds(240);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var widthAnim = new DoubleAnimation(AnnotationToolbarWidth, duration) { EasingFunction = ease };
        var leftAnim = new DoubleAnimation(targetLeft, duration) { EasingFunction = ease };
        var barWidthAnim = new DoubleAnimation(AnnotationToolbarWidth, duration) { EasingFunction = ease };

        widthAnim.Completed += (s, e) =>
        {
            Width = AnnotationToolbarWidth;
            Left = targetLeft;
            QuickActionsBar.Width = AnnotationToolbarWidth;

            BeginAnimation(WidthProperty, null);
            BeginAnimation(LeftProperty, null);
            QuickActionsBar.BeginAnimation(WidthProperty, null);

            UpdateUnifiedIslandGeometry(AnnotationToolbarWidth, QuickActionsHeight);
            _overlayService.ReapplyTopmost(_hwnd);
            if (_annotationOverlayWindow != null && _annotationOverlayWindow.Hwnd != IntPtr.Zero)
            {
                NativeMethods.SetWindowPos(_annotationOverlayWindow.Hwnd, _hwnd, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            }
            OrbitLogger.Info(LogCategory, "Annotation toolbar horizontally expanded successfully.");
        };

        BeginAnimation(WidthProperty, widthAnim);
        BeginAnimation(LeftProperty, leftAnim);
        QuickActionsBar.BeginAnimation(WidthProperty, barWidthAnim);
    }

    /// <summary>
    /// Exits Annotation Toolbar Mode and either restores the standard Quick Actions tray
    /// or transitions directly back to the collapsed mascot pill.
    /// </summary>
    public void ExitAnnotationToolbarMode(bool returnToQuickActions = true, bool immediate = false)
    {
        if (!_isAnnotationModeActive) return;
        _isAnnotationModeActive = false;
        _isAnnotationToolbarTucked = false;

        UninstallAnnotationKeyboardHook();

        if (BtnTuckedAnnotationRestore != null)
        {
            BtnTuckedAnnotationRestore.Visibility = Visibility.Collapsed;
            BtnTuckedAnnotationRestore.Opacity = 0.0;
            BtnTuckedAnnotationRestore.IsHitTestVisible = false;
        }

        if (AnnotationToastBorder != null)
        {
            AnnotationToastBorder.Visibility = Visibility.Collapsed;
            AnnotationToastBorder.Opacity = 0.0;
        }

        if (AnnotationColorPalettePopup != null)
        {
            AnnotationColorPalettePopup.IsOpen = false;
        }

        if (_annotationOverlayWindow != null)
        {
            try
            {
                _annotationOverlayWindow.ClearAllStrokes();
                _annotationOverlayWindow.Close();
            }
            catch (Exception ex)
            {
                OrbitLogger.Warn(LogCategory, $"Failed to close annotation overlay: {ex.Message}");
            }
            _annotationOverlayWindow = null;
        }

        OrbitLogger.Info(LogCategory, $"Exiting annotation toolbar mode (returnToQuickActions={returnToQuickActions}, immediate={immediate}).");

        if (immediate)
        {
            AnnotationToolbarPanel.BeginAnimation(OpacityProperty, null);
            AnnotationToolbarPanel.Visibility = Visibility.Collapsed;
            AnnotationToolbarPanel.Opacity = 0.0;
            AnnotationToolbarPanel.IsHitTestVisible = false;

            if (returnToQuickActions)
            {
                StandardQuickActionsPanel.BeginAnimation(OpacityProperty, null);
                StandardQuickActionsPanel.Visibility = Visibility.Visible;
                StandardQuickActionsPanel.Opacity = 1.0;
                StandardQuickActionsPanel.IsHitTestVisible = true;

                Width = QuickActionsWidth;
                QuickActionsBar.Width = QuickActionsWidth;
                var primary = _monitorService.GetPrimaryMonitor();
                var (targetLeft, topY) = _monitorService.GetTopCenterPosition(primary, QuickActionsWidth, TopMargin);
                Left = targetLeft;
                UpdateUnifiedIslandGeometry(QuickActionsWidth, QuickActionsHeight);
            }
            else
            {
                Width = CollapsedWidth;
                Height = CollapsedHeight;
                QuickActionsBar.Width = QuickActionsWidth;
                QuickActionsBar.Visibility = Visibility.Collapsed;
                QuickActionsBar.Opacity = 0.0;
                QuickActionsBar.IsHitTestVisible = false;

                PositionAtTopCenter(CollapsedWidth);
                UpdateUnifiedIslandGeometry(CollapsedWidth, CollapsedHeight);

                MascotContainerGrid.BeginAnimation(OpacityProperty, null);
                MascotContainerGrid.Visibility = Visibility.Visible;
                MascotContainerGrid.Opacity = 1.0;
                MascotContainerGrid.IsHitTestVisible = true;
                PillMascot.RestoreNormalEyes(TimeSpan.Zero);
            }
            return;
        }

        if (returnToQuickActions)
        {
            // Cross-fade back to StandardQuickActionsPanel
            var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(100));
            fadeOut.Completed += (s, e) =>
            {
                AnnotationToolbarPanel.Visibility = Visibility.Collapsed;
                AnnotationToolbarPanel.IsHitTestVisible = false;

                StandardQuickActionsPanel.Visibility = Visibility.Visible;
                StandardQuickActionsPanel.IsHitTestVisible = true;

                var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(140));
                StandardQuickActionsPanel.BeginAnimation(OpacityProperty, fadeIn);
            };
            AnnotationToolbarPanel.BeginAnimation(OpacityProperty, fadeOut);

            // Animate width from 385 back to QuickActionsWidth (310)
            var primary = _monitorService.GetPrimaryMonitor();
            var (targetLeft, topY) = _monitorService.GetTopCenterPosition(primary, QuickActionsWidth, TopMargin);

            var duration = TimeSpan.FromMilliseconds(220);
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

            var widthAnim = new DoubleAnimation(QuickActionsWidth, duration) { EasingFunction = ease };
            var leftAnim = new DoubleAnimation(targetLeft, duration) { EasingFunction = ease };
            var barWidthAnim = new DoubleAnimation(QuickActionsWidth, duration) { EasingFunction = ease };

            widthAnim.Completed += (s, e) =>
            {
                Width = QuickActionsWidth;
                Left = targetLeft;
                QuickActionsBar.Width = QuickActionsWidth;

                BeginAnimation(WidthProperty, null);
                BeginAnimation(LeftProperty, null);
                QuickActionsBar.BeginAnimation(WidthProperty, null);

                UpdateUnifiedIslandGeometry(QuickActionsWidth, QuickActionsHeight);
                _overlayService.ReapplyTopmost(_hwnd);
                OrbitLogger.Info(LogCategory, "Restored Standard Quick Actions bar from Annotation mode.");
            };

            BeginAnimation(WidthProperty, widthAnim);
            BeginAnimation(LeftProperty, leftAnim);
            QuickActionsBar.BeginAnimation(WidthProperty, barWidthAnim);
        }
        else
        {
            // Transition directly back to compact mascot
            _isHoverCollapsing = true;
            _isQuickActionsOpen = false;

            QuickActionsBar.IsHitTestVisible = false;
            var actionsFadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(100));
            QuickActionsBar.BeginAnimation(OpacityProperty, actionsFadeOut);

            var primary = _monitorService.GetPrimaryMonitor();
            var (targetLeft, topY) = _monitorService.GetTopCenterPosition(primary, CollapsedWidth, TopMargin);

            if (double.IsNaN(Left)) Left = targetLeft;
            if (double.IsNaN(Width)) Width = CollapsedWidth;
            if (double.IsNaN(Height)) Height = CollapsedHeight;

            var duration = TimeSpan.FromMilliseconds(220);
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

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
                QuickActionsBar.Width = QuickActionsWidth;

                BeginAnimation(WidthProperty, null);
                BeginAnimation(HeightProperty, null);
                BeginAnimation(LeftProperty, null);

                UpdateUnifiedIslandGeometry(CollapsedWidth, CollapsedHeight);

                QuickActionsBar.Visibility = Visibility.Collapsed;
                QuickActionsBar.Opacity = 0.0;
                AnnotationToolbarPanel.Visibility = Visibility.Collapsed;
                StandardQuickActionsPanel.Visibility = Visibility.Visible;
                StandardQuickActionsPanel.Opacity = 1.0;

                MascotContainerGrid.IsHitTestVisible = true;
                MascotContainerGrid.Opacity = 1.0;

                _isHoverCollapsing = false;
                _isHovering = false;

                _overlayService.ReapplyTopmost(_hwnd);
                OrbitLogger.Info(LogCategory, "Annotation mode cleanly collapsed back to normal mascot.");
            };

            BeginAnimation(WidthProperty, widthAnim);
            BeginAnimation(HeightProperty, heightAnim);
            BeginAnimation(LeftProperty, leftAnim);
        }
    }

    private void OnAnnotateBackClicked(object sender, RoutedEventArgs e)
    {
        ExitAnnotationToolbarMode(returnToQuickActions: true);
    }

    private void OnAnnotateCursorClicked(object sender, RoutedEventArgs e)
    {
        SelectAnnotationTool(AnnotationToolMode.Cursor);
    }

    private void OnAnnotatePenClicked(object sender, RoutedEventArgs e)
    {
        SelectAnnotationTool(AnnotationToolMode.Pen);
    }

    private void OnAnnotateEraserClicked(object sender, RoutedEventArgs e)
    {
        SelectAnnotationTool(AnnotationToolMode.Eraser);
    }

    private void OnAnnotateClearClicked(object sender, RoutedEventArgs e)
    {
        _annotationOverlayWindow?.ClearAllStrokes();
        OrbitLogger.Info(LogCategory, "Annotation Clear All clicked.");
    }

    private async void OnAnnotateSaveClicked(object sender, RoutedEventArgs e)
    {
        await CaptureAnnotationScreenshotAsync();
    }

    private void OnAnnotateTuckClicked(object sender, RoutedEventArgs e)
    {
        TuckAnnotationToolbar(true);
    }

    private void OnTuckedAnnotationRestoreClicked(object sender, RoutedEventArgs e)
    {
        TuckAnnotationToolbar(false);
    }

    private void OnTuckedAnnotationRestoreMouseEnter(object sender, MouseEventArgs e)
    {
        TuckAnnotationToolbar(false);
    }

    public void TuckAnnotationToolbar(bool tuck)
    {
        if (!_isAnnotationModeActive || _isAnnotationToolbarTucked == tuck) return;
        _isAnnotationToolbarTucked = tuck;

        if (AnnotationColorPalettePopup != null)
        {
            AnnotationColorPalettePopup.IsOpen = false;
        }

        var primary = _monitorService.GetPrimaryMonitor();
        double targetWidth = tuck ? TuckedToolbarWidth : AnnotationToolbarWidth;
        var (targetLeft, topY) = _monitorService.GetTopCenterPosition(primary, targetWidth, TopMargin);

        if (double.IsNaN(Left)) Left = targetLeft;
        if (double.IsNaN(Width)) Width = targetWidth;

        var duration = TimeSpan.FromMilliseconds(200);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        if (tuck)
        {
            // Fade out full toolbar panel
            AnnotationToolbarPanel.BeginAnimation(OpacityProperty, null);
            AnnotationToolbarPanel.Visibility = Visibility.Collapsed;
            AnnotationToolbarPanel.Opacity = 0.0;
            AnnotationToolbarPanel.IsHitTestVisible = false;

            // Animate width to tucked width
            if (double.IsNaN(Left)) Left = targetLeft;
            if (double.IsNaN(Width)) Width = targetWidth;
            if (double.IsNaN(Height)) Height = QuickActionsHeight;

        var widthAnim = new DoubleAnimation(targetWidth, duration) { EasingFunction = ease };
            var leftAnim = new DoubleAnimation(targetLeft, duration) { EasingFunction = ease };
            var barWidthAnim = new DoubleAnimation(targetWidth, duration) { EasingFunction = ease };

            widthAnim.Completed += (s, e) =>
            {
                Width = targetWidth;
                Left = targetLeft;
                QuickActionsBar.Width = targetWidth;
                BeginAnimation(WidthProperty, null);
                BeginAnimation(LeftProperty, null);
                QuickActionsBar.BeginAnimation(WidthProperty, null);

                UpdateUnifiedIslandGeometry(targetWidth, QuickActionsHeight);
                _overlayService.ReapplyTopmost(_hwnd);

                // Reveal tucked restore button
                if (BtnTuckedAnnotationRestore != null)
                {
                    BtnTuckedAnnotationRestore.Visibility = Visibility.Visible;
                    BtnTuckedAnnotationRestore.Opacity = 1.0;
                    BtnTuckedAnnotationRestore.IsHitTestVisible = true;
                }
            };

            BeginAnimation(WidthProperty, widthAnim);
            BeginAnimation(LeftProperty, leftAnim);
            QuickActionsBar.BeginAnimation(WidthProperty, barWidthAnim);
            OrbitLogger.Info(LogCategory, "Annotation toolbar tucked away.");
        }
        else
        {
            // Hide tucked restore button
            if (BtnTuckedAnnotationRestore != null)
            {
                BtnTuckedAnnotationRestore.Visibility = Visibility.Collapsed;
                BtnTuckedAnnotationRestore.Opacity = 0.0;
                BtnTuckedAnnotationRestore.IsHitTestVisible = false;
            }

            // Animate width back to full toolbar width
            if (double.IsNaN(Left)) Left = targetLeft;
            if (double.IsNaN(Width)) Width = targetWidth;
            if (double.IsNaN(Height)) Height = QuickActionsHeight;

        var widthAnim = new DoubleAnimation(targetWidth, duration) { EasingFunction = ease };
            var leftAnim = new DoubleAnimation(targetLeft, duration) { EasingFunction = ease };
            var barWidthAnim = new DoubleAnimation(targetWidth, duration) { EasingFunction = ease };

            widthAnim.Completed += (s, e) =>
            {
                Width = targetWidth;
                Left = targetLeft;
                QuickActionsBar.Width = targetWidth;
                BeginAnimation(WidthProperty, null);
                BeginAnimation(LeftProperty, null);
                QuickActionsBar.BeginAnimation(WidthProperty, null);

                UpdateUnifiedIslandGeometry(targetWidth, QuickActionsHeight);
                _overlayService.ReapplyTopmost(_hwnd);

                // Reveal full toolbar panel
                AnnotationToolbarPanel.Visibility = Visibility.Visible;
                AnnotationToolbarPanel.Opacity = 1.0;
                AnnotationToolbarPanel.IsHitTestVisible = true;
            };

            BeginAnimation(WidthProperty, widthAnim);
            BeginAnimation(LeftProperty, leftAnim);
            QuickActionsBar.BeginAnimation(WidthProperty, barWidthAnim);
            OrbitLogger.Info(LogCategory, "Annotation toolbar restored from tucked state.");
        }
    }

    /// <summary>
    /// Captures the full screen combining underlying windows and annotation ink strokes,
    /// excluding the ORBIT dynamic island toolbar. Saves to Pictures\ORBIT Screenshots and copies to Clipboard.
    /// </summary>
    public async Task<string?> CaptureAnnotationScreenshotAsync()
    {
        try
        {
            OrbitLogger.Info(LogCategory, "Starting annotation screenshot capture.");

            // 1. Temporarily hide MainWindow and Palette popup
            bool paletteWasOpen = AnnotationColorPalettePopup != null && AnnotationColorPalettePopup.IsOpen;
            if (paletteWasOpen)
            {
                AnnotationColorPalettePopup.IsOpen = false;
            }

            double previousOpacity = Opacity;
            Opacity = 0.0;

            // Wait a brief frame so DWM composites the desktop without MainWindow
            await Task.Delay(50);

            // Determine virtual screen bounds (spans all connected monitors)
            int left = (int)SystemParameters.VirtualScreenLeft;
            int top = (int)SystemParameters.VirtualScreenTop;
            int width = Math.Max(1, (int)SystemParameters.VirtualScreenWidth);
            int height = Math.Max(1, (int)SystemParameters.VirtualScreenHeight);

            using var bitmap = new System.Drawing.Bitmap(width, height);
            using (var g = System.Drawing.Graphics.FromImage(bitmap))
            {
                try
                {
                    g.CopyFromScreen(left, top, 0, 0, new System.Drawing.Size(width, height), System.Drawing.CopyPixelOperation.SourceCopy);
                }
                catch (Exception screenEx)
                {
                    OrbitLogger.Warn(LogCategory, $"CopyFromScreen headless fallback: {screenEx.Message}");
                    g.Clear(System.Drawing.Color.FromArgb(20, 20, 30));
                }
            }

            // Restore MainWindow visibility
            Opacity = previousOpacity;
            if (paletteWasOpen)
            {
                AnnotationColorPalettePopup.IsOpen = true;
            }

            // Trigger visual shutter flash on the overlay
            _annotationOverlayWindow?.TriggerShutterFlash();

            // 2. Determine target save file path
            string picturesDir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            string screenshotsDir = Path.Combine(picturesDir, "ORBIT Screenshots");
            Directory.CreateDirectory(screenshotsDir);

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"ORBIT_Annotation_{timestamp}.png";
            string filePath = Path.Combine(screenshotsDir, fileName);

            int suffix = 1;
            while (File.Exists(filePath))
            {
                filePath = Path.Combine(screenshotsDir, $"ORBIT_Annotation_{timestamp}_{suffix++}.png");
            }

            // 3. Save composited bitmap as PNG
            bitmap.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
            OrbitLogger.Info(LogCategory, $"Annotation screenshot saved to: {filePath}");

            void RunOnUi(Action act)
            {
                if (Dispatcher.CheckAccess()) act();
                else Dispatcher.Invoke(act);
            }

            // 4. Copy actual image data to Windows Clipboard on UI/STA thread
            try
            {
                using var ms = new MemoryStream();
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;

                RunOnUi(() =>
                {
                    var bitmapImage = new System.Windows.Media.Imaging.BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bitmapImage.StreamSource = ms;
                    bitmapImage.EndInit();
                    bitmapImage.Freeze();

                    System.Windows.Clipboard.SetImage(bitmapImage);
                    OrbitLogger.Info(LogCategory, "Annotation screenshot copied to Windows clipboard.");
                });
            }
            catch (Exception clipEx)
            {
                OrbitLogger.Warn(LogCategory, $"Failed to copy screenshot to clipboard: {clipEx.Message}");
            }

            // 5. Show toast notification on the dynamic island
            RunOnUi(() => ShowAnnotationToast("Screenshot saved & copied!"));

            return filePath;
        }
        catch (Exception ex)
        {
            void RunOnUi(Action act)
            {
                if (Dispatcher.CheckAccess()) act();
                else Dispatcher.Invoke(act);
            }

            RunOnUi(() =>
            {
                Opacity = 1.0;
                ShowAnnotationToast("Failed to save screenshot", isError: true);
            });
            OrbitLogger.Error(LogCategory, $"Failed to capture annotation screenshot: {ex.Message}");
            return null;
        }
    }

    private CancellationTokenSource? _annotationToastCts;

    public void ShowAnnotationToast(string message, bool isError = false)
    {
        if (AnnotationToastBorder == null || TxtAnnotationToast == null) return;

        _annotationToastCts?.Cancel();
        _annotationToastCts = new CancellationTokenSource();
        var ct = _annotationToastCts.Token;

        TxtAnnotationToast.Text = message;
        if (isError)
        {
            AnnotationToastBorder.BorderBrush = new SolidColorBrush(MediaColor.FromRgb(0xF4, 0x3F, 0x5E));
            TxtAnnotationToast.Foreground = new SolidColorBrush(MediaColor.FromRgb(0xFB, 0x71, 0x85));
        }
        else
        {
            AnnotationToastBorder.BorderBrush = new SolidColorBrush(MediaColor.FromRgb(0x4A, 0xDE, 0x80));
            TxtAnnotationToast.Foreground = new SolidColorBrush(MediaColor.FromRgb(0x4A, 0xDE, 0x80));
        }

        AnnotationToastBorder.Visibility = Visibility.Visible;
        AnnotationToastBorder.Opacity = 1.0;

        Task.Delay(2400, ct).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            Dispatcher.InvokeAsync(() =>
            {
                var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(200));
                fadeOut.Completed += (s, e) =>
                {
                    AnnotationToastBorder.Visibility = Visibility.Collapsed;
                };
                AnnotationToastBorder.BeginAnimation(OpacityProperty, fadeOut);
            });
        }, TaskScheduler.Default);
    }

    private void ShowAnnotationHideConfirmation()
    {
        _isConfirmHideDialogOpen = true;
        try
        {
            OrbitLogger.Info(LogCategory, "Showing annotation hide confirmation dialog.");
            var dialog = new Orbit.UI.AnnotationConfirmHideDialog();
            dialog.Owner = this;
            bool? result = dialog.ShowDialog();

            if (result == true)
            {
                OrbitLogger.Info(LogCategory, "User confirmed clearing annotations and hiding ORBIT.");
                ExitAnnotationToolbarMode(returnToQuickActions: false, immediate: true);
                _lifecycleService.Hide();
            }
            else
            {
                OrbitLogger.Info(LogCategory, "User cancelled hide request. Continuing annotation session.");
            }
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, $"Failed to show hide confirmation dialog: {ex.Message}");
        }
        finally
        {
            _isConfirmHideDialogOpen = false;
        }
    }

    private IntPtr _keyboardHook = IntPtr.Zero;
    private NativeMethods.LowLevelKeyboardProc? _keyboardProc;

    private void InstallAnnotationKeyboardHook()
    {
        if (_keyboardHook != IntPtr.Zero) return;

        try
        {
            _keyboardProc = LowLevelKeyboardCallback;
            using var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
            using var currentModule = currentProcess.MainModule;
            var moduleHandle = NativeMethods.GetModuleHandle(currentModule?.ModuleName);

            _keyboardHook = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_KEYBOARD_LL,
                _keyboardProc,
                moduleHandle,
                0);

            OrbitLogger.Info(LogCategory, $"Scoped annotation keyboard hook installed (Hook=0x{_keyboardHook.ToInt64():X}).");
        }
        catch (Exception ex)
        {
            OrbitLogger.Warn(LogCategory, $"Failed to install annotation keyboard hook: {ex.Message}");
        }
    }

    private void UninstallAnnotationKeyboardHook()
    {
        if (_keyboardHook == IntPtr.Zero) return;

        try
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            OrbitLogger.Info(LogCategory, "Scoped annotation keyboard hook uninstalled.");
        }
        catch (Exception ex)
        {
            OrbitLogger.Warn(LogCategory, $"Failed to uninstall annotation keyboard hook: {ex.Message}");
        }
        finally
        {
            _keyboardHook = IntPtr.Zero;
            _keyboardProc = null;
        }
    }

    private IntPtr LowLevelKeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)NativeMethods.WM_KEYDOWN || wParam == (IntPtr)NativeMethods.WM_SYSKEYDOWN))
        {
            int vkCode = Marshal.ReadInt32(lParam);
            if (vkCode == NativeMethods.VK_ESCAPE && _isAnnotationModeActive)
            {
                OrbitLogger.Info(LogCategory, "Global Escape key pressed during annotation mode. Exiting annotation mode.");
                Dispatcher.InvokeAsync(() =>
                {
                    if (_isAnnotationModeActive)
                    {
                        ExitAnnotationToolbarMode(returnToQuickActions: false, immediate: false);
                    }
                });
                return (IntPtr)1; // Consume Escape key
            }
        }

        return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    public void SelectAnnotationTool(AnnotationToolMode mode)
    {
        _currentAnnotationTool = mode;
        UpdateAnnotationToolVisuals();

        // Subtle animation when switching tools
        if (mode == AnnotationToolMode.Cursor)
            AnimateToolButtonSelection(BorderAnnotateCursorBg);
        else if (mode == AnnotationToolMode.Pen)
            AnimateToolButtonSelection(BorderAnnotatePenBg);
        else if (mode == AnnotationToolMode.Eraser)
            AnimateToolButtonSelection(BorderAnnotateEraserBg);

        _annotationOverlayWindow?.SetTool(mode);

        OrbitLogger.Info(LogCategory, $"Annotation tool selected: {mode}");
    }

    private void AnimateToolButtonSelection(Border? border)
    {
        if (border == null) return;
        if (border.RenderTransform is not ScaleTransform)
        {
            border.RenderTransformOrigin = new Point(0.5, 0.5);
            border.RenderTransform = new ScaleTransform(1.0, 1.0);
        }
        var scaleAnim = new DoubleAnimation(0.88, 1.0, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        border.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        border.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
    }

    private void UpdateAnnotationToolVisuals()
    {
        // 1. Cursor / Select Tool
        // Unselected: white border only, transparent fill.
        // Selected: blue color fill & stroke + blue neon glow.
        bool isCursor = _currentAnnotationTool == AnnotationToolMode.Cursor;
        if (BorderAnnotateCursorBg != null)
        {
            BorderAnnotateCursorBg.Background = Brushes.Transparent;
            BorderAnnotateCursorBg.BorderBrush = Brushes.Transparent;
            BorderAnnotateCursorBg.BorderThickness = new Thickness(0);
            BorderAnnotateCursorBg.Effect = null;
        }
        if (IconAnnotateCursor != null)
        {
            IconAnnotateCursor.Fill = isCursor
                ? new SolidColorBrush(MediaColor.FromRgb(0x38, 0xBD, 0xF8))
                : Brushes.Transparent;
            IconAnnotateCursor.Stroke = isCursor
                ? new SolidColorBrush(MediaColor.FromRgb(0x38, 0xBD, 0xF8))
                : Brushes.White;
            IconAnnotateCursor.Effect = isCursor
                ? new System.Windows.Media.Effects.DropShadowEffect { Color = MediaColor.FromRgb(0x38, 0xBD, 0xF8), BlurRadius = 10, ShadowDepth = 0, Opacity = 0.95 }
                : null;
        }

        // 2. Pen Tool
        // Normally: white bordered, filled with color chosen from palette.
        // When selected: neon border glow matching the chosen color!
        bool isPen = _currentAnnotationTool == AnnotationToolMode.Pen;
        if (BorderAnnotatePenBg != null)
        {
            BorderAnnotatePenBg.Background = Brushes.Transparent;
            BorderAnnotatePenBg.BorderBrush = Brushes.Transparent;
            BorderAnnotatePenBg.BorderThickness = new Thickness(0);
            BorderAnnotatePenBg.Effect = null;
        }
        if (IconAnnotatePen != null)
        {
            IconAnnotatePen.Stroke = Brushes.White;
            IconAnnotatePen.Fill = new SolidColorBrush(_currentAnnotationColor);
            IconAnnotatePen.Effect = isPen
                ? new System.Windows.Media.Effects.DropShadowEffect { Color = _currentAnnotationColor, BlurRadius = 12, ShadowDepth = 0, Opacity = 1.0 }
                : null;
        }

        // 3. Eraser Tool
        // Unselected: white border only, transparent fill.
        // When selected: filled white with white neon glow!
        bool isEraser = _currentAnnotationTool == AnnotationToolMode.Eraser;
        if (BorderAnnotateEraserBg != null)
        {
            BorderAnnotateEraserBg.Background = Brushes.Transparent;
            BorderAnnotateEraserBg.BorderBrush = Brushes.Transparent;
            BorderAnnotateEraserBg.BorderThickness = new Thickness(0);
            BorderAnnotateEraserBg.Effect = null;
        }
        if (IconAnnotateEraser != null)
        {
            IconAnnotateEraser.Fill = isEraser ? Brushes.White : Brushes.Transparent;
            IconAnnotateEraser.Stroke = Brushes.White;
            IconAnnotateEraser.Effect = isEraser
                ? new System.Windows.Media.Effects.DropShadowEffect { Color = MediaColor.FromRgb(0xFF, 0xFF, 0xFF), BlurRadius = 10, ShadowDepth = 0, Opacity = 0.95 }
                : null;
        }

        // 4. Update Current Color Swatch in toolbar
        if (BorderCurrentColorSwatch != null)
        {
            BorderCurrentColorSwatch.Background = new SolidColorBrush(_currentAnnotationColor);
        }
        if (GlowCurrentColorSwatch != null)
        {
            GlowCurrentColorSwatch.Color = _currentAnnotationColor;
        }
    }

    private void OnAnnotateToolButtonMouseEnter(object sender, MouseEventArgs e)
    {
        var hoverBrush = new SolidColorBrush(MediaColor.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
        if (ReferenceEquals(sender, BtnAnnotateCursor) && _currentAnnotationTool != AnnotationToolMode.Cursor && BorderAnnotateCursorBg != null)
            BorderAnnotateCursorBg.Background = hoverBrush;
        else if (ReferenceEquals(sender, BtnAnnotatePen) && _currentAnnotationTool != AnnotationToolMode.Pen && BorderAnnotatePenBg != null)
            BorderAnnotatePenBg.Background = hoverBrush;
        else if (ReferenceEquals(sender, BtnAnnotateEraser) && _currentAnnotationTool != AnnotationToolMode.Eraser && BorderAnnotateEraserBg != null)
            BorderAnnotateEraserBg.Background = hoverBrush;
    }

    private void OnAnnotateToolButtonMouseLeave(object sender, MouseEventArgs e)
    {
        UpdateAnnotationToolVisuals();
    }

    private void OnAnnotateColorPickerClicked(object sender, RoutedEventArgs e)
    {
        if (AnnotationColorPalettePopup == null) return;
        AnnotationColorPalettePopup.IsOpen = !AnnotationColorPalettePopup.IsOpen;
    }

    private void OnAnnotateColorPickerMouseEnter(object sender, MouseEventArgs e)
    {
        if (BorderColorPickerContainer != null)
            BorderColorPickerContainer.Background = new SolidColorBrush(MediaColor.FromArgb(0x25, 0xFF, 0xFF, 0xFF));
    }

    private void OnAnnotateColorPickerMouseLeave(object sender, MouseEventArgs e)
    {
        if (BorderColorPickerContainer != null)
            BorderColorPickerContainer.Background = Brushes.Transparent;
    }

    private void OnPaletteColorPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.Tag is string hex)
        {
            OrbitLogger.Info(LogCategory, $"Color swatch selected via PreviewMouseDown: {hex}");
            SelectAnnotationColor(hex);
            if (AnnotationColorPalettePopup != null)
            {
                AnnotationColorPalettePopup.IsOpen = false;
            }
            e.Handled = true;
        }
    }

    private void OnPaletteColorClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string hex)
        {
            OrbitLogger.Info(LogCategory, $"Color swatch clicked: {hex}");
            SelectAnnotationColor(hex);
            if (AnnotationColorPalettePopup != null)
            {
                AnnotationColorPalettePopup.IsOpen = false;
            }
        }
    }

    /// <summary>
    /// Checks whether a given physical screen point is over ORBIT's dynamic island or the color palette popup.
    /// Used by DesktopAnnotationOverlayWindow to return HTTRANSPARENT so ORBIT UI is always clickable.
    /// </summary>
    public bool IsPointOverOrbitUI(NativeMethods.POINT pt)
    {
        // 1. Check MainWindow screen bounds
        if (_hwnd != IntPtr.Zero && NativeMethods.IsWindow(_hwnd))
        {
            if (NativeMethods.GetWindowRect(_hwnd, out var mainRect))
            {
                if (pt.X >= mainRect.Left && pt.X < mainRect.Right &&
                    pt.Y >= mainRect.Top && pt.Y < mainRect.Bottom)
                {
                    return true;
                }
            }
        }

        // 2. Check AnnotationColorPalettePopup if open
        if (AnnotationColorPalettePopup != null && AnnotationColorPalettePopup.IsOpen)
        {
            if (AnnotationColorPalettePopup.Child is UIElement child && child.IsVisible)
            {
                try
                {
                    if (PresentationSource.FromVisual(child) is HwndSource source &&
                        source.Handle != IntPtr.Zero && NativeMethods.IsWindow(source.Handle))
                    {
                        if (NativeMethods.GetWindowRect(source.Handle, out var popupRect))
                        {
                            if (pt.X >= popupRect.Left - 6 && pt.X <= popupRect.Right + 6 &&
                                pt.Y >= popupRect.Top - 6 && pt.Y <= popupRect.Bottom + 6)
                            {
                                return true;
                            }
                        }
                    }

                    var topLeft = child.PointToScreen(new Point(0, 0));
                    var bottomRight = child.PointToScreen(new Point(child.RenderSize.Width, child.RenderSize.Height));
                    if (pt.X >= topLeft.X - 6 && pt.X <= bottomRight.X + 6 &&
                        pt.Y >= topLeft.Y - 6 && pt.Y <= bottomRight.Y + 6)
                    {
                        return true;
                    }
                }
                catch
                {
                    // Visual tree might be detaching during close
                }
            }
        }

        return false;
    }

    public void SelectAnnotationColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return;

        try
        {
            var color = (MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            _currentAnnotationColorHex = hex;
            _currentAnnotationColor = color;

            UpdatePaletteCheckmarks(hex);
            UpdateAnnotationToolVisuals();

            _annotationOverlayWindow?.SetPenColor(_currentAnnotationColor);

            OrbitLogger.Info(LogCategory, $"Annotation color selected: {hex}");
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, $"Failed to set annotation color {hex}: {ex.Message}");
        }
    }

    private void UpdatePaletteCheckmarks(string selectedHex)
    {
        if (Check_Red != null) Check_Red.Visibility = string.Equals(selectedHex, "#EF4444", StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        if (Check_Orange != null) Check_Orange.Visibility = string.Equals(selectedHex, "#F97316", StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        if (Check_Yellow != null) Check_Yellow.Visibility = string.Equals(selectedHex, "#EAB308", StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        if (Check_Green != null) Check_Green.Visibility = string.Equals(selectedHex, "#22C55E", StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        if (Check_Cyan != null) Check_Cyan.Visibility = string.Equals(selectedHex, "#06B6D4", StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        if (Check_Blue != null) Check_Blue.Visibility = string.Equals(selectedHex, "#3B82F6", StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        if (Check_Purple != null) Check_Purple.Visibility = string.Equals(selectedHex, "#A855F7", StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        if (Check_Pink != null) Check_Pink.Visibility = string.Equals(selectedHex, "#EC4899", StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        if (Check_White != null) Check_White.Visibility = string.Equals(selectedHex, "#FFFFFF", StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        if (Check_Black != null) Check_Black.Visibility = string.Equals(selectedHex, "#0F172A", StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
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

        bool isExpandedDashboard = _lifecycleService?.CurrentState == CompanionState.Expanded
            || _lifecycleService?.CurrentState == CompanionState.Expanding
            || _lifecycleService?.CurrentState == CompanionState.Collapsing
            || _isTransitioningToFullExpand
            || PillMascot.IsExpanded
            || (ExpandedView != null && ExpandedView.Visibility == Visibility.Visible);

        // Dedicated notch shelf line: faint indication of the mascot notch boundary
        var notchShelf = new StreamGeometry();
        using (var ctx = notchShelf.Open())
        {
            if (isExpandedDashboard)
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
            else if (_isAiGenerating || _isAiResponseBubbleOpen)
            {
                double startX = cx - 80.0;
                double shelfY = 30.0;
                double y0 = shelfY * (7.0 / 30.0);
                double y1 = shelfY * (14.0 / 30.0);
                double y2 = shelfY * (21.0 / 30.0);

                ctx.BeginFigure(new Point(startX + 18, 0), isFilled: false, isClosed: false);
                ctx.BezierTo(new Point(startX + 25, 0), new Point(startX + 33, y0), new Point(startX + 36, y1), true, true);
                ctx.BezierTo(new Point(startX + 39, y2), new Point(startX + 46, shelfY), new Point(startX + 54, shelfY), true, true);
                ctx.LineTo(new Point(startX + 106, shelfY), true, true);
                ctx.BezierTo(new Point(startX + 114, shelfY), new Point(startX + 121, y2), new Point(startX + 124, y1), true, true);
                ctx.BezierTo(new Point(startX + 127, y0), new Point(startX + 135, 0), new Point(startX + 142, 0), true, true);
            }
            else
            {
                double shelfY = Math.Min(40.0, height - 8.0);
                if (height <= 40.0) shelfY = 30.0;
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
        }
        notchShelf.Freeze();

        // 1. Morphing / Expanded island card contour (STRICTLY only when fully expanding or expanded!)
        if (isExpandedDashboard)
        {
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

        // 2. AI Response Bubble or Generating: Centered 160 DIP notch cradle at top only (rest of window remains transparent)
        if (_isAiGenerating || _isAiResponseBubbleOpen)
        {
            double startX = cx - 80.0;
            double shelfY = 30.0;
            double y0 = shelfY * (7.0 / 30.0);
            double y1 = shelfY * (14.0 / 30.0);
            double y2 = shelfY * (21.0 / 30.0);

            var bg = new StreamGeometry();
            using (var ctx = bg.Open())
            {
                ctx.BeginFigure(new Point(startX, 0), isFilled: true, isClosed: true);
                ctx.LineTo(new Point(startX + 18, 0), true, true);
                ctx.BezierTo(new Point(startX + 25, 0), new Point(startX + 33, y0), new Point(startX + 36, y1), true, true);
                ctx.BezierTo(new Point(startX + 39, y2), new Point(startX + 46, shelfY), new Point(startX + 54, shelfY), true, true);
                ctx.LineTo(new Point(startX + 106, shelfY), true, true);
                ctx.BezierTo(new Point(startX + 114, shelfY), new Point(startX + 121, y2), new Point(startX + 124, y1), true, true);
                ctx.BezierTo(new Point(startX + 127, y0), new Point(startX + 135, 0), new Point(startX + 142, 0), true, true);
                ctx.LineTo(new Point(startX + 160, 0), true, true);
            }
            bg.Freeze();

            var outline = new StreamGeometry();
            using (var ctx = outline.Open())
            {
                ctx.BeginFigure(new Point(startX, 0), isFilled: false, isClosed: false);
                ctx.LineTo(new Point(startX + 18, 0), true, true);
                ctx.BezierTo(new Point(startX + 25, 0), new Point(startX + 33, y0), new Point(startX + 36, y1), true, true);
                ctx.BezierTo(new Point(startX + 39, y2), new Point(startX + 46, shelfY), new Point(startX + 54, shelfY), true, true);
                ctx.LineTo(new Point(startX + 106, shelfY), true, true);
                ctx.BezierTo(new Point(startX + 114, shelfY), new Point(startX + 121, y2), new Point(startX + 124, y1), true, true);
                ctx.BezierTo(new Point(startX + 127, y0), new Point(startX + 135, 0), new Point(startX + 142, 0), true, true);
                ctx.LineTo(new Point(startX + 160, 0), true, true);
            }
            outline.Freeze();

            return (bg, outline, notchShelf);
        }

        // 3. Collapsed State: Standard compact notch or horizontally expanded QuickActions notch
        // NEVER draw the solid dashboard background when in Collapsed state, regardless of window Height!
        {
            double shelfY = Math.Min(40.0, height - 8.0);
            if (height <= 40.0) shelfY = 30.0;
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

    protected override void OnClosed(EventArgs e)
    {
        if (_annotationOverlayWindow != null)
        {
            try
            {
                _annotationOverlayWindow.ClearAllStrokes();
                _annotationOverlayWindow.Close();
            }
            catch { }
            _annotationOverlayWindow = null;
        }

        base.OnClosed(e);
    }

    #endregion
}
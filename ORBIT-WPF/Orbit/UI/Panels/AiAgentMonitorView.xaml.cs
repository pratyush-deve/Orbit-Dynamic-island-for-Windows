using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Orbit.Core;
using Orbit.Models;
using Orbit.Services.AiObserver;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace Orbit.UI.Panels;

public partial class AiAgentMonitorView : UserControl, IDisposable
{
    private const string LogCategory = "AI_MONITOR_VIEW";
    private const double TargetHeight = 510.0;

    public event Action? BackRequested;
    public event Action<double>? DesiredHeightChanged;

    private IAgentStateManager? _stateManager;
    private AntigravityAppType _selectedApp = AntigravityAppType.AntigravityIde;
    private bool _userExplicitlySelected;
    private bool _isPromptExpanded;
    private bool _disposed;

    private readonly SolidColorBrush _brushCardSelectedBorder = new((Color)ColorConverter.ConvertFromString("#A855F7"));
    private readonly SolidColorBrush _brushCardSelectedBg = new((Color)ColorConverter.ConvertFromString("#23163A"));
    private readonly SolidColorBrush _brushCardNormalBorder = new((Color)ColorConverter.ConvertFromString("#3D295F"));
    private readonly SolidColorBrush _brushCardNormalBg = new((Color)ColorConverter.ConvertFromString("#191226"));

    public AiAgentMonitorView()
    {
        InitializeComponent();
    }

    public double GetDesiredHeight() => TargetHeight;

    public void Initialize(IAgentStateManager stateManager)
    {
        if (_stateManager != null) return;

        _stateManager = stateManager;
        _stateManager.StateUpdated += OnStateUpdated;

        // Initial render with current state
        UpdateUI(_stateManager.AntigravitySession, _stateManager.AntigravityIdeSession);
    }

    private void OnStateUpdated(AgentSessionInfo ag, AgentSessionInfo ide)
    {
        if (CheckAccess())
        {
            UpdateUI(ag, ide);
        }
        else
        {
            Dispatcher.Invoke(() => UpdateUI(ag, ide));
        }
    }

    private void UpdateUI(AgentSessionInfo ag, AgentSessionInfo ide)
    {
        // 1. Dynamic Agent Selection
        bool agWorking = ag.State == AgentState.Working || ag.State == AgentState.Attention;
        bool ideWorking = ide.State == AgentState.Working || ide.State == AgentState.Attention;

        if (!_userExplicitlySelected)
        {
            if (agWorking && !ideWorking)
            {
                _selectedApp = AntigravityAppType.Antigravity;
            }
            else if (ideWorking && !agWorking)
            {
                _selectedApp = AntigravityAppType.AntigravityIde;
            }
            else if (!agWorking && !ideWorking)
            {
                // Default to whichever has the most recent activity or Antigravity IDE
                _selectedApp = ag.LastModifiedTime > ide.LastModifiedTime ? AntigravityAppType.Antigravity : AntigravityAppType.AntigravityIde;
            }
        }

        // Active agents count badge
        int activeCount = (agWorking ? 1 : 0) + (ideWorking ? 1 : 0);
        TxtActiveCount.Text = activeCount > 0 ? $"{activeCount} ACTIVE" : "IDLE";
        BadgeActiveAgents.BorderBrush = activeCount > 0 ? (Brush)FindResource("Brush.Accent.PurpleLight") : _brushCardNormalBorder;

        // 2. Render Card 1: Antigravity
        ImgAgIcon.Source = ag.AppIcon;
        TxtAgState.Text = GetStateLabel(ag.State);
        BadgeAgState.Background = GetStateBadgeBackground(ag.State);
        TxtAgState.Foreground = GetStateTextColor(ag.State);
        TxtAgTaskTitle.Text = !string.IsNullOrWhiteSpace(ag.TaskTitle) ? ag.TaskTitle : "No active task";
        TxtAgWorkspace.Text = !string.IsNullOrWhiteSpace(ag.WorkspaceName) ? ag.WorkspaceName : "Standing by";

        // 3. Render Card 2: Antigravity IDE
        ImgIdeIcon.Source = ide.AppIcon;
        TxtIdeState.Text = GetStateLabel(ide.State);
        BadgeIdeState.Background = GetStateBadgeBackground(ide.State);
        TxtIdeState.Foreground = GetStateTextColor(ide.State);
        TxtIdeTaskTitle.Text = !string.IsNullOrWhiteSpace(ide.TaskTitle) ? ide.TaskTitle : "No active task";
        TxtIdeWorkspace.Text = !string.IsNullOrWhiteSpace(ide.WorkspaceName) ? ide.WorkspaceName : "Standing by";

        // Highlight selected card
        if (_selectedApp == AntigravityAppType.Antigravity)
        {
            CardAntigravity.BorderBrush = _brushCardSelectedBorder;
            CardAntigravity.Background = _brushCardSelectedBg;
            CardAntigravity.BorderThickness = new Thickness(1.5);

            CardAntigravityIde.BorderBrush = _brushCardNormalBorder;
            CardAntigravityIde.Background = _brushCardNormalBg;
            CardAntigravityIde.BorderThickness = new Thickness(1);
        }
        else
        {
            CardAntigravityIde.BorderBrush = _brushCardSelectedBorder;
            CardAntigravityIde.Background = _brushCardSelectedBg;
            CardAntigravityIde.BorderThickness = new Thickness(1.5);

            CardAntigravity.BorderBrush = _brushCardNormalBorder;
            CardAntigravity.Background = _brushCardNormalBg;
            CardAntigravity.BorderThickness = new Thickness(1);
        }

        // 4. Render Detailed Panel for Selected App
        var selected = _selectedApp == AntigravityAppType.Antigravity ? ag : ide;
        RenderDetailPanel(selected);
    }

    private void RenderDetailPanel(AgentSessionInfo session)
    {
        bool hasTask = !string.IsNullOrWhiteSpace(session.TaskTitle) || !string.IsNullOrWhiteSpace(session.LatestPrompt);

        if (!hasTask && session.State == AgentState.Idle)
        {
            DetailScrollViewer.Visibility = Visibility.Collapsed;
            PanelEmptyState.Visibility = Visibility.Visible;
            return;
        }

        DetailScrollViewer.Visibility = Visibility.Visible;
        PanelEmptyState.Visibility = Visibility.Collapsed;

        TxtDetailAppName.Text = session.AppName;
        TxtDetailWorkspace.Text = !string.IsNullOrWhiteSpace(session.WorkspaceName) ? session.WorkspaceName : "Global Workspace";
        TxtDetailStepCount.Text = $"Step {session.StepCount:N0}";

        TxtDetailTaskTitle.Text = !string.IsNullOrWhiteSpace(session.TaskTitle) ? session.TaskTitle : "Active Task";
        TxtDetailPrompt.Text = !string.IsNullOrWhiteSpace(session.LatestPrompt) ? session.LatestPrompt : "No prompt submitted.";
        TxtDetailLatestActivity.Text = !string.IsNullOrWhiteSpace(session.StatusMessage) ? session.StatusMessage : "Agent standing by.";

        // Modified files list
        if (session.ModifiedFiles.Count > 0)
        {
            PanelModifiedFiles.Visibility = Visibility.Visible;
            TxtHeaderModifiedFiles.Text = $"MODIFIED FILES ({session.ModifiedFiles.Count})";
            ItemsModifiedFiles.ItemsSource = session.ModifiedFiles.Take(6).ToList();
        }
        else
        {
            PanelModifiedFiles.Visibility = Visibility.Collapsed;
        }

        // Executed commands list
        if (session.ExecutedCommands.Count > 0)
        {
            PanelExecutedCommands.Visibility = Visibility.Visible;
            TxtHeaderCommands.Text = $"RECENT COMMANDS ({session.ExecutedCommands.Count})";
            ItemsExecutedCommands.ItemsSource = session.ExecutedCommands.Take(4).ToList();
        }
        else
        {
            PanelExecutedCommands.Visibility = Visibility.Collapsed;
        }
    }

    private void OnCardAntigravityClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _selectedApp = AntigravityAppType.Antigravity;
        _userExplicitlySelected = true;
        if (_stateManager != null)
        {
            UpdateUI(_stateManager.AntigravitySession, _stateManager.AntigravityIdeSession);
        }
    }

    private void OnCardAntigravityIdeClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _selectedApp = AntigravityAppType.AntigravityIde;
        _userExplicitlySelected = true;
        if (_stateManager != null)
        {
            UpdateUI(_stateManager.AntigravitySession, _stateManager.AntigravityIdeSession);
        }
    }

    private void OnTogglePromptClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _isPromptExpanded = !_isPromptExpanded;
        if (_isPromptExpanded)
        {
            TxtDetailPrompt.MaxHeight = double.PositiveInfinity;
            TxtDetailPrompt.TextTrimming = TextTrimming.None;
            BtnTogglePrompt.Text = "Show Less";
        }
        else
        {
            TxtDetailPrompt.MaxHeight = 50;
            TxtDetailPrompt.TextTrimming = TextTrimming.CharacterEllipsis;
            BtnTogglePrompt.Text = "Show More";
        }
        DesiredHeightChanged?.Invoke(GetDesiredHeight());
    }

    private void OnBackClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        BackRequested?.Invoke();
    }

    private void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _stateManager?.Refresh();
    }

    private void OnViewBackgroundMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private static string GetStateLabel(AgentState state) => state switch
    {
        AgentState.Working => "WORKING",
        AgentState.Attention => "ATTENTION",
        AgentState.Error => "ERROR",
        AgentState.Completed => "DONE",
        AgentState.Cancelled => "CANCELLED",
        _ => "IDLE"
    };

    private static Brush GetStateBadgeBackground(AgentState state) => state switch
    {
        AgentState.Working => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B1D5A")),
        AgentState.Attention => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#452A15")),
        AgentState.Error => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#451A1A")),
        AgentState.Completed => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#153E2A")),
        _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#201633"))
    };

    private static Brush GetStateTextColor(AgentState state) => state switch
    {
        AgentState.Working => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C084FC")),
        AgentState.Attention => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FBBF24")),
        AgentState.Error => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F87171")),
        AgentState.Completed => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399")),
        _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B7A9F"))
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_stateManager != null)
        {
            _stateManager.StateUpdated -= OnStateUpdated;
        }
    }
}

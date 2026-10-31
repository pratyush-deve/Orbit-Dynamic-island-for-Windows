using System.Windows;
using System.Windows.Controls;
using Orbit.Core;
using Orbit.Models;
using Orbit.Services.AiObserver;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Image = System.Windows.Controls.Image;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace Orbit.UI.Panels;

/// <summary>
/// Interaction logic for AiAgentMonitorView.xaml.
/// Lightweight, compact AI Activity Monitor displaying only currently running Antigravity applications
/// with clean WORKING / IDLE / DONE states and instant "See Output" window activation.
/// </summary>
public partial class AiAgentMonitorView : UserControl, IDisposable
{
    private const string LogCategory = "AI_MONITOR_VIEW";

    public event Action? BackRequested;
    public event Action<double>? DesiredHeightChanged;

    private IAgentStateManager? _stateManager;
    private bool _disposed;

    // State styling brushes
    private readonly SolidColorBrush _brushWorkingBg = new((Color)ColorConverter.ConvertFromString("#2E1848"));
    private readonly SolidColorBrush _brushWorkingFg = new((Color)ColorConverter.ConvertFromString("#C084FC"));
    private readonly SolidColorBrush _brushWorkingBorder = new((Color)ColorConverter.ConvertFromString("#A855F7"));

    private readonly SolidColorBrush _brushDoneBg = new((Color)ColorConverter.ConvertFromString("#133827"));
    private readonly SolidColorBrush _brushDoneFg = new((Color)ColorConverter.ConvertFromString("#34D399"));
    private readonly SolidColorBrush _brushDoneBorder = new((Color)ColorConverter.ConvertFromString("#10B981"));

    private readonly SolidColorBrush _brushAttentionBg = new((Color)ColorConverter.ConvertFromString("#451A1A"));
    private readonly SolidColorBrush _brushAttentionFg = new((Color)ColorConverter.ConvertFromString("#F87171"));
    private readonly SolidColorBrush _brushAttentionBorder = new((Color)ColorConverter.ConvertFromString("#EF4444"));

    private readonly SolidColorBrush _brushIdleBg = new((Color)ColorConverter.ConvertFromString("#201633"));
    private readonly SolidColorBrush _brushIdleFg = new((Color)ColorConverter.ConvertFromString("#8B7A9F"));
    private readonly SolidColorBrush _brushIdleBorder = new((Color)ColorConverter.ConvertFromString("#3D295F"));

    public AiAgentMonitorView()
    {
        InitializeComponent();
    }

    public void Initialize(IAgentStateManager stateManager)
    {
        _stateManager = stateManager;
        _stateManager.StateUpdated += OnStateUpdated;

        UpdateUI(_stateManager.AntigravitySession, _stateManager.AntigravityIdeSession);
    }

    public double GetDesiredHeight()
    {
        if (_stateManager == null) return 175.0;

        bool agRunning = _stateManager.AntigravitySession.IsProcessRunning;
        bool ideRunning = _stateManager.AntigravityIdeSession.IsProcessRunning;

        if (!agRunning && !ideRunning)
        {
            return 140.0;
        }

        return 175.0;
    }

    private void OnStateUpdated(AgentSessionInfo ag, AgentSessionInfo ide)
    {
        Dispatcher.Invoke(() => UpdateUI(ag, ide));
    }

    private void UpdateUI(AgentSessionInfo ag, AgentSessionInfo ide)
    {
        bool agRunning = ag.IsProcessRunning;
        bool ideRunning = ide.IsProcessRunning;

        // 1. Neither running -> Empty state
        if (!agRunning && !ideRunning)
        {
            GridCardsContainer.Visibility = Visibility.Collapsed;
            PanelEmptyState.Visibility = Visibility.Visible;

            TxtSummaryStatus.Text = "OFFLINE";
            TxtSummaryStatus.Foreground = _brushIdleFg;
            BadgeSummaryStatus.BorderBrush = _brushIdleBorder;

            DesiredHeightChanged?.Invoke(GetDesiredHeight());
            return;
        }

        // At least one app is running
        PanelEmptyState.Visibility = Visibility.Collapsed;
        GridCardsContainer.Visibility = Visibility.Visible;

        // 2. Adjust Grid layout according to active apps
        if (agRunning && ideRunning)
        {
            // Both running -> 2 columns
            ColCard1.Width = new GridLength(1, GridUnitType.Star);
            ColSpacer.Width = new GridLength(10);
            ColCard2.Width = new GridLength(1, GridUnitType.Star);

            Grid.SetColumn(CardAntigravity, 0);
            Grid.SetColumnSpan(CardAntigravity, 1);
            CardAntigravity.Visibility = Visibility.Visible;

            Grid.SetColumn(CardAntigravityIde, 2);
            Grid.SetColumnSpan(CardAntigravityIde, 1);
            CardAntigravityIde.Visibility = Visibility.Visible;

            RenderCard(ag, ImgAgIcon, TxtAgState, BadgeAgState, TxtAgTaskTitle, CardAntigravity);
            RenderCard(ide, ImgIdeIcon, TxtIdeState, BadgeIdeState, TxtIdeTaskTitle, CardAntigravityIde);

            int workingCount = (ag.State == AgentState.Working ? 1 : 0) + (ide.State == AgentState.Working ? 1 : 0);
            TxtSummaryStatus.Text = workingCount > 0 ? $"{workingCount} WORKING" : "2 APPS OPEN";
            TxtSummaryStatus.Foreground = workingCount > 0 ? _brushWorkingFg : _brushDoneFg;
            BadgeSummaryStatus.BorderBrush = workingCount > 0 ? _brushWorkingBorder : _brushDoneBorder;
        }
        else if (agRunning)
        {
            // Only Antigravity running -> full width
            ColCard1.Width = new GridLength(1, GridUnitType.Star);
            ColSpacer.Width = new GridLength(0);
            ColCard2.Width = new GridLength(0);

            Grid.SetColumn(CardAntigravity, 0);
            Grid.SetColumnSpan(CardAntigravity, 3);
            CardAntigravity.Visibility = Visibility.Visible;
            CardAntigravityIde.Visibility = Visibility.Collapsed;

            RenderCard(ag, ImgAgIcon, TxtAgState, BadgeAgState, TxtAgTaskTitle, CardAntigravity);

            TxtSummaryStatus.Text = ag.State == AgentState.Working ? "WORKING" : "ANTIGRAVITY OPEN";
            TxtSummaryStatus.Foreground = ag.State == AgentState.Working ? _brushWorkingFg : _brushIdleFg;
            BadgeSummaryStatus.BorderBrush = ag.State == AgentState.Working ? _brushWorkingBorder : _brushIdleBorder;
        }
        else
        {
            // Only Antigravity IDE running -> full width
            ColCard1.Width = new GridLength(0);
            ColSpacer.Width = new GridLength(0);
            ColCard2.Width = new GridLength(1, GridUnitType.Star);

            Grid.SetColumn(CardAntigravityIde, 0);
            Grid.SetColumnSpan(CardAntigravityIde, 3);
            CardAntigravity.Visibility = Visibility.Collapsed;
            CardAntigravityIde.Visibility = Visibility.Visible;

            RenderCard(ide, ImgIdeIcon, TxtIdeState, BadgeIdeState, TxtIdeTaskTitle, CardAntigravityIde);

            TxtSummaryStatus.Text = ide.State == AgentState.Working ? "WORKING" : "ANTIGRAVITY IDE OPEN";
            TxtSummaryStatus.Foreground = ide.State == AgentState.Working ? _brushWorkingFg : _brushIdleFg;
            BadgeSummaryStatus.BorderBrush = ide.State == AgentState.Working ? _brushWorkingBorder : _brushIdleBorder;
        }

        DesiredHeightChanged?.Invoke(GetDesiredHeight());
    }

    private void RenderCard(
        AgentSessionInfo session,
        Image iconControl,
        TextBlock stateText,
        Border stateBadge,
        TextBlock taskTitleText,
        Border cardBorder)
    {
        iconControl.Source = session.AppIcon;

        // Simplified primary states: WORKING / IDLE / DONE
        if (session.State == AgentState.Working)
        {
            stateText.Text = "WORKING";
            stateText.Foreground = _brushWorkingFg;
            stateBadge.Background = _brushWorkingBg;
            stateBadge.BorderBrush = _brushWorkingBorder;
            stateBadge.BorderThickness = new Thickness(1);
            cardBorder.BorderBrush = _brushWorkingBorder;
        }
        else if (session.State == AgentState.Completed)
        {
            stateText.Text = "DONE";
            stateText.Foreground = _brushDoneFg;
            stateBadge.Background = _brushDoneBg;
            stateBadge.BorderBrush = _brushDoneBorder;
            stateBadge.BorderThickness = new Thickness(1);
            cardBorder.BorderBrush = _brushDoneBorder;
        }
        else if (session.State == AgentState.Attention)
        {
            stateText.Text = "ATTENTION";
            stateText.Foreground = _brushAttentionFg;
            stateBadge.Background = _brushAttentionBg;
            stateBadge.BorderBrush = _brushAttentionBorder;
            stateBadge.BorderThickness = new Thickness(1);
            cardBorder.BorderBrush = _brushAttentionBorder;
        }
        else
        {
            stateText.Text = "IDLE";
            stateText.Foreground = _brushIdleFg;
            stateBadge.Background = _brushIdleBg;
            stateBadge.BorderBrush = _brushIdleBorder;
            stateBadge.BorderThickness = new Thickness(1);
            cardBorder.BorderBrush = _brushIdleBorder;
        }

        // Task Title
        if (!string.IsNullOrWhiteSpace(session.TaskTitle))
        {
            taskTitleText.Text = session.TaskTitle;
            taskTitleText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9"));
        }
        else
        {
            taskTitleText.Text = "Standing by";
            taskTitleText.Foreground = _brushIdleFg;
        }
    }

    private void OnBackClicked(object sender, RoutedEventArgs e)
    {
        BackRequested?.Invoke();
    }

    private void OnAgSeeOutputClicked(object sender, RoutedEventArgs e)
    {
        OrbitLogger.Info(LogCategory, "User clicked See Output for Antigravity");
        if (_stateManager != null)
        {
            bool ok = _stateManager.ActivateApplication(AntigravityAppType.Antigravity);
            if (!ok)
            {
                OrbitLogger.Warn(LogCategory, "Could not activate Antigravity window.");
            }
        }
    }

    private void OnIdeSeeOutputClicked(object sender, RoutedEventArgs e)
    {
        OrbitLogger.Info(LogCategory, "User clicked See Output for Antigravity IDE");
        if (_stateManager != null)
        {
            bool ok = _stateManager.ActivateApplication(AntigravityAppType.AntigravityIde);
            if (!ok)
            {
                OrbitLogger.Warn(LogCategory, "Could not activate Antigravity IDE window.");
            }
        }
    }

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

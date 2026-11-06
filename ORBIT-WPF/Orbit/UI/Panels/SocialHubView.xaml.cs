using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Orbit.Core;
using Orbit.Models;
using Orbit.Services.Hubs;
using Orbit.UI.Components;

namespace Orbit.UI.Panels;

public partial class SocialHubView : UserControl, IDisposable
{
    private const string LogCategory = "SOCIAL_HUB_VIEW";

    public event Action? BackRequested;
    public event Action? ManageAppsRequested;
    public event Action<double>? DesiredHeightChanged;

    private ISocialHubService? _socialHubService;
    private bool _isInitialized;
    private bool _disposed;

    private double _targetScrollOffset;
    private DispatcherTimer? _smoothScrollTimer;

    public SocialHubView()
    {
        InitializeComponent();
    }

    public void Initialize(ISocialHubService socialHubService)
    {
        if (_isInitialized) return;

        _socialHubService = socialHubService;
        _socialHubService.HubChanged += OnHubChanged;
        _isInitialized = true;

        UpdateAppsDisplay();
    }

    public double CalculateDesiredHeight()
    {
        return MainWindow.ExpandedHeight;
    }

    public void UpdateAppsDisplay()
    {
        if (_socialHubService == null) return;

        void Update()
        {
            var selected = _socialHubService.SelectedApps;
            ItemsWorkspaceApps.ItemsSource = selected;

            bool hasApps = selected.Count > 0;
            EmptyWorkspacePrompt.Visibility = hasApps ? Visibility.Collapsed : Visibility.Visible;
            ItemsWorkspaceApps.Visibility = hasApps ? Visibility.Visible : Visibility.Collapsed;

            // Reset scroll position on dataset update
            _targetScrollOffset = 0;
            AppsScrollViewer.ScrollToHorizontalOffset(0);

            UpdateAppsScrollProgress();
            DesiredHeightChanged?.Invoke(CalculateDesiredHeight());
        }

        if (CheckAccess())
        {
            Update();
        }
        else
        {
            Dispatcher.Invoke(Update);
        }
    }

    private void OnHubChanged()
    {
        UpdateAppsDisplay();
    }

    private void OnBackClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        BackRequested?.Invoke();
    }

    private void OnManageAppsClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        ManageAppsRequested?.Invoke();
    }

    private async void OnAppCardClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is DevAppCard { App: not null } card && _socialHubService != null)
        {
            OrbitLogger.Info(LogCategory, $"User clicked social app card: {card.App.Name}");
            await _socialHubService.LaunchAppAsync(card.App);
        }
    }

    private void OnViewBackgroundMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void OnAppsScrollViewerScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        UpdateAppsScrollProgress();
    }

    private void UpdateAppsScrollProgress()
    {
        if (AppsScrollViewer == null) return;

        double scrollable = AppsScrollViewer.ScrollableWidth;
        if (scrollable > 0.5)
        {
            if (ItemsWorkspaceApps.HorizontalAlignment != System.Windows.HorizontalAlignment.Left)
            {
                ItemsWorkspaceApps.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            }

            if (AppsScrollTrack != null && AppsScrollThumb != null && AppsScrollThumbTransform != null)
            {
                AppsScrollTrack.Visibility = Visibility.Visible;
                double extent = AppsScrollViewer.ExtentWidth;
                double viewport = AppsScrollViewer.ViewportWidth;
                double trackWidth = AppsScrollTrack.ActualWidth > 0 ? AppsScrollTrack.ActualWidth : AppsScrollTrack.Width;
                if (trackWidth <= 0) trackWidth = 90.0;

                double thumbWidth = Math.Max(24, (viewport / extent) * trackWidth);
                AppsScrollThumb.Width = thumbWidth;

                double maxThumbOffset = Math.Max(0, trackWidth - thumbWidth);
                double ratio = AppsScrollViewer.HorizontalOffset / scrollable;
                AppsScrollThumbTransform.X = Math.Clamp(ratio * maxThumbOffset, 0, maxThumbOffset);
            }
        }
        else
        {
            if (ItemsWorkspaceApps.HorizontalAlignment != System.Windows.HorizontalAlignment.Center)
            {
                ItemsWorkspaceApps.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            }

            if (AppsScrollTrack != null)
            {
                AppsScrollTrack.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void OnAppsScrollViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (AppsScrollViewer == null || AppsScrollViewer.ScrollableWidth <= 0) return;

        if (_smoothScrollTimer == null || !_smoothScrollTimer.IsEnabled)
        {
            _targetScrollOffset = AppsScrollViewer.HorizontalOffset;
        }

        double delta = -e.Delta * 0.75;
        _targetScrollOffset = Math.Clamp(_targetScrollOffset + delta, 0, AppsScrollViewer.ScrollableWidth);

        StartSmoothScroll();
        e.Handled = true;
    }

    private void StartSmoothScroll()
    {
        if (_smoothScrollTimer == null)
        {
            _smoothScrollTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _smoothScrollTimer.Tick += OnSmoothScrollTick;
        }

        if (!_smoothScrollTimer.IsEnabled)
        {
            _smoothScrollTimer.Start();
        }
    }

    private void OnSmoothScrollTick(object? sender, EventArgs e)
    {
        if (AppsScrollViewer == null)
        {
            _smoothScrollTimer?.Stop();
            return;
        }

        double current = AppsScrollViewer.HorizontalOffset;
        double diff = _targetScrollOffset - current;

        if (Math.Abs(diff) < 0.8)
        {
            AppsScrollViewer.ScrollToHorizontalOffset(_targetScrollOffset);
            _smoothScrollTimer?.Stop();
        }
        else
        {
            double next = current + (diff * 0.35);
            AppsScrollViewer.ScrollToHorizontalOffset(next);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _smoothScrollTimer?.Stop();

        if (_socialHubService != null)
        {
            _socialHubService.HubChanged -= OnHubChanged;
        }
    }
}

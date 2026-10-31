using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Orbit.Core;
using Orbit.Models;
using Orbit.Services.Hubs;
using Orbit.UI.Components;

namespace Orbit.UI.Panels;

public partial class ManageHubAppsView : UserControl, IDisposable
{
    private const string LogCategory = "MANAGE_HUB_APPS_VIEW";
    private const double TargetExpandedHeight = 475.0;

    public event Action? BackRequested;
    public event Action<double>? DesiredHeightChanged;

    private IHubService? _hubService;
    private bool _isInitialized;
    private bool _disposed;
    private string _currentSearchQuery = string.Empty;

    public ManageHubAppsView()
    {
        InitializeComponent();
    }

    public void Initialize(IHubService hubService, string title, string badgeText)
    {
        if (_isInitialized) return;

        _hubService = hubService;
        TxtTitle.Text = title;
        TxtBadge.Text = badgeText;

        _hubService.HubChanged += OnHubChanged;
        _isInitialized = true;

        UpdateAppsList();
    }

    private void OnHubChanged()
    {
        Dispatcher.Invoke(() =>
        {
            UpdateAppsList();
            DesiredHeightChanged?.Invoke(GetDesiredHeight());
        });
    }

    public void UpdateAppsList()
    {
        if (_hubService == null) return;

        void Update()
        {
            var allApps = _hubService.AllDiscoveredApps;

            var filtered = string.IsNullOrWhiteSpace(_currentSearchQuery)
                ? allApps
                : allApps.Where(a => a.Name.Contains(_currentSearchQuery, StringComparison.OrdinalIgnoreCase) ||
                                     a.Id.Contains(_currentSearchQuery, StringComparison.OrdinalIgnoreCase)).ToList();

            ItemsDiscoveredApps.ItemsSource = filtered;

            bool hasResults = filtered.Count > 0;
            AppRowsScrollViewer.Visibility = hasResults ? Visibility.Visible : Visibility.Collapsed;
            EmptySearchResultsBorder.Visibility = hasResults ? Visibility.Collapsed : Visibility.Visible;
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

    private void OnRowToggled(OrbitApp app, bool isSelected)
    {
        _hubService?.ToggleAppSelection(app.Id, isSelected);
    }

    public double GetDesiredHeight() => TargetExpandedHeight;

    private void OnBackClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        BackRequested?.Invoke();
    }

    private void OnToggleSearchClicked(object sender, RoutedEventArgs e)
    {
        if (SearchContainer.Visibility == Visibility.Visible)
        {
            SearchContainer.Visibility = Visibility.Collapsed;
            TxtSearchQuery.Text = string.Empty;
        }
        else
        {
            SearchContainer.Visibility = Visibility.Visible;
            TxtSearchQuery.Focus();
        }
    }

    private void OnSearchQueryChanged(object sender, TextChangedEventArgs e)
    {
        _currentSearchQuery = TxtSearchQuery.Text.Trim();
        TxtSearchPlaceholder.Visibility = string.IsNullOrEmpty(_currentSearchQuery) ? Visibility.Visible : Visibility.Collapsed;
        BtnClearSearch.Visibility = string.IsNullOrEmpty(_currentSearchQuery) ? Visibility.Collapsed : Visibility.Visible;
        UpdateAppsList();
    }

    private void OnClearSearchClicked(object sender, RoutedEventArgs e)
    {
        TxtSearchQuery.Text = string.Empty;
    }

    private void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        OrbitLogger.Info(LogCategory, "User requested application discovery refresh.");
        if (_hubService != null)
        {
            _ = _hubService.RefreshDiscoveryAsync();
        }
    }

    private void OnViewBackgroundMouseDown(object sender, MouseButtonEventArgs e)
    {
        Keyboard.ClearFocus();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hubService != null)
        {
            _hubService.HubChanged -= OnHubChanged;
        }
    }
}

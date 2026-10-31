using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Orbit.Core;
using Orbit.Models;
using Orbit.Services.DevWorkspace;

namespace Orbit.UI.Panels;

public partial class ManageAppsView : UserControl, IDisposable
{
    private const string LogCategory = "MANAGE_APPS_VIEW";

    public event Action? BackRequested;
    public event Action<double>? DesiredHeightChanged;

    private const double ManageAppsTargetHeight = 475.0;

    private IDevWorkspaceService? _workspaceService;
    private bool _isInitialized;
    private bool _disposed;
    private string _currentFilter = string.Empty;

    public ManageAppsView()
    {
        InitializeComponent();
    }

    public void Initialize(IDevWorkspaceService workspaceService)
    {
        if (_isInitialized) return;

        _workspaceService = workspaceService;
        _workspaceService.WorkspaceChanged += OnWorkspaceChanged;
        _isInitialized = true;

        UpdateAppsList();
    }

    public void UpdateAppsList()
    {
        if (_workspaceService == null) return;

        void Update()
        {
            var all = _workspaceService.AllDiscoveredApps;
            IEnumerable<DevApp> filtered = all;

            if (!string.IsNullOrWhiteSpace(_currentFilter))
            {
                filtered = all.Where(a => a.Name.Contains(_currentFilter, StringComparison.OrdinalIgnoreCase) ||
                                          a.ExecutablePath.Contains(_currentFilter, StringComparison.OrdinalIgnoreCase));
            }

            var list = filtered.ToList();
            ItemsDiscoveredApps.ItemsSource = list;
            TxtNoResults.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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

    private void OnWorkspaceChanged()
    {
        UpdateAppsList();
    }

    public double GetDesiredHeight() => ManageAppsTargetHeight;

    private void OnToggleSearchClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (SearchPanel.Visibility == Visibility.Visible)
        {
            // Collapse search panel and reset filter
            SearchPanel.Visibility = Visibility.Collapsed;
            TxtSearch.Text = string.Empty;
        }
        else
        {
            // Expand search panel and focus text input
            SearchPanel.Visibility = Visibility.Visible;
            TxtSearch.Focus();
        }
        DesiredHeightChanged?.Invoke(GetDesiredHeight());
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _currentFilter = TxtSearch.Text.Trim();
        TxtPlaceholder.Visibility = string.IsNullOrEmpty(_currentFilter) ? Visibility.Visible : Visibility.Collapsed;
        BtnClearSearch.Visibility = string.IsNullOrEmpty(_currentFilter) ? Visibility.Collapsed : Visibility.Visible;
        UpdateAppsList();
    }

    private void OnClearSearchClicked(object sender, RoutedEventArgs e)
    {
        TxtSearch.Text = string.Empty;
        TxtSearch.Focus();
    }

    private void OnRowToggled(DevApp app, bool isSelected)
    {
        if (_workspaceService == null) return;
        OrbitLogger.Info(LogCategory, $"Row toggle for app '{app.Name}': {isSelected}");
        _workspaceService.ToggleAppSelection(app.Id, isSelected);
    }

    private void OnBackClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        BackRequested?.Invoke();
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_workspaceService == null) return;

        OrbitLogger.Info(LogCategory, "Manual refresh requested for application discovery.");
        BtnRefresh.IsEnabled = false;
        try
        {
            await _workspaceService.RefreshDiscoveryAsync();
        }
        finally
        {
            BtnRefresh.IsEnabled = true;
        }
    }

    private void OnViewBackgroundMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_workspaceService != null)
        {
            _workspaceService.WorkspaceChanged -= OnWorkspaceChanged;
        }
    }
}


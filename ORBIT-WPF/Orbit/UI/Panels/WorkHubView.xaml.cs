using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Orbit.Core;
using Orbit.Models;
using Orbit.Services.Hubs;
using Orbit.UI.Components;

namespace Orbit.UI.Panels;

public partial class WorkHubView : UserControl, IDisposable
{
    private const string LogCategory = "WORK_HUB_VIEW";

    public event Action? BackRequested;
    public event Action? ManageAppsRequested;
    public event Action<double>? DesiredHeightChanged;

    private IWorkHubService? _workHubService;
    private bool _isInitialized;
    private bool _disposed;

    public WorkHubView()
    {
        InitializeComponent();
    }

    public void Initialize(IWorkHubService workHubService)
    {
        if (_isInitialized) return;

        _workHubService = workHubService;
        _workHubService.HubChanged += OnHubChanged;
        _isInitialized = true;

        UpdateAppsDisplay();
    }

    public double CalculateDesiredHeight()
    {
        int count = _workHubService?.SelectedApps.Count ?? 0;
        if (count == 0) return 200.0;
        if (count <= 4) return 255.0; // 1 row of cards + header + container padding
        if (count <= 8) return 335.0; // 2 rows
        return 385.0;                 // 3+ rows (scrollable cards)
    }

    public void UpdateAppsDisplay()
    {
        if (_workHubService == null) return;

        void Update()
        {
            var selected = _workHubService.SelectedApps;
            ItemsWorkspaceApps.ItemsSource = selected;

            bool hasApps = selected.Count > 0;
            EmptyWorkspacePrompt.Visibility = hasApps ? Visibility.Collapsed : Visibility.Visible;
            ItemsWorkspaceApps.Visibility = hasApps ? Visibility.Visible : Visibility.Collapsed;

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
        if (sender is DevAppCard { App: not null } card && _workHubService != null)
        {
            OrbitLogger.Info(LogCategory, $"User clicked work app card: {card.App.Name}");
            await _workHubService.LaunchAppAsync(card.App);
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

        if (_workHubService != null)
        {
            _workHubService.HubChanged -= OnHubChanged;
        }
    }
}

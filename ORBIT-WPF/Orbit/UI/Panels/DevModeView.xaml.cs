using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Orbit.Core;
using Orbit.Services.DevWorkspace;

namespace Orbit.UI.Panels;

public partial class DevModeView : UserControl, IDisposable
{
    private const string LogCategory = "DEV_MODE_VIEW";

    public event Action? BackRequested;
    public event Action? ManageAppsRequested;
    public event Action<double>? DesiredHeightChanged;

    private IDevWorkspaceService? _workspaceService;
    private bool _isInitialized;
    private bool _disposed;
    private bool _isLaunching;

    public DevModeView()
    {
        InitializeComponent();
    }

    public void Initialize(IDevWorkspaceService workspaceService)
    {
        if (_isInitialized) return;

        _workspaceService = workspaceService;
        _workspaceService.WorkspaceChanged += OnWorkspaceChanged;
        _isInitialized = true;

        UpdateWorkspaceDisplay();
    }

    public double CalculateDesiredHeight()
    {
        int count = _workspaceService?.SelectedApps.Count ?? 0;
        if (count == 0) return 240.0;
        if (count <= 4) return 255.0; // 1 compact row + header + activate
        if (count <= 8) return 335.0; // 2 rows
        return 385.0;                 // 3+ rows (scrollable cards)
    }

    public void UpdateWorkspaceDisplay()
    {
        if (_workspaceService == null) return;

        void Update()
        {
            var selected = _workspaceService.SelectedApps;
            ItemsWorkspaceApps.ItemsSource = selected;

            bool hasApps = selected.Count > 0;
            EmptyWorkspacePrompt.Visibility = hasApps ? Visibility.Collapsed : Visibility.Visible;
            BtnActivate.IsEnabled = hasApps;
            BtnActivate.Opacity = hasApps ? 1.0 : 0.45;

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

    private void OnWorkspaceChanged()
    {
        UpdateWorkspaceDisplay();
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

    private async void OnActivateClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_workspaceService == null || _isLaunching) return;

        _isLaunching = true;
        BtnActivate.IsEnabled = false;

        try
        {
            OrbitLogger.Info(LogCategory, "ACTIVATE button clicked. Launching DEV workspace...");
            var result = await _workspaceService.LaunchWorkspaceAsync();
            OrbitLogger.Info(LogCategory, $"Workspace activation complete. Launched: {result.LaunchedCount}, Brought to focus: {result.ActivatedCount}, Failed: {result.FailedAppNames.Count}");
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, "Failed during workspace activation.", ex);
        }
        finally
        {
            await Task.Delay(400);
            _isLaunching = false;
            BtnActivate.IsEnabled = true;
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


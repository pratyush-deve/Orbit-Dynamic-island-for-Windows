using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Orbit.Core;
using Orbit.Integrations.Desktop;
using Orbit.Models;

namespace Orbit.UI.Panels;

/// <summary>
/// Active applications manager view with in-place refresh-on-open and full diagnostics.
/// </summary>
public partial class ActiveAppsView : UserControl
{
    private const string LogCategory = "ACTIVE_APPS_VIEW";

    public event Action? BackRequested;
    public event Action<double>? DesiredHeightChanged;
    public event Action? DismissAndCollapseRequested;

    private readonly ObservableCollection<WindowInfo> _windows = new();
    private IWindowManager? _windowManager;
    private bool _isHandlingAppAction;
    private bool _isLoading;

    public ActiveAppsView()
    {
        InitializeComponent();
        AppsItemsControl.ItemsSource = _windows;
    }

    public void Initialize(IWindowManager windowManager)
    {
        _windowManager = windowManager;
    }

    /// <summary>
    /// Refresh the eligible desktop windows once upon opening Active Apps view without tearing down existing visual containers.
    /// </summary>
    public async Task LoadWindowsOnOpenAsync()
    {
        if (_windowManager == null || _isLoading) return;
        _isLoading = true;
        _isHandlingAppAction = false;

        // Reset error state
        ErrorStateBorder.Visibility = Visibility.Collapsed;

        // Show loading indicator only if we have 0 windows currently
        if (_windows.Count == 0)
        {
            LoadingStateBorder.Visibility = Visibility.Visible;
            EmptyStateBorder.Visibility = Visibility.Collapsed;
        }

        try
        {
            var openWindows = await _windowManager.GetOpenWindowsAsync();

            LoadingStateBorder.Visibility = Visibility.Collapsed;

            // In-place reconciliation: update existing cards, remove closed, add new
            // Avoids tearing down item containers and avoids blank screen frames in WPF composition
            if (_windows.Count == 0)
            {
                foreach (var win in openWindows)
                {
                    _windows.Add(win);
                }
            }
            else
            {
                var freshByHwnd = openWindows.ToDictionary(w => w.Handle);

                // 1. Remove closed windows
                for (int i = _windows.Count - 1; i >= 0; i--)
                {
                    var existing = _windows[i];
                    if (!freshByHwnd.TryGetValue(existing.Handle, out var fresh) || existing.ProcessId != fresh.ProcessId)
                    {
                        _windows.RemoveAt(i);
                    }
                }

                // 2. Update existing windows in-place
                var existingHandles = new HashSet<IntPtr>();
                foreach (var existing in _windows)
                {
                    existingHandles.Add(existing.Handle);
                    if (freshByHwnd.TryGetValue(existing.Handle, out var fresh))
                    {
                        existing.UpdateFrom(fresh);
                    }
                }

                // 3. Add new windows
                foreach (var win in openWindows)
                {
                    if (!existingHandles.Contains(win.Handle))
                    {
                        _windows.Add(win);
                    }
                }
            }

            int count = _windows.Count;
            TxtAppCount.Text = count == 1 ? "1 Window" : $"{count} Windows";
            EmptyStateBorder.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;

            double targetWindowHeight = CalculateDesiredHeight(count);
            DesiredHeightChanged?.Invoke(targetWindowHeight);

            OrbitLogger.Info(LogCategory, $"Active apps loaded on open: {count} windows found.");
            LogDiagnosticState("LoadWindowsOnOpenAsync Completed");
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, "Failed to load active windows on open.", ex);
            LoadingStateBorder.Visibility = Visibility.Collapsed;
            EmptyStateBorder.Visibility = Visibility.Collapsed;
            ErrorStateBorder.Visibility = Visibility.Visible;
            TxtErrorMessage.Text = ex.Message;
            LogDiagnosticState("LoadWindowsOnOpenAsync Error");
        }
        finally
        {
            _isLoading = false;
        }
    }

    public void RefreshWindows()
    {
        _ = LoadWindowsOnOpenAsync();
    }

    public static double CalculateDesiredHeight(int windowCount)
    {
        if (windowCount <= 0) return 230.0;
        int rows = (windowCount + 2) / 3;
        if (rows == 1) return 230.0;
        if (rows == 2) return 310.0;
        if (rows == 3) return 390.0;
        return 420.0;
    }

    private void OnBackClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        BackRequested?.Invoke();
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await LoadWindowsOnOpenAsync();
    }

    private void OnViewBackgroundMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Diagnostics triggered when clicking the surface (e.g., if blank)
        LogDiagnosticState("Surface Click Diagnostics");
    }

    private async void OnCardActivated(WindowInfo info)
    {
        if (_windowManager == null || _isHandlingAppAction) return;
        _isHandlingAppAction = true;

        try
        {
            OrbitLogger.Info(LogCategory, $"Card clicked: activating '{info.Title}' (HWND 0x{info.Handle:X})");

            // 1. Restore & activate target window
            await _windowManager.ActivateWindowAsync(info.Handle);

            // 2. Navigate ORBIT back to home and initiate smooth collapse
            DismissAndCollapseRequested?.Invoke();
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, $"Error activating window 0x{info.Handle:X}", ex);
            DismissAndCollapseRequested?.Invoke();
        }
    }

    private async void OnCardCloseRequested(WindowInfo info)
    {
        if (_windowManager == null || _isHandlingAppAction) return;
        _isHandlingAppAction = true;

        try
        {
            bool isWinBefore = Orbit.Platform.Windows.NativeMethods.IsWindow(info.Handle);
            OrbitLogger.Info(LogCategory, $"[CLOSE_ACTION] Sending WM_CLOSE to '{info.Title}' (HWND 0x{info.Handle:X}), IsWindowBefore={isWinBefore}");

            // 1. Post graceful WM_CLOSE - targets only this specific HWND
            bool posted = await _windowManager.CloseWindowAsync(info.Handle);
            OrbitLogger.Info(LogCategory, $"[CLOSE_ACTION] PostMessage WM_CLOSE returned: {posted}");

            // Background verification of closure
            _ = Task.Run(async () =>
            {
                await Task.Delay(250);
                bool isWinAfter = Orbit.Platform.Windows.NativeMethods.IsWindow(info.Handle);
                OrbitLogger.Info(LogCategory, $"[CLOSE_ACTION_VERIFY] HWND 0x{info.Handle:X} IsWindowAfter(250ms)={isWinAfter}");
            });

            // 2. Navigate ORBIT back to home and initiate smooth collapse
            DismissAndCollapseRequested?.Invoke();
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, $"Error closing window 0x{info.Handle:X}", ex);
            DismissAndCollapseRequested?.Invoke();
        }
    }

    public void LogDiagnosticState(string triggerReason)
    {
        try
        {
            bool isDispatcher = Dispatcher.CheckAccess();
            int ocCount = _windows.Count;
            int icCount = AppsItemsControl.Items.Count;
            string icSourceType = AppsItemsControl.ItemsSource?.GetType().Name ?? "null";
            var icVis = AppsItemsControl.Visibility;
            var scrollVis = GridScrollViewer.Visibility;
            double scrollH = GridScrollViewer.ActualHeight;
            double scrollW = GridScrollViewer.ActualWidth;
            double scrollOffset = GridScrollViewer.VerticalOffset;
            double scrollableH = GridScrollViewer.ScrollableHeight;
            var viewVis = Visibility;
            bool viewIsVis = IsVisible;
            double viewOp = Opacity;
            double viewW = ActualWidth;
            double viewH = ActualHeight;

            OrbitLogger.Info(LogCategory,
                $"[DIAGNOSTIC] Trigger='{triggerReason}' | DispatcherThread={isDispatcher} | OC.Count={ocCount} | ItemsControl.Count={icCount} | " +
                $"ItemsSource={icSourceType} | View.Vis={viewVis} | View.IsVis={viewIsVis} | View.Opacity={viewOp:0.##} | " +
                $"View.Dimensions=[{viewW:0.#}x{viewH:0.#}] | ScrollViewer=[{scrollW:0.#}x{scrollH:0.#}, Vis={scrollVis}, Offset={scrollOffset:0.#}, ScrollableH={scrollableH:0.#}] | " +
                $"LoadingVis={LoadingStateBorder.Visibility} | EmptyVis={EmptyStateBorder.Visibility} | ErrorVis={ErrorStateBorder.Visibility}");

            for (int i = 0; i < Math.Min(icCount, 6); i++)
            {
                var container = AppsItemsControl.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (container != null)
                {
                    var card = container as Components.ApplicationCard ?? FindVisualChild<Components.ApplicationCard>(container);
                    OrbitLogger.Info(LogCategory,
                        $"[DIAGNOSTIC-CARD #{i}] Container={container.GetType().Name} | Vis={container.Visibility} | Opacity={container.Opacity:0.##} | " +
                        $"Dim=[{container.ActualWidth:0.#}x{container.ActualHeight:0.#}] | IsHitTestVisible={container.IsHitTestVisible} | " +
                        $"Title='{card?.WindowInfo?.Title}' | HWND=0x{card?.WindowInfo?.Handle:X}");
                }
                else
                {
                    OrbitLogger.Info(LogCategory, $"[DIAGNOSTIC-CARD #{i}] Container is NULL (not generated yet)");
                }
            }
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, "Failed to capture diagnostics", ex);
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) return typed;
            var nested = FindVisualChild<T>(child);
            if (nested != null) return nested;
        }
        return null;
    }
}

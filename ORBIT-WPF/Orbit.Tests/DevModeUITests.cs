using System.Windows;
using Orbit.Integrations.Desktop;
using Orbit.Models;
using Orbit.Services.DevWorkspace;
using Orbit.Services.SystemControls;
using Orbit.UI.Components;
using Orbit.UI.Panels;
using Xunit;

namespace Orbit.Tests;

[Collection("UITests")]
public class DevModeUITests
{
    private static readonly object _appLock = new();

    private void RunOnSta(Action action)
    {
        Exception? error = null;
        lock (_appLock)
        {
            var thread = new Thread(() =>
            {
                try
                {
                    if (Application.Current == null)
                    {
                        try { _ = new Application(); } catch { }
                    }
                    action();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
        }

        if (error != null)
        {
            throw new Exception("STA test execution failed: " + error.ToString(), error);
        }
    }

    [Fact]
    public void DevAppCard_UpdatesVisualsCorrectly()
    {
        RunOnSta(() =>
        {
            var card = new DevAppCard
            {
                App = new DevApp
                {
                    Id = "test-app",
                    Name = "Android Studio",
                    ExecutablePath = @"C:\dummy\studio64.exe"
                }
            };

            Assert.Equal("Android Studio", card.TxtAppName.Text);
            Assert.Contains("Android Studio", card.ToolTip?.ToString() ?? string.Empty);
        });
    }

    [Fact]
    public void DevAppRow_TogglesStateCorrectly()
    {
        RunOnSta(() =>
        {
            var app = new DevApp
            {
                Id = "test-row-app",
                Name = "VS Code",
                IsSelected = false
            };

            var row = new DevAppRow
            {
                App = app
            };

            bool toggledEventFired = false;
            row.Toggled += (devApp, isSelected) =>
            {
                toggledEventFired = true;
                Assert.True(isSelected);
            };

            // Trigger toggle
            row.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice,
                0,
                System.Windows.Input.MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonUpEvent
            });

            Assert.True(app.IsSelected);
            Assert.True(toggledEventFired);
        });
    }

    [Fact]
    public void ExpandedIsland_NavigatesToDevMode_AndManageApps()
    {
        RunOnSta(() =>
        {
            using var windowManager = new WindowManager();
            using var controlsService = new SystemControlsService();
            var iconExtractor = new IconExtractorService();
            var discovery = new AppDiscoveryService(iconExtractor);
            using var workspaceService = new DevWorkspaceService(discovery);

            var island = new ExpandedIsland();
            island.Initialize(windowManager, controlsService, workspaceService);

            Assert.False(island.IsInDevModeView);
            Assert.False(island.IsInManageAppsView);

            // 1. Click DEV Card on dashboard
            island.CardDevMode.RaiseEvent(new RoutedEventArgs(NavigationCard.ClickEvent));
            Assert.True(island.IsInDevModeView);
            Assert.False(island.IsInManageAppsView);

            // 2. Click Settings in DevModePanel -> Opens ManageAppsView
            island.DevModePanel.BtnManageApps.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.False(island.IsInDevModeView);
            Assert.True(island.IsInManageAppsView);

            // 3. Click Back in ManageAppsView -> Returns to DevModeView
            island.ManageAppsPanel.BtnBack.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.True(island.IsInDevModeView);
            Assert.False(island.IsInManageAppsView);

            // 4. Click Back in DevModeView -> Returns to Home
            island.DevModePanel.BtnBack.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.False(island.IsInDevModeView);
            Assert.False(island.IsInManageAppsView);
        });
    }

    [Fact]
    public void ManageAppsView_FiltersInstalledApps()
    {
        RunOnSta(() =>
        {
            var iconExtractor = new IconExtractorService();
            var discovery = new AppDiscoveryService(iconExtractor);
            using var workspaceService = new DevWorkspaceService(discovery);
            workspaceService.InitializeAsync().GetAwaiter().GetResult();

            var view = new ManageAppsView();
            view.Initialize(workspaceService);

            // Filter by "Studio"
            view.TxtSearch.Text = "Studio";
            var filtered = view.ItemsDiscoveredApps.ItemsSource as System.Collections.IEnumerable;
            Assert.NotNull(filtered);

            foreach (DevApp item in filtered)
            {
                Assert.Contains("Studio", item.Name, StringComparison.OrdinalIgnoreCase);
            }
        });
    }
}

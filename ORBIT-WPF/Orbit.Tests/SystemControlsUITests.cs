using System.Windows;
using Orbit.Core;
using Orbit.Integrations.Desktop;
using Orbit.Platform.Windows;
using Orbit.Services.SystemControls;
using Orbit.UI.Components;
using Orbit.UI.Panels;
using Xunit;

namespace Orbit.Tests;

[Collection("UITests")]
public class SystemControlsUITests
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
    public void SystemControlCard_UpdatesStatus_EnabledState()
    {
        RunOnSta(() =>
        {
            var card = new SystemControlCard
            {
                ControlType = SystemControlType.Microphone
            };

            card.UpdateStatus(new SystemControlStatus(SystemControlType.Microphone, true, true, "ON"));
            Assert.Equal(SystemControlType.Microphone, card.ControlType);
        });
    }

    [Fact]
    public void SystemControlCard_UpdatesStatus_DisabledState()
    {
        RunOnSta(() =>
        {
            var card = new SystemControlCard
            {
                ControlType = SystemControlType.SystemVolume
            };

            card.UpdateStatus(new SystemControlStatus(SystemControlType.SystemVolume, true, false, "MUTED"));
            Assert.Equal(SystemControlType.SystemVolume, card.ControlType);
        });
    }

    [Fact]
    public void SystemControlCard_UpdatesStatus_UnavailableState()
    {
        RunOnSta(() =>
        {
            var card = new SystemControlCard
            {
                ControlType = SystemControlType.Bluetooth
            };

            card.UpdateStatus(new SystemControlStatus(SystemControlType.Bluetooth, false, false, "UNAVAILABLE", "No Bluetooth radio"));
            Assert.Equal(SystemControlType.Bluetooth, card.ControlType);
        });
    }

    [Fact]
    public void SystemControlsView_InitializesWithService_AndFiresBackRequested()
    {
        RunOnSta(() =>
        {
            using var service = new SystemControlsService();
            var view = new SystemControlsView();
            view.Initialize(service);

            bool backFired = false;
            view.BackRequested += () => backFired = true;

            view.BtnBack.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.True(backFired);
        });
    }

    [Fact]
    public void SystemControlsView_ContainsFiveEqualCards_AndSettingsCardInitializes()
    {
        RunOnSta(() =>
        {
            using var service = new SystemControlsService();
            var view = new SystemControlsView();
            view.Initialize(service);

            Assert.NotNull(view.CardMicrophone);
            Assert.NotNull(view.CardWiFi);
            Assert.NotNull(view.CardBluetooth);
            Assert.NotNull(view.CardSystemVolume);
            Assert.NotNull(view.CardSettings);

            Assert.Equal(SystemControlType.Microphone, view.CardMicrophone.ControlType);
            Assert.Equal(SystemControlType.WiFi, view.CardWiFi.ControlType);
            Assert.Equal(SystemControlType.Bluetooth, view.CardBluetooth.ControlType);
            Assert.Equal(SystemControlType.SystemVolume, view.CardSystemVolume.ControlType);
            Assert.Equal(SystemControlType.Settings, view.CardSettings.ControlType);
            Assert.Equal(Visibility.Collapsed, view.CardSettings.BadgeStatus.Visibility);
            Assert.Equal("Settings", view.CardSettings.TxtControlName.Text);
            Assert.Equal("Open Windows Settings", view.CardSettings.ToolTip);
        });
    }

    [Fact]
    public void WiFiCard_DisplaysSSID_WhenConnected_AndHidesBadge()
    {
        RunOnSta(() =>
        {
            var card = new SystemControlCard
            {
                ControlType = SystemControlType.WiFi
            };

            card.UpdateStatus(new SystemControlStatus(SystemControlType.WiFi, true, true, "tuktuk jio", "Connected to tuktuk jio"));

            Assert.Equal(Visibility.Collapsed, card.BadgeStatus.Visibility);
            Assert.Equal(Visibility.Visible, card.TxtDeviceInfo.Visibility);
            Assert.Equal("tuktuk jio", card.TxtDeviceInfo.Text);
            Assert.Contains("tuktuk jio", card.ToolTip?.ToString() ?? string.Empty);
        });
    }

    [Fact]
    public void WiFiCard_DisplaysNotConnected_WhenDisconnected()
    {
        RunOnSta(() =>
        {
            var card = new SystemControlCard
            {
                ControlType = SystemControlType.WiFi
            };

            card.UpdateStatus(new SystemControlStatus(SystemControlType.WiFi, true, true, "Not Connected", "Wi-Fi is ON (Not connected)"));

            Assert.Equal(Visibility.Collapsed, card.BadgeStatus.Visibility);
            Assert.Equal(Visibility.Visible, card.TxtDeviceInfo.Visibility);
            Assert.Equal("Not Connected", card.TxtDeviceInfo.Text);
        });
    }

    [Fact]
    public void WiFiCard_DisplaysDisabled_WhenRadioOff()
    {
        RunOnSta(() =>
        {
            var card = new SystemControlCard
            {
                ControlType = SystemControlType.WiFi
            };

            card.UpdateStatus(new SystemControlStatus(SystemControlType.WiFi, true, false, "Disabled", "Wi-Fi is turned off"));

            Assert.Equal(Visibility.Collapsed, card.BadgeStatus.Visibility);
            Assert.Equal(Visibility.Visible, card.TxtDeviceInfo.Visibility);
            Assert.Equal("Disabled", card.TxtDeviceInfo.Text);
        });
    }

    [Fact]
    public void BluetoothCard_DisplaysDeviceName_WhenConnected_AndHidesBadge()
    {
        RunOnSta(() =>
        {
            var card = new SystemControlCard
            {
                ControlType = SystemControlType.Bluetooth
            };

            card.UpdateStatus(new SystemControlStatus(SystemControlType.Bluetooth, true, true, "BT5.1 Mouse", "Connected: BT5.1 Mouse"));

            Assert.Equal(Visibility.Collapsed, card.BadgeStatus.Visibility);
            Assert.Equal(Visibility.Visible, card.TxtDeviceInfo.Visibility);
            Assert.Equal("BT5.1 Mouse", card.TxtDeviceInfo.Text);
        });
    }

    [Fact]
    public void BluetoothCard_DisplaysMultipleCount_WhenMultipleConnected()
    {
        RunOnSta(() =>
        {
            var card = new SystemControlCard
            {
                ControlType = SystemControlType.Bluetooth
            };

            card.UpdateStatus(new SystemControlStatus(SystemControlType.Bluetooth, true, true, "2 Connected", "Connected devices: BT5.1 Mouse, Buds"));

            Assert.Equal(Visibility.Collapsed, card.BadgeStatus.Visibility);
            Assert.Equal(Visibility.Visible, card.TxtDeviceInfo.Visibility);
            Assert.Equal("2 Connected", card.TxtDeviceInfo.Text);
        });
    }

    [Fact]
    public void BluetoothCard_DisplaysNoDevices_WhenNoneConnected()
    {
        RunOnSta(() =>
        {
            var card = new SystemControlCard
            {
                ControlType = SystemControlType.Bluetooth
            };

            card.UpdateStatus(new SystemControlStatus(SystemControlType.Bluetooth, true, true, "No Devices", "Bluetooth is ON (No devices connected)"));

            Assert.Equal(Visibility.Collapsed, card.BadgeStatus.Visibility);
            Assert.Equal(Visibility.Visible, card.TxtDeviceInfo.Visibility);
            Assert.Equal("No Devices", card.TxtDeviceInfo.Text);
        });
    }

    [Fact]
    public void BluetoothCard_DisplaysDisabled_WhenRadioOff()
    {
        RunOnSta(() =>
        {
            var card = new SystemControlCard
            {
                ControlType = SystemControlType.Bluetooth
            };

            card.UpdateStatus(new SystemControlStatus(SystemControlType.Bluetooth, true, false, "Disabled", "Bluetooth is turned off"));

            Assert.Equal(Visibility.Collapsed, card.BadgeStatus.Visibility);
            Assert.Equal(Visibility.Visible, card.TxtDeviceInfo.Visibility);
            Assert.Equal("Disabled", card.TxtDeviceInfo.Text);
        });
    }

    [Fact]
    public void SettingsCard_IsActionButton_BadgeIsCollapsed_NoToggleState()
    {
        RunOnSta(() =>
        {
            var card = new SystemControlCard
            {
                ControlType = SystemControlType.Settings
            };

            card.UpdateStatus(new SystemControlStatus(SystemControlType.Settings, true, false, string.Empty));

            Assert.Equal(Visibility.Collapsed, card.BadgeStatus.Visibility);
            Assert.Equal("Settings", card.TxtControlName.Text);
            Assert.Equal("Open Windows Settings", card.ToolTip);
            Assert.Equal(Visibility.Visible, card.IconSettings.Visibility);
        });
    }

    [Fact]
    public void GlobalHotkeyService_DoesNotRegisterKeyboardHook()
    {
        using var hotkeyService = new GlobalHotkeyService();
        bool registered = hotkeyService.Register(new IntPtr(0x1234), 0, 0, () => { });
        Assert.False(registered);
    }

    [Fact]
    public void ExpandedIsland_NavigatesToControls_AndResetsHome()
    {
        RunOnSta(() =>
        {
            using var windowManager = new WindowManager();
            using var controlsService = new SystemControlsService();

            var island = new ExpandedIsland();
            island.Initialize(windowManager, controlsService);

            Assert.False(island.IsInControlsView);
            Assert.False(island.IsInActiveAppsView);

            // Trigger CTRL card click
            island.CardControls.RaiseEvent(new RoutedEventArgs(NavigationCard.ClickEvent));
            Assert.True(island.IsInControlsView);

            // Resetting home returns IsInControlsView to false
            island.ResetToHomeView();
            Assert.False(island.IsInControlsView);
            Assert.False(island.IsInActiveAppsView);
        });
    }
}

using Orbit.Services.SystemControls;
using Xunit;

namespace Orbit.Tests;

[Collection("AudioTests")]
public class SystemControlsServiceTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public SystemControlsServiceTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
    }
    [Fact]
    public async Task Service_InitializesAndReportsAllFourControls()
    {
        using var service = new SystemControlsService();
        await service.InitializeAsync();

        var mic = service.GetStatus(SystemControlType.Microphone);
        var vol = service.GetStatus(SystemControlType.SystemVolume);
        var wifi = service.GetStatus(SystemControlType.WiFi);
        var bt = service.GetStatus(SystemControlType.Bluetooth);

        Assert.Equal(SystemControlType.Microphone, mic.ControlType);
        Assert.Equal(SystemControlType.SystemVolume, vol.ControlType);
        Assert.Equal(SystemControlType.WiFi, wifi.ControlType);
        Assert.Equal(SystemControlType.Bluetooth, bt.ControlType);

        Assert.False(string.IsNullOrEmpty(mic.StatusLabel));
        Assert.False(string.IsNullOrEmpty(vol.StatusLabel));
        Assert.False(string.IsNullOrEmpty(wifi.StatusLabel));
        Assert.False(string.IsNullOrEmpty(bt.StatusLabel));
    }

    [Fact]
    public async Task Probe_Bluetooth_And_Wifi_Devices()
    {
        // 1. Wi-Fi Probe
        var profile = Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
        string? wifiSsid = profile?.WlanConnectionProfileDetails?.GetConnectedSsid();

        // 2. Bluetooth Classic Probe
        var btSelector = Windows.Devices.Bluetooth.BluetoothDevice.GetDeviceSelectorFromConnectionStatus(
            Windows.Devices.Bluetooth.BluetoothConnectionStatus.Connected);
        var btDevices = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(btSelector);

        // 3. Bluetooth LE Probe
        var bleSelector = Windows.Devices.Bluetooth.BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(
            Windows.Devices.Bluetooth.BluetoothConnectionStatus.Connected);
        var bleDevices = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(bleSelector);

        // 4. AEP Probe with IsConnected property
        string[] requestedProperties = { "System.Devices.Aep.IsConnected", "System.ItemNameDisplay" };
        var aepSelector = "System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cdb3-4642-bbd1-4462b019b5cd}\" OR System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-a4b5-231574fb6932}\"";
        var aepDevices = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(aepSelector, requestedProperties);

        int connectedAepCount = 0;
        foreach (var dev in aepDevices)
        {
            if (dev.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var isConn) && isConn is bool b && b)
            {
                connectedAepCount++;
            }
        }

        _output.WriteLine($"WiFi SSID: {wifiSsid}");
        _output.WriteLine($"BT Classic Connected Count: {btDevices.Count}");
        foreach (var d in btDevices) _output.WriteLine($"  BT: {d.Name} ({d.Id})");

        _output.WriteLine($"BLE Connected Count: {bleDevices.Count}");
        foreach (var d in bleDevices) _output.WriteLine($"  BLE: {d.Name} ({d.Id})");

        _output.WriteLine($"AEP Connected Count: {connectedAepCount}");
        foreach (var dev in aepDevices)
        {
            if (dev.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var isConn) && isConn is bool b && b)
            {
                _output.WriteLine($"  AEP: {dev.Name} ({dev.Id})");
            }
        }
    }

    [Fact]
    public async Task Service_TogglesAudioVolumeCorrectly()
    {
        using var service = new SystemControlsService();
        await service.InitializeAsync();

        var initial = service.GetStatus(SystemControlType.SystemVolume);
        if (initial.IsAvailable)
        {
            var eventFired = false;
            service.StatusChanged += status =>
            {
                if (status.ControlType == SystemControlType.SystemVolume)
                {
                    eventFired = true;
                }
            };

            // Toggle once
            bool toggleSuccess = await service.ToggleControlAsync(SystemControlType.SystemVolume);
            Assert.True(toggleSuccess);
            Assert.True(eventFired);

            var updated = service.GetStatus(SystemControlType.SystemVolume);
            Assert.NotEqual(initial.IsEnabled, updated.IsEnabled);

            // Revert back
            await service.ToggleControlAsync(SystemControlType.SystemVolume);
            var reverted = service.GetStatus(SystemControlType.SystemVolume);
            Assert.Equal(initial.IsEnabled, reverted.IsEnabled);
        }
    }

    [Fact]
    public async Task Service_TogglesMicrophoneCorrectly()
    {
        using var service = new SystemControlsService();
        await service.InitializeAsync();

        var initial = service.GetStatus(SystemControlType.Microphone);
        if (initial.IsAvailable)
        {
            var eventFired = false;
            service.StatusChanged += status =>
            {
                if (status.ControlType == SystemControlType.Microphone)
                {
                    eventFired = true;
                }
            };

            bool toggleSuccess = await service.ToggleControlAsync(SystemControlType.Microphone);
            Assert.True(toggleSuccess);
            Assert.True(eventFired);

            var updated = service.GetStatus(SystemControlType.Microphone);
            Assert.NotEqual(initial.IsEnabled, updated.IsEnabled);

            // Revert back
            await service.ToggleControlAsync(SystemControlType.Microphone);
            var reverted = service.GetStatus(SystemControlType.Microphone);
            Assert.Equal(initial.IsEnabled, reverted.IsEnabled);
        }
    }
}


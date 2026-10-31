using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Networking.Connectivity;
using Orbit.Core;
using Orbit.Platform.Windows.Audio;

namespace Orbit.Services.SystemControls;

public sealed class SystemControlsService : ISystemControlsService
{
    private const string LogCategory = "SYSTEM_CONTROLS";

    private readonly object _syncLock = new();
    private AudioEndpointController? _renderAudio;
    private AudioEndpointController? _captureAudio;

    private Radio? _wifiRadio;
    private Radio? _bluetoothRadio;
    private bool _initialized;
    private bool _disposed;

    // Real-time device state caching
    private string? _cachedWifiSsid;
    private readonly object _btLock = new();
    private List<string> _connectedBtDevices = new();
    private DeviceWatcher? _btWatcher;
    private CancellationTokenSource? _btDebounceCts;

    public event Action<SystemControlStatus>? StatusChanged;

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        try
        {
            OrbitLogger.Info(LogCategory, "Initializing System Controls Service...");

            // 1. Audio endpoints (System Volume & Microphone)
            InitializeAudioEndpoints();

            // 2. WinRT Radios (Bluetooth & Wi-Fi)
            await InitializeRadiosAsync();

            // 3. Network change listeners for Wi-Fi SSID and connectivity
            NetworkChange.NetworkAddressChanged += OnNetworkChanged;
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
            try
            {
                Windows.Networking.Connectivity.NetworkInformation.NetworkStatusChanged += OnWinRtNetworkStatusChanged;
            }
            catch (Exception ex)
            {
                OrbitLogger.Warn(LogCategory, $"NetworkStatusChanged registration warning: {ex.Message}");
            }

            // 4. Initial Wi-Fi SSID detection
            RefreshWifiSsid();

            // 5. Bluetooth device watcher and initial scan
            InitializeBluetoothWatcher();
            await RefreshBluetoothConnectedDevicesAsync();

            _initialized = true;
            OrbitLogger.Info(LogCategory, "System Controls Service initialized successfully.");
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, "Failed to fully initialize System Controls Service.", ex);
        }
    }

    private void InitializeAudioEndpoints()
    {
        lock (_syncLock)
        {
            try
            {
                _renderAudio = new AudioEndpointController(EDataFlow.eRender, ERole.eMultimedia);
                _renderAudio.MuteChanged += isMuted =>
                {
                    OrbitLogger.Info(LogCategory, $"External System Volume mute changed: isMuted={isMuted}");
                    NotifyStatus(SystemControlType.SystemVolume);
                };
            }
            catch (Exception ex)
            {
                OrbitLogger.Warn(LogCategory, $"Render audio endpoint failed: {ex.Message}");
            }

            try
            {
                _captureAudio = new AudioEndpointController(EDataFlow.eCapture, ERole.eCommunications);
                _captureAudio.MuteChanged += isMuted =>
                {
                    OrbitLogger.Info(LogCategory, $"External Microphone mute changed: isMuted={isMuted}");
                    NotifyStatus(SystemControlType.Microphone);
                };
            }
            catch (Exception ex)
            {
                OrbitLogger.Warn(LogCategory, $"Capture audio endpoint failed: {ex.Message}");
            }
        }
    }

    private async Task InitializeRadiosAsync()
    {
        try
        {
            var access = await Radio.RequestAccessAsync();
            OrbitLogger.Info(LogCategory, $"Radio request access status: {access}");

            var radios = await Radio.GetRadiosAsync();
            foreach (var radio in radios)
            {
                if (radio.Kind == RadioKind.WiFi)
                {
                    _wifiRadio = radio;
                    _wifiRadio.StateChanged += (sender, args) =>
                    {
                        OrbitLogger.Info(LogCategory, $"External Wi-Fi radio state changed to {_wifiRadio.State}");
                        if (_wifiRadio.State == RadioState.On)
                        {
                            _ = Task.Run(async () =>
                            {
                                await Task.Delay(2000);
                                RefreshWifiSsid();
                                NotifyStatus(SystemControlType.WiFi);
                            });
                        }
                        else
                        {
                            _cachedWifiSsid = null;
                            NotifyStatus(SystemControlType.WiFi);
                        }
                    };
                    OrbitLogger.Info(LogCategory, $"Found Wi-Fi radio: {radio.Name}, State: {radio.State}");
                }
                else if (radio.Kind == RadioKind.Bluetooth)
                {
                    _bluetoothRadio = radio;
                    _bluetoothRadio.StateChanged += (sender, args) =>
                    {
                        OrbitLogger.Info(LogCategory, $"External Bluetooth radio state changed to {_bluetoothRadio.State}");
                        if (_bluetoothRadio.State == RadioState.On)
                        {
                            ScheduleBluetoothUpdate();
                        }
                        else
                        {
                            lock (_btLock)
                            {
                                _connectedBtDevices.Clear();
                            }
                            NotifyStatus(SystemControlType.Bluetooth);
                        }
                    };
                    OrbitLogger.Info(LogCategory, $"Found Bluetooth radio: {radio.Name}, State: {radio.State}");
                }
            }
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, "Failed to query system radios.", ex);
        }
    }

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        RefreshWifiSsid();
        NotifyStatus(SystemControlType.WiFi);
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        RefreshWifiSsid();
        NotifyStatus(SystemControlType.WiFi);
    }

    private void OnWinRtNetworkStatusChanged(object? sender)
    {
        RefreshWifiSsid();
        NotifyStatus(SystemControlType.WiFi);
    }

    private void InitializeBluetoothWatcher()
    {
        try
        {
            string[] requestedProperties = { "System.Devices.Aep.IsConnected" };
            var aepSelector = "System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cdb3-4642-bbd1-4462b019b5cd}\" OR System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-a4b5-231574fb6932}\"";
            _btWatcher = DeviceInformation.CreateWatcher(
                aepSelector,
                requestedProperties,
                DeviceInformationKind.AssociationEndpoint);

            _btWatcher.Added += (s, e) => ScheduleBluetoothUpdate();
            _btWatcher.Updated += (s, e) => ScheduleBluetoothUpdate();
            _btWatcher.Removed += (s, e) => ScheduleBluetoothUpdate();
            _btWatcher.EnumerationCompleted += (s, e) => ScheduleBluetoothUpdate();
            _btWatcher.Start();
            OrbitLogger.Info(LogCategory, "Bluetooth DeviceWatcher started successfully.");
        }
        catch (Exception ex)
        {
            OrbitLogger.Warn(LogCategory, $"Failed to start Bluetooth DeviceWatcher: {ex.Message}");
        }
    }

    private void ScheduleBluetoothUpdate()
    {
        lock (_syncLock)
        {
            _btDebounceCts?.Cancel();
            _btDebounceCts = new CancellationTokenSource();
            var token = _btDebounceCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(350, token);
                    if (!token.IsCancellationRequested)
                    {
                        await RefreshBluetoothConnectedDevicesAsync();
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    OrbitLogger.Warn(LogCategory, $"Debounced Bluetooth update warning: {ex.Message}");
                }
            }, token);
        }
    }

    private async Task RefreshBluetoothConnectedDevicesAsync()
    {
        if (_bluetoothRadio != null && _bluetoothRadio.State != RadioState.On)
        {
            lock (_btLock)
            {
                _connectedBtDevices.Clear();
            }
            NotifyStatus(SystemControlType.Bluetooth);
            return;
        }

        var names = new List<string>();

        // 1. Classic Bluetooth connected devices
        try
        {
            var selector = BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected);
            var devices = await DeviceInformation.FindAllAsync(selector);
            foreach (var d in devices)
            {
                if (!string.IsNullOrWhiteSpace(d.Name) && !names.Contains(d.Name))
                {
                    names.Add(d.Name);
                }
            }
        }
        catch (Exception ex)
        {
            OrbitLogger.Warn(LogCategory, $"Bluetooth classic probe warning: {ex.Message}");
        }

        // 2. Bluetooth LE connected devices
        try
        {
            var leSelector = BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected);
            var leDevices = await DeviceInformation.FindAllAsync(leSelector);
            foreach (var d in leDevices)
            {
                if (!string.IsNullOrWhiteSpace(d.Name) && !names.Contains(d.Name))
                {
                    names.Add(d.Name);
                }
            }
        }
        catch (Exception ex)
        {
            OrbitLogger.Warn(LogCategory, $"Bluetooth LE probe warning: {ex.Message}");
        }

        // 3. AEP query for connected peripheral/audio endpoints
        try
        {
            string[] requestedProperties = { "System.Devices.Aep.IsConnected", "System.ItemNameDisplay" };
            var aepSelector = "System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cdb3-4642-bbd1-4462b019b5cd}\" OR System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-a4b5-231574fb6932}\"";
            var aepDevices = await DeviceInformation.FindAllAsync(aepSelector, requestedProperties);
            foreach (var dev in aepDevices)
            {
                if (dev.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var isConn) && isConn is bool b && b)
                {
                    if (!string.IsNullOrWhiteSpace(dev.Name) && !names.Contains(dev.Name))
                    {
                        names.Add(dev.Name);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            OrbitLogger.Warn(LogCategory, $"Bluetooth AEP probe warning: {ex.Message}");
        }

        lock (_btLock)
        {
            _connectedBtDevices = names;
        }

        NotifyStatus(SystemControlType.Bluetooth);
    }

    private void RefreshWifiSsid()
    {
        try
        {
            // 1. Internet connection profile
            var internetProfile = Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
            if (internetProfile != null && internetProfile.IsWlanConnectionProfile)
            {
                var ssid = internetProfile.WlanConnectionProfileDetails?.GetConnectedSsid();
                if (!string.IsNullOrWhiteSpace(ssid))
                {
                    _cachedWifiSsid = ssid;
                    return;
                }
            }

            // 2. Scan all connection profiles
            var profiles = Windows.Networking.Connectivity.NetworkInformation.GetConnectionProfiles();
            foreach (var profile in profiles)
            {
                if (profile.IsWlanConnectionProfile)
                {
                    var level = profile.GetNetworkConnectivityLevel();
                    if (level != Windows.Networking.Connectivity.NetworkConnectivityLevel.None)
                    {
                        var ssid = profile.WlanConnectionProfileDetails?.GetConnectedSsid();
                        if (!string.IsNullOrWhiteSpace(ssid))
                        {
                            _cachedWifiSsid = ssid;
                            return;
                        }
                        if (!string.IsNullOrWhiteSpace(profile.ProfileName))
                        {
                            _cachedWifiSsid = profile.ProfileName;
                            return;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            OrbitLogger.Warn(LogCategory, $"WinRT WiFi SSID query warning: {ex.Message}");
        }

        // 3. Fallback: query netsh
        try
        {
            var netshSsid = QueryNetshWifiSsid();
            if (!string.IsNullOrWhiteSpace(netshSsid))
            {
                _cachedWifiSsid = netshSsid;
                return;
            }
        }
        catch { }

        _cachedWifiSsid = null;
    }

    private static string? QueryNetshWifiSsid()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = "wlan show interfaces",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return null;
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(1000);

            bool isConnected = false;
            string? ssid = null;

            using var reader = new StringReader(output);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("State", StringComparison.OrdinalIgnoreCase) &&
                    trimmed.Contains("connected", StringComparison.OrdinalIgnoreCase))
                {
                    isConnected = true;
                }
                else if (trimmed.StartsWith("SSID", StringComparison.OrdinalIgnoreCase) &&
                         !trimmed.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = trimmed.Split(':', 2);
                    if (parts.Length == 2)
                    {
                        ssid = parts[1].Trim();
                    }
                }
            }

            return isConnected ? ssid : null;
        }
        catch
        {
            return null;
        }
    }

    public SystemControlStatus GetStatus(SystemControlType type)
    {
        return type switch
        {
            SystemControlType.Microphone => GetMicrophoneStatus(),
            SystemControlType.WiFi => GetWiFiStatus(),
            SystemControlType.Bluetooth => GetBluetoothStatus(),
            SystemControlType.SystemVolume => GetSystemVolumeStatus(),
            SystemControlType.Settings => new SystemControlStatus(SystemControlType.Settings, true, false, string.Empty, "Windows Settings"),
            _ => new SystemControlStatus(type, false, false, "UNKNOWN")
        };
    }

    private SystemControlStatus GetMicrophoneStatus()
    {
        if (_captureAudio == null || !_captureAudio.IsAvailable)
        {
            return new SystemControlStatus(SystemControlType.Microphone, false, false, "UNAVAILABLE", "No microphone detected");
        }

        bool isMuted = _captureAudio.GetMute();
        // Enabled means microphone is ACTIVE (unmuted). Disabled means MUTED.
        bool isEnabled = !isMuted;
        string label = isEnabled ? "ON" : "MUTED";

        return new SystemControlStatus(SystemControlType.Microphone, true, isEnabled, label);
    }

    private SystemControlStatus GetSystemVolumeStatus()
    {
        if (_renderAudio == null || !_renderAudio.IsAvailable)
        {
            return new SystemControlStatus(SystemControlType.SystemVolume, false, false, "UNAVAILABLE", "No audio output detected");
        }

        bool isMuted = _renderAudio.GetMute();
        // Enabled means system audio is ACTIVE (unmuted). Disabled means MUTED.
        bool isEnabled = !isMuted;
        string label = isEnabled ? "ON" : "MUTED";

        return new SystemControlStatus(SystemControlType.SystemVolume, true, isEnabled, label);
    }

    private SystemControlStatus GetWiFiStatus()
    {
        // 1. Check Wi-Fi radio if detected
        if (_wifiRadio != null)
        {
            var state = _wifiRadio.State;
            if (state == RadioState.Off || state == RadioState.Disabled)
            {
                return new SystemControlStatus(
                    SystemControlType.WiFi,
                    true,
                    false,
                    "Disabled",
                    "Wi-Fi is turned off");
            }
        }

        // 2. Secondary check for Wi-Fi adapter presence
        try
        {
            var wifiAdapter = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);

            if (wifiAdapter == null && _wifiRadio == null)
            {
                return new SystemControlStatus(
                    SystemControlType.WiFi,
                    false,
                    false,
                    "Unavailable",
                    "No Wi-Fi adapter detected");
            }

            if (wifiAdapter != null &&
                (wifiAdapter.OperationalStatus == OperationalStatus.Down ||
                 wifiAdapter.OperationalStatus == OperationalStatus.NotPresent) &&
                _wifiRadio?.State != RadioState.On)
            {
                return new SystemControlStatus(
                    SystemControlType.WiFi,
                    true,
                    false,
                    "Disabled",
                    "Wi-Fi adapter is disabled");
            }
        }
        catch (Exception ex)
        {
            OrbitLogger.Warn(LogCategory, $"Error checking network adapters: {ex.Message}");
        }

        // 3. Wi-Fi is enabled. Ensure SSID is fresh
        RefreshWifiSsid();

        if (!string.IsNullOrWhiteSpace(_cachedWifiSsid))
        {
            return new SystemControlStatus(
                SystemControlType.WiFi,
                true,
                true,
                _cachedWifiSsid,
                $"Connected to {_cachedWifiSsid}");
        }

        return new SystemControlStatus(
            SystemControlType.WiFi,
            true,
            true,
            "Not Connected",
            "Wi-Fi is ON (Not connected)");
    }

    private SystemControlStatus GetBluetoothStatus()
    {
        if (_bluetoothRadio != null)
        {
            var state = _bluetoothRadio.State;
            if (state == RadioState.Off || state == RadioState.Disabled)
            {
                return new SystemControlStatus(
                    SystemControlType.Bluetooth,
                    true,
                    false,
                    "Disabled",
                    "Bluetooth is turned off");
            }

            if (state == RadioState.On)
            {
                List<string> devices;
                lock (_btLock)
                {
                    devices = new List<string>(_connectedBtDevices);
                }

                if (devices.Count == 0)
                {
                    return new SystemControlStatus(
                        SystemControlType.Bluetooth,
                        true,
                        true,
                        "No Devices",
                        "Bluetooth is ON (No devices connected)");
                }
                else if (devices.Count == 1)
                {
                    return new SystemControlStatus(
                        SystemControlType.Bluetooth,
                        true,
                        true,
                        devices[0],
                        $"Connected: {devices[0]}");
                }
                else
                {
                    return new SystemControlStatus(
                        SystemControlType.Bluetooth,
                        true,
                        true,
                        $"{devices.Count} Connected",
                        $"Connected devices: {string.Join(", ", devices)}");
                }
            }
        }

        return new SystemControlStatus(
            SystemControlType.Bluetooth,
            false,
            false,
            "Unavailable",
            "No Bluetooth radio detected");
    }

    public async Task<bool> ToggleControlAsync(SystemControlType type)
    {
        OrbitLogger.Info(LogCategory, $"Toggle requested for {type}.");

        try
        {
            bool success = type switch
            {
                SystemControlType.Microphone => ToggleMicrophone(),
                SystemControlType.SystemVolume => ToggleSystemVolume(),
                SystemControlType.WiFi => await ToggleWiFiAsync(),
                SystemControlType.Bluetooth => await ToggleBluetoothAsync(),
                SystemControlType.Settings => LaunchWindowsSettings(),
                _ => false
            };

            // Notify UI with latest status for toggles (Settings is an action button with no state)
            if (type != SystemControlType.Settings)
            {
                NotifyStatus(type);
            }
            return success;
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, $"Error toggling {type}.", ex);
            NotifyStatus(type);
            return false;
        }
    }

    private bool ToggleMicrophone()
    {
        if (_captureAudio == null || !_captureAudio.IsAvailable)
        {
            // Try to reinitialize in case a mic was plugged in
            _captureAudio?.InitializeEndpoint();
            if (_captureAudio == null || !_captureAudio.IsAvailable) return false;
        }

        return _captureAudio.ToggleMute();
    }

    private bool ToggleSystemVolume()
    {
        if (_renderAudio == null || !_renderAudio.IsAvailable)
        {
            _renderAudio?.InitializeEndpoint();
            if (_renderAudio == null || !_renderAudio.IsAvailable) return false;
        }

        return _renderAudio.ToggleMute();
    }

    private async Task<bool> ToggleWiFiAsync()
    {
        if (_wifiRadio != null)
        {
            var targetState = _wifiRadio.State == RadioState.On ? RadioState.Off : RadioState.On;
            OrbitLogger.Info(LogCategory, $"Setting Wi-Fi radio state to {targetState}...");
            var result = await _wifiRadio.SetStateAsync(targetState);
            OrbitLogger.Info(LogCategory, $"Wi-Fi SetStateAsync result: {result}");

            if (result == RadioAccessStatus.Allowed)
            {
                if (targetState == RadioState.On)
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(2000);
                        RefreshWifiSsid();
                        NotifyStatus(SystemControlType.WiFi);
                    });
                }
                else
                {
                    _cachedWifiSsid = null;
                }
                return true;
            }
            return false;
        }

        // Fallback: try netsh if radio not found
        try
        {
            var wifiAdapter = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);

            if (wifiAdapter != null)
            {
                bool isCurrentlyUp = wifiAdapter.OperationalStatus != OperationalStatus.Down;
                string newAdminState = isCurrentlyUp ? "DISABLED" : "ENABLED";

                var startInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"interface set interface name=\"{wifiAdapter.Name}\" admin={newAdminState}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    if (process.ExitCode == 0)
                    {
                        if (newAdminState == "ENABLED")
                        {
                            _ = Task.Run(async () =>
                            {
                                await Task.Delay(2000);
                                RefreshWifiSsid();
                                NotifyStatus(SystemControlType.WiFi);
                            });
                        }
                        else
                        {
                            _cachedWifiSsid = null;
                        }
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            OrbitLogger.Warn(LogCategory, $"Wi-Fi fallback netsh toggle failed: {ex.Message}");
        }

        return false;
    }

    private async Task<bool> ToggleBluetoothAsync()
    {
        if (_bluetoothRadio != null)
        {
            var targetState = _bluetoothRadio.State == RadioState.On ? RadioState.Off : RadioState.On;
            OrbitLogger.Info(LogCategory, $"Setting Bluetooth radio state to {targetState}...");
            var result = await _bluetoothRadio.SetStateAsync(targetState);
            OrbitLogger.Info(LogCategory, $"Bluetooth SetStateAsync result: {result}");

            if (result == RadioAccessStatus.Allowed)
            {
                if (targetState == RadioState.On)
                {
                    ScheduleBluetoothUpdate();
                }
                else
                {
                    lock (_btLock)
                    {
                        _connectedBtDevices.Clear();
                    }
                }
                return true;
            }
            return false;
        }

        OrbitLogger.Warn(LogCategory, "Cannot toggle Bluetooth: No radio available.");
        return false;
    }

    private static bool LaunchWindowsSettings()
    {
        try
        {
            OrbitLogger.Info(LogCategory, "Launching native Windows Settings via ms-settings: ...");
            var startInfo = new ProcessStartInfo
            {
                FileName = "ms-settings:",
                UseShellExecute = true
            };
            Process.Start(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, "Failed to launch Windows Settings.", ex);
            return false;
        }
    }

    public async Task RefreshAllStatesAsync()
    {
        // Re-query radios if needed
        if (_wifiRadio == null || _bluetoothRadio == null)
        {
            await InitializeRadiosAsync();
        }

        RefreshWifiSsid();
        await RefreshBluetoothConnectedDevicesAsync();

        NotifyStatus(SystemControlType.Microphone);
        NotifyStatus(SystemControlType.WiFi);
        NotifyStatus(SystemControlType.Bluetooth);
        NotifyStatus(SystemControlType.SystemVolume);
        NotifyStatus(SystemControlType.Settings);
    }

    private void NotifyStatus(SystemControlType type)
    {
        var status = GetStatus(type);
        try
        {
            StatusChanged?.Invoke(status);
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, $"Error invoking StatusChanged for {type}.", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_btWatcher != null)
        {
            try
            {
                _btWatcher.Stop();
            }
            catch { }
            _btWatcher = null;
        }

        _btDebounceCts?.Cancel();
        _btDebounceCts?.Dispose();
        _btDebounceCts = null;

        try
        {
            Windows.Networking.Connectivity.NetworkInformation.NetworkStatusChanged -= OnWinRtNetworkStatusChanged;
        }
        catch { }

        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;

        _renderAudio?.Dispose();
        _captureAudio?.Dispose();
    }
}


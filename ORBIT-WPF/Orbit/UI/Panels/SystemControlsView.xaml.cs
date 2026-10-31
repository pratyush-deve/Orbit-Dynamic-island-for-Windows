using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Orbit.Core;
using Orbit.Services.SystemControls;

namespace Orbit.UI.Panels;

public partial class SystemControlsView : UserControl, IDisposable
{
    private const string LogCategory = "CONTROLS_VIEW";

    public event Action? BackRequested;

    private ISystemControlsService? _controlsService;
    private bool _isInitialized;
    private bool _disposed;

    public SystemControlsView()
    {
        InitializeComponent();
    }

    public void Initialize(ISystemControlsService controlsService)
    {
        if (_isInitialized) return;

        _controlsService = controlsService;
        _controlsService.StatusChanged += OnServiceStatusChanged;
        _isInitialized = true;

        UpdateAllCards();
    }

    public async Task RefreshStateAsync()
    {
        if (_controlsService == null) return;

        UpdateAllCards();
        await _controlsService.RefreshAllStatesAsync();
    }

    private void UpdateAllCards()
    {
        if (_controlsService == null) return;

        void Update()
        {
            CardMicrophone.UpdateStatus(_controlsService.GetStatus(SystemControlType.Microphone));
            CardWiFi.UpdateStatus(_controlsService.GetStatus(SystemControlType.WiFi));
            CardBluetooth.UpdateStatus(_controlsService.GetStatus(SystemControlType.Bluetooth));
            CardSystemVolume.UpdateStatus(_controlsService.GetStatus(SystemControlType.SystemVolume));
            CardSettings.UpdateStatus(_controlsService.GetStatus(SystemControlType.Settings));
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

    private void OnServiceStatusChanged(SystemControlStatus status)
    {
        void Update()
        {
            switch (status.ControlType)
            {
                case SystemControlType.Microphone:
                    CardMicrophone.UpdateStatus(status);
                    break;
                case SystemControlType.WiFi:
                    CardWiFi.UpdateStatus(status);
                    break;
                case SystemControlType.Bluetooth:
                    CardBluetooth.UpdateStatus(status);
                    break;
                case SystemControlType.SystemVolume:
                    CardSystemVolume.UpdateStatus(status);
                    break;
                case SystemControlType.Settings:
                    CardSettings.UpdateStatus(status);
                    break;
            }
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

    private async void OnCardMicrophoneClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_controlsService == null) return;

        OrbitLogger.Info(LogCategory, "Microphone card clicked. Toggling mute...");
        await _controlsService.ToggleControlAsync(SystemControlType.Microphone);
    }

    private async void OnCardWiFiClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_controlsService == null) return;

        OrbitLogger.Info(LogCategory, "Wi-Fi card clicked. Toggling Wi-Fi...");
        await _controlsService.ToggleControlAsync(SystemControlType.WiFi);
    }

    private async void OnCardBluetoothClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_controlsService == null) return;

        OrbitLogger.Info(LogCategory, "Bluetooth card clicked. Toggling Bluetooth...");
        await _controlsService.ToggleControlAsync(SystemControlType.Bluetooth);
    }

    private async void OnCardSystemVolumeClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_controlsService == null) return;

        OrbitLogger.Info(LogCategory, "Volume card clicked. Toggling audio mute...");
        await _controlsService.ToggleControlAsync(SystemControlType.SystemVolume);
    }

    public void OnCardSettingsClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        try
        {
            OrbitLogger.Info(LogCategory, "Launching native Windows Settings via ms-settings: ...");
            var startInfo = new ProcessStartInfo
            {
                FileName = "ms-settings:",
                UseShellExecute = true
            };
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, "Failed to launch Windows Settings.", ex);
        }
    }

    private void OnBackClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        BackRequested?.Invoke();
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        OrbitLogger.Info(LogCategory, "Manual refresh requested for system controls.");
        await RefreshStateAsync();
    }

    private void OnViewBackgroundMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_controlsService != null)
        {
            _controlsService.StatusChanged -= OnServiceStatusChanged;
        }
    }
}

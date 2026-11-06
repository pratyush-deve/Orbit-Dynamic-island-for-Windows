using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Orbit.Services.SystemMonitor;

namespace Orbit.UI.Panels;

/// <summary>
/// Interaction logic for SystemMonitorView.xaml.
/// Displays live, lightweight hardware metrics: CPU, RAM, Wi-Fi, and GPU.
/// </summary>
public partial class SystemMonitorView : UserControl
{
    public event Action? BackRequested;

    private ISystemMonitorService? _monitorService;

    public SystemMonitorView()
    {
        InitializeComponent();
    }

    public void Initialize(ISystemMonitorService monitorService)
    {
        _monitorService = monitorService;
        _monitorService.MetricsUpdated += OnMetricsUpdated;
    }

    public void StartMonitoring()
    {
        if (_monitorService == null) return;
        _monitorService.StartMonitoring();
        UpdateUi(_monitorService.GetCurrentMetrics());
    }

    public void StopMonitoring()
    {
        _monitorService?.StopMonitoring();
    }

    private void OnMetricsUpdated(SystemMetrics metrics)
    {
        Dispatcher.Invoke(() => UpdateUi(metrics));
    }

    private void UpdateUi(SystemMetrics metrics)
    {
        // 1. CPU
        TxtCpuUsage.Text = $"{metrics.CpuUsagePercent:0}%";

        // 2. RAM
        TxtRamPercent.Text = $"{metrics.RamUsagePercent}%";
        TxtRamDetails.Text = $"{metrics.RamUsedGb:0.0} / {metrics.RamTotalGb:0.0} GB";

        // 3. Wi-Fi
        TxtWifiDownload.Text = FormatSpeed(metrics.WifiDownloadSpeedBps);
        TxtWifiUpload.Text = FormatSpeed(metrics.WifiUploadSpeedBps);

        // 4. GPU
        TxtGpuUsage.Text = $"{metrics.GpuUsagePercent:0}%";
    }

    private static string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec <= 0) return "0 B/s";
        if (bytesPerSec < 1024) return $"{bytesPerSec:0} B/s";
        if (bytesPerSec < 1024 * 1024) return $"{bytesPerSec / 1024.0:0.0} KB/s";
        return $"{bytesPerSec / (1024.0 * 1024.0):0.0} MB/s";
    }

    private void OnBackClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        BackRequested?.Invoke();
    }

    private void OnViewBackgroundMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Intercept background clicks so they do not bubble up to parent cards or collapse requests
        e.Handled = true;
    }

    public double GetDesiredHeight()
    {
        return MainWindow.ExpandedHeight; // 168.0px
    }
}


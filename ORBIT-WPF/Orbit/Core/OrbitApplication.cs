using Microsoft.Extensions.DependencyInjection;
using Orbit.Integrations.Desktop;
using Orbit.Platform.Windows;
using Orbit.Services;
using Orbit.Services.DevWorkspace;
using Orbit.Services.SystemControls;

namespace Orbit.Core;

/// <summary>
/// Root dependency injection container builder and application service coordinator.
/// </summary>
public sealed class OrbitApplication : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private bool _disposed;

    public IServiceProvider Services => _serviceProvider;

    public OrbitApplication(ISingleInstanceService? singleInstanceService = null)
    {
        var services = new ServiceCollection();

        // 1. Core & Logging
        services.AddSingleton<ILoggingService, LoggingService>();

        // 2. Platform & Monitor Services
        services.AddSingleton<IMonitorService, MonitorService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IWindowsOverlayService, WindowsOverlayService>();
        services.AddSingleton<IWindowPositionService, WindowPositionService>();
        services.AddSingleton<ISystemTrayService, SystemTrayService>();
        services.AddSingleton<IGlobalHotkeyService, GlobalHotkeyService>();

        // Reuse existing single-instance service if provided
        if (singleInstanceService != null)
        {
            services.AddSingleton<ISingleInstanceService>(singleInstanceService);
        }
        else
        {
            services.AddSingleton<ISingleInstanceService, SingleInstanceService>();
        }

        // 3. Application Lifecycle
        services.AddSingleton<IWindowLifecycleService, WindowLifecycleService>();

        // 4. Integrations & Controls
        services.AddSingleton<IWindowManager, WindowManager>();
        services.AddSingleton<ISystemControlsService, SystemControlsService>();

        // 5. Developer Workspace
        services.AddSingleton<IIconExtractorService, IconExtractorService>();
        services.AddSingleton<IAppDiscoveryService, AppDiscoveryService>();
        services.AddSingleton<IDevWorkspaceService, DevWorkspaceService>();

        // 6. Windows
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();
    }

    public T GetService<T>() where T : notnull
    {
        return _serviceProvider.GetRequiredService<T>();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _serviceProvider.GetService<ISingleInstanceService>()?.Dispose();
            _serviceProvider.GetService<ISystemTrayService>()?.Dispose();
            _serviceProvider.GetService<IGlobalHotkeyService>()?.Dispose();
            _serviceProvider.GetService<IWindowManager>()?.Dispose();
            _serviceProvider.GetService<ISystemControlsService>()?.Dispose();
            _serviceProvider.GetService<IDevWorkspaceService>()?.Dispose();
            _serviceProvider.Dispose();
        }
        catch (Exception ex)
        {
            OrbitLogger.Warn("ORBIT_APP", $"Error disposing services: {ex.Message}");
        }
    }
}

using System.Windows;
using System.Windows.Threading;
using Orbit.Core;
using Orbit.Platform.Windows;
using Orbit.Services;

namespace Orbit;

/// <summary>
/// Application entry point and orchestrator for ORBIT 2.0.
/// </summary>
public partial class App : Application
{
    private const string LogCategory = "APP";
    private OrbitApplication? _orbitApp;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. Initialize logging
        OrbitLogger.Initialize();
        OrbitLogger.Info(LogCategory, "==================================================");
        OrbitLogger.Info(LogCategory, $"ORBIT 2.0 Starting up on Windows (CLR: {Environment.Version})");
        OrbitLogger.Info(LogCategory, "==================================================");

        // 2. Setup global exception handlers
        SetupExceptionHandlers();

        // 3. Single instance check
        var singleInstanceService = new SingleInstanceService();
        if (!singleInstanceService.IsFirstInstance)
        {
            OrbitLogger.Info(LogCategory, "Secondary launch detected. Signaling running instance and terminating.");
            singleInstanceService.NotifyExistingInstance();
            singleInstanceService.Dispose();
            Shutdown(0);
            return;
        }

        // 4. Initialize Orbit Application Container with the primary single instance service
        _orbitApp = new OrbitApplication(singleInstanceService);

        var lifecycleService = _orbitApp.GetService<IWindowLifecycleService>();
        var trayService = _orbitApp.GetService<ISystemTrayService>();
        var mainWindow = _orbitApp.GetService<MainWindow>();

        // 5. Connect IPC restore requests
        singleInstanceService.RestoreRequested += () =>
        {
            Dispatcher.Invoke(() =>
            {
                OrbitLogger.Info(LogCategory, "IPC restore requested by secondary launch.");
                lifecycleService.Show();
                mainWindow.Show();
                mainWindow.Activate();
                var overlayService = _orbitApp.GetService<IWindowsOverlayService>();
                var hwnd = new System.Windows.Interop.WindowInteropHelper(mainWindow).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    overlayService.BringToFront(hwnd);
                }
            });
        };
        singleInstanceService.StartListening();

        // 6. Initialize System Tray
        trayService.Initialize(
            onShowRequested: () => Dispatcher.Invoke(lifecycleService.Show),
            onHideRequested: () => Dispatcher.Invoke(lifecycleService.Hide),
            onResetPositionRequested: () => Dispatcher.Invoke(lifecycleService.ResetPosition),
            onExitRequested: () => Dispatcher.Invoke(lifecycleService.RequestExit));

        // 7. Wire application lifecycle exit
        lifecycleService.ExitRequested += () =>
        {
            Dispatcher.Invoke(() =>
            {
                OrbitLogger.Info(LogCategory, "Shutdown sequence triggered.");
                Shutdown(0);
            });
        };

        // 8. Launch Companion Window
        MainWindow = mainWindow;
        mainWindow.Show();

        OrbitLogger.Info(LogCategory, "ORBIT companion running smoothly.");
    }

    private void SetupExceptionHandlers()
    {
        DispatcherUnhandledException += (s, e) =>
        {
            OrbitLogger.Error(LogCategory, "Unhandled Dispatcher Exception encountered.", e.Exception);
            // Prevent crash on recoverable UI errors
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                OrbitLogger.Error(LogCategory, "Unhandled AppDomain Exception.", ex);
            }
            else
            {
                OrbitLogger.Error(LogCategory, $"Unhandled non-exception object: {e.ExceptionObject}");
            }
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            OrbitLogger.Error(LogCategory, "Unobserved Task Exception.", e.Exception);
            e.SetObserved();
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        OrbitLogger.Info(LogCategory, $"ORBIT exiting with code {e.ApplicationExitCode}. Cleaning up resources.");
        _orbitApp?.Dispose();
        base.OnExit(e);
    }
}

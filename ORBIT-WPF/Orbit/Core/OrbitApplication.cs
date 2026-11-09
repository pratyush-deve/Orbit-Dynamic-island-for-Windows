using Microsoft.Extensions.DependencyInjection;
using Orbit.Integrations.Desktop;
using Orbit.Platform.Windows;
using Orbit.Services;
using Orbit.Services.AiObserver;
using Orbit.Services.Commands;
using Orbit.Services.Commands.Handlers;
using Orbit.Services.DevWorkspace;
using Orbit.Services.FileDock;
using Orbit.Services.Hubs;
using Orbit.Services.Notes;
using Orbit.Services.SystemControls;
using Orbit.Services.SystemMonitor;
using Orbit.Services.Voice;
using Orbit.Services.Ai;

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

        // 3. Application Lifecycle & Startup Registration
        services.AddSingleton<IWindowLifecycleService, WindowLifecycleService>();
        services.AddSingleton<IStartupRegistrationService, StartupRegistrationService>();

        // 4. Integrations & Controls
        services.AddSingleton<IWindowManager, WindowManager>();
        services.AddSingleton<ISystemControlsService, SystemControlsService>();

        // 5. Developer Workspace & Application Discovery
        services.AddSingleton<IIconExtractorService, IconExtractorService>();
        services.AddSingleton<IAppDiscoveryService, AppDiscoveryService>();
        services.AddSingleton<IDevWorkspaceService, DevWorkspaceService>();
        services.AddSingleton<IFileDockService, FileDockService>();

        // 6. Application Hubs (Social Hub & Work Hub)
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<ISocialHubService, SocialHubService>();
        services.AddSingleton<IWorkHubService, WorkHubService>();

        // 7. AI Agent Hub / Antigravity Observer
        services.AddSingleton<IAntigravityProcessMonitor, AntigravityProcessMonitor>();
        services.AddSingleton<IAntigravityDatabaseObserver, AntigravityDatabaseObserver>();
        services.AddSingleton<IAntigravityTranscriptWatcher, AntigravityTranscriptWatcher>();
        services.AddSingleton<IAntigravitySessionResolver, AntigravitySessionResolver>();
        services.AddSingleton<IAgentStateManager, AgentStateManager>();
        services.AddSingleton<IOrbitAttentionManager, OrbitAttentionManager>();

        // 8. Built-in Notes Service & System Hardware Monitor
        services.AddSingleton<INotesService, NotesService>();
        services.AddSingleton<ISystemMonitorService, SystemMonitorService>();

        // 8.5 Voice Assistant (WASAPI Audio Capture & Offline Whisper Transcription)
        services.AddSingleton<IAudioCaptureService, WasapiAudioCaptureService>();
        services.AddSingleton<IWhisperModelManager, WhisperModelManager>();
        services.AddSingleton<IWhisperTranscriptionService, WhisperTranscriptionService>();
        services.AddSingleton<IVoiceSessionManager, VoiceSessionManager>();

        // 8.6 Voice Assistant Command Parsing & Execution
        services.AddSingleton<AppRegistry>();
        services.AddSingleton<ICommandParser, CommandParser>();
        services.AddSingleton<ICommandHandler, AppLaunchHandler>();
        services.AddSingleton<ICommandHandler, WebSearchHandler>();
        services.AddSingleton<ICommandHandler, WebsiteSearchHandler>();
        services.AddSingleton<ICommandHandler, FileSearchHandler>();
        services.AddSingleton<ICommandHandler, SystemControlHandler>();
        services.AddSingleton<ICommandHandler, SystemMetricsHandler>();
        services.AddSingleton<ICommandHandler, ScreenshotHandler>();
        services.AddSingleton<ICommandHandler, NotesHandler>();
        services.AddSingleton<Orbit.Services.CodeGeneration.ICodeGenerationService, Orbit.Services.CodeGeneration.CodeGenerationService>();
        services.AddSingleton<ICommandHandler, GenerateCodeHandler>();
        services.AddSingleton<IVoiceCommandExecutor, CommandExecutionService>();

        // 8.7 Local AI Assistant (Ollama, Model Lifecycle, AI Assistant Service)
        services.AddSingleton<IOllamaClient, OllamaClient>();
        services.AddSingleton<IModelLifecycleManager, ModelLifecycleManager>();
        services.AddSingleton<IAiAssistantService, AiAssistantService>();

        // 9. Windows
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
            _serviceProvider.GetService<ISocialHubService>()?.Dispose();
            _serviceProvider.GetService<IWorkHubService>()?.Dispose();
            _serviceProvider.GetService<IAgentStateManager>()?.Dispose();
            _serviceProvider.GetService<IAntigravityDatabaseObserver>()?.Dispose();
            _serviceProvider.GetService<IAntigravityProcessMonitor>()?.Dispose();
            _serviceProvider.GetService<IAntigravityTranscriptWatcher>()?.Dispose();
            _serviceProvider.GetService<IVoiceSessionManager>()?.Dispose();
            _serviceProvider.Dispose();
        }
        catch (Exception ex)
        {
            OrbitLogger.Warn("ORBIT_APP", $"Error disposing services: {ex.Message}");
        }
    }
}

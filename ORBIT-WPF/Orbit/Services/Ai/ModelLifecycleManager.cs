using System;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Core;
using Orbit.Services.SystemMonitor;

namespace Orbit.Services.Ai;

public interface IModelLifecycleManager : IDisposable
{
    bool IsModelLoaded { get; }
    bool IsActiveGenerationRunning { get; }
    void NotifyGenerationStarted();
    void NotifyGenerationCompleted();
    Task RequestImmediateUnloadAsync(CancellationToken cancellationToken = default);
}

public sealed class ModelLifecycleManager : IModelLifecycleManager
{
    private const string LogCategory = "MODEL_LIFECYCLE";
    private const double RamUnloadThresholdPercent = 85.0; // 85% total system RAM threshold
    private const int SustainedHighMemoryRequiredSeconds = 5;

    private readonly IOllamaClient _ollamaClient;
    private readonly ISystemMonitorService? _systemMonitorService;
    private readonly object _lock = new();

    private bool _isActiveGenerationRunning;
    private bool _isModelLoaded;
    private int _consecutiveHighMemorySeconds = 0;
    private bool _unloadPendingAfterActiveGeneration;
    private bool _disposed;

    public bool IsModelLoaded
    {
        get
        {
            lock (_lock) return _isModelLoaded;
        }
    }

    public bool IsActiveGenerationRunning
    {
        get
        {
            lock (_lock) return _isActiveGenerationRunning;
        }
    }

    public ModelLifecycleManager(IOllamaClient ollamaClient, ISystemMonitorService? systemMonitorService = null)
    {
        _ollamaClient = ollamaClient ?? throw new ArgumentNullException(nameof(ollamaClient));
        _systemMonitorService = systemMonitorService;

        if (_systemMonitorService != null)
        {
            _systemMonitorService.MetricsUpdated += OnSystemMetricsUpdated;
        }
    }

    public void NotifyGenerationStarted()
    {
        lock (_lock)
        {
            _isActiveGenerationRunning = true;
            _isModelLoaded = true;
            _consecutiveHighMemorySeconds = 0;
            _unloadPendingAfterActiveGeneration = false;
        }
    }

    public void NotifyGenerationCompleted()
    {
        bool shouldUnload;
        lock (_lock)
        {
            _isActiveGenerationRunning = false;
            shouldUnload = _unloadPendingAfterActiveGeneration;
            _unloadPendingAfterActiveGeneration = false;
        }

        if (shouldUnload)
        {
            OrbitLogger.Info(LogCategory, "Generation completed while under high RAM pressure. Unloading model now.");
            _ = Task.Run(async () => await RequestImmediateUnloadAsync().ConfigureAwait(false));
        }
    }

    public async Task RequestImmediateUnloadAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_isActiveGenerationRunning)
            {
                OrbitLogger.Info(LogCategory, "Unload requested but active generation is running. Deferring until request finishes.");
                _unloadPendingAfterActiveGeneration = true;
                return;
            }
        }

        try
        {
            OrbitLogger.Info(LogCategory, "Initiating immediate model memory unload (keep_alive: 0)...");
            bool unloaded = await _ollamaClient.UnloadModelAsync("qwen2.5:3b", cancellationToken).ConfigureAwait(false);
            lock (_lock)
            {
                if (unloaded)
                {
                    _isModelLoaded = false;
                    _consecutiveHighMemorySeconds = 0;
                }
            }
        }
        catch (Exception ex)
        {
            OrbitLogger.Warn(LogCategory, $"Failed to unload model: {ex.Message}");
        }
    }

    private void OnSystemMetricsUpdated(SystemMetrics metrics)
    {
        if (_disposed) return;

        // If RAM usage meets or exceeds 85%
        if (metrics.RamUsagePercent >= RamUnloadThresholdPercent)
        {
            int highSeconds;
            lock (_lock)
            {
                highSeconds = ++_consecutiveHighMemorySeconds;
            }

            if (highSeconds >= SustainedHighMemoryRequiredSeconds)
            {
                lock (_lock)
                {
                    if (_isActiveGenerationRunning)
                    {
                        if (!_unloadPendingAfterActiveGeneration)
                        {
                            OrbitLogger.Warn(LogCategory, $"System RAM sustained at {metrics.RamUsagePercent}% (>=85%). Inference in progress; will unload upon completion.");
                            _unloadPendingAfterActiveGeneration = true;
                        }
                        return;
                    }

                    if (!_isModelLoaded)
                    {
                        return;
                    }
                }

                OrbitLogger.Warn(LogCategory, $"System RAM sustained at {metrics.RamUsagePercent}% for {highSeconds}s. Triggering safety model unload.");
                _ = Task.Run(async () => await RequestImmediateUnloadAsync().ConfigureAwait(false));
            }
        }
        else
        {
            lock (_lock)
            {
                _consecutiveHighMemorySeconds = 0;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_systemMonitorService != null)
        {
            _systemMonitorService.MetricsUpdated -= OnSystemMetricsUpdated;
        }
    }
}


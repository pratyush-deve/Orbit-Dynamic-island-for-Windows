using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Core;

namespace Orbit.Services.Ai;

public enum OllamaServerStatus
{
    Unavailable,
    Starting,
    Running,
    Recovering
}

public interface IOllamaServerManager : IDisposable
{
    OllamaServerStatus Status { get; }
    bool IsOrbitOwned { get; }
    bool IsModelReady { get; }
    string? ModelDiagnosticMessage { get; }

    event Action<OllamaServerStatus, string?>? StatusChanged;

    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<bool> EnsureServerRunningAsync(CancellationToken cancellationToken = default);
    Task<bool> CheckHealthAndRecoverAsync(CancellationToken cancellationToken = default);
    void StopServerIfOwned();
}

/// <summary>
/// Manages the automatic lifecycle, detection, startup, periodic health monitoring,
/// and safe shutdown of the local Ollama AI server.
/// </summary>
public sealed class OllamaServerManager : IOllamaServerManager
{
    private const string LogCategory = "OLLAMA_SERVER";
    private const string DefaultEndpoint = "http://127.0.0.1:11434";
    public const string TargetModel = "qwen2.5:3b";
    private const int HealthCheckIntervalSeconds = 12;
    private const int MaxRecoveryAttempts = 3;

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _startupLock = new(1, 1);
    private readonly object _processLock = new();
    private readonly System.Threading.Timer _healthTimer;

    private Process? _managedProcess;
    private bool _isOrbitOwned;
    private OllamaServerStatus _status = OllamaServerStatus.Unavailable;
    private bool _isModelReady;
    private string? _modelDiagnosticMessage;
    private int _consecutiveFailures;
    private DateTime _lastFailureTime = DateTime.MinValue;
    private readonly TimeSpan _cooldownPeriod = TimeSpan.FromSeconds(45);
    private bool _disposed;

    public OllamaServerStatus Status => _status;
    public bool IsOrbitOwned => _isOrbitOwned;
    public bool IsModelReady => _isModelReady;
    public string? ModelDiagnosticMessage => _modelDiagnosticMessage;

    public event Action<OllamaServerStatus, string?>? StatusChanged;

    public OllamaServerManager(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient
        {
            BaseAddress = new Uri(DefaultEndpoint),
            Timeout = TimeSpan.FromSeconds(30)
        };

        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        // Periodic background health monitor
        _healthTimer = new System.Threading.Timer(
            OnHealthTimerTick,
            null,
            TimeSpan.FromSeconds(HealthCheckIntervalSeconds),
            TimeSpan.FromSeconds(HealthCheckIntervalSeconds));
    }

    /// <summary>
    /// Locates the Ollama executable on the local machine without hardcoding assumptions.
    /// </summary>
    public static string? LocateOllamaExecutable()
    {
        // 1. Standard LocalAppData location
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            string candidate = Path.Combine(localAppData, @"Programs\Ollama\ollama.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        // 2. UserProfile fallback
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            string candidate = Path.Combine(userProfile, @"AppData\Local\Programs\Ollama\ollama.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        // 3. Program Files fallback
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            string candidate = Path.Combine(programFiles, @"Ollama\ollama.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        // 4. System PATH search
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            foreach (var dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    string candidate = Path.Combine(dir, "ollama.exe");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch
                {
                    // Ignore malformed PATH entries
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Initial startup sequence called after ORBIT window initialization and delayed startup.
    /// Non-blocking, checks availability and launches if offline.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        OrbitLogger.Info(LogCategory, "Starting Ollama server detection and lifecycle initialization...");
        await EnsureServerRunningAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Guarantees that an Ollama server is running. If already running, reuses it without starting another.
    /// If unavailable, launches ollama.exe serve and waits asynchronously for health.
    /// </summary>
    public async Task<bool> EnsureServerRunningAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return false;

        // Fast-path: Check if already healthy and model status resolved
        if (_status == OllamaServerStatus.Running && await CheckApiHealthInternalAsync(cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        await _startupLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await AttemptStartupOrRecoveryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _startupLock.Release();
        }
    }

    /// <summary>
    /// Explicit health check and recovery trigger.
    /// </summary>
    public async Task<bool> CheckHealthAndRecoverAsync(CancellationToken cancellationToken = default)
    {
        return await EnsureServerRunningAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> AttemptStartupOrRecoveryAsync(CancellationToken cancellationToken)
    {
        // 1. Check whether Ollama API is already responding
        if (await CheckApiHealthInternalAsync(cancellationToken).ConfigureAwait(false))
        {
            lock (_processLock)
            {
                // If we didn't start it previously, do not claim ownership
                if (_managedProcess == null)
                {
                    _isOrbitOwned = false;
                }
            }

            await RefreshModelStatusAsync(cancellationToken).ConfigureAwait(false);
            SetStatus(OllamaServerStatus.Running);
            OrbitLogger.Info(LogCategory, $"Existing Ollama server responding at {DefaultEndpoint}. Reusing server (Owned: {_isOrbitOwned}).");
            _consecutiveFailures = 0;
            return true;
        }

        // 2. Check whether an Ollama server process is already starting or running
        var runningProcesses = Process.GetProcessesByName("ollama");
        if (runningProcesses.Length > 0)
        {
            bool isOurManaged = false;
            lock (_processLock)
            {
                isOurManaged = _isOrbitOwned && _managedProcess != null && runningProcesses.Any(p => p.Id == _managedProcess.Id);
            }

            if (isOurManaged)
            {
                // Our previously launched process is unresponsive
                OrbitLogger.Warn(LogCategory, "Managed Ollama server process is unresponsive. Terminating for restart...");
                StopServerIfOwned();
            }
            else
            {
                // An external process exists and may be booting up; wait asynchronously up to 6 seconds
                OrbitLogger.Info(LogCategory, "External Ollama process detected. Awaiting API responsiveness...");
                for (int i = 0; i < 12 && !cancellationToken.IsCancellationRequested; i++)
                {
                    await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                    if (await CheckApiHealthInternalAsync(cancellationToken).ConfigureAwait(false))
                    {
                        await RefreshModelStatusAsync(cancellationToken).ConfigureAwait(false);
                        SetStatus(OllamaServerStatus.Running);
                        OrbitLogger.Info(LogCategory, "External Ollama process became healthy. Reusing instance.");
                        _consecutiveFailures = 0;
                        return true;
                    }
                }

                OrbitLogger.Warn(LogCategory, "External Ollama process did not become healthy within wait window.");
                SetStatus(OllamaServerStatus.Unavailable, "External Ollama process not responding.");
                return false;
            }
        }

        // 3. Locate ollama executable
        string? ollamaExe = LocateOllamaExecutable();
        if (string.IsNullOrWhiteSpace(ollamaExe) || !File.Exists(ollamaExe))
        {
            string notFoundMsg = "Ollama executable not found on this machine.";
            OrbitLogger.Error(LogCategory, notFoundMsg);
            SetStatus(OllamaServerStatus.Unavailable, notFoundMsg);
            return false;
        }

        // 4. Launch ollama.exe serve in background
        SetStatus(OllamaServerStatus.Starting, "Starting local Ollama server...");
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ollamaExe,
                Arguments = "serve",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(ollamaExe) ?? string.Empty
            };

            var proc = Process.Start(startInfo);
            if (proc == null)
            {
                OrbitLogger.Error(LogCategory, "Process.Start returned null for ollama serve.");
                SetStatus(OllamaServerStatus.Unavailable, "Failed to start Ollama server process.");
                return false;
            }

            lock (_processLock)
            {
                _managedProcess = proc;
                _isOrbitOwned = true;
            }

            OrbitLogger.Info(LogCategory, $"Successfully started ORBIT-managed Ollama server process (PID {proc.Id}). Waiting for API health...");

            // 5. Wait asynchronously for API to become healthy (up to 25 seconds)
            DateTime deadline = DateTime.UtcNow.AddSeconds(25);
            while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                if (proc.HasExited)
                {
                    int exitCode = proc.ExitCode;
                    OrbitLogger.Error(LogCategory, $"Ollama process exited prematurely with code {exitCode}.");
                    lock (_processLock)
                    {
                        _managedProcess = null;
                        _isOrbitOwned = false;
                    }
                    SetStatus(OllamaServerStatus.Unavailable, $"Ollama exited with code {exitCode}.");
                    return false;
                }

                if (await CheckApiHealthInternalAsync(cancellationToken).ConfigureAwait(false))
                {
                    OrbitLogger.Info(LogCategory, "Ollama server API is healthy and accepting requests.");
                    await RefreshModelStatusAsync(cancellationToken).ConfigureAwait(false);
                    SetStatus(OllamaServerStatus.Running);
                    _consecutiveFailures = 0;
                    return true;
                }

                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }

            OrbitLogger.Warn(LogCategory, "Timed out waiting for Ollama server API to respond after startup.");
            SetStatus(OllamaServerStatus.Unavailable, "Ollama server startup timed out.");
            return false;
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, $"Failed to launch Ollama server: {ex.Message}", ex);
            SetStatus(OllamaServerStatus.Unavailable, ex.Message);
            return false;
        }
    }

    private async Task<bool> CheckApiHealthInternalAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(2.5));

            var response = await _httpClient.GetAsync("/api/tags", cts.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Verifies availability of the target local model (qwen2.5:3b) through the Ollama API.
    /// Strictly inspects existing models; does not trigger automatic downloads.
    /// </summary>
    private async Task RefreshModelStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(3.5));

            var response = await _httpClient.GetAsync("/api/tags", cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _isModelReady = false;
                _modelDiagnosticMessage = $"Failed to query Ollama models (HTTP {(int)response.StatusCode}).";
                return;
            }

            var json = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            bool found = false;

            if (doc.RootElement.TryGetProperty("models", out var modelsElem) && modelsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var model in modelsElem.EnumerateArray())
                {
                    if (model.TryGetProperty("name", out var nameElem))
                    {
                        var name = nameElem.GetString();
                        if (!string.IsNullOrEmpty(name) &&
                            (name.Equals(TargetModel, StringComparison.OrdinalIgnoreCase) ||
                             name.StartsWith($"{TargetModel}:", StringComparison.OrdinalIgnoreCase) ||
                             name.StartsWith($"{TargetModel}-", StringComparison.OrdinalIgnoreCase)))
                        {
                            found = true;
                            break;
                        }
                    }
                }
            }

            _isModelReady = found;
            if (found)
            {
                _modelDiagnosticMessage = $"Model '{TargetModel}' is ready for local inference.";
                OrbitLogger.Info(LogCategory, $"Model readiness confirmed: '{TargetModel}' is available in local Ollama library.");
            }
            else
            {
                _modelDiagnosticMessage = $"Model '{TargetModel}' is not installed in local Ollama library. Run 'ollama pull {TargetModel}' to install.";
                OrbitLogger.Warn(LogCategory, _modelDiagnosticMessage);
            }
        }
        catch (Exception ex)
        {
            _isModelReady = false;
            _modelDiagnosticMessage = $"Error verifying model readiness: {ex.Message}";
            OrbitLogger.Warn(LogCategory, _modelDiagnosticMessage);
        }
    }

    private async void OnHealthTimerTick(object? state)
    {
        if (_disposed) return;

        // Skip tick if startup/recovery lock is already active
        if (!_startupLock.Wait(0))
        {
            return;
        }

        try
        {
            // Cooldown check
            if (_consecutiveFailures >= MaxRecoveryAttempts)
            {
                if (DateTime.UtcNow - _lastFailureTime < _cooldownPeriod)
                {
                    return; // In backoff cooldown
                }
                OrbitLogger.Info(LogCategory, "Cooldown expired. Retrying Ollama health check...");
            }

            bool healthy = await CheckApiHealthInternalAsync(CancellationToken.None).ConfigureAwait(false);
            if (healthy)
            {
                if (_status != OllamaServerStatus.Running)
                {
                    await RefreshModelStatusAsync(CancellationToken.None).ConfigureAwait(false);
                    SetStatus(OllamaServerStatus.Running);
                    OrbitLogger.Info(LogCategory, "Ollama server is running and healthy.");
                }
                _consecutiveFailures = 0;
            }
            else
            {
                // Ollama was running or starting, but is now unresponsive
                if (_status == OllamaServerStatus.Running || _status == OllamaServerStatus.Starting)
                {
                    OrbitLogger.Warn(LogCategory, "Ollama server unexpectedly stopped responding. Initiating recovery...");
                    SetStatus(OllamaServerStatus.Recovering, "Recovering Ollama server connection...");
                }

                _consecutiveFailures++;
                _lastFailureTime = DateTime.UtcNow;

                if (_consecutiveFailures <= MaxRecoveryAttempts)
                {
                    OrbitLogger.Info(LogCategory, $"Attempting Ollama recovery attempt {_consecutiveFailures} of {MaxRecoveryAttempts}...");
                    bool recovered = await AttemptStartupOrRecoveryAsync(CancellationToken.None).ConfigureAwait(false);
                    if (recovered)
                    {
                        _consecutiveFailures = 0;
                        SetStatus(OllamaServerStatus.Running);
                        OrbitLogger.Info(LogCategory, "Ollama server recovered successfully.");
                    }
                    else if (_consecutiveFailures >= MaxRecoveryAttempts)
                    {
                        OrbitLogger.Error(LogCategory, $"Ollama recovery reached maximum attempts ({MaxRecoveryAttempts}). Transitioning to Unavailable with cooldown.");
                        SetStatus(OllamaServerStatus.Unavailable, "Ollama server unavailable after recovery attempts.");
                    }
                }
                else
                {
                    SetStatus(OllamaServerStatus.Unavailable, "Ollama server is offline.");
                }
            }
        }
        catch (Exception ex)
        {
            OrbitLogger.Warn(LogCategory, $"Exception during periodic health check: {ex.Message}");
        }
        finally
        {
            _startupLock.Release();
        }
    }

    private void SetStatus(OllamaServerStatus newStatus, string? diagnostic = null)
    {
        bool changed = _status != newStatus || (!string.IsNullOrEmpty(diagnostic) && _modelDiagnosticMessage != diagnostic);
        _status = newStatus;
        if (!string.IsNullOrEmpty(diagnostic))
        {
            _modelDiagnosticMessage = diagnostic;
        }

        if (changed)
        {
            try
            {
                StatusChanged?.Invoke(_status, _modelDiagnosticMessage);
            }
            catch (Exception ex)
            {
                OrbitLogger.Warn(LogCategory, $"Error notifying status changed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Terminates only the Ollama server process instance started by ORBIT.
    /// Never touches independently started Ollama processes.
    /// </summary>
    public void StopServerIfOwned()
    {
        lock (_processLock)
        {
            if (!_isOrbitOwned || _managedProcess == null)
            {
                return;
            }

            try
            {
                if (!_managedProcess.HasExited)
                {
                    OrbitLogger.Info(LogCategory, $"Terminating ORBIT-managed Ollama server process (PID {_managedProcess.Id})...");
                    _managedProcess.Kill(entireProcessTree: true);
                    _managedProcess.WaitForExit(3000);
                    OrbitLogger.Info(LogCategory, "ORBIT-managed Ollama server process terminated cleanly.");
                }
            }
            catch (Exception ex)
            {
                OrbitLogger.Warn(LogCategory, $"Failed to cleanly terminate managed Ollama process: {ex.Message}");
            }
            finally
            {
                try
                {
                    _managedProcess.Dispose();
                }
                catch
                {
                    // Ignore dispose errors
                }
                _managedProcess = null;
                _isOrbitOwned = false;
            }
        }
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        StopServerIfOwned();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;

        try
        {
            _healthTimer.Dispose();
        }
        catch { }

        StopServerIfOwned();

        try
        {
            _startupLock.Dispose();
            _httpClient.Dispose();
        }
        catch { }
    }
}

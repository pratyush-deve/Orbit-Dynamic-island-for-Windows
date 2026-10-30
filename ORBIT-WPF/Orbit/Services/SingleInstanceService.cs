using System.IO;
using System.IO.Pipes;
using System.Text;
using Orbit.Core;

namespace Orbit.Services;

public interface ISingleInstanceService : IDisposable
{
    bool IsFirstInstance { get; }
    event Action? RestoreRequested;
    bool NotifyExistingInstance();
    void StartListening();
}

/// <summary>
/// Ensures single-instance execution via a Windows named Mutex and provides NamedPipe IPC to signal the primary instance.
/// </summary>
public sealed class SingleInstanceService : ISingleInstanceService
{
    private const string LogCategory = "SINGLE_INSTANCE";
    private const string MutexName = @"Global\Orbit_DesktopCompanion_SingleInstance_v2";
    private const string PipeName = "Orbit_DesktopCompanion_IPC_Pipe_v2";

    private Mutex? _mutex;
    private CancellationTokenSource? _cts;
    private bool _isFirstInstance;
    private bool _disposed;

    public bool IsFirstInstance => _isFirstInstance;
    public event Action? RestoreRequested;

    public SingleInstanceService()
    {
        try
        {
            _mutex = new Mutex(true, MutexName, out _isFirstInstance);
            OrbitLogger.Info(LogCategory, $"Single instance check: IsFirstInstance = {_isFirstInstance}");
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, "Failed to create named mutex.", ex);
            _isFirstInstance = true; // Safe fallback
        }
    }

    public bool NotifyExistingInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000); // 2 second timeout
            using var writer = new StreamWriter(client, Encoding.UTF8);
            writer.WriteLine("RESTORE");
            writer.Flush();
            OrbitLogger.Info(LogCategory, "Notified primary instance via named pipe.");
            return true;
        }
        catch (Exception ex)
        {
            OrbitLogger.Warn(LogCategory, $"Could not signal primary instance via pipe: {ex.Message}");
            return false;
        }
    }

    public void StartListening()
    {
        if (!_isFirstInstance) return;

        _cts = new CancellationTokenSource();
        Task.Run(() => PipeListenerLoop(_cts.Token));
        OrbitLogger.Info(LogCategory, "Started IPC named pipe listener.");
    }

    private async Task PipeListenerLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(token);

                using var reader = new StreamReader(server, Encoding.UTF8);
                var message = await reader.ReadLineAsync(token);
                OrbitLogger.Info(LogCategory, $"Received IPC message from secondary launch: '{message}'");

                if (message == "RESTORE" || message == "SHOW")
                {
                    RestoreRequested?.Invoke();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                OrbitLogger.Warn(LogCategory, $"Pipe communication issue: {ex.Message}");
            }
            finally
            {
                server?.Dispose();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _cts?.Cancel();
            _cts?.Dispose();

            if (_mutex != null)
            {
                if (_isFirstInstance)
                {
                    _mutex.ReleaseMutex();
                }
                _mutex.Dispose();
            }

            OrbitLogger.Info(LogCategory, "Single instance resources released.");
        }
        catch (Exception ex)
        {
            OrbitLogger.Warn(LogCategory, $"Error during single instance cleanup: {ex.Message}");
        }
    }
}

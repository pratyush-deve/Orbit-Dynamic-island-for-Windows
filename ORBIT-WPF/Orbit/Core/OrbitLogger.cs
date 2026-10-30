using System.IO;
using System.Text;

namespace Orbit.Core;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error
}

/// <summary>
/// Lightweight, reliable, thread-safe logger writing to %LOCALAPPDATA%\ORBIT\logs.
/// </summary>
public static class OrbitLogger
{
    private static readonly object _syncLock = new();
    private static string? _logDirectory;
    private static string? _currentLogFilePath;
    private static bool _isInitialized = false;

    public static string LogDirectory
    {
        get
        {
            if (_logDirectory == null)
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                _logDirectory = Path.Combine(localAppData, "ORBIT", "logs");
            }
            return _logDirectory;
        }
    }

    public static void Initialize()
    {
        lock (_syncLock)
        {
            if (_isInitialized) return;

            try
            {
                Directory.CreateDirectory(LogDirectory);
                var fileName = $"orbit_{DateTime.UtcNow:yyyyMMdd}.log";
                _currentLogFilePath = Path.Combine(LogDirectory, fileName);
                _isInitialized = true;
                
                WriteDirect(LogLevel.Info, "LOGGER", "Orbit logging initialized. Log target: " + _currentLogFilePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CRITICAL] Failed to initialize OrbitLogger: {ex}");
            }
        }
    }

    public static void Info(string category, string message) => Log(LogLevel.Info, category, message);
    public static void Debug(string category, string message) => Log(LogLevel.Debug, category, message);
    public static void Warn(string category, string message) => Log(LogLevel.Warn, category, message);
    public static void Error(string category, string message, Exception? ex = null) => Log(LogLevel.Error, category, message, ex);

    public static void Log(LogLevel level, string category, string message, Exception? ex = null)
    {
        lock (_syncLock)
        {
            if (!_isInitialized)
            {
                Initialize();
            }

            var fullMessage = ex != null ? $"{message} | Exception: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}" : message;
            WriteDirect(level, category, fullMessage);
        }
    }

    private static void WriteDirect(LogLevel level, string category, string message)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var threadId = Environment.CurrentManagedThreadId;
        var formatted = $"[{timestamp} UTC] [{level,-5}] [T{threadId:D2}] [{category}] {message}";

        // Write to debugger output
        System.Diagnostics.Debug.WriteLine(formatted);

        // Write to log file
        if (!string.IsNullOrEmpty(_currentLogFilePath))
        {
            try
            {
                File.AppendAllText(_currentLogFilePath, formatted + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Fallback to debug console if file is locked
            }
        }
    }
}

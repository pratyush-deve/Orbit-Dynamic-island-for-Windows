using Orbit.Core;

namespace Orbit.Services;

public interface ILoggingService
{
    void Debug(string category, string message);
    void Info(string category, string message);
    void Warn(string category, string message);
    void Error(string category, string message, Exception? ex = null);
}

/// <summary>
/// Service wrapper around OrbitLogger for dependency injection.
/// </summary>
public sealed class LoggingService : ILoggingService
{
    public LoggingService()
    {
        OrbitLogger.Initialize();
    }

    public void Debug(string category, string message) => OrbitLogger.Debug(category, message);
    public void Info(string category, string message) => OrbitLogger.Info(category, message);
    public void Warn(string category, string message) => OrbitLogger.Warn(category, message);
    public void Error(string category, string message, Exception? ex = null) => OrbitLogger.Error(category, message, ex);
}

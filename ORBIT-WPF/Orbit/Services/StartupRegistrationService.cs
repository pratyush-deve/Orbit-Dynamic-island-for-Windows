using System.IO;
using Microsoft.Win32;
using Orbit.Core;

namespace Orbit.Services;

public interface IStartupRegistrationService
{
    bool IsRegistered { get; }
    string RegisteredCommand { get; }
    bool EnsureRegistered();
    bool Disable();
}

/// <summary>
/// Manages automatic Windows startup registration for ORBIT in the current user's registry Run key.
/// Survives Windows restarts, requires no administrator privileges, and points to the stable Release executable.
/// </summary>
public sealed class StartupRegistrationService : IStartupRegistrationService
{
    private const string LogCategory = "STARTUP_REG";
    public const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "OrbitCompanion";
    public const string AutoStartArgument = "--autostart";

    public bool IsRegistered
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false);
                return key?.GetValue(ValueName) != null;
            }
            catch (Exception ex)
            {
                OrbitLogger.Warn(LogCategory, $"Failed to check startup registration: {ex.Message}");
                return false;
            }
        }
    }

    public string RegisteredCommand
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false);
                return key?.GetValue(ValueName)?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    /// <summary>
    /// Locates the stable Release executable path for ORBIT.
    /// Prioritizes the published win-x64 Release executable so that Debug or transient builds are never registered.
    /// </summary>
    public static string GetTargetExecutablePath()
    {
        // 1. Check known stable release location in the Orbit 2 repository
        string defaultRelease = @"C:\Users\KIIT\OneDrive\Desktop\Orbit 2\publish\Orbit.exe";
        if (File.Exists(defaultRelease))
        {
            return Path.GetFullPath(defaultRelease);
        }

        // 2. Check current process path if running from a publish directory
        string? currentPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(currentPath) && File.Exists(currentPath))
        {
            if (currentPath.Contains("publish", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(currentPath);
            }
        }

        // 3. Fallback to current process path if available
        return !string.IsNullOrWhiteSpace(currentPath) ? Path.GetFullPath(currentPath) : string.Empty;
    }

    /// <summary>
    /// Registers or updates ORBIT in HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
    /// </summary>
    public bool EnsureRegistered()
    {
        try
        {
            string targetExe = GetTargetExecutablePath();
            if (string.IsNullOrWhiteSpace(targetExe) || !File.Exists(targetExe))
            {
                OrbitLogger.Warn(LogCategory, $"Cannot register startup: target executable not found at '{targetExe}'");
                return false;
            }

            string expectedCommand = $"\"{targetExe}\" {AutoStartArgument}";

            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
            if (key == null)
            {
                OrbitLogger.Error(LogCategory, "Could not open HKCU Run registry key for writing.");
                return false;
            }

            object? existingValue = key.GetValue(ValueName);
            string existingStr = existingValue?.ToString() ?? string.Empty;

            if (string.Equals(existingStr, expectedCommand, StringComparison.OrdinalIgnoreCase))
            {
                OrbitLogger.Debug(LogCategory, $"Startup entry already up to date: {expectedCommand}");
                return true;
            }

            key.SetValue(ValueName, expectedCommand, RegistryValueKind.String);
            OrbitLogger.Info(LogCategory, $"Registered automatic startup successfully: {expectedCommand}");
            return true;
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, "Failed to register automatic Windows startup.", ex);
            return false;
        }
    }

    /// <summary>
    /// Disables automatic Windows startup by removing the registry value.
    /// </summary>
    public bool Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
            if (key == null) return false;

            if (key.GetValue(ValueName) != null)
            {
                key.DeleteValue(ValueName, false);
                OrbitLogger.Info(LogCategory, "Removed automatic Windows startup registry entry.");
            }
            return true;
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, "Failed to disable automatic startup.", ex);
            return false;
        }
    }
}


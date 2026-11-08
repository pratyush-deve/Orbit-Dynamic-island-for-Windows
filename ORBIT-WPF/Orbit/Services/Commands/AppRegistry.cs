using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using Orbit.Core;
using Orbit.Models;

namespace Orbit.Services.Commands;

public record AppTarget(
    string DisplayName,
    string ExecutableOrUri,
    string? Arguments = null,
    string? WorkingDirectory = null,
    bool UseShellExecute = true
);

/// <summary>
/// Registry and alias resolver for safe Windows application launching via voice commands.
/// Protects against launching arbitrary executables from untrusted transcription text.
/// </summary>
public sealed class AppRegistry
{
    private const string LogCategory = "APP_REGISTRY";

    private static readonly Dictionary<string, (string CanonicalName, string DefaultTarget, string? Arguments)> KnownBuiltIns =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["notepad"] = ("Notepad", "notepad.exe", null),
            ["text editor"] = ("Notepad", "notepad.exe", null),
            ["calculator"] = ("Calculator", "calc.exe", null),
            ["calc"] = ("Calculator", "calc.exe", null),
            ["terminal"] = ("Windows Terminal", "wt.exe", null),
            ["windows terminal"] = ("Windows Terminal", "wt.exe", null),
            ["command prompt"] = ("Command Prompt", "cmd.exe", null),
            ["cmd"] = ("Command Prompt", "cmd.exe", null),
            ["powershell"] = ("PowerShell", "powershell.exe", null),
            ["file explorer"] = ("File Explorer", "explorer.exe", null),
            ["explorer"] = ("File Explorer", "explorer.exe", null),
            ["settings"] = ("Windows Settings", "ms-settings:", null),
            ["windows settings"] = ("Windows Settings", "ms-settings:", null),
            ["task manager"] = ("Task Manager", "taskmgr.exe", null),
            ["google"] = ("Google", "https://www.google.com", null),
            ["browser"] = ("Web Browser", "https://www.google.com", null),
            ["web"] = ("Web Browser", "https://www.google.com", null),
            ["internet"] = ("Web Browser", "https://www.google.com", null),
        };

    private static readonly Dictionary<string, string> AppAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["android studio"] = "android-studio",
            ["android"] = "android-studio",
            ["studio"] = "android-studio",

            ["google chrome"] = "google-chrome",
            ["chrome"] = "google-chrome",
            ["google"] = "google-chrome",
            ["browser"] = "google-chrome",
            ["web"] = "google-chrome",
            ["internet"] = "google-chrome",

            ["visual studio code"] = "vscode",
            ["vs code"] = "vscode",
            ["vscode"] = "vscode",
            ["code"] = "vscode",

            ["visual studio"] = "visual-studio",
            ["devenv"] = "visual-studio",

            ["cursor"] = "cursor",
            ["windsurf"] = "windsurf",
            ["claude"] = "claude-desktop",
            ["claude desktop"] = "claude-desktop",

            ["edge"] = "msedge",
            ["microsoft edge"] = "msedge",

            ["intellij"] = "intellij-idea",
            ["intellij idea"] = "intellij-idea",
            ["pycharm"] = "pycharm",
            ["rider"] = "rider",

            ["discord"] = "discord",
            ["slack"] = "slack",
            ["teams"] = "ms-teams",
            ["microsoft teams"] = "ms-teams",
            ["whatsapp"] = "whatsapp",
            ["telegram"] = "telegram",
            ["zoom"] = "zoom",
            ["spotify"] = "spotify",

            ["word"] = "ms-word",
            ["microsoft word"] = "ms-word",
            ["excel"] = "ms-excel",
            ["microsoft excel"] = "ms-excel",
            ["powerpoint"] = "ms-powerpoint",
            ["microsoft powerpoint"] = "ms-powerpoint",
        };

    /// <summary>
    /// Checks whether an application query can be resolved to a known safe application.
    /// </summary>
    public bool IsRecognizedApp(string appName)
    {
        if (string.IsNullOrWhiteSpace(appName)) return false;
        var clean = appName.Trim().ToLowerInvariant();

        if (KnownBuiltIns.ContainsKey(clean)) return true;
        if (AppAliases.ContainsKey(clean)) return true;

        return false;
    }

    /// <summary>
    /// Resolves an application name into a safe, launchable AppTarget.
    /// Uses discovered Orbit applications, known built-in tools, or Windows App Paths.
    /// </summary>
    public AppTarget? ResolveApp(string requestedApp, IEnumerable<OrbitApp>? discoveredApps = null)
    {
        if (string.IsNullOrWhiteSpace(requestedApp)) return null;

        var clean = requestedApp.Trim().ToLowerInvariant();

        // 1. Check known built-in Windows applications
        if (KnownBuiltIns.TryGetValue(clean, out var builtIn))
        {
            return new AppTarget(builtIn.CanonicalName, builtIn.DefaultTarget, builtIn.Arguments);
        }

        // 2. Check alias mapping
        string targetId = AppAliases.TryGetValue(clean, out var mappedId) ? mappedId : clean;

        // 3. Match against discovered Orbit apps
        if (discoveredApps != null)
        {
            var matched = discoveredApps.FirstOrDefault(a =>
                string.Equals(a.Id, targetId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a.Name, requestedApp, StringComparison.OrdinalIgnoreCase) ||
                a.Name.IndexOf(clean, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (!string.IsNullOrEmpty(a.ProcessName) && string.Equals(a.ProcessName, clean, StringComparison.OrdinalIgnoreCase)));

            if (matched != null && !string.IsNullOrWhiteSpace(matched.ExecutablePath))
            {
                return new AppTarget(
                    DisplayName: matched.Name,
                    ExecutableOrUri: matched.ExecutablePath,
                    Arguments: matched.Arguments,
                    WorkingDirectory: matched.WorkingDirectory
                );
            }
        }

        // 4. Try resolving via Windows Registry App Paths (safe system registration)
        var registryTarget = ResolveFromAppPaths(clean);
        if (registryTarget != null)
        {
            return registryTarget;
        }

        OrbitLogger.Warn(LogCategory, $"Could not safely resolve application target for: '{requestedApp}'.");
        return null;
    }

    private static AppTarget? ResolveFromAppPaths(string appName)
    {
        try
        {
            string exeName = appName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? appName : $"{appName}.exe";

            // Check HKCU then HKLM
            string[] subKeys =
            [
                $@"Software\Microsoft\Windows\CurrentVersion\App Paths\{exeName}",
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exeName}"
            ];

            foreach (var keyPath in subKeys)
            {
                using var key = Registry.CurrentUser.OpenSubKey(keyPath) ?? Registry.LocalMachine.OpenSubKey(keyPath);
                if (key != null)
                {
                    string? path = key.GetValue(null)?.ToString();
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    {
                        return new AppTarget(
                            DisplayName: Path.GetFileNameWithoutExtension(path),
                            ExecutableOrUri: path
                        );
                    }
                }
            }
        }
        catch (Exception ex)
        {
            OrbitLogger.Warn(LogCategory, $"Error resolving App Paths for '{appName}': {ex.Message}");
        }

        return null;
    }
}


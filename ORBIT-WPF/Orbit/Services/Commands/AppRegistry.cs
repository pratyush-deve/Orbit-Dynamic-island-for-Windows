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
            ["gmail"] = ("Gmail", "https://mail.google.com", null),
            ["mail"] = ("Gmail", "https://mail.google.com", null),
            ["email"] = ("Gmail", "https://mail.google.com", null),
            ["males"] = ("Gmail", "https://mail.google.com", null),
            ["chrome"] = ("Google Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe", null),
            ["google chrome"] = ("Google Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe", null),
            ["android studio"] = ("Android Studio", @"C:\Program Files\Android\Android Studio\bin\studio64.exe", null),
            ["studio"] = ("Android Studio", @"C:\Program Files\Android\Android Studio\bin\studio64.exe", null),
            ["operate studio"] = ("Android Studio", @"C:\Program Files\Android\Android Studio\bin\studio64.exe", null),
            ["android thing"] = ("Android Studio", @"C:\Program Files\Android\Android Studio\bin\studio64.exe", null),
            ["android ide"] = ("Android Studio", @"C:\Program Files\Android\Android Studio\bin\studio64.exe", null),
            ["vs code"] = ("Visual Studio Code", @"C:\Users\KIIT\AppData\Local\Programs\Microsoft VS Code\Code.exe", null),
            ["vscode"] = ("Visual Studio Code", @"C:\Users\KIIT\AppData\Local\Programs\Microsoft VS Code\Code.exe", null),
            ["code"] = ("Visual Studio Code", @"C:\Users\KIIT\AppData\Local\Programs\Microsoft VS Code\Code.exe", null),
            ["visual studio code"] = ("Visual Studio Code", @"C:\Users\KIIT\AppData\Local\Programs\Microsoft VS Code\Code.exe", null),
            ["antigravity"] = ("Antigravity IDE", @"C:\Users\KIIT\AppData\Local\Programs\Antigravity IDE\Antigravity IDE.exe", null),
            ["antigravity ide"] = ("Antigravity IDE", @"C:\Users\KIIT\AppData\Local\Programs\Antigravity IDE\Antigravity IDE.exe", null),
            ["agy"] = ("Antigravity IDE", @"C:\Users\KIIT\AppData\Local\Programs\Antigravity IDE\Antigravity IDE.exe", null),
            ["whatsapp"] = ("WhatsApp", @"shell:AppsFolder\5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App", null),
            ["amazon"] = ("Amazon", "https://www.amazon.in", null),
            ["amazon in"] = ("Amazon", "https://www.amazon.in", null),
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
            ["operate studio"] = "android-studio",
            ["android thing"] = "android-studio",
            ["android ide"] = "android-studio",

            ["antigravity"] = "antigravity",
            ["antigravity ide"] = "antigravity",
            ["agy"] = "antigravity",

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

    private static readonly Dictionary<string, string[]> SemanticRoleMappings =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["messenger"] = ["whatsapp", "telegram", "discord", "ms-teams", "slack", "signal", "skype"],
            ["messaging"] = ["whatsapp", "telegram", "discord", "ms-teams", "slack", "signal", "skype"],
            ["chat"] = ["whatsapp", "telegram", "discord", "ms-teams", "slack", "signal"],
            ["social"] = ["whatsapp", "telegram", "discord", "ms-teams"],
            ["browser"] = ["google-chrome", "chrome", "msedge"],
            ["web browser"] = ["google-chrome", "chrome", "msedge"],
            ["internet"] = ["google-chrome", "chrome"],
            ["ide"] = ["android-studio", "antigravity", "vscode"],
            ["code editor"] = ["vscode", "android-studio", "antigravity"],
            ["coding"] = ["vscode", "android-studio", "antigravity"],
            ["editor"] = ["notepad", "vscode"],
            ["notes"] = ["notepad", "ms-onenote"],
            ["mail"] = ["gmail", "ms-outlook"],
            ["email"] = ["gmail", "ms-outlook"],
            ["music"] = ["spotify"],
            ["songs"] = ["spotify"],
            ["terminal"] = ["windows-terminal", "cmd", "powershell"],
            ["command line"] = ["windows-terminal", "cmd", "powershell"]
        };

    /// <summary>
    /// Checks whether an application query can be resolved to a known safe application.
    /// </summary>
    public bool IsRecognizedApp(string appName)
    {
        if (string.IsNullOrWhiteSpace(appName)) return false;
        var clean = appName.Trim().ToLowerInvariant();
        if (clean.StartsWith("the ")) clean = clean[4..].Trim();
        if (clean.StartsWith("my ")) clean = clean[3..].Trim();

        if (KnownBuiltIns.ContainsKey(clean)) return true;
        if (AppAliases.ContainsKey(clean)) return true;

        if (clean.Contains("chrome") ||
            clean.Contains("studio") || clean.Contains("android") ||
            clean.Contains("code") || clean.Contains("vscode") ||
            clean.Contains("antigravity") || clean.Contains("agy") ||
            clean.Contains("whatsapp") || clean.Contains("mail") ||
            clean.Contains("males") || clean.Contains("amazon"))
        {
            return true;
        }

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
        if (clean.StartsWith("the ")) clean = clean[4..].Trim();
        if (clean.StartsWith("my ")) clean = clean[3..].Trim();

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

        // 5. Intelligent fallback mappings for key desktop developer & productivity tools
        if (clean.Contains("chrome"))
        {
            return new AppTarget("Google Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe");
        }
        if (clean.Contains("studio") || clean.Contains("android"))
        {
            return new AppTarget("Android Studio", @"C:\Program Files\Android\Android Studio\bin\studio64.exe");
        }
        if (clean.Contains("code") || clean.Contains("vscode"))
        {
            return new AppTarget("Visual Studio Code", @"C:\Users\KIIT\AppData\Local\Programs\Microsoft VS Code\Code.exe");
        }
        if (clean.Contains("antigravity") || clean.Contains("agy"))
        {
            return new AppTarget("Antigravity IDE", @"C:\Users\KIIT\AppData\Local\Programs\Antigravity IDE\Antigravity IDE.exe");
        }
        if (clean.Contains("whatsapp"))
        {
            return new AppTarget("WhatsApp", @"shell:AppsFolder\5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App");
        }
        if (clean.Contains("mail") || clean.Contains("males") || clean.Contains("gmail"))
        {
            return new AppTarget("Gmail", "https://mail.google.com");
        }
        if (clean.Contains("amazon"))
        {
            return new AppTarget("Amazon", "https://www.amazon.in");
        }

        OrbitLogger.Warn(LogCategory, $"Could not safely resolve application target for: '{requestedApp}'.");
        return null;
    }

    /// <summary>
    /// Intelligently suggests a candidate application when an exact match isn't found,
    /// using semantic category/role mapping, substring matching, or phonetic/Levenshtein similarity.
    /// E.g. "messenger app" -> WhatsApp, "coding app" -> Android Studio, "crome" -> Google Chrome.
    /// </summary>
    public AppTarget? SuggestCandidate(string requestedApp, IEnumerable<OrbitApp>? discoveredApps = null)
    {
        if (string.IsNullOrWhiteSpace(requestedApp)) return null;

        string clean = requestedApp.Trim().ToLowerInvariant();
        if (clean.StartsWith("the ")) clean = clean[4..].Trim();
        if (clean.StartsWith("my ")) clean = clean[3..].Trim();
        if (clean.EndsWith(" app")) clean = clean[..^4].Trim();
        if (clean.EndsWith(" application")) clean = clean[..^12].Trim();
        if (clean.EndsWith(" program")) clean = clean[..^8].Trim();

        // 1. Semantic role / category matching (generic terms like "messenger", "chat", "browser", "coding")
        foreach (var (role, candidateKeys) in SemanticRoleMappings)
        {
            if (clean == role || clean.Contains(role) || role.Contains(clean))
            {
                foreach (var key in candidateKeys)
                {
                    var resolved = ResolveApp(key, discoveredApps);
                    if (resolved != null)
                    {
                        return resolved;
                    }
                }
            }
        }

        // 2. Substring containment in Canonical Names
        foreach (var kvp in KnownBuiltIns)
        {
            if (kvp.Value.CanonicalName.IndexOf(clean, StringComparison.OrdinalIgnoreCase) >= 0 ||
                clean.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new AppTarget(kvp.Value.CanonicalName, kvp.Value.DefaultTarget, kvp.Value.Arguments);
            }
        }

        // 3. Fuzzy Levenshtein matching across built-in aliases
        string? bestAlias = null;
        int bestDistance = int.MaxValue;

        foreach (var key in KnownBuiltIns.Keys.Concat(AppAliases.Keys))
        {
            int dist = LevenshteinDistance(clean, key);
            int maxAllowedDist = Math.Max(1, key.Length / 3);
            if (dist <= maxAllowedDist && dist < bestDistance)
            {
                bestDistance = dist;
                bestAlias = key;
            }
        }

        if (bestAlias != null)
        {
            var resolved = ResolveApp(bestAlias, discoveredApps);
            if (resolved != null)
            {
                return resolved;
            }
        }

        // 4. Fuzzy matching against discovered apps
        if (discoveredApps != null)
        {
            OrbitApp? bestDiscovered = null;
            int bestDiscoveredDist = int.MaxValue;

            foreach (var app in discoveredApps)
            {
                int dist = LevenshteinDistance(clean, app.Name.ToLowerInvariant());
                if (dist <= 2 && dist < bestDiscoveredDist)
                {
                    bestDiscoveredDist = dist;
                    bestDiscovered = app;
                }
            }

            if (bestDiscovered != null && !string.IsNullOrWhiteSpace(bestDiscovered.ExecutablePath))
            {
                return new AppTarget(
                    bestDiscovered.Name,
                    bestDiscovered.ExecutablePath,
                    bestDiscovered.Arguments,
                    bestDiscovered.WorkingDirectory);
            }
        }

        return null;
    }

    public static int LevenshteinDistance(string s, string t)
    {
        if (string.IsNullOrEmpty(s)) return t?.Length ?? 0;
        if (string.IsNullOrEmpty(t)) return s.Length;

        int[,] d = new int[s.Length + 1, t.Length + 1];
        for (int i = 0; i <= s.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= t.Length; j++) d[0, j] = j;

        for (int j = 1; j <= t.Length; j++)
        {
            for (int i = 1; i <= s.Length; i++)
            {
                int cost = char.ToLowerInvariant(s[i - 1]) == char.ToLowerInvariant(t[j - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[s.Length, t.Length];
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


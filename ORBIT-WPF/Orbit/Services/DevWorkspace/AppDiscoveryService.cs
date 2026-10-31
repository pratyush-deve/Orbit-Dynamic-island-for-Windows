using System.IO;
using Microsoft.Win32;
using Orbit.Core;
using Orbit.Models;

namespace Orbit.Services.DevWorkspace;

public sealed class AppDiscoveryService : IAppDiscoveryService
{
    private const string LogCategory = "APP_DISCOVERY";
    private readonly IIconExtractorService _iconExtractor;

    // Known developer application signatures for discovery ranking and categorization
    private static readonly (string Id, string CanonicalName, string[] ExecutableMatches, string[] NameMatches, string ProcessName, string? DefaultArgs)[] KnownDeveloperApps =
    [
        ("android-studio", "Android Studio", ["studio64.exe", "studio.exe"], ["Android Studio"], "studio64", null),
        ("antigravity-ide", "Antigravity IDE", ["Antigravity IDE.exe"], ["Antigravity IDE"], "Antigravity IDE", null),
        ("antigravity", "Antigravity", ["Antigravity.exe"], ["Antigravity"], "Antigravity", null),
        ("google-chrome", "Google Chrome (with ChatGPT)", ["chrome.exe"], ["Google Chrome"], "chrome", "https://chatgpt.com/"),
        ("vscode", "Visual Studio Code", ["Code.exe"], ["Visual Studio Code"], "Code", null),
        ("cursor", "Cursor", ["Cursor.exe"], ["Cursor"], "Cursor", null),
        ("windsurf", "Windsurf", ["Windsurf.exe"], ["Windsurf"], "Windsurf", null),
        ("claude-desktop", "Claude Desktop", ["Claude.exe"], ["Claude"], "Claude", null),
        ("msedge", "Microsoft Edge", ["msedge.exe"], ["Microsoft Edge"], "msedge", null),
        ("visual-studio", "Visual Studio", ["devenv.exe"], ["Visual Studio 2022", "Visual Studio 2019", "Visual Studio Community", "Visual Studio Professional"], "devenv", null),
        ("intellij-idea", "IntelliJ IDEA", ["idea64.exe", "idea.exe"], ["IntelliJ IDEA"], "idea64", null),
        ("pycharm", "PyCharm", ["pycharm64.exe", "pycharm.exe"], ["PyCharm"], "pycharm64", null),
        ("github-desktop", "GitHub Desktop", ["GitHubDesktop.exe"], ["GitHub Desktop"], "GitHubDesktop", null),
        ("docker-desktop", "Docker Desktop", ["Docker Desktop.exe"], ["Docker Desktop"], "Docker Desktop", null),
        ("postman", "Postman", ["Postman.exe"], ["Postman"], "Postman", null),
        ("windows-terminal", "Windows Terminal", ["wt.exe"], ["Windows Terminal"], "WindowsTerminal", null),
        ("git-bash", "Git Bash", ["git-bash.exe"], ["Git Bash", "Git"], "git-bash", null),
        ("rider", "JetBrains Rider", ["rider64.exe", "rider.exe"], ["JetBrains Rider", "Rider"], "rider64", null),
        ("sublime-text", "Sublime Text", ["sublime_text.exe"], ["Sublime Text"], "sublime_text", null),
        ("dbeaver", "DBeaver", ["dbeaver.exe"], ["DBeaver"], "dbeaver", null),
        ("wireshark", "Wireshark", ["Wireshark.exe"], ["Wireshark"], "Wireshark", null),
        ("virtualbox", "Oracle VirtualBox", ["VirtualBox.exe"], ["Oracle VM VirtualBox", "VirtualBox"], "VirtualBox", null)
    ];

    public AppDiscoveryService(IIconExtractorService iconExtractor)
    {
        _iconExtractor = iconExtractor;
    }

    public List<DevApp> GetDefaultApplications()
    {
        var discovered = DiscoverDeveloperAppsAsync().GetAwaiter().GetResult();
        var defaults = new List<DevApp>();

        // 1. Android Studio
        var studio = discovered.FirstOrDefault(a => a.Id == "android-studio")
                     ?? FallbackDefaultApp("android-studio", "Android Studio", "studio64.exe", "studio64", null);
        studio.IsSelected = true;
        defaults.Add(studio);

        // 2. Antigravity IDE
        var ide = discovered.FirstOrDefault(a => a.Id == "antigravity-ide")
                  ?? FallbackDefaultApp("antigravity-ide", "Antigravity IDE", "Antigravity IDE.exe", "Antigravity IDE", null);
        ide.IsSelected = true;
        defaults.Add(ide);

        // 3. Antigravity
        var antigravity = discovered.FirstOrDefault(a => a.Id == "antigravity")
                          ?? FallbackDefaultApp("antigravity", "Antigravity", "Antigravity.exe", "Antigravity", null);
        antigravity.IsSelected = true;
        defaults.Add(antigravity);

        // 4. Google Chrome (with ChatGPT)
        var chrome = discovered.FirstOrDefault(a => a.Id == "google-chrome")
                     ?? FallbackDefaultApp("google-chrome", "Google Chrome (with ChatGPT)", "chrome.exe", "chrome", "https://chatgpt.com/");
        chrome.IsSelected = true;
        defaults.Add(chrome);

        return defaults;
    }

    public async Task<List<DevApp>> DiscoverDeveloperAppsAsync()
    {
        return await Task.Run(() =>
        {
            var results = new Dictionary<string, DevApp>(StringComparer.OrdinalIgnoreCase);

            // 1. Scan Start Menu Shortcuts
            ScanStartMenuShortcuts(results);

            // 2. Scan Registry Uninstall Keys
            ScanRegistryUninstallKeys(results);

            // 3. Scan App Paths in Registry
            ScanAppPathsRegistry(results);

            // 4. Scan LocalAppData Programs
            ScanLocalAppDataPrograms(results);

            // 5. Ensure core developer applications exist in the discovered list
            EnsureDefaultAppExists(results, "android-studio", "Android Studio", "studio64.exe", "studio64", null);
            EnsureDefaultAppExists(results, "antigravity-ide", "Antigravity IDE", "Antigravity IDE.exe", "Antigravity IDE", null);
            EnsureDefaultAppExists(results, "antigravity", "Antigravity", "Antigravity.exe", "Antigravity", null);
            EnsureDefaultAppExists(results, "google-chrome", "Google Chrome (with ChatGPT)", "chrome.exe", "chrome", "https://chatgpt.com/");

            // 6. Extract icons for all discovered applications
            foreach (var app in results.Values)
            {
                if (app.Icon == null)
                {
                    string pathToIcon = !string.IsNullOrWhiteSpace(app.IconPath) ? app.IconPath : app.ExecutablePath;
                    app.Icon = _iconExtractor.GetOrExtractIcon(pathToIcon);
                }
            }

            // Order with known priority: Android Studio, Antigravity IDE, Antigravity, Google Chrome, then alphabetically
            return results.Values
                .OrderBy(a => a.Id switch
                {
                    "android-studio" => 1,
                    "antigravity-ide" => 2,
                    "antigravity" => 3,
                    "google-chrome" => 4,
                    _ => 10
                })
                .ThenBy(a => a.Name)
                .ToList();
        });
    }

    private void ScanStartMenuShortcuts(Dictionary<string, DevApp> results)
    {
        try
        {
            var dirs = new List<string>();

            string common = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs");
            if (Directory.Exists(common)) dirs.Add(common);

            string user = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
            if (Directory.Exists(user)) dirs.Add(user);

            foreach (var dir in dirs)
            {
                try
                {
                    var lnkFiles = Directory.GetFiles(dir, "*.lnk", SearchOption.AllDirectories);
                    foreach (var lnk in lnkFiles)
                    {
                        string shortcutName = Path.GetFileNameWithoutExtension(lnk);
                        var (target, args, workDir) = ResolveShortcut(lnk);

                        var matchedKnown = FindKnownApp(shortcutName, target);
                        if (matchedKnown.HasValue)
                        {
                            var (id, canonicalName, _, _, procName, defaultArgs) = matchedKnown.Value;
                            if (!results.ContainsKey(id))
                            {
                                results[id] = new DevApp
                                {
                                    Id = id,
                                    Name = canonicalName,
                                    ExecutablePath = File.Exists(target) ? target : lnk,
                                    Arguments = defaultArgs ?? args,
                                    WorkingDirectory = workDir,
                                    ProcessName = procName,
                                    IconPath = lnk,
                                    IsInstalled = true
                                };
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    OrbitLogger.Debug(LogCategory, $"Error scanning start menu directory '{dir}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            OrbitLogger.Warn(LogCategory, $"Start menu scan warning: {ex.Message}");
        }
    }

    private void ScanRegistryUninstallKeys(Dictionary<string, DevApp> results)
    {
        var roots = new[]
        {
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall")
        };

        foreach (var (hive, subKeyPath) in roots)
        {
            try
            {
                using var key = hive.OpenSubKey(subKeyPath);
                if (key == null) continue;

                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    try
                    {
                        using var appKey = key.OpenSubKey(subKeyName);
                        if (appKey == null) continue;

                        string? displayName = appKey.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(displayName)) continue;

                        string? displayIcon = appKey.GetValue("DisplayIcon") as string;
                        string? installLocation = appKey.GetValue("InstallLocation") as string;

                        var matchedKnown = FindKnownApp(displayName, displayIcon ?? installLocation ?? string.Empty);
                        if (matchedKnown.HasValue)
                        {
                            var (id, canonicalName, exes, _, procName, defaultArgs) = matchedKnown.Value;
                            if (!results.ContainsKey(id))
                            {
                                string exePath = ResolveExePath(displayIcon, installLocation, exes);
                                if (!string.IsNullOrWhiteSpace(exePath))
                                {
                                    results[id] = new DevApp
                                    {
                                        Id = id,
                                        Name = canonicalName,
                                        ExecutablePath = exePath,
                                        Arguments = defaultArgs,
                                        ProcessName = procName,
                                        IconPath = displayIcon ?? exePath,
                                        IsInstalled = true
                                    };
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                OrbitLogger.Debug(LogCategory, $"Registry uninstall scan warning: {ex.Message}");
            }
        }
    }

    private void ScanAppPathsRegistry(Dictionary<string, DevApp> results)
    {
        var roots = new[]
        {
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"),
            (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\App Paths")
        };

        foreach (var (hive, subKeyPath) in roots)
        {
            try
            {
                using var key = hive.OpenSubKey(subKeyPath);
                if (key == null) continue;

                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    try
                    {
                        using var appKey = key.OpenSubKey(subKeyName);
                        if (appKey == null) continue;

                        string? defaultVal = appKey.GetValue(null) as string;
                        if (string.IsNullOrWhiteSpace(defaultVal)) continue;

                        string exePath = defaultVal.Trim('"', '\'');
                        if (!File.Exists(exePath)) continue;

                        var matchedKnown = FindKnownApp(subKeyName, exePath);
                        if (matchedKnown.HasValue)
                        {
                            var (id, canonicalName, _, _, procName, defaultArgs) = matchedKnown.Value;
                            if (!results.ContainsKey(id))
                            {
                                results[id] = new DevApp
                                {
                                    Id = id,
                                    Name = canonicalName,
                                    ExecutablePath = exePath,
                                    Arguments = defaultArgs,
                                    ProcessName = procName,
                                    IconPath = exePath,
                                    IsInstalled = true
                                };
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    private void ScanLocalAppDataPrograms(Dictionary<string, DevApp> results)
    {
        try
        {
            string localPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
            if (!Directory.Exists(localPrograms)) return;

            foreach (var dir in Directory.GetDirectories(localPrograms))
            {
                string folderName = Path.GetFileName(dir);
                var matchedKnown = FindKnownApp(folderName, string.Empty);
                if (matchedKnown.HasValue)
                {
                    var (id, canonicalName, exes, _, procName, defaultArgs) = matchedKnown.Value;
                    if (!results.ContainsKey(id))
                    {
                        foreach (var exeName in exes)
                        {
                            string candidate = Path.Combine(dir, exeName);
                            if (File.Exists(candidate))
                            {
                                results[id] = new DevApp
                                {
                                    Id = id,
                                    Name = canonicalName,
                                    ExecutablePath = candidate,
                                    Arguments = defaultArgs,
                                    ProcessName = procName,
                                    IconPath = candidate,
                                    IsInstalled = true
                                };
                                break;
                            }
                        }
                    }
                }
            }
        }
        catch { }
    }

    private static (string Id, string CanonicalName, string[] ExecutableMatches, string[] NameMatches, string ProcessName, string? DefaultArgs)?
        FindKnownApp(string name, string targetOrPath)
    {
        string normName = name.ToLowerInvariant();
        string normTarget = targetOrPath.ToLowerInvariant();

        // 1. Check FileVersionInfo if path points to an existing file
        if (File.Exists(targetOrPath))
        {
            try
            {
                var vi = System.Diagnostics.FileVersionInfo.GetVersionInfo(targetOrPath);
                string prod = (vi.ProductName ?? string.Empty).ToLowerInvariant();
                string desc = (vi.FileDescription ?? string.Empty).ToLowerInvariant();

                if (prod.Contains("antigravity ide") || desc.Contains("antigravity ide"))
                {
                    return KnownDeveloperApps.FirstOrDefault(e => e.Id == "antigravity-ide");
                }
                else if ((prod.Contains("antigravity") || desc.Contains("antigravity")) &&
                         !prod.Contains("ide") && !desc.Contains("ide"))
                {
                    return KnownDeveloperApps.FirstOrDefault(e => e.Id == "antigravity");
                }
            }
            catch { }
        }

        // 2. Iterate known developer apps
        foreach (var entry in KnownDeveloperApps)
        {
            // Explicit guard: Regular Antigravity must never match "IDE"
            if (entry.Id == "antigravity" && (normName.Contains("ide") || normTarget.Contains("ide")))
            {
                continue;
            }

            // Antigravity IDE must specifically have "ide"
            if (entry.Id == "antigravity-ide" && !normName.Contains("ide") && !normTarget.Contains("ide"))
            {
                continue;
            }

            // Match by executable file name
            foreach (var exe in entry.ExecutableMatches)
            {
                string normExe = exe.ToLowerInvariant();
                if (normTarget.EndsWith(normExe) || normTarget.Contains("\\" + normExe))
                {
                    return entry;
                }
            }

            // Match by name
            foreach (var nm in entry.NameMatches)
            {
                string normNm = nm.ToLowerInvariant();
                if (entry.Id == "antigravity")
                {
                    if (normName == "antigravity" || normName.StartsWith("antigravity ") || normTarget.Contains(@"\antigravity\"))
                    {
                        return entry;
                    }
                }
                else if (normName.Contains(normNm))
                {
                    return entry;
                }
            }
        }

        return null;
    }

    private static string ResolveExePath(string? displayIcon, string? installLocation, string[] exes)
    {
        if (!string.IsNullOrWhiteSpace(displayIcon))
        {
            string p = displayIcon.Trim().Trim('"', '\'');
            if (p.Contains(".exe,", StringComparison.OrdinalIgnoreCase))
            {
                p = p.Split(',')[0].Trim();
            }
            if (File.Exists(p) && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return p;
            }
        }

        if (!string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation))
        {
            foreach (var exe in exes)
            {
                string candidate = Path.Combine(installLocation, exe);
                if (File.Exists(candidate)) return candidate;

                string binCandidate = Path.Combine(installLocation, "bin", exe);
                if (File.Exists(binCandidate)) return binCandidate;
            }
        }

        return string.Empty;
    }

    private static (string targetPath, string? arguments, string? workingDir) ResolveShortcut(string lnkPath)
    {
        string target = string.Empty;
        string? args = null;
        string? dir = null;

        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType != null)
            {
                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell != null)
                {
                    dynamic shortcut = shell.CreateShortcut(lnkPath);
                    target = (string)shortcut.TargetPath;
                    args = (string)shortcut.Arguments;
                    dir = (string)shortcut.WorkingDirectory;
                }
            }
        }
        catch { }

        // If WScript.Shell failed to resolve target (e.g. MSI/advertised or special Electron shortcut),
        // inspect the binary content of the .lnk file or working directory
        if (string.IsNullOrWhiteSpace(target) || !File.Exists(target))
        {
            try
            {
                if (File.Exists(lnkPath))
                {
                    byte[] bytes = File.ReadAllBytes(lnkPath);
                    string ascii = System.Text.Encoding.ASCII.GetString(bytes);
                    var match = System.Text.RegularExpressions.Regex.Match(ascii, @"[a-zA-Z]:\\[^:\*\?""<>\|\r\n\t]+\.exe");
                    if (match.Success && File.Exists(match.Value))
                    {
                        target = match.Value;
                    }
                    else if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                    {
                        string lnkName = Path.GetFileNameWithoutExtension(lnkPath);
                        string candidate = Path.Combine(dir, lnkName + ".exe");
                        if (File.Exists(candidate))
                        {
                            target = candidate;
                        }
                    }
                }
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(dir) && !string.IsNullOrWhiteSpace(target) && File.Exists(target))
        {
            dir = Path.GetDirectoryName(target);
        }

        return (string.IsNullOrWhiteSpace(target) ? lnkPath : target, string.IsNullOrWhiteSpace(args) ? null : args, string.IsNullOrWhiteSpace(dir) ? null : dir);
    }

    private void EnsureDefaultAppExists(
        Dictionary<string, DevApp> results,
        string id,
        string canonicalName,
        string exePattern,
        string procName,
        string? defaultArgs)
    {
        if (results.ContainsKey(id)) return;

        var fallback = FallbackDefaultApp(id, canonicalName, exePattern, procName, defaultArgs);
        results[id] = fallback;
    }

    private DevApp FallbackDefaultApp(string id, string name, string exePattern, string procName, string? defaultArgs)
    {
        // Search common standard directories for this executable
        string[] searchPaths = id switch
        {
            "android-studio" =>
            [
                @"C:\Program Files\Android\Android Studio1\bin\studio64.exe",
                @"C:\Program Files\Android\Android Studio\bin\studio64.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Android Studio\bin\studio64.exe")
            ],
            "antigravity-ide" =>
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Antigravity IDE\Antigravity IDE.exe"),
                @"C:\Program Files\Antigravity IDE\Antigravity IDE.exe",
                @"C:\Program Files\Antigravity\Antigravity IDE.exe"
            ],
            "antigravity" =>
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\antigravity\Antigravity.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Antigravity\Antigravity.exe"),
                @"C:\Program Files\Antigravity\Antigravity.exe"
            ],
            "google-chrome" =>
            [
                @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe")
            ],
            _ => []
        };

        string resolvedPath = searchPaths.FirstOrDefault(File.Exists) ?? exePattern;
        bool exists = File.Exists(resolvedPath);

        return new DevApp
        {
            Id = id,
            Name = name,
            ExecutablePath = resolvedPath,
            Arguments = defaultArgs,
            ProcessName = procName,
            IconPath = resolvedPath,
            IsInstalled = exists,
            Icon = exists ? _iconExtractor.GetOrExtractIcon(resolvedPath) : null
        };
    }
}


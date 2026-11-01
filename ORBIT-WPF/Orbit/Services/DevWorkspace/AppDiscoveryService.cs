using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Orbit.Core;
using Orbit.Models;

namespace Orbit.Services.DevWorkspace;

public sealed class AppDiscoveryService : IAppDiscoveryService
{
    private const string LogCategory = "APP_DISCOVERY";
    private readonly IIconExtractorService _iconExtractor;

    // Known application signatures across all categories
    private static readonly (string Id, string CanonicalName, AppCategory Category, string[] ExecutableMatches, string[] NameMatches, string ProcessName, string? DefaultArgs, string? PackageFamilyName)[] KnownApps =
    [
        // === Developer Applications ===
        ("android-studio", "Android Studio", AppCategory.Developer, ["studio64.exe", "studio.exe"], ["Android Studio"], "studio64", null, null),
        ("antigravity-ide", "Antigravity IDE", AppCategory.Developer, ["Antigravity IDE.exe"], ["Antigravity IDE"], "Antigravity IDE", null, null),
        ("antigravity", "Antigravity", AppCategory.Developer, ["Antigravity.exe"], ["Antigravity"], "Antigravity", null, null),
        ("google-chrome", "Google Chrome (with ChatGPT)", AppCategory.Developer, ["chrome.exe"], ["Google Chrome"], "chrome", "https://chatgpt.com/", null),
        ("vscode", "Visual Studio Code", AppCategory.Developer, ["Code.exe"], ["Visual Studio Code"], "Code", null, null),
        ("cursor", "Cursor", AppCategory.Developer, ["Cursor.exe"], ["Cursor"], "Cursor", null, null),
        ("windsurf", "Windsurf", AppCategory.Developer, ["Windsurf.exe"], ["Windsurf"], "Windsurf", null, null),
        ("claude-desktop", "Claude Desktop", AppCategory.Developer, ["Claude.exe"], ["Claude"], "Claude", null, null),
        ("msedge", "Microsoft Edge", AppCategory.Developer, ["msedge.exe"], ["Microsoft Edge"], "msedge", null, null),
        ("visual-studio", "Visual Studio", AppCategory.Developer, ["devenv.exe"], ["Visual Studio 2022", "Visual Studio 2019", "Visual Studio Community", "Visual Studio Professional"], "devenv", null, null),
        ("intellij-idea", "IntelliJ IDEA", AppCategory.Developer, ["idea64.exe", "idea.exe"], ["IntelliJ IDEA"], "idea64", null, null),
        ("pycharm", "PyCharm", AppCategory.Developer, ["pycharm64.exe", "pycharm.exe"], ["PyCharm"], "pycharm64", null, null),
        ("github-desktop", "GitHub Desktop", AppCategory.Developer, ["GitHubDesktop.exe"], ["GitHub Desktop"], "GitHubDesktop", null, null),
        ("docker-desktop", "Docker Desktop", AppCategory.Developer, ["Docker Desktop.exe"], ["Docker Desktop"], "Docker Desktop", null, null),
        ("postman", "Postman", AppCategory.Developer, ["Postman.exe"], ["Postman"], "Postman", null, null),
        ("windows-terminal", "Windows Terminal", AppCategory.Developer, ["wt.exe"], ["Windows Terminal"], "WindowsTerminal", null, null),
        ("git-bash", "Git Bash", AppCategory.Developer, ["git-bash.exe"], ["Git Bash", "Git"], "git-bash", null, null),
        ("rider", "JetBrains Rider", AppCategory.Developer, ["rider64.exe", "rider.exe"], ["JetBrains Rider", "Rider"], "rider64", null, null),
        ("sublime-text", "Sublime Text", AppCategory.Developer, ["sublime_text.exe"], ["Sublime Text"], "sublime_text", null, null),
        ("dbeaver", "DBeaver", AppCategory.Developer, ["dbeaver.exe"], ["DBeaver"], "dbeaver", null, null),
        ("wireshark", "Wireshark", AppCategory.Developer, ["Wireshark.exe"], ["Wireshark"], "Wireshark", null, null),
        ("virtualbox", "Oracle VirtualBox", AppCategory.Developer, ["VirtualBox.exe"], ["Oracle VM VirtualBox", "VirtualBox"], "VirtualBox", null, null),

        // === Social & Communication Applications ===
        ("whatsapp", "WhatsApp", AppCategory.Social, ["WhatsApp.exe", "WhatsApp.Root.exe"], ["WhatsApp"], "WhatsApp.Root", null, "5319275A.WhatsAppDesktop"),
        ("discord", "Discord", AppCategory.Social, ["Discord.exe"], ["Discord"], "Discord", null, null),
        ("telegram", "Telegram", AppCategory.Social, ["Telegram.exe"], ["Telegram Desktop", "Telegram"], "Telegram", null, null),
        ("zoom", "Zoom", AppCategory.Social, ["Zoom.exe"], ["Zoom Workplace", "Zoom", "Zoom Meetings"], "Zoom", null, null),
        ("webex", "Webex", AppCategory.Social, ["CiscoCollabHost.exe", "webex.exe"], ["Webex", "Cisco Webex"], "CiscoCollabHost", null, null),
        ("ms-teams", "Microsoft Teams", AppCategory.Social, ["ms-teams.exe", "Teams.exe"], ["Microsoft Teams"], "ms-teams", null, "MSTeams"),
        ("slack", "Slack", AppCategory.Social, ["slack.exe"], ["Slack"], "slack", null, null),
        ("skype", "Skype", AppCategory.Social, ["Skype.exe"], ["Skype"], "Skype", null, "Microsoft.SkypeApp"),
        ("signal", "Signal", AppCategory.Social, ["Signal.exe"], ["Signal"], "Signal", null, null),
        ("wechat", "WeChat", AppCategory.Social, ["WeChat.exe"], ["WeChat"], "WeChat", null, null),

        // === Work & Productivity Applications ===
        ("ms-word", "Microsoft Word", AppCategory.Work, ["WINWORD.EXE"], ["Microsoft Word", "Word"], "WINWORD", null, null),
        ("ms-excel", "Microsoft Excel", AppCategory.Work, ["EXCEL.EXE"], ["Microsoft Excel", "Excel"], "EXCEL", null, null),
        ("ms-powerpoint", "Microsoft PowerPoint", AppCategory.Work, ["POWERPNT.EXE"], ["Microsoft PowerPoint", "PowerPoint"], "POWERPNT", null, null),
        ("ms-outlook", "Microsoft Outlook", AppCategory.Work, ["OUTLOOK.EXE", "olk.exe"], ["Microsoft Outlook", "Outlook"], "OUTLOOK", null, null),
        ("ms-onenote", "Microsoft OneNote", AppCategory.Work, ["ONENOTE.EXE"], ["Microsoft OneNote", "OneNote"], "ONENOTE", null, null),
        ("notepad", "Notepad", AppCategory.Work, ["notepad.exe"], ["Notepad"], "notepad", null, "Microsoft.WindowsNotepad"),
        ("calculator", "Calculator", AppCategory.Work, ["calc.exe", "CalculatorApp.exe"], ["Calculator"], "CalculatorApp", null, "Microsoft.WindowsCalculator"),
        ("adobe-acrobat", "Adobe Acrobat", AppCategory.Work, ["Acrobat.exe", "AcroRd32.exe"], ["Adobe Acrobat", "Acrobat Reader", "Adobe Reader"], "Acrobat", null, null),
        ("libreoffice-writer", "LibreOffice Writer", AppCategory.Work, ["swriter.exe"], ["LibreOffice Writer"], "soffice.bin", null, null),
        ("libreoffice-calc", "LibreOffice Calc", AppCategory.Work, ["scalc.exe"], ["LibreOffice Calc"], "soffice.bin", null, null),
        ("libreoffice-impress", "LibreOffice Impress", AppCategory.Work, ["simpress.exe"], ["LibreOffice Impress"], "soffice.bin", null, null),
        ("wordpad", "WordPad", AppCategory.Work, ["wordpad.exe"], ["WordPad"], "wordpad", null, null)
    ];

    public AppDiscoveryService(IIconExtractorService iconExtractor)
    {
        _iconExtractor = iconExtractor;
    }

    #region Developer Apps Discovery (Preserves existing DEV Mode contract)

    public List<DevApp> GetDefaultApplications()
    {
        var discovered = DiscoverDeveloperAppsAsync().GetAwaiter().GetResult();
        var defaults = new List<DevApp>();

        // 1. Android Studio
        var studio = discovered.FirstOrDefault(a => a.Id == "android-studio")
                     ?? FallbackDefaultDevApp("android-studio", "Android Studio", "studio64.exe", "studio64", null);
        studio.IsSelected = true;
        defaults.Add(studio);

        // 2. Antigravity IDE
        var ide = discovered.FirstOrDefault(a => a.Id == "antigravity-ide")
                  ?? FallbackDefaultDevApp("antigravity-ide", "Antigravity IDE", "Antigravity IDE.exe", "Antigravity IDE", null);
        ide.IsSelected = true;
        defaults.Add(ide);

        // 3. Antigravity
        var antigravity = discovered.FirstOrDefault(a => a.Id == "antigravity")
                          ?? FallbackDefaultDevApp("antigravity", "Antigravity", "Antigravity.exe", "Antigravity", null);
        antigravity.IsSelected = true;
        defaults.Add(antigravity);

        // 4. Google Chrome (with ChatGPT)
        var chrome = discovered.FirstOrDefault(a => a.Id == "google-chrome")
                     ?? FallbackDefaultDevApp("google-chrome", "Google Chrome (with ChatGPT)", "chrome.exe", "chrome", "https://chatgpt.com/");
        chrome.IsSelected = true;
        defaults.Add(chrome);

        return defaults;
    }

    public async Task<List<DevApp>> DiscoverDeveloperAppsAsync()
    {
        var all = await DiscoverAppsByCategoryAsync(AppCategory.Developer);
        return all.Select(a => new DevApp
        {
            Id = a.Id,
            Name = a.Name,
            Category = a.Category,
            ExecutablePath = a.ExecutablePath,
            Arguments = a.Arguments,
            WorkingDirectory = a.WorkingDirectory,
            ProcessName = a.ProcessName,
            IconPath = a.IconPath,
            IsInstalled = a.IsInstalled,
            Icon = a.Icon
        }).ToList();
    }

    private DevApp FallbackDefaultDevApp(string id, string name, string exePattern, string procName, string? defaultArgs)
    {
        var baseApp = FallbackDefaultApp(id, name, AppCategory.Developer, exePattern, procName, defaultArgs, null);
        return new DevApp
        {
            Id = baseApp.Id,
            Name = baseApp.Name,
            Category = baseApp.Category,
            ExecutablePath = baseApp.ExecutablePath,
            Arguments = baseApp.Arguments,
            WorkingDirectory = baseApp.WorkingDirectory,
            ProcessName = baseApp.ProcessName,
            IconPath = baseApp.IconPath,
            IsInstalled = baseApp.IsInstalled,
            Icon = baseApp.Icon
        };
    }

    #endregion

    #region Social & Work Apps Discovery

    public async Task<List<OrbitApp>> DiscoverSocialAppsAsync()
    {
        return await DiscoverAppsByCategoryAsync(AppCategory.Social);
    }

    public async Task<List<OrbitApp>> DiscoverWorkAppsAsync()
    {
        return await DiscoverAppsByCategoryAsync(AppCategory.Work);
    }

    public async Task<List<OrbitApp>> DiscoverAppsByCategoryAsync(AppCategory category)
    {
        return await Task.Run(() =>
        {
            var results = new Dictionary<string, OrbitApp>(StringComparer.OrdinalIgnoreCase);

            // 1. Scan Start Menu Shortcuts
            ScanStartMenuShortcuts(results, category);

            // 2. Scan Registry App Paths
            ScanAppPathsRegistry(results, category);

            // 3. Scan Registry Uninstall Keys
            ScanRegistryUninstallKeys(results, category);

            // 4. Scan LocalAppData Programs
            ScanLocalAppDataPrograms(results, category);

            // 5. Scan Packaged WindowsApps (WhatsApp, Teams, Notepad, Calculator)
            ScanPackagedWindowsApps(results, category);

            // 6. Category-specific guarantees & fallbacks
            if (category == AppCategory.Developer)
            {
                EnsureDefaultAppExists(results, "android-studio", "Android Studio", AppCategory.Developer, "studio64.exe", "studio64", null, null);
                EnsureDefaultAppExists(results, "antigravity-ide", "Antigravity IDE", AppCategory.Developer, "Antigravity IDE.exe", "Antigravity IDE", null, null);
                EnsureDefaultAppExists(results, "antigravity", "Antigravity", AppCategory.Developer, "Antigravity.exe", "Antigravity", null, null);
                EnsureDefaultAppExists(results, "google-chrome", "Google Chrome (with ChatGPT)", AppCategory.Developer, "chrome.exe", "chrome", "https://chatgpt.com/", null);
            }
            else if (category == AppCategory.Social)
            {
                // Always include Google Meet through Chrome (or Edge fallback)
                EnsureGoogleMeetExists(results);
            }
            else if (category == AppCategory.Work)
            {
                // Ensure built-in Windows productivity tools (Notepad, Calculator) are available if on system
                EnsureBuiltinWorkApps(results);
            }

            // 7. Extract and resolve icons
            foreach (var app in results.Values)
            {
                if (app.Icon == null)
                {
                    if (app.Id == "google-meet")
                    {
                        app.Icon = CreateGoogleMeetIcon();
                    }
                    else
                    {
                        if (app.Id == "whatsapp")
                        {
                            string? waLogo = FindUserWhatsAppLogo();
                            if (!string.IsNullOrWhiteSpace(waLogo) && File.Exists(waLogo))
                            {
                                app.IconPath = waLogo;
                            }
                        }

                        string pathToIcon = !string.IsNullOrWhiteSpace(app.IconPath) ? app.IconPath : app.ExecutablePath;
                        if (!string.IsNullOrWhiteSpace(pathToIcon) && File.Exists(pathToIcon))
                        {
                            app.Icon = _iconExtractor.GetOrExtractIcon(pathToIcon);
                        }
                    }
                }

                OrbitLogger.Info(LogCategory, $"App '{app.Name}' (ID: {app.Id}): IconPath='{app.IconPath}', IconLoaded={app.Icon != null}");
            }

            // 8. Order results logically
            return OrderDiscoveredApps(results.Values, category);
        });
    }

    private static List<OrbitApp> OrderDiscoveredApps(IEnumerable<OrbitApp> apps, AppCategory category)
    {
        if (category == AppCategory.Developer)
        {
            return apps
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
        }

        if (category == AppCategory.Social)
        {
            return apps
                .OrderBy(a => a.Id switch
                {
                    "whatsapp" => 1,
                    "ms-teams" => 2,
                    "zoom" => 3,
                    "webex" => 4,
                    "google-meet" => 5,
                    "discord" => 6,
                    "telegram" => 7,
                    "slack" => 8,
                    _ => 10
                })
                .ThenBy(a => a.Name)
                .ToList();
        }

        // Work Category ordering: Office apps, Notepad, Calc, PDF, others
        return apps
            .OrderBy(a => a.Id switch
            {
                "notepad" => 1,
                "ms-word" => 2,
                "ms-excel" => 3,
                "ms-powerpoint" => 4,
                "ms-onenote" => 5,
                "ms-outlook" => 6,
                "calculator" => 7,
                "adobe-acrobat" => 8,
                _ => 10
            })
            .ThenBy(a => a.Name)
            .ToList();
    }

    #endregion

    #region Discovery Scanners

    private void ScanStartMenuShortcuts(Dictionary<string, OrbitApp> results, AppCategory category)
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

                        var matchedKnown = FindKnownApp(shortcutName, target, category);
                        if (matchedKnown.HasValue)
                        {
                            var (id, canonicalName, cat, _, _, procName, defaultArgs, pkgId) = matchedKnown.Value;
                            if (!results.ContainsKey(id))
                            {
                                results[id] = new OrbitApp
                                {
                                    Id = id,
                                    Name = canonicalName,
                                    Category = cat,
                                    ExecutablePath = File.Exists(target) ? target : lnk,
                                    Arguments = defaultArgs ?? args,
                                    WorkingDirectory = workDir,
                                    ProcessName = procName,
                                    IconPath = lnk,
                                    PackageId = pkgId,
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

    private void ScanAppPathsRegistry(Dictionary<string, OrbitApp> results, AppCategory category)
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

                        var matchedKnown = FindKnownApp(subKeyName, exePath, category);
                        if (matchedKnown.HasValue)
                        {
                            var (id, canonicalName, cat, _, _, procName, defaultArgs, pkgId) = matchedKnown.Value;
                            if (!results.ContainsKey(id))
                            {
                                results[id] = new OrbitApp
                                {
                                    Id = id,
                                    Name = canonicalName,
                                    Category = cat,
                                    ExecutablePath = exePath,
                                    Arguments = defaultArgs,
                                    ProcessName = procName,
                                    IconPath = exePath,
                                    PackageId = pkgId,
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

    private void ScanRegistryUninstallKeys(Dictionary<string, OrbitApp> results, AppCategory category)
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

                        var matchedKnown = FindKnownApp(displayName, displayIcon ?? installLocation ?? string.Empty, category);
                        if (matchedKnown.HasValue)
                        {
                            var (id, canonicalName, cat, exes, _, procName, defaultArgs, pkgId) = matchedKnown.Value;
                            if (!results.ContainsKey(id))
                            {
                                string exePath = ResolveExePath(displayIcon, installLocation, exes);
                                if (!string.IsNullOrWhiteSpace(exePath))
                                {
                                    results[id] = new OrbitApp
                                    {
                                        Id = id,
                                        Name = canonicalName,
                                        Category = cat,
                                        ExecutablePath = exePath,
                                        Arguments = defaultArgs,
                                        ProcessName = procName,
                                        IconPath = displayIcon ?? exePath,
                                        PackageId = pkgId,
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

    private void ScanLocalAppDataPrograms(Dictionary<string, OrbitApp> results, AppCategory category)
    {
        try
        {
            string localPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
            if (!Directory.Exists(localPrograms)) return;

            foreach (var dir in Directory.GetDirectories(localPrograms))
            {
                string folderName = Path.GetFileName(dir);
                var matchedKnown = FindKnownApp(folderName, string.Empty, category);
                if (matchedKnown.HasValue)
                {
                    var (id, canonicalName, cat, exes, _, procName, defaultArgs, pkgId) = matchedKnown.Value;
                    if (!results.ContainsKey(id))
                    {
                        foreach (var exeName in exes)
                        {
                            string candidate = Path.Combine(dir, exeName);
                            if (File.Exists(candidate))
                            {
                                results[id] = new OrbitApp
                                {
                                    Id = id,
                                    Name = canonicalName,
                                    Category = cat,
                                    ExecutablePath = candidate,
                                    Arguments = defaultArgs,
                                    ProcessName = procName,
                                    IconPath = candidate,
                                    PackageId = pkgId,
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

    private void ScanPackagedWindowsApps(Dictionary<string, OrbitApp> results, AppCategory category)
    {
        try
        {
            var relevantKnown = KnownApps.Where(a => a.Category == category && !string.IsNullOrWhiteSpace(a.PackageFamilyName)).ToList();
            if (relevantKnown.Count == 0) return;

            const string appModelPackagesKey = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
            using var baseKey = Registry.CurrentUser.OpenSubKey(appModelPackagesKey);
            if (baseKey == null) return;

            var subKeyNames = baseKey.GetSubKeyNames();

            foreach (var known in relevantKnown)
            {
                if (results.ContainsKey(known.Id)) continue;

                // Match package family subkeys (e.g. 5319275A.WhatsAppDesktop* or MSTeams*)
                var matchingPackages = subKeyNames.Where(k =>
                    k.StartsWith(known.PackageFamilyName!, StringComparison.OrdinalIgnoreCase)).ToList();

                string? exeTarget = null;
                string? iconTarget = null;
                string? displayName = null;

                if (known.Id.Equals("whatsapp", StringComparison.OrdinalIgnoreCase))
                {
                    iconTarget = FindUserWhatsAppLogo();
                }

                foreach (var pkgName in matchingPackages)
                {
                    try
                    {
                        using var pkgKey = baseKey.OpenSubKey(pkgName);
                        if (pkgKey == null) continue;

                        string? root = pkgKey.GetValue("PackageRootFolder") as string;
                        string? disp = pkgKey.GetValue("DisplayName") as string;
                        if (!string.IsNullOrWhiteSpace(disp) && !disp.StartsWith("@{") && displayName == null)
                        {
                            displayName = disp;
                        }

                        if (!string.IsNullOrWhiteSpace(root))
                        {
                            if (string.IsNullOrWhiteSpace(iconTarget))
                            {
                                iconTarget = FindPackageAssetIcon(root);
                            }

                            foreach (var exe in known.ExecutableMatches)
                            {
                                string candidate = Path.Combine(root, exe);
                                if (File.Exists(candidate))
                                {
                                    exeTarget = candidate;
                                    break;
                                }
                            }
                        }

                        if (exeTarget != null && !string.IsNullOrWhiteSpace(iconTarget)) break;
                    }
                    catch { }
                }

                if (exeTarget != null || matchingPackages.Count > 0)
                {
                    string aumid = known.Id switch
                    {
                        "whatsapp" => "5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App",
                        "ms-teams" => "MSTeams_8wekyb3d8bbwe!MSTeams",
                        "calculator" => "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App",
                        "notepad" => "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App",
                        "ms-outlook" => "Microsoft.OutlookForWindows_8wekyb3d8bbwe!Microsoft.OutlookforWindows",
                        _ => $"{known.PackageFamilyName}!App"
                    };

                    results[known.Id] = new OrbitApp
                    {
                        Id = known.Id,
                        Name = !string.IsNullOrWhiteSpace(displayName) ? displayName : known.CanonicalName,
                        Category = known.Category,
                        ExecutablePath = $"shell:AppsFolder\\{aumid}",
                        Arguments = null,
                        ProcessName = known.ProcessName,
                        IconPath = iconTarget ?? exeTarget,
                        PackageId = aumid,
                        IsInstalled = true
                    };
                }
            }
        }
        catch (Exception ex)
        {
            OrbitLogger.Warn(LogCategory, $"Packaged apps scan warning: {ex.Message}");
        }
    }

    #endregion

    #region Helpers & Fallbacks

    private void EnsureGoogleMeetExists(Dictionary<string, OrbitApp> results)
    {
        if (results.ContainsKey("google-meet")) return;

        string chromePath = FindChromeOrEdgePath();
        if (!string.IsNullOrWhiteSpace(chromePath))
        {
            results["google-meet"] = new OrbitApp
            {
                Id = "google-meet",
                Name = "Google Meet",
                Category = AppCategory.Social,
                ExecutablePath = chromePath,
                Arguments = "https://meet.google.com/",
                WebUrl = "https://meet.google.com/",
                ProcessName = Path.GetFileNameWithoutExtension(chromePath),
                IsInstalled = true,
                Icon = CreateGoogleMeetIcon()
            };
        }
    }

    private void EnsureBuiltinWorkApps(Dictionary<string, OrbitApp> results)
    {
        // 1. Notepad
        if (!results.ContainsKey("notepad"))
        {
            string[] notepadPaths =
            [
                @"C:\Windows\System32\notepad.exe",
                @"C:\Windows\notepad.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\notepad.exe")
            ];
            string np = notepadPaths.FirstOrDefault(File.Exists) ?? @"C:\Windows\System32\notepad.exe";
            if (File.Exists(np))
            {
                results["notepad"] = new OrbitApp
                {
                    Id = "notepad",
                    Name = "Notepad",
                    Category = AppCategory.Work,
                    ExecutablePath = np,
                    ProcessName = "notepad",
                    IconPath = np,
                    IsInstalled = true
                };
            }
        }

        // 2. Calculator
        if (!results.ContainsKey("calculator"))
        {
            string calc = @"C:\Windows\System32\calc.exe";
            if (File.Exists(calc))
            {
                results["calculator"] = new OrbitApp
                {
                    Id = "calculator",
                    Name = "Calculator",
                    Category = AppCategory.Work,
                    ExecutablePath = calc,
                    ProcessName = "CalculatorApp",
                    IconPath = calc,
                    IsInstalled = true
                };
            }
        }
    }

    private string FindChromeOrEdgePath()
    {
        string[] candidates =
        [
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe"),
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe"
        ];
        return candidates.FirstOrDefault(File.Exists) ?? "chrome.exe";
    }

    private static (string Id, string CanonicalName, AppCategory Category, string[] ExecutableMatches, string[] NameMatches, string ProcessName, string? DefaultArgs, string? PackageFamilyName)?
        FindKnownApp(string name, string targetOrPath, AppCategory category)
    {
        string normName = name.ToLowerInvariant();
        string normTarget = targetOrPath.ToLowerInvariant();

        // 1. Check FileVersionInfo if path points to an existing file
        if (File.Exists(targetOrPath))
        {
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(targetOrPath);
                string prod = (vi.ProductName ?? string.Empty).ToLowerInvariant();
                string desc = (vi.FileDescription ?? string.Empty).ToLowerInvariant();

                if (category == AppCategory.Developer)
                {
                    if (prod.Contains("antigravity ide") || desc.Contains("antigravity ide"))
                    {
                        return KnownApps.FirstOrDefault(e => e.Id == "antigravity-ide");
                    }
                    else if ((prod.Contains("antigravity") || desc.Contains("antigravity")) &&
                             !prod.Contains("ide") && !desc.Contains("ide"))
                    {
                        return KnownApps.FirstOrDefault(e => e.Id == "antigravity");
                    }
                }
            }
            catch { }
        }

        // Skip uninstallation helpers or background server daemons
        if (normName.Contains("uninstall") || normTarget.Contains("uninstall") ||
            normName.Contains("skypeserver") || normTarget.Contains("skypeserver"))
        {
            return null;
        }

        // 2. Iterate matching known apps for this category
        foreach (var entry in KnownApps.Where(a => a.Category == category))
        {
            if (entry.Id == "antigravity" && (normName.Contains("ide") || normTarget.Contains("ide")))
            {
                continue;
            }
            if (entry.Id == "antigravity-ide" && !normName.Contains("ide") && !normTarget.Contains("ide"))
            {
                continue;
            }

            // Match by executable file name
            foreach (var exe in entry.ExecutableMatches)
            {
                string normExe = exe.ToLowerInvariant();
                if (normTarget.EndsWith(normExe) || normTarget.Contains("\\" + normExe) || normName.EndsWith(normExe))
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
        Dictionary<string, OrbitApp> results,
        string id,
        string canonicalName,
        AppCategory category,
        string exePattern,
        string procName,
        string? defaultArgs,
        string? pkgId)
    {
        if (results.ContainsKey(id)) return;

        var fallback = FallbackDefaultApp(id, canonicalName, category, exePattern, procName, defaultArgs, pkgId);
        results[id] = fallback;
    }

    private OrbitApp FallbackDefaultApp(string id, string name, AppCategory category, string exePattern, string procName, string? defaultArgs, string? pkgId)
    {
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

        return new OrbitApp
        {
            Id = id,
            Name = name,
            Category = category,
            ExecutablePath = resolvedPath,
            Arguments = defaultArgs,
            ProcessName = procName,
            IconPath = resolvedPath,
            PackageId = pkgId,
            IsInstalled = exists,
            Icon = exists ? _iconExtractor.GetOrExtractIcon(resolvedPath) : null
        };
    }

    /// <summary>
    /// Generates a crisp official-geometry Google Meet vector icon.
    /// </summary>
    public static ImageSource CreateGoogleMeetIcon()
    {
        var group = new DrawingGroup();

        // 1. Camera body (Google green #00AC47)
        var bodyGeometry = new RectangleGeometry(new Rect(2, 6, 17, 18), 4, 4);
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0xAC, 0x47)), null, bodyGeometry));

        // 2. Camera top-left notch (darker green #00832D)
        var notchGeometry = new RectangleGeometry(new Rect(2, 6, 6, 6), 2, 2);
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x83, 0x2D)), null, notchGeometry));

        // 3. Blue lens top segment (#2684FC)
        var blueLens = new PathGeometry();
        var blueFig = new PathFigure { StartPoint = new Point(19, 11), IsClosed = true };
        blueFig.Segments.Add(new LineSegment(new Point(28, 6), false));
        blueFig.Segments.Add(new LineSegment(new Point(28, 11), false));
        blueLens.Figures.Add(blueFig);
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x26, 0x84, 0xFC)), null, blueLens));

        // 4. Yellow lens center segment (#FFBA00)
        var yellowLens = new PathGeometry();
        var yellowFig = new PathFigure { StartPoint = new Point(19, 11), IsClosed = true };
        yellowFig.Segments.Add(new LineSegment(new Point(28, 11), false));
        yellowFig.Segments.Add(new LineSegment(new Point(28, 19), false));
        yellowFig.Segments.Add(new LineSegment(new Point(19, 19), false));
        yellowLens.Figures.Add(yellowFig);
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xBA, 0x00)), null, yellowLens));

        // 5. Red lens bottom segment (#EA4335)
        var redLens = new PathGeometry();
        var redFig = new PathFigure { StartPoint = new Point(19, 19), IsClosed = true };
        redFig.Segments.Add(new LineSegment(new Point(28, 19), false));
        redFig.Segments.Add(new LineSegment(new Point(28, 24), false));
        redLens.Figures.Add(redFig);
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEA, 0x43, 0x35)), null, redLens));

        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    /// <summary>
    /// Searches for custom user-supplied WhatsApp logo files (e.g. in Saved Pictures or Pictures).
    /// </summary>
    private static string? FindUserWhatsAppLogo()
    {
        try
        {
            // 1. Check user's Saved Pictures folder dynamically via SpecialFolder
            string myPictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (!string.IsNullOrWhiteSpace(myPictures) && Directory.Exists(myPictures))
            {
                string savedPicLogo = Path.Combine(myPictures, "Saved Pictures", "whatsapp logo.png");
                if (File.Exists(savedPicLogo)) return savedPicLogo;

                string savedPicAlt = Path.Combine(myPictures, "Saved Pictures", "whatsapp.png");
                if (File.Exists(savedPicAlt)) return savedPicAlt;

                string picLogo = Path.Combine(myPictures, "whatsapp logo.png");
                if (File.Exists(picLogo)) return picLogo;
            }

            // 2. Check localized OneDrive paths and common directories
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] candidateDirs = [
                Path.Combine(userProfile, "OneDrive", "사진", "Saved Pictures"),
                Path.Combine(userProfile, "OneDrive", "Pictures", "Saved Pictures"),
                Path.Combine(userProfile, "Pictures", "Saved Pictures")
            ];

            foreach (var dir in candidateDirs)
            {
                if (Directory.Exists(dir))
                {
                    string file1 = Path.Combine(dir, "whatsapp logo.png");
                    if (File.Exists(file1)) return file1;
                    string file2 = Path.Combine(dir, "whatsapp.png");
                    if (File.Exists(file2)) return file2;
                }
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Locates the official application logo PNG inside a WindowsApps package root folder.
    /// </summary>
    private static string? FindPackageAssetIcon(string packageRoot)
    {
        try
        {
            string assetsDir = Path.Combine(packageRoot, "Assets");
            if (Directory.Exists(assetsDir))
            {
                string[] preferredNames = [
                    "AppList.targetsize-256.png",
                    "AppList.targetsize-96.png",
                    "AppList.targetsize-64.png",
                    "AppList.targetsize-48.png",
                    "AppList.scale-200.png",
                    "AppList.png",
                    "StoreLogo.scale-200.png",
                    "StoreLogo.png",
                    "Logo.png",
                    "Square150x150Logo.scale-200.png",
                    "Square44x44Logo.targetsize-256.png",
                    "Square44x44Logo.targetsize-48.png"
                ];

                foreach (var name in preferredNames)
                {
                    string candidate = Path.Combine(assetsDir, name);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }

                // Fallback to largest AppList*.png
                var anyAppList = Directory.GetFiles(assetsDir, "AppList*.png");
                if (anyAppList.Length > 0)
                {
                    return anyAppList.OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
                }
            }
        }
        catch { }

        return null;
    }

    #endregion
}

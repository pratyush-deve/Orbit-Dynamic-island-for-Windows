using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Core;
using Orbit.Models;
using Orbit.Services.DevWorkspace;

namespace Orbit.Services.Commands.Handlers;

public sealed class AppLaunchHandler : ICommandHandler
{
    private const string LogCategory = "APP_LAUNCH_HANDLER";
    private readonly AppRegistry _appRegistry;
    private readonly IAppDiscoveryService _discoveryService;

    public CommandType SupportedType => CommandType.LaunchApp;

    public AppLaunchHandler(AppRegistry appRegistry, IAppDiscoveryService discoveryService)
    {
        _appRegistry = appRegistry;
        _discoveryService = discoveryService;
    }

    public async Task<CommandResult> ExecuteAsync(CommandIntent intent, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(intent.Target))
        {
            return new CommandResult(false, "No application specified", intent);
        }

        string appName = intent.Target.Trim();
        OrbitLogger.Info(LogCategory, $"Attempting to launch application: '{appName}'");

        try
        {
            // Discover installed apps across developer, social, and work categories
            var devApps = await _discoveryService.DiscoverAppsByCategoryAsync(AppCategory.Developer);
            var socialApps = await _discoveryService.DiscoverAppsByCategoryAsync(AppCategory.Social);
            var workApps = await _discoveryService.DiscoverAppsByCategoryAsync(AppCategory.Work);

            var allDiscovered = devApps.Concat(socialApps).Concat(workApps);

            var target = _appRegistry.ResolveApp(appName, allDiscovered);
            if (target == null)
            {
                var candidate = _appRegistry.SuggestCandidate(appName, allDiscovered);
                OrbitLogger.Warn(LogCategory, $"Application not directly recognized: '{appName}'. Candidate: '{candidate?.DisplayName}'");

                string prompt = candidate != null
                    ? $"Did you mean {candidate.DisplayName}?"
                    : $"I couldn't find an app called \"{appName}\". Which app would you like me to open?";

                return new CommandResult(false, prompt, intent);
            }

            // Launch safe target
            if (target.ExecutableOrUri.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
            {
                var psi = new ProcessStartInfo("explorer.exe", target.ExecutableOrUri)
                {
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            else if (target.ExecutableOrUri.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase) ||
                     target.ExecutableOrUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                     target.ExecutableOrUri.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = target.ExecutableOrUri,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            else
            {
                var psi = new ProcessStartInfo
                {
                    FileName = target.ExecutableOrUri,
                    Arguments = target.Arguments ?? string.Empty,
                    WorkingDirectory = !string.IsNullOrWhiteSpace(target.WorkingDirectory) && Directory.Exists(target.WorkingDirectory)
                        ? target.WorkingDirectory
                        : (File.Exists(target.ExecutableOrUri) ? Path.GetDirectoryName(target.ExecutableOrUri) ?? string.Empty : string.Empty),
                    UseShellExecute = true
                };
                Process.Start(psi);
            }

            OrbitLogger.Info(LogCategory, $"Successfully launched {target.DisplayName} ({target.ExecutableOrUri})");
            return new CommandResult(true, $"Opening {target.DisplayName}...", intent, target.ExecutableOrUri);
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, $"Failed to launch '{appName}'", ex);
            return new CommandResult(false, $"Failed to open {appName}", intent, ex.Message);
        }
    }
}


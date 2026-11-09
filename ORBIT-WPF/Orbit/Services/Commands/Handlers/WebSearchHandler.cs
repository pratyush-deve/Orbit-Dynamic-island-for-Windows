using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Core;

namespace Orbit.Services.Commands.Handlers;

public sealed class WebSearchHandler : ICommandHandler
{
    private const string LogCategory = "WEB_SEARCH_HANDLER";
    private const string ChromePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";

    public CommandType SupportedType => CommandType.WebSearch;

    public Task<CommandResult> ExecuteAsync(CommandIntent intent, CancellationToken cancellationToken = default)
    {
        string query = !string.IsNullOrWhiteSpace(intent.Query) ? intent.Query.Trim() : (intent.Target?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult(new CommandResult(false, "No search query provided", intent));
        }

        // If a specific site scope is present, build using SiteSearchRegistry
        string url;
        string providerName = "Google";
        if (!string.IsNullOrWhiteSpace(intent.Scope) && !intent.Scope.Equals("google", StringComparison.OrdinalIgnoreCase))
        {
            var provider = SiteSearchRegistry.FindProvider(intent.Scope);
            providerName = provider?.DisplayName ?? intent.Scope;
            url = SiteSearchRegistry.BuildSearchUrl(intent.Scope, query);
        }
        else
        {
            url = $"https://www.google.com/search?q={Uri.EscapeDataString(query)}";
        }

        OrbitLogger.Info(LogCategory, $"Performing web search ({providerName}): query='{query}', url='{url}'");

        try
        {
            // Prefer Google Chrome if installed, otherwise fall back to system default browser
            ProcessStartInfo psi;
            if (File.Exists(ChromePath))
            {
                psi = new ProcessStartInfo
                {
                    FileName = ChromePath,
                    Arguments = $"\"{url}\"",
                    UseShellExecute = true
                };
            }
            else
            {
                psi = new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                };
            }

            Process.Start(psi);

            return Task.FromResult(new CommandResult(true, $"Searching {providerName}: \"{query}\"", intent, url));
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, $"Failed to open web search for '{query}'", ex);
            return Task.FromResult(new CommandResult(false, "Failed to perform web search", intent, ex.Message));
        }
    }
}


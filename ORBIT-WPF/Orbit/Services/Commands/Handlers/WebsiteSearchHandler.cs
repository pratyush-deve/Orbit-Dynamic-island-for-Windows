using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Core;

namespace Orbit.Services.Commands.Handlers;

/// <summary>
/// Handles targeted in-site searches (e.g. Amazon, Myntra, Flipkart, YouTube, GitHub, Reddit, Wikipedia,
/// or unknown websites via Google site: search) and launches the browser directly to the catalog/results URL.
/// </summary>
public sealed class WebsiteSearchHandler : ICommandHandler
{
    private const string LogCategory = "WEBSITE_SEARCH_HANDLER";
    private const string ChromePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";

    public CommandType SupportedType => CommandType.WebsiteSearch;

    public Task<CommandResult> ExecuteAsync(CommandIntent intent, CancellationToken cancellationToken = default)
    {
        string site = intent.Scope?.Trim() ?? string.Empty;
        string query = !string.IsNullOrWhiteSpace(intent.Query) ? intent.Query.Trim() : (intent.Target?.Trim() ?? string.Empty);

        if (string.IsNullOrWhiteSpace(site))
        {
            // If scope is missing, check target as site
            site = intent.Target?.Trim() ?? "google";
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult(new CommandResult(false, "No search query provided", intent));
        }

        var provider = SiteSearchRegistry.FindProvider(site);
        string displayName = provider?.DisplayName ?? site;
        string url = SiteSearchRegistry.BuildSearchUrl(site, query);

        OrbitLogger.Info(LogCategory, $"Executing targeted website search: site='{site}' ({displayName}), query='{query}', url='{url}'");

        try
        {
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

            return Task.FromResult(new CommandResult(true, $"Searching {displayName}: \"{query}\"", intent, url));
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, $"Failed to open website search for '{query}' on '{site}'", ex);
            return Task.FromResult(new CommandResult(false, $"Failed to search {displayName}", intent, ex.Message));
        }
    }
}


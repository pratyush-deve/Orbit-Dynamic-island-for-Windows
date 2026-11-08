using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Core;

namespace Orbit.Services.Commands.Handlers;

public sealed class WebSearchHandler : ICommandHandler
{
    private const string LogCategory = "WEB_SEARCH_HANDLER";

    public CommandType SupportedType => CommandType.WebSearch;

    public Task<CommandResult> ExecuteAsync(CommandIntent intent, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(intent.Target))
        {
            return Task.FromResult(new CommandResult(false, "No search query provided", intent));
        }

        string query = intent.Target.Trim();
        OrbitLogger.Info(LogCategory, $"Performing web search for: '{query}'");

        try
        {
            string url = $"https://www.google.com/search?q={Uri.EscapeDataString(query)}";
            var psi = new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            Process.Start(psi);

            return Task.FromResult(new CommandResult(true, $"Searching Google: \"{query}\"", intent, url));
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, $"Failed to open web search for '{query}'", ex);
            return Task.FromResult(new CommandResult(false, "Failed to perform web search", intent, ex.Message));
        }
    }
}


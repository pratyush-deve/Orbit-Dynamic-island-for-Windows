using Orbit.Core;
using Orbit.Models;

namespace Orbit.Services.AiObserver;

public interface IAntigravitySessionResolver
{
    AntigravityAppType ResolveOriginatingApp(RawConversationSummary summary, IAntigravityProcessMonitor processMonitor);
}

public sealed class AntigravitySessionResolver : IAntigravitySessionResolver
{
    private const string LogCategory = "SESSION_RESOLVER";

    public AntigravityAppType ResolveOriginatingApp(RawConversationSummary summary, IAntigravityProcessMonitor processMonitor)
    {
        bool ideRunning = processMonitor.IsAntigravityIdeRunning;
        bool agRunning = processMonitor.IsAntigravityRunning;
        var ideWorkspaces = processMonitor.IdeWorkspaceDirectories;

        // 1. Direct workspace match with active Antigravity IDE workspaces (from agy --add-dir)
        if (summary.WorkspacePaths.Count > 0 && ideWorkspaces.Count > 0)
        {
            foreach (var wp in summary.WorkspacePaths)
            {
                string normWp = NormalizePath(wp);
                foreach (var ideWp in ideWorkspaces)
                {
                    string normIdeWp = NormalizePath(ideWp);
                    if (string.Equals(normWp, normIdeWp, StringComparison.OrdinalIgnoreCase) ||
                        normWp.StartsWith(normIdeWp, StringComparison.OrdinalIgnoreCase) ||
                        normIdeWp.StartsWith(normWp, StringComparison.OrdinalIgnoreCase))
                    {
                        return AntigravityAppType.AntigravityIde;
                    }
                }
            }
        }

        // 2. Process presence elimination
        if (ideRunning && !agRunning)
        {
            return AntigravityAppType.AntigravityIde;
        }

        if (agRunning && !ideRunning)
        {
            return AntigravityAppType.Antigravity;
        }

        // 3. If both are running, examine workspace path heuristics
        if (ideRunning && agRunning)
        {
            // If IDE has open workspaces and this conversation does NOT match any of them, it belongs to regular Antigravity
            if (ideWorkspaces.Count > 0 && summary.WorkspacePaths.Count > 0)
            {
                bool matchesAnyIde = false;
                foreach (var wp in summary.WorkspacePaths)
                {
                    string normWp = NormalizePath(wp);
                    if (ideWorkspaces.Any(iw => string.Equals(normWp, NormalizePath(iw), StringComparison.OrdinalIgnoreCase)))
                    {
                        matchesAnyIde = true;
                        break;
                    }
                }

                if (!matchesAnyIde)
                {
                    return AntigravityAppType.Antigravity;
                }
                else
                {
                    return AntigravityAppType.AntigravityIde;
                }
            }
        }

        // 4. Default to Unknown if ambiguous
        return AntigravityAppType.Unknown;
    }

    private static string NormalizePath(string path)
    {
        return path.Trim().TrimEnd('\\', '/').ToLowerInvariant();
    }
}

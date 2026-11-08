using System.Threading;
using System.Threading.Tasks;

namespace Orbit.Services.Commands;

/// <summary>
/// Service interface for executing parsed voice command intents.
/// </summary>
public interface IVoiceCommandExecutor
{
    Task<CommandResult> ExecuteAsync(CommandIntent intent, CancellationToken cancellationToken = default);
}

/// <summary>
/// Backward-compatible alias for IVoiceCommandExecutor.
/// </summary>
public interface ICommandExecutor : IVoiceCommandExecutor
{
}

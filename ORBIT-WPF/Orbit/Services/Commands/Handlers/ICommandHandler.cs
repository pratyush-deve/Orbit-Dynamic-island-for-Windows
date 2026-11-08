using System.Threading;
using System.Threading.Tasks;

namespace Orbit.Services.Commands.Handlers;

/// <summary>
/// Interface for a specific voice command intent handler.
/// </summary>
public interface ICommandHandler
{
    CommandType SupportedType { get; }
    Task<CommandResult> ExecuteAsync(CommandIntent intent, CancellationToken cancellationToken = default);
}


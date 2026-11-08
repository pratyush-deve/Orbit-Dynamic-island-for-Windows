using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Core;
using Orbit.Services.Commands.Handlers;

namespace Orbit.Services.Commands;

/// <summary>
/// Service coordinating the execution of parsed voice command intents
/// through registered specialized command handlers.
/// </summary>
public sealed class CommandExecutionService : IVoiceCommandExecutor, ICommandExecutor
{
    private const string LogCategory = "COMMAND_EXECUTOR";
    private readonly Dictionary<CommandType, ICommandHandler> _handlers = new();

    public CommandExecutionService(IEnumerable<ICommandHandler> handlers)
    {
        foreach (var handler in handlers)
        {
            _handlers[handler.SupportedType] = handler;
        }
    }

    public async Task<CommandResult> ExecuteAsync(CommandIntent intent, CancellationToken cancellationToken = default)
    {
        if (intent.Type == CommandType.Unknown)
        {
            OrbitLogger.Info(LogCategory, $"Execution skipped: unknown intent '{intent.RawText}'.");
            return new CommandResult(false, "Command not recognized", intent);
        }

        if (!_handlers.TryGetValue(intent.Type, out var handler))
        {
            OrbitLogger.Warn(LogCategory, $"No handler registered for command intent type: {intent.Type}");
            return new CommandResult(false, $"Unsupported command: {intent.Type}", intent);
        }

        OrbitLogger.Info(LogCategory, $"Dispatching command intent: {intent.Type} to {handler.GetType().Name}");

        try
        {
            return await handler.ExecuteAsync(intent, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            OrbitLogger.Info(LogCategory, $"Command execution cancelled for intent {intent.Type}.");
            return new CommandResult(false, "Command cancelled", intent);
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, $"Unhandled error during execution of intent {intent.Type}", ex);
            return new CommandResult(false, "Execution failed", intent, ex.Message);
        }
    }
}

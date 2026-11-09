using System;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Core;
using Orbit.Services.Ai;
using Orbit.Services.CodeGeneration;

namespace Orbit.Services.Commands.Handlers;

public sealed class GenerateCodeHandler : ICommandHandler
{
    private const string LogCategory = "GENERATE_CODE_HANDLER";
    private readonly ICodeGenerationService _codeGenService;
    private readonly IOllamaClient _ollamaClient;

    public CommandType SupportedType => CommandType.GenerateCode;

    public GenerateCodeHandler(ICodeGenerationService codeGenService, IOllamaClient ollamaClient)
    {
        _codeGenService = codeGenService ?? throw new ArgumentNullException(nameof(codeGenService));
        _ollamaClient = ollamaClient ?? throw new ArgumentNullException(nameof(ollamaClient));
    }

    public async Task<CommandResult> ExecuteAsync(CommandIntent intent, CancellationToken cancellationToken = default)
    {
        var data = intent.CodeGenData;
        if (data == null)
        {
            OrbitLogger.Warn(LogCategory, "ExecuteAsync called without CodeGenData.");
            return new CommandResult(false, "Missing code generation metadata.", intent);
        }

        if (string.IsNullOrEmpty(data.ResolvedFolderPath) || string.IsNullOrEmpty(data.ProposedFileName))
        {
            OrbitLogger.Warn(LogCategory, "ExecuteAsync called before destination or filename resolution.");
            return new CommandResult(false, "Destination or filename not confirmed.", intent);
        }

        OrbitLogger.Info(LogCategory, $"Executing code generation for '{data.ProposedFileName}' in '{data.ResolvedFolderPath}'");

        var result = await _codeGenService.GenerateAndWriteFileAsync(
            data.ResolvedFolderPath,
            data.ProposedFileName,
            data.DisplayLocation ?? data.DestinationFolder,
            data.Purpose,
            data.Language,
            _ollamaClient,
            cancellationToken);

        return new CommandResult(result.Success, result.Message, intent, result.FilePath);
    }
}


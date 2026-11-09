namespace Orbit.Services.CodeGeneration;

/// <summary>
/// Languages supported for voice-driven code generation and file creation.
/// Strictly limited to C and Python.
/// </summary>
public enum CodeLanguage
{
    Unknown,
    C,
    Python,
    Unsupported
}

/// <summary>
/// Workflow stages for the multi-step voice code generation confirmation pipeline.
/// </summary>
public enum CodeGenerationStage
{
    Initial,
    AwaitingLanguageClarification,
    AwaitingDestinationConfirmation,
    AwaitingFolderCreationConfirmation,
    AwaitingFileNameConfirmation,
    AwaitingOverwriteConfirmation,
    ReadyToExecute
}


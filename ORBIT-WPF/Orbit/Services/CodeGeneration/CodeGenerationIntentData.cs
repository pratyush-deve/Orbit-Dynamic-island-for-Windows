namespace Orbit.Services.CodeGeneration;

/// <summary>
/// Structured state payload for voice-driven code generation requests.
/// </summary>
public record CodeGenerationIntentData(
    CodeLanguage Language,
    string Purpose,
    string DestinationFolder,
    string? ProposedFileName = null,
    string? UnsupportedLanguageName = null,
    CodeGenerationStage Stage = CodeGenerationStage.Initial,
    string? ResolvedFolderPath = null,
    string? DisplayLocation = null,
    bool OverwriteApproved = false
);


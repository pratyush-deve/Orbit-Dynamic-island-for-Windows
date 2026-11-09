namespace Orbit.Services.CodeGeneration;

/// <summary>
/// Structured result returned after safe file generation and verification.
/// </summary>
public record CodeGenerationResult(
    bool Success,
    string Message,
    string? FilePath = null,
    string? FileName = null,
    string? DisplayLocation = null,
    string? Purpose = null,
    string? Error = null
);


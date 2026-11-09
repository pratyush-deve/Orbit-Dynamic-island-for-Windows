using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Services.Ai;

namespace Orbit.Services.CodeGeneration;

public interface ICodeGenerationService
{
    /// <summary>
    /// Parses and classifies the requested programming language (strictly C or Python).
    /// Identifies unsupported languages.
    /// </summary>
    CodeLanguage ParseLanguage(string text, out string? unsupportedName);

    /// <summary>
    /// Retrieves the allowed root directories (Desktop and Documents, including OneDrive redirections).
    /// </summary>
    IReadOnlyList<string> GetAllowedRootFolders();

    /// <summary>
    /// Validates that a requested destination is securely within Desktop or Documents.
    /// Prevents path traversal, UNC, unauthorized drives, and returns canonical path and display name.
    /// </summary>
    (bool IsValid, string? ResolvedPath, string? DisplayName, string? Error) ValidateAndResolveDestination(string rawDestination);

    /// <summary>
    /// Validates a file name for invalid characters, Windows reserved device names, and language extension match.
    /// </summary>
    (bool IsValid, string? SanitizedName, string? Error) ValidateFileName(string fileName, CodeLanguage language);

    /// <summary>
    /// Generates a reasonable, clean snake_case file name based on program purpose and language.
    /// </summary>
    string SuggestFileName(string purpose, CodeLanguage language);

    /// <summary>
    /// Checks if a directory currently exists on disk.
    /// </summary>
    bool DirectoryExists(string path);

    /// <summary>
    /// Safely creates a directory within authorized boundaries.
    /// </summary>
    bool CreateDirectorySafely(string path);

    /// <summary>
    /// Checks if a file currently exists on disk.
    /// </summary>
    bool FileExists(string path);

    /// <summary>
    /// Generates a numbered alternative file name when a file already exists (e.g. name_2.c).
    /// </summary>
    string GetNextAvailableFileName(string folderPath, string fileName);

    /// <summary>
    /// Prompts Qwen for clean source code, writes UTF-8 to disk, verifies creation, and returns result summary.
    /// </summary>
    Task<CodeGenerationResult> GenerateAndWriteFileAsync(
        string folderPath,
        string fileName,
        string displayLocation,
        string purpose,
        CodeLanguage language,
        IOllamaClient ollamaClient,
        CancellationToken cancellationToken = default);
}


using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Core;
using Orbit.Services.Ai;

namespace Orbit.Services.CodeGeneration;

public sealed class CodeGenerationService : ICodeGenerationService
{
    private const string LogCategory = "CODE_GEN_SERVICE";

    private static readonly HashSet<string> WindowsReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "for", "in", "on", "of", "and", "or", "that", "which", "as", "into", "to",
        "program", "script", "code", "file", "write", "create", "generate", "make", "build", "save", "please",
        "convert", "converting", "calculate", "calculating", "calculator", "compute", "computing", "implement", "implementing",
        "number", "numbers", "value", "values", "folder", "named", "is", "it", "with", "from", "should"
    };

    public CodeLanguage ParseLanguage(string text, out string? unsupportedName)
    {
        unsupportedName = null;
        if (string.IsNullOrWhiteSpace(text)) return CodeLanguage.Unknown;

        string normalized = text.ToLowerInvariant();

        // Check unsupported languages first to prevent substring or misclassification
        var unsupportedMatch = Regex.Match(
            normalized,
            @"(?:\bc\+\+|\bcpp\b|\bc#|\bcsharp\b|\bc\s+sharp\b|\bjava\b|\bkotlin\b|\bjavascript\b|\bjs\b|\btypescript\b|\bts\b|\brust\b|\bgolang\b|\bgo\b|\bruby\b|\bphp\b|\bswift\b|\bhtml\b|\bcss\b)",
            RegexOptions.IgnoreCase);

        if (unsupportedMatch.Success)
        {
            unsupportedName = unsupportedMatch.Value.ToUpperInvariant();
            return CodeLanguage.Unsupported;
        }

        // Strict C match (ensure not C++ or C#)
        if (Regex.IsMatch(normalized, @"(?:\bc\b|\bc\s+program|\bc\s+file|\bc\s+code|\bc\s+script)(?!\+|\#)", RegexOptions.IgnoreCase))
        {
            return CodeLanguage.C;
        }

        // Python match
        if (Regex.IsMatch(normalized, @"\b(?:python|py)\b", RegexOptions.IgnoreCase))
        {
            return CodeLanguage.Python;
        }

        return CodeLanguage.Unknown;
    }

    public IReadOnlyList<string> GetAllowedRootFolders()
    {
        var roots = new List<string>();

        // 1. Desktop
        string specialDesktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (!string.IsNullOrWhiteSpace(specialDesktop) && Directory.Exists(specialDesktop))
        {
            roots.Add(Path.GetFullPath(specialDesktop));
        }

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            string oneDriveDesktop = Path.Combine(userProfile, "OneDrive", "Desktop");
            if (Directory.Exists(oneDriveDesktop))
            {
                string full = Path.GetFullPath(oneDriveDesktop);
                if (!roots.Contains(full, StringComparer.OrdinalIgnoreCase))
                {
                    roots.Add(full);
                }
            }
        }

        // 2. Documents
        string specialDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(specialDocs) && Directory.Exists(specialDocs))
        {
            string full = Path.GetFullPath(specialDocs);
            if (!roots.Contains(full, StringComparer.OrdinalIgnoreCase))
            {
                roots.Add(full);
            }
        }

        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            // Standard Documents under user profile
            string userDocs = Path.Combine(userProfile, "Documents");
            if (Directory.Exists(userDocs))
            {
                string full = Path.GetFullPath(userDocs);
                if (!roots.Contains(full, StringComparer.OrdinalIgnoreCase))
                {
                    roots.Add(full);
                }
            }

            // OneDrive Documents (English & Localized names like Korean '문서')
            string oneDriveDocs = Path.Combine(userProfile, "OneDrive", "Documents");
            if (Directory.Exists(oneDriveDocs))
            {
                string full = Path.GetFullPath(oneDriveDocs);
                if (!roots.Contains(full, StringComparer.OrdinalIgnoreCase))
                {
                    roots.Add(full);
                }
            }

            string oneDriveDocsKr = Path.Combine(userProfile, "OneDrive", "문서");
            if (Directory.Exists(oneDriveDocsKr))
            {
                string full = Path.GetFullPath(oneDriveDocsKr);
                if (!roots.Contains(full, StringComparer.OrdinalIgnoreCase))
                {
                    roots.Add(full);
                }
            }
        }

        return roots;
    }

    public (bool IsValid, string? ResolvedPath, string? DisplayName, string? Error) ValidateAndResolveDestination(string rawDestination)
    {
        if (string.IsNullOrWhiteSpace(rawDestination))
        {
            rawDestination = "Desktop";
        }

        string normalized = rawDestination.Trim();

        // Security check: reject explicit path traversal patterns
        if (normalized.Contains("..") || normalized.StartsWith(@"\\") || normalized.StartsWith("//"))
        {
            return (false, null, null, "Path traversal or network UNC paths are strictly forbidden.");
        }

        var allowedRoots = GetAllowedRootFolders();
        if (allowedRoots.Count == 0)
        {
            return (false, null, null, "No allowed root folders could be identified on this system.");
        }

        // Identify intended base root (Desktop vs Documents)
        bool isDocuments = normalized.Contains("document", StringComparison.OrdinalIgnoreCase) ||
                           normalized.Contains("docs", StringComparison.OrdinalIgnoreCase) ||
                           normalized.Contains("문서", StringComparison.OrdinalIgnoreCase);

        string rootDisplayName = isDocuments ? "Documents" : "Desktop";

        // Find primary root directory for chosen base
        string? matchedRoot = null;
        if (isDocuments)
        {
            matchedRoot = allowedRoots.FirstOrDefault(r =>
                r.Contains("document", StringComparison.OrdinalIgnoreCase) ||
                r.Contains("문서", StringComparison.OrdinalIgnoreCase)) ?? allowedRoots.FirstOrDefault();
        }
        else
        {
            matchedRoot = allowedRoots.FirstOrDefault(r =>
                r.Contains("desktop", StringComparison.OrdinalIgnoreCase)) ?? allowedRoots.FirstOrDefault();
        }

        if (string.IsNullOrEmpty(matchedRoot))
        {
            return (false, null, null, $"Could not resolve allowed base root for {rootDisplayName}.");
        }

        // Extract any relative subfolder requested (e.g. "Test", "college/assignments")
        string relativeSubPath = ExtractSubPath(normalized, isDocuments);

        string combined;
        try
        {
            combined = string.IsNullOrEmpty(relativeSubPath)
                ? matchedRoot
                : Path.Combine(matchedRoot, relativeSubPath);

            combined = Path.GetFullPath(combined);
        }
        catch (Exception ex)
        {
            return (false, null, null, $"Invalid destination path: {ex.Message}");
        }

        // Canonical verification: Must strictly start with one of the allowed roots
        bool isWithinAllowed = allowedRoots.Any(root =>
            combined.Equals(root, StringComparison.OrdinalIgnoreCase) ||
            combined.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

        if (!isWithinAllowed)
        {
            return (false, null, null, "Destination path escapes allowed Desktop and Documents directories.");
        }

        string displayLocation = string.IsNullOrEmpty(relativeSubPath)
            ? rootDisplayName
            : $"{rootDisplayName} → {relativeSubPath.Replace(Path.DirectorySeparatorChar, '→').Replace(Path.AltDirectorySeparatorChar, '→')}";

        return (true, combined, displayLocation, null);
    }

    private static string ExtractSubPath(string raw, bool isDocuments)
    {
        string text = raw.Trim();

        // Strip leading base word
        string pattern = isDocuments
            ? @"^(?:documents?|docs?|문서)\s*(?:folder|directory)?(?:\s*(?:in|on|/|\\)\s*|\s+)?(?:\s*folder\s*)?"
            : @"^(?:desktop)\s*(?:folder|directory)?(?:\s*(?:in|on|/|\\)\s*|\s+)?(?:\s*folder\s*)?";

        string remainder = Regex.Replace(text, pattern, "", RegexOptions.IgnoreCase).Trim();

        // 1. "in a folder named as test", "folder named test", "folder called test"
        var matchNamedFolder = Regex.Match(text, @"(?:(?:in|inside)\s+)?(?:(?:a|the)\s+)?folder\s+(?:named|called)(?:\s+as)?\s+([a-zA-Z0-9_\-]+)", RegexOptions.IgnoreCase);
        if (matchNamedFolder.Success)
        {
            return matchNamedFolder.Groups[1].Value.Trim();
        }

        // 2. If the pattern was "in the Test folder on Desktop"
        var matchFolderOnRoot = Regex.Match(text, @"(?:in|inside)\s+(?:the\s+)?([a-zA-Z0-9_\- ]+?)(?:\s+folder)?\s+(?:on|in)\s+(?:my\s+|the\s+)?(?:desktop|documents)", RegexOptions.IgnoreCase);
        if (matchFolderOnRoot.Success)
        {
            remainder = matchFolderOnRoot.Groups[1].Value.Trim();
        }

        // Clean up common prefixes and suffixes
        remainder = Regex.Replace(remainder, @"^(?:in\s+|inside\s+|on\s+|the\s+|a\s+)", "", RegexOptions.IgnoreCase).Trim();
        remainder = Regex.Replace(remainder, @"\s+folder$", "", RegexOptions.IgnoreCase).Trim();
        remainder = remainder.Trim('/', '\\', ' ');

        return remainder;
    }

    public (bool IsValid, string? SanitizedName, string? Error) ValidateFileName(string fileName, CodeLanguage language)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return (false, null, "Filename cannot be empty.");
        }

        string name = fileName.Trim();

        // Disallow path separators inside filename
        if (name.Contains('/') || name.Contains('\\'))
        {
            return (false, null, "Filename cannot contain folder path separators.");
        }

        // Check invalid Windows filename chars
        char[] invalidChars = Path.GetInvalidFileNameChars();
        if (name.IndexOfAny(invalidChars) >= 0)
        {
            return (false, null, "Filename contains invalid Windows characters.");
        }

        string baseName = Path.GetFileNameWithoutExtension(name);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            return (false, null, "Filename must have a base name before extension.");
        }

        // Check Windows reserved device names
        if (WindowsReservedDeviceNames.Contains(baseName))
        {
            return (false, null, $"'{baseName}' is a reserved Windows device name and cannot be used.");
        }

        string expectedExt = language switch
        {
            CodeLanguage.C => ".c",
            CodeLanguage.Python => ".py",
            _ => string.Empty
        };

        if (string.IsNullOrEmpty(expectedExt))
        {
            return (false, null, "Unsupported language extension.");
        }

        // Ensure extension
        if (!name.EndsWith(expectedExt, StringComparison.OrdinalIgnoreCase))
        {
            // If user passed no extension, append it
            if (!name.Contains('.'))
            {
                name += expectedExt;
            }
            else
            {
                return (false, null, $"Filename extension must match selected language ({expectedExt}).");
            }
        }

        return (true, name, null);
    }

    public string SuggestFileName(string purpose, CodeLanguage language)
    {
        string ext = language == CodeLanguage.C ? ".c" : ".py";

        if (string.IsNullOrWhiteSpace(purpose))
        {
            return language == CodeLanguage.C ? "program.c" : "script.py";
        }

        // Clean purpose into snake_case words
        var words = Regex.Matches(purpose.ToLowerInvariant(), @"[a-zA-Z0-9]+")
            .Select(m => m.Value == "sorting" ? "sort" : m.Value)
            .Where(w => !StopWords.Contains(w) && w.Length > 1)
            .Take(4)
            .ToList();

        if (words.Count == 0)
        {
            return language == CodeLanguage.C ? "program.c" : "script.py";
        }

        // Standardize common programming patterns (e.g. "sort an array" -> "array_sort", "convert decimal to binary" -> "decimal_to_binary")
        if (words.Contains("sort") && words.Contains("array"))
        {
            words.Remove("sort");
            words.Remove("array");
            words.Insert(0, "array");
            words.Insert(1, "sort");
        }
        else if (words.Contains("decimal") && words.Contains("binary"))
        {
            words = ["decimal", "to", "binary"];
        }

        string baseName = string.Join("_", words);
        if (baseName.Length > 32)
        {
            baseName = baseName.Substring(0, 32).TrimEnd('_');
        }

        if (WindowsReservedDeviceNames.Contains(baseName))
        {
            baseName += "_code";
        }

        return baseName + ext;
    }

    public bool DirectoryExists(string path)
    {
        return !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);
    }

    public bool CreateDirectorySafely(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            // Verify path is within allowed roots before creating
            var (isValid, canonical, _, _) = ValidateAndResolveDestination(path);
            if (!isValid || string.IsNullOrEmpty(canonical))
            {
                OrbitLogger.Warn(LogCategory, $"Refusing to create directory outside allowed roots: '{path}'");
                return false;
            }

            Directory.CreateDirectory(canonical);
            OrbitLogger.Info(LogCategory, $"Successfully created directory: '{canonical}'");
            return Directory.Exists(canonical);
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, $"Failed creating directory '{path}': {ex.Message}", ex);
            return false;
        }
    }

    public bool FileExists(string path)
    {
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
    }

    public string GetNextAvailableFileName(string folderPath, string fileName)
    {
        string baseName = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);

        // Strip any existing _\d+ suffix
        baseName = Regex.Replace(baseName, @"_\d+$", "");

        int counter = 2;
        while (counter < 1000)
        {
            string candidate = $"{baseName}_{counter}{ext}";
            string candidatePath = Path.Combine(folderPath, candidate);
            if (!File.Exists(candidatePath))
            {
                return candidate;
            }
            counter++;
        }

        return $"{baseName}_{Guid.NewGuid().ToString("N").Substring(0, 4)}{ext}";
    }

    public async Task<CodeGenerationResult> GenerateAndWriteFileAsync(
        string folderPath,
        string fileName,
        string displayLocation,
        string purpose,
        CodeLanguage language,
        IOllamaClient ollamaClient,
        CancellationToken cancellationToken = default)
    {
        OrbitLogger.Info(LogCategory, $"Starting code generation for '{fileName}' in '{folderPath}' (Lang={language}, Purpose='{purpose}')");

        try
        {
            // 1. Ensure directory exists
            if (!Directory.Exists(folderPath))
            {
                bool created = CreateDirectorySafely(folderPath);
                if (!created)
                {
                    return new CodeGenerationResult(false, $"Could not create target directory: {displayLocation}", Error: "Directory creation failed");
                }
            }

            string fullFilePath = Path.Combine(folderPath, fileName);

            // 2. Prepare targeted generation prompt for Ollama
            string langName = language == CodeLanguage.C ? "C" : "Python";
            string prompt = language == CodeLanguage.C
                ? $"Write a complete, clean, self-contained C program to: {purpose}.\n" +
                  "Include necessary standard library headers (e.g. #include <stdio.h>), a complete main() function, and clear logic.\n" +
                  "Output ONLY valid C source code. Do not include markdown commentary, intro, or outro."
                : $"Write a complete, clean, self-contained Python script to: {purpose}.\n" +
                  "Include necessary standard library imports, clear logic, and if __name__ == '__main__': entry point.\n" +
                  "Output ONLY valid Python source code. Do not include markdown commentary, intro, or outro.";

            var messages = new List<AiChatMessage>
            {
                new("system", "You are an expert software developer. You generate clean, readable, correct code without conversational banter."),
                new("user", prompt)
            };

            var codeBuilder = new StringBuilder();

            await ollamaClient.StreamChatAsync(
                messages,
                chunk => codeBuilder.Append(chunk),
                "qwen2.5:3b",
                cancellationToken,
                numPredict: 1500).ConfigureAwait(false);

            string rawGenerated = codeBuilder.ToString().Trim();

            if (string.IsNullOrWhiteSpace(rawGenerated))
            {
                return new CodeGenerationResult(false, "The AI model returned an empty code response.", Error: "Empty output");
            }

            // 3. Clean and strip any markdown code fence wrappers
            string cleanCode = StripCodeFences(rawGenerated, language);

            if (string.IsNullOrWhiteSpace(cleanCode))
            {
                return new CodeGenerationResult(false, "Failed to extract clean code from generation output.", Error: "Extraction failure");
            }

            // 4. Safely write file with UTF-8 encoding
            await File.WriteAllTextAsync(fullFilePath, cleanCode, Encoding.UTF8, cancellationToken).ConfigureAwait(false);

            // 5. Verify file creation on disk
            if (!File.Exists(fullFilePath))
            {
                return new CodeGenerationResult(false, "File creation verification failed: file not found on disk.", Error: "Verification failure");
            }

            var fileInfo = new FileInfo(fullFilePath);
            if (fileInfo.Length == 0)
            {
                return new CodeGenerationResult(false, "File creation verification failed: generated file is empty.", Error: "Zero bytes written");
            }

            OrbitLogger.Info(LogCategory, $"Successfully created and verified file: '{fullFilePath}' ({fileInfo.Length} bytes)");

            string successSummary = $"File created successfully!\n\n`{fileName}`\n\nLocation: {displayLocation}\n\nPurpose: {FormatPurposeSummary(purpose)}";

            return new CodeGenerationResult(
                Success: true,
                Message: successSummary,
                FilePath: fullFilePath,
                FileName: fileName,
                DisplayLocation: displayLocation,
                Purpose: purpose);
        }
        catch (OperationCanceledException)
        {
            OrbitLogger.Info(LogCategory, "Code generation and writing was cancelled.");
            return new CodeGenerationResult(false, "Code generation cancelled.", Error: "Cancelled");
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, $"Failed to generate and write code file: {ex.Message}", ex);
            return new CodeGenerationResult(false, $"Error creating file: {ex.Message}", Error: ex.Message);
        }
    }

    private static string StripCodeFences(string text, CodeLanguage language)
    {
        string cleaned = text.Trim();

        // 1. If there is a markdown code block ```c ... ``` or ```python ... ```, extract ONLY the code block
        var match = Regex.Match(cleaned, @"```(?:c|cpp|python|py)?\s*[\r\n]+([\s\S]+?)[\r\n]+```", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        // 2. If it starts with ``` without closing ```
        if (cleaned.StartsWith("```"))
        {
            int firstNewline = cleaned.IndexOf('\n');
            if (firstNewline >= 0)
            {
                cleaned = cleaned.Substring(firstNewline + 1).Trim();
            }
            if (cleaned.EndsWith("```"))
            {
                cleaned = cleaned.Substring(0, cleaned.Length - 3).Trim();
            }
            return cleaned;
        }

        // 3. If there is no code fence, but model generated explanatory prose after C code
        if (language == CodeLanguage.C)
        {
            int lastBrace = cleaned.LastIndexOf('}');
            if (lastBrace >= 0 && lastBrace < cleaned.Length - 1)
            {
                cleaned = cleaned.Substring(0, lastBrace + 1).Trim();
            }
        }

        return cleaned;
    }

    private static string FormatPurposeSummary(string purpose)
    {
        if (string.IsNullOrWhiteSpace(purpose)) return "Generates requested program.";
        string trimmed = purpose.Trim();
        // Capitalize first letter
        if (char.IsLower(trimmed[0]))
        {
            trimmed = char.ToUpperInvariant(trimmed[0]) + trimmed.Substring(1);
        }
        if (!trimmed.EndsWith('.'))
        {
            trimmed += ".";
        }
        return trimmed;
    }
}

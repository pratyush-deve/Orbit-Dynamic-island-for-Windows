using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Services.Ai;
using Orbit.Services.CodeGeneration;
using Orbit.Services.Commands;
using Xunit;

namespace Orbit.Tests;

public class CodeGenerationServiceTests
{
    private sealed class MockOllamaClient : IOllamaClient
    {
        public bool IsServiceAvailable => true;
        public string CodeToStream { get; set; } = "```c\n#include <stdio.h>\nint main() { printf(\"Hello\"); return 0; }\n```";

        public Task<bool> CheckServiceHealthAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task StreamChatAsync(
            IEnumerable<AiChatMessage> messages,
            Action<string> onChunkReceived,
            string model = "qwen2.5:3b",
            CancellationToken cancellationToken = default,
            int numPredict = 150)
        {
            onChunkReceived(CodeToStream);
            return Task.CompletedTask;
        }

        public Task<bool> UnloadModelAsync(string model = "qwen2.5:3b", CancellationToken cancellationToken = default) => Task.FromResult(true);

        public void Dispose() { }
    }

    [Theory]
    [InlineData("c", CodeLanguage.C)]
    [InlineData("c program", CodeLanguage.C)]
    [InlineData("write a c script", CodeLanguage.C)]
    [InlineData("python", CodeLanguage.Python)]
    [InlineData("py", CodeLanguage.Python)]
    [InlineData("python script", CodeLanguage.Python)]
    public void ParseLanguage_SupportedLanguages_ReturnsExpected(string input, CodeLanguage expected)
    {
        var service = new CodeGenerationService();
        var lang = service.ParseLanguage(input, out string? unsupported);

        Assert.Equal(expected, lang);
        Assert.Null(unsupported);
    }

    [Theory]
    [InlineData("c++", "C++")]
    [InlineData("cpp", "CPP")]
    [InlineData("c#", "C#")]
    [InlineData("csharp", "CSHARP")]
    [InlineData("java", "JAVA")]
    [InlineData("kotlin", "KOTLIN")]
    [InlineData("javascript", "JAVASCRIPT")]
    [InlineData("typescript", "TYPESCRIPT")]
    [InlineData("rust", "RUST")]
    public void ParseLanguage_UnsupportedLanguages_ReturnsUnsupported(string input, string expectedName)
    {
        var service = new CodeGenerationService();
        var lang = service.ParseLanguage(input, out string? unsupported);

        Assert.Equal(CodeLanguage.Unsupported, lang);
        Assert.Equal(expectedName, unsupported);
    }

    [Fact]
    public void ParseLanguage_OmittedLanguage_ReturnsUnknown()
    {
        var service = new CodeGenerationService();
        var lang = service.ParseLanguage("create a program to sort an array", out string? unsupported);

        Assert.Equal(CodeLanguage.Unknown, lang);
        Assert.Null(unsupported);
    }

    [Theory]
    [InlineData("..\\secret")]
    [InlineData("../../windows/system32")]
    [InlineData(@"\\network\share\code")]
    [InlineData("//server/folder")]
    public void ValidateAndResolveDestination_PathTraversalOrNetwork_RejectsSecurityRisk(string invalidPath)
    {
        var service = new CodeGenerationService();
        var (isValid, canonical, _, error) = service.ValidateAndResolveDestination(invalidPath);

        Assert.False(isValid);
        Assert.Null(canonical);
        Assert.NotNull(error);
    }

    [Fact]
    public void ValidateAndResolveDestination_DesktopAndDocuments_ResolvesWithinAllowedRoots()
    {
        var service = new CodeGenerationService();

        var desktopRes = service.ValidateAndResolveDestination("Desktop");
        Assert.True(desktopRes.IsValid);
        Assert.NotNull(desktopRes.ResolvedPath);
        Assert.Equal("Desktop", desktopRes.DisplayName);

        var docRes = service.ValidateAndResolveDestination("Documents");
        Assert.True(docRes.IsValid);
        Assert.NotNull(docRes.ResolvedPath);
        Assert.Equal("Documents", docRes.DisplayName);

        var subRes = service.ValidateAndResolveDestination("Desktop/Test");
        Assert.True(subRes.IsValid);
        Assert.Contains("Test", subRes.ResolvedPath!);
        Assert.Equal("Desktop → Test", subRes.DisplayName);
    }

    [Theory]
    [InlineData("CON.c", CodeLanguage.C)]
    [InlineData("prn.py", CodeLanguage.Python)]
    [InlineData("aux.c", CodeLanguage.C)]
    [InlineData("nul.py", CodeLanguage.Python)]
    [InlineData("com1.c", CodeLanguage.C)]
    [InlineData("lpt1.py", CodeLanguage.Python)]
    public void ValidateFileName_ReservedDeviceNames_Rejected(string reservedName, CodeLanguage lang)
    {
        var service = new CodeGenerationService();
        var (isValid, _, error) = service.ValidateFileName(reservedName, lang);

        Assert.False(isValid);
        Assert.Contains("reserved", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("test/file.c")]
    [InlineData("folder\\file.py")]
    [InlineData("file?.c")]
    [InlineData("file*.py")]
    [InlineData("file<>.c")]
    public void ValidateFileName_InvalidCharsAndPathSeparators_Rejected(string invalidName)
    {
        var service = new CodeGenerationService();
        var (isValid, _, error) = service.ValidateFileName(invalidName, CodeLanguage.C);

        Assert.False(isValid);
        Assert.NotNull(error);
    }

    [Fact]
    public void ValidateFileName_MismatchedExtension_Rejected()
    {
        var service = new CodeGenerationService();
        var (isValid, _, error) = service.ValidateFileName("program.py", CodeLanguage.C);

        Assert.False(isValid);
        Assert.Contains(".c", error);
    }

    [Fact]
    public void ValidateFileName_ValidNames_AcceptedAndNormalized()
    {
        var service = new CodeGenerationService();
        var (isValidC, nameC, _) = service.ValidateFileName("decimal_to_binary.c", CodeLanguage.C);
        Assert.True(isValidC);
        Assert.Equal("decimal_to_binary.c", nameC);

        var (isValidPy, namePy, _) = service.ValidateFileName("rename_script", CodeLanguage.Python);
        Assert.True(isValidPy);
        Assert.Equal("rename_script.py", namePy);
    }

    [Theory]
    [InlineData("convert decimal to binary", CodeLanguage.C, "decimal_to_binary.c")]
    [InlineData("calculate factorial", CodeLanguage.Python, "factorial.py")]
    [InlineData("sort an array", CodeLanguage.C, "array_sort.c")]
    [InlineData("array sorting", CodeLanguage.C, "array_sort.c")]
    [InlineData("student marks calculator", CodeLanguage.Python, "student_marks.py")]
    public void SuggestFileName_GeneratesCleanSnakeCase(string purpose, CodeLanguage lang, string expected)
    {
        var service = new CodeGenerationService();
        string suggested = service.SuggestFileName(purpose, lang);

        Assert.Equal(expected, suggested);
    }

    [Fact]
    public void GetNextAvailableFileName_GeneratesNumberedSuffix()
    {
        var service = new CodeGenerationService();
        string tempDir = Path.Combine(Path.GetTempPath(), "OrbitTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            string file1 = Path.Combine(tempDir, "decimal_to_binary.c");
            File.WriteAllText(file1, "/* test */");

            string nextName = service.GetNextAvailableFileName(tempDir, "decimal_to_binary.c");
            Assert.Equal("decimal_to_binary_2.c", nextName);

            string file2 = Path.Combine(tempDir, "decimal_to_binary_2.c");
            File.WriteAllText(file2, "/* test */");

            string nextName3 = service.GetNextAvailableFileName(tempDir, "decimal_to_binary.c");
            Assert.Equal("decimal_to_binary_3.c", nextName3);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task GenerateAndWriteFileAsync_SafelyWritesAndVerifiesCleanCode()
    {
        var service = new CodeGenerationService();
        var mockOllama = new MockOllamaClient
        {
            CodeToStream = "```c\n#include <stdio.h>\nint main() {\n    printf(\"Binary: %d\\n\", 1010);\n    return 0;\n}\n```"
        };

        string tempFolder = Path.Combine(Path.GetTempPath(), "OrbitCodeGenTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);

        try
        {
            var result = await service.GenerateAndWriteFileAsync(
                tempFolder,
                "decimal_to_binary.c",
                "Desktop → Test",
                "convert decimal to binary",
                CodeLanguage.C,
                mockOllama);

            Assert.True(result.Success);
            Assert.NotNull(result.FilePath);
            Assert.True(File.Exists(result.FilePath));

            string content = await File.ReadAllTextAsync(result.FilePath);
            Assert.DoesNotContain("```", content); // Markdown fences stripped
            Assert.Contains("#include <stdio.h>", content);
            Assert.Contains("File created successfully!", result.Message);
        }
        finally
        {
            if (Directory.Exists(tempFolder))
            {
                Directory.Delete(tempFolder, true);
            }
        }
    }

    [Fact]
    public async Task GenerateAndWriteFileAsync_StripsTrailingExplanatoryTextAfterClosingFence()
    {
        var service = new CodeGenerationService();
        var mockOllama = new MockOllamaClient
        {
            CodeToStream = "```c\n#include <stdio.h>\nint main() {\n    return 0;\n}\n```\n\nThis C program includes a function decimalToBinary..."
        };

        string tempFolder = Path.Combine(Path.GetTempPath(), "OrbitCodeGenTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);

        try
        {
            var result = await service.GenerateAndWriteFileAsync(
                tempFolder,
                "decimal_to_binary.c",
                "Desktop → test",
                "convert decimal number into binary numbers",
                CodeLanguage.C,
                mockOllama);

            Assert.True(result.Success);
            string content = await File.ReadAllTextAsync(result.FilePath!);
            Assert.DoesNotContain("```", content);
            Assert.DoesNotContain("This C program includes", content);
            Assert.EndsWith("}", content.Trim());
        }
        finally
        {
            if (Directory.Exists(tempFolder))
            {
                Directory.Delete(tempFolder, true);
            }
        }
    }

    [Fact]
    public void SuggestFileName_DecimalToBinary_ReturnsDecimalToBinary()
    {
        var service = new CodeGenerationService();
        string suggested = service.SuggestFileName("convert decimal number into binary numbers", CodeLanguage.C);
        Assert.Equal("decimal_to_binary.c", suggested);
    }
}

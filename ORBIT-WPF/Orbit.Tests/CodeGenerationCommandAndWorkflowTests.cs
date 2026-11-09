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

public class CodeGenerationCommandAndWorkflowTests
{
    private sealed class WorkflowOllamaClient : IOllamaClient
    {
        public bool IsServiceAvailable => true;
        public string ReturnedCode { get; set; } = "# Sample Python code\nprint('Factorial computed')";

        public Task<bool> CheckServiceHealthAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task StreamChatAsync(
            IEnumerable<AiChatMessage> messages,
            Action<string> onChunkReceived,
            string model = "qwen2.5:3b",
            CancellationToken cancellationToken = default,
            int numPredict = 150)
        {
            onChunkReceived(ReturnedCode);
            return Task.CompletedTask;
        }

        public Task<bool> UnloadModelAsync(string model = "qwen2.5:3b", CancellationToken cancellationToken = default) => Task.FromResult(true);

        public void Dispose() { }
    }

    [Theory]
    [InlineData("Create a C program to convert decimal to binary in the Test folder on my Desktop")]
    [InlineData("Write a Python script to rename files")]
    [InlineData("Generate a Python program and save it on my Desktop")]
    [InlineData("Create a C file inside the Test folder on Desktop")]
    [InlineData("Write a Python program to calculate factorial in Documents")]
    [InlineData("Create a program to sort an array and save it in my college folder")]
    [InlineData("Write a Python factorial program in Documents")]
    [InlineData("Create a Python script on my Desktop")]
    [InlineData("Generate a C program to sort an array")]
    [InlineData("Create a program to calculate student marks")]
    public void CommandParser_CodeGenerationRequests_ParsedAsGenerateCode(string phrase)
    {
        var parser = new CommandParser(new AppRegistry());
        var intent = parser.Parse(phrase);

        Assert.Equal(CommandType.GenerateCode, intent.Type);
        Assert.NotNull(intent.CodeGenData);
    }

    [Theory]
    [InlineData("How do I write a C program to reverse a string")]
    [InlineData("Explain quicksort in Python")]
    [InlineData("What is a binary search algorithm")]
    public void CommandParser_ConversationalRequests_NotParsedAsGenerateCode(string phrase)
    {
        var parser = new CommandParser(new AppRegistry());
        var intent = parser.Parse(phrase);

        Assert.NotEqual(CommandType.GenerateCode, intent.Type);
    }

    [Fact]
    public void CommandParser_UnsupportedLanguage_Identified()
    {
        var parser = new CommandParser(new AppRegistry());
        var intent = parser.Parse("Create a C++ program to sort an array");

        Assert.Equal(CommandType.GenerateCode, intent.Type);
        Assert.NotNull(intent.CodeGenData);
        Assert.Equal(CodeLanguage.Unsupported, intent.CodeGenData.Language);
        Assert.Equal("C++", intent.CodeGenData.UnsupportedLanguageName);
    }

    [Fact]
    public async Task AiAssistantService_UnsupportedLanguage_ReturnsHelpfulMessage()
    {
        var ollama = new WorkflowOllamaClient();
        var lifecycle = new ModelLifecycleManager(ollama);
        var appReg = new AppRegistry();
        var parser = new CommandParser(appReg);

        using var aiService = new AiAssistantService(ollama, lifecycle, appReg, parser);

        string response = await aiService.ProcessVoiceTranscriptionAsync("Create a C++ program to sort an array");

        Assert.Contains("supports C and Python only", response);
        Assert.Null(aiService.PendingClarification);
    }

    [Fact]
    public async Task AiAssistantService_OmittedLanguage_PromptsForLanguageChoice()
    {
        var ollama = new WorkflowOllamaClient();
        var lifecycle = new ModelLifecycleManager(ollama);
        var appReg = new AppRegistry();
        var parser = new CommandParser(appReg);

        using var aiService = new AiAssistantService(ollama, lifecycle, appReg, parser);

        string prompt = await aiService.ProcessVoiceTranscriptionAsync("Create a program to sort an array");

        Assert.Contains("Which language would you like to use, C or Python?", prompt);
        Assert.NotNull(aiService.PendingClarification);
        Assert.Equal(CodeGenerationStage.AwaitingLanguageClarification, aiService.PendingClarification.CodeGenData!.Stage);

        // User responds with "Python"
        string nextStep = await aiService.ProcessVoiceTranscriptionAsync("Python");
        Assert.Contains("Save it in", nextStep);
        Assert.Equal(CodeGenerationStage.AwaitingDestinationConfirmation, aiService.PendingClarification.CodeGenData!.Stage);
        Assert.Equal(CodeLanguage.Python, aiService.PendingClarification.CodeGenData!.Language);
    }

    [Fact]
    public async Task AiAssistantService_FullMultiStepConfirmationAndWritingWorkflow()
    {
        var ollama = new WorkflowOllamaClient
        {
            ReturnedCode = "def factorial(n):\n    return 1 if n <= 1 else n * factorial(n - 1)\n\nif __name__ == '__main__':\n    print(factorial(5))"
        };
        var lifecycle = new ModelLifecycleManager(ollama);
        var appReg = new AppRegistry();
        var parser = new CommandParser(appReg);

        using var aiService = new AiAssistantService(ollama, lifecycle, appReg, parser);

        // Step 1: Initial request
        string step1 = await aiService.ProcessVoiceTranscriptionAsync("Write a Python program to calculate factorial in Documents");
        Assert.Contains("Save it in Documents", step1);
        Assert.NotNull(aiService.PendingClarification);
        Assert.Equal(CodeGenerationStage.AwaitingDestinationConfirmation, aiService.PendingClarification.CodeGenData!.Stage);

        // Step 2: Destination confirmed via voice "Yes"
        string step2 = await aiService.ProcessVoiceTranscriptionAsync("Yes");
        Assert.Contains("factorial.py", step2);
        Assert.Equal(CodeGenerationStage.AwaitingFileNameConfirmation, aiService.PendingClarification.CodeGenData!.Stage);

        // Step 3: Filename confirmed via voice "Yes"
        string step3 = await aiService.ProcessVoiceTranscriptionAsync("Yes");
        if (step3.Contains("already exists"))
        {
            // If file exists from prior execution, verify Overwrite flow
            step3 = await aiService.ProcessVoiceTranscriptionAsync("Overwrite");
        }
        Assert.Contains("File created successfully!", step3);
        Assert.Contains("factorial.py", step3);
        Assert.Contains("Location: Documents", step3);
        Assert.Null(aiService.PendingClarification); // Workflow successfully finished and reset
    }

    [Fact]
    public async Task AiAssistantService_DestinationCorrection_UpdatesPendingDestination()
    {
        var ollama = new WorkflowOllamaClient();
        var lifecycle = new ModelLifecycleManager(ollama);
        var appReg = new AppRegistry();
        var parser = new CommandParser(appReg);

        using var aiService = new AiAssistantService(ollama, lifecycle, appReg, parser);

        // Step 1: User says on Desktop
        await aiService.ProcessVoiceTranscriptionAsync("Write a Python script on my Desktop");
        Assert.NotNull(aiService.PendingClarification);
        Assert.Contains("Desktop", aiService.PendingClarification.ClarificationPrompt!);

        // Step 2: User corrects "No, save it in Documents instead"
        string correctedStep = await aiService.ProcessVoiceTranscriptionAsync("No, save it in Documents instead");
        Assert.Contains("Save it in Documents", correctedStep);
        Assert.Equal(CodeGenerationStage.AwaitingDestinationConfirmation, aiService.PendingClarification.CodeGenData!.Stage);
    }

    [Fact]
    public async Task AiAssistantService_FolderCreation_AcceptsNaturalAffirmativeSpeech()
    {
        var ollama = new WorkflowOllamaClient();
        var lifecycle = new ModelLifecycleManager(ollama);
        var appReg = new AppRegistry();
        var parser = new CommandParser(appReg);

        using var aiService = new AiAssistantService(ollama, lifecycle, appReg, parser);

        string testFolderName = "OrbitUnitTestFolder_" + Guid.NewGuid().ToString("N")[..8];
        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string fullFolder = Path.Combine(desktopPath, testFolderName);

        try
        {
            // Step 1: Request in new folder
            string step1 = await aiService.ProcessVoiceTranscriptionAsync($"Create a C program to convert decimal to binary in the {testFolderName} folder on my Desktop");
            Assert.Contains("doesn't exist", step1);
            Assert.NotNull(aiService.PendingClarification);
            Assert.Equal(CodeGenerationStage.AwaitingFolderCreationConfirmation, aiService.PendingClarification.CodeGenData!.Stage);

            // Step 2: Natural affirmative speech: "yes please create the required folder"
            string step2 = await aiService.ProcessVoiceTranscriptionAsync("yes please create the required folder");
            Assert.Contains("Folder created", step2);
            Assert.True(Directory.Exists(fullFolder));
            Assert.Equal(CodeGenerationStage.AwaitingFileNameConfirmation, aiService.PendingClarification.CodeGenData!.Stage);

            // Step 3: Filename confirmation
            string step3 = await aiService.ProcessVoiceTranscriptionAsync("yes proceed");
            Assert.Contains("File created successfully!", step3);
        }
        finally
        {
            if (Directory.Exists(fullFolder))
            {
                Directory.Delete(fullFolder, true);
            }
        }
    }

    [Fact]
    public void CommandParser_FolderNamedAsTest_CorrectlyExtractsFolderAndPurpose()
    {
        var parser = new CommandParser(new AppRegistry());
        var intent = parser.Parse("Create a C program in desktop in a folder named as test. The program should convert decimal number into binary numbers");

        Assert.Equal(CommandType.GenerateCode, intent.Type);
        Assert.NotNull(intent.CodeGenData);
        Assert.Equal("Desktop/test", intent.CodeGenData.DestinationFolder);
        Assert.Equal(CodeLanguage.C, intent.CodeGenData.Language);
        Assert.Contains("convert decimal number into binary numbers", intent.CodeGenData.Purpose);
        Assert.DoesNotContain("folder named as test", intent.CodeGenData.Purpose);
    }
}

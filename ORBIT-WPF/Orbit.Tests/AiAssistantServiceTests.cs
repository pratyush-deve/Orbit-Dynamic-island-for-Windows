using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Models;
using Orbit.Services.Ai;
using Orbit.Services.Commands;
using Orbit.Services.SystemMonitor;
using Xunit;

namespace Orbit.Tests;

public class AiAssistantServiceTests
{
    private sealed class FakeOllamaClient : IOllamaClient
    {
        public bool IsServiceAvailable { get; set; } = true;
        public int UnloadCallCount { get; private set; }
        public List<string> StreamChunks { get; set; } = new() { "Hello ", "Pratyush! ", "Orbit is ready." };

        public Task<bool> CheckServiceHealthAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(IsServiceAvailable);
        }

        public Task StreamChatAsync(
            IEnumerable<AiChatMessage> messages,
            Action<string> onChunkReceived,
            string model = "qwen2.5:3b",
            CancellationToken cancellationToken = default)
        {
            foreach (var chunk in StreamChunks)
            {
                if (cancellationToken.IsCancellationRequested) break;
                onChunkReceived(chunk);
            }
            return Task.CompletedTask;
        }

        public Task<bool> UnloadModelAsync(string model = "qwen2.5:3b", CancellationToken cancellationToken = default)
        {
            UnloadCallCount++;
            return Task.FromResult(true);
        }

        public void Dispose() { }
    }

    private sealed class FakeSystemMonitorService : ISystemMonitorService
    {
        public SystemMetrics CurrentMetrics { get; set; } = new(10.0, 16.0, 32.0, 50, 0.0, 0.0, 5.0);
        public event Action<SystemMetrics>? MetricsUpdated;

        public void TriggerMetrics(int ramPercent)
        {
            CurrentMetrics = new SystemMetrics(10.0, (ramPercent / 100.0) * 32.0, 32.0, ramPercent, 0.0, 0.0, 5.0);
            MetricsUpdated?.Invoke(CurrentMetrics);
        }

        public void StartMonitoring() { }
        public void StopMonitoring() { }
        public SystemMetrics GetCurrentMetrics() => CurrentMetrics;
        public void Dispose() { }
    }

    [Fact]
    public async Task AiAssistantService_StreamsTokens_AndCompletes()
    {
        var fakeClient = new FakeOllamaClient();
        var fakeMonitor = new FakeSystemMonitorService();
        using var lifecycle = new ModelLifecycleManager(fakeClient, fakeMonitor);
        var appRegistry = new AppRegistry();
        using var aiService = new AiAssistantService(fakeClient, lifecycle, appRegistry);

        bool startedFired = false;
        var receivedTokens = new List<string>();
        string? completedText = null;

        aiService.GenerationStarted += () => startedFired = true;
        aiService.TokenReceived += chunk => receivedTokens.Add(chunk);
        aiService.GenerationCompleted += full => completedText = full;

        var result = await aiService.ProcessVoiceTranscriptionAsync("Hello Orbit");

        Assert.True(startedFired);
        Assert.Equal(3, receivedTokens.Count);
        Assert.Equal("Hello Pratyush! Orbit is ready.", completedText);
        Assert.Equal(completedText, result);
    }

    [Fact]
    public async Task AiAssistantService_HandlesOfflineOllama_Gracefully()
    {
        var fakeClient = new FakeOllamaClient { IsServiceAvailable = false };
        var fakeMonitor = new FakeSystemMonitorService();
        using var lifecycle = new ModelLifecycleManager(fakeClient, fakeMonitor);
        var appRegistry = new AppRegistry();
        using var aiService = new AiAssistantService(fakeClient, lifecycle, appRegistry);

        string? failedMessage = null;
        aiService.GenerationFailed += err => failedMessage = err;

        var result = await aiService.ProcessVoiceTranscriptionAsync("Open Android Studio");

        Assert.NotNull(failedMessage);
        Assert.Contains("offline", failedMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(failedMessage, result);
    }

    [Fact]
    public async Task ModelLifecycleManager_DefersUnload_WhileInferenceIsActive()
    {
        var fakeClient = new FakeOllamaClient();
        var fakeMonitor = new FakeSystemMonitorService();
        using var lifecycle = new ModelLifecycleManager(fakeClient, fakeMonitor);

        // Mark active generation
        lifecycle.NotifyGenerationStarted();

        // Trigger RAM over 85% threshold for 5 consecutive seconds (hysteresis requirement)
        for (int i = 0; i < 5; i++)
        {
            fakeMonitor.TriggerMetrics(90);
        }

        // Should NOT unload while active generation is occurring
        Assert.Equal(0, fakeClient.UnloadCallCount);

        // Now complete generation
        lifecycle.NotifyGenerationCompleted();

        // Allow async unload task to complete
        await Task.Delay(60);

        // Model should now be unloaded
        Assert.Equal(1, fakeClient.UnloadCallCount);
        Assert.False(lifecycle.IsModelLoaded);
    }

    [Fact]
    public async Task AiAssistantService_TriggersClarification_AndConfirmsSuccessfully()
    {
        var fakeClient = new FakeOllamaClient();
        var fakeMonitor = new FakeSystemMonitorService();
        using var lifecycle = new ModelLifecycleManager(fakeClient, fakeMonitor);
        var appRegistry = new AppRegistry();
        var parser = new CommandParser(appRegistry);

        bool executed = false;
        var fakeExecutor = new FakeCommandExecutor(intent =>
        {
            executed = true;
            return Task.FromResult(new CommandResult(true, "Opening Android Studio...", intent));
        });

        using var aiService = new AiAssistantService(fakeClient, lifecycle, appRegistry, parser, fakeExecutor);

        CommandIntent? requestedIntent = null;
        aiService.ClarificationRequested += intent => requestedIntent = intent;

        var resultText = await aiService.ProcessVoiceTranscriptionAsync("Studio");

        // Clarification prompt returned
        Assert.NotNull(requestedIntent);
        Assert.Equal(CommandType.Clarification, requestedIntent.Type);
        Assert.Equal("Did you mean Android Studio?", resultText);
        Assert.False(executed); // Has NOT executed yet!

        // Confirm clarification (YES)
        var confirmResult = await aiService.ConfirmClarificationAsync();

        Assert.NotNull(confirmResult);
        Assert.True(confirmResult.Success);
        Assert.True(executed); // Executed exactly once!
        Assert.Null(aiService.PendingClarification); // Stale action cleared
    }

    [Fact]
    public async Task AiAssistantService_RejectsClarification_WithoutExecuting()
    {
        var fakeClient = new FakeOllamaClient();
        var fakeMonitor = new FakeSystemMonitorService();
        using var lifecycle = new ModelLifecycleManager(fakeClient, fakeMonitor);
        var appRegistry = new AppRegistry();
        var parser = new CommandParser(appRegistry);

        bool executed = false;
        var fakeExecutor = new FakeCommandExecutor(intent =>
        {
            executed = true;
            return Task.FromResult(new CommandResult(true, "Launched", intent));
        });

        using var aiService = new AiAssistantService(fakeClient, lifecycle, appRegistry, parser, fakeExecutor);

        await aiService.ProcessVoiceTranscriptionAsync("Studio");
        Assert.NotNull(aiService.PendingClarification);

        // Reject clarification (NO)
        aiService.RejectClarification();

        Assert.Null(aiService.PendingClarification);
        Assert.False(executed); // Never executed!

        // Stale confirmation cannot run anything
        var staleResult = await aiService.ConfirmClarificationAsync();
        Assert.Null(staleResult);
        Assert.False(executed);
    }

    private sealed class FakeCommandExecutor : IVoiceCommandExecutor
    {
        private readonly Func<CommandIntent, Task<CommandResult>> _executeFunc;

        public FakeCommandExecutor(Func<CommandIntent, Task<CommandResult>> executeFunc)
        {
            _executeFunc = executeFunc;
        }

        public Task<CommandResult> ExecuteAsync(CommandIntent intent, CancellationToken cancellationToken = default)
        {
            return _executeFunc(intent);
        }
    }
}

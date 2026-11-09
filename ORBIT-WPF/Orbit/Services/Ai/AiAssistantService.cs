using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Core;
using Orbit.Services.Commands;

namespace Orbit.Services.Ai;

public interface IAiAssistantService : IDisposable
{
    bool IsGenerating { get; }
    event Action? GenerationStarted;
    event Action<string>? TokenReceived;
    event Action<string>? GenerationCompleted;
    event Action<string>? GenerationFailed;
    event Action<CommandIntent>? ClarificationRequested;
    event Action? ClarificationDismissed;

    CommandIntent? PendingClarification { get; }
    Task<CommandResult?> ConfirmClarificationAsync();
    void RejectClarification();

    Task<string> ProcessVoiceTranscriptionAsync(string transcription, CancellationToken cancellationToken = default);
    void CancelActiveGeneration();
    void ResetConversation();
}

public sealed class AiAssistantService : IAiAssistantService
{
    private const string LogCategory = "AI_ASSISTANT";

    private readonly IOllamaClient _ollamaClient;
    private readonly IModelLifecycleManager _lifecycleManager;
    private readonly AppRegistry _appRegistry;
    private readonly ICommandParser? _commandParser;
    private readonly IVoiceCommandExecutor? _commandExecutor;
    private readonly Orbit.Services.SystemMonitor.ISystemMonitorService? _systemMonitorService;
    private readonly object _stateLock = new();
    private readonly List<AiChatMessage> _conversationHistory = new();

    private CancellationTokenSource? _currentCts;
    private bool _isGenerating;
    private bool _disposed;
    private CommandIntent? _pendingClarification;

    public bool IsGenerating
    {
        get
        {
            lock (_stateLock) return _isGenerating;
        }
        private set
        {
            lock (_stateLock) _isGenerating = value;
        }
    }

    public CommandIntent? PendingClarification
    {
        get
        {
            lock (_stateLock) return _pendingClarification;
        }
        private set
        {
            lock (_stateLock) _pendingClarification = value;
        }
    }

    public event Action? GenerationStarted;
    public event Action<string>? TokenReceived;
    public event Action<string>? GenerationCompleted;
    public event Action<string>? GenerationFailed;
    public event Action<CommandIntent>? ClarificationRequested;
    public event Action? ClarificationDismissed;

    public AiAssistantService(
        IOllamaClient ollamaClient,
        IModelLifecycleManager lifecycleManager,
        AppRegistry appRegistry,
        ICommandParser? commandParser = null,
        IVoiceCommandExecutor? commandExecutor = null,
        Orbit.Services.SystemMonitor.ISystemMonitorService? systemMonitorService = null)
    {
        _ollamaClient = ollamaClient ?? throw new ArgumentNullException(nameof(ollamaClient));
        _lifecycleManager = lifecycleManager ?? throw new ArgumentNullException(nameof(lifecycleManager));
        _appRegistry = appRegistry ?? throw new ArgumentNullException(nameof(appRegistry));
        _commandParser = commandParser;
        _commandExecutor = commandExecutor;
        _systemMonitorService = systemMonitorService;
    }

    private string BuildSystemPrompt()
    {
        var sb = new StringBuilder(@"You are ORBIT, an intelligent, helpful, and concise desktop companion for Windows 11.
Guidelines:
1. Speak warmly, casually, and concisely. Keep responses under 2-3 friendly sentences.
2. Voice transcriptions may have phonetic typos or imperfect phrasing (e.g. 'memories' for memory/RAM, 'studios' for Android Studio, 'lappy' for laptop). Infer the user's intent naturally.
3. If the user asks or mentions their laptop's RAM, memory, battery, CPU, performance, or whether their laptop can handle/run an app (such as Android Studio, games, or IDEs), ALWAYS use the Live Laptop Status below to give a direct, helpful, and accurate answer with real numbers!
4. You also help with general knowledge, questions, computer science, and programming (such as DAA lab algorithms: insertion sort, binary search, recursion).
5. Do not invent or execute system actions on your own. System actions are managed by ORBIT's command engine.");

        if (_systemMonitorService != null)
        {
            try
            {
                var m = _systemMonitorService.GetCurrentMetrics();
                double freeRam = Math.Max(0.0, m.RamTotalGb - m.RamUsedGb);
                sb.AppendLine("\nLive Laptop Status:");
                sb.AppendLine($"- Total RAM: {m.RamTotalGb:F1} GB");
                sb.AppendLine($"- Used RAM: {m.RamUsedGb:F1} GB ({m.RamUsagePercent}%)");
                sb.AppendLine($"- Free Available RAM: {freeRam:F1} GB");
                sb.AppendLine($"- CPU Usage: {m.CpuUsagePercent:F0}%");
                sb.AppendLine($"- GPU Usage: {m.GpuUsagePercent:F0}%");
                if (m.BatteryPercent >= 0)
                {
                    string battState = m.IsCharging ? "Charging" : (m.IsOnBattery ? "On Battery" : "Plugged in");
                    sb.AppendLine($"- Battery: {m.BatteryPercent}% ({battState})");
                }
            }
            catch { }
        }

        return sb.ToString();
    }

    public async Task<string> ProcessVoiceTranscriptionAsync(string transcription, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transcription))
        {
            return string.Empty;
        }

        CancellationTokenSource linkedCts;
        lock (_stateLock)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AiAssistantService));

            // Cancel any previous in-flight generation
            _currentCts?.Cancel();
            _currentCts?.Dispose();
            _currentCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts = _currentCts;
            IsGenerating = true;
        }

        _lifecycleManager.NotifyGenerationStarted();
        GenerationStarted?.Invoke();

        // Stage 1: Deterministic Command Classification
        if (_commandParser != null)
        {
            try
            {
                var intent = _commandParser.Parse(transcription);

                // Case A: Clarification needed (Ambiguous app name, phonetic uncertainty, compound command)
                if (intent.Type == CommandType.Clarification)
                {
                    PendingClarification = intent;
                    string clarificationMsg = intent.ClarificationPrompt ?? $"Did you mean {intent.Target}?";
                    OrbitLogger.Info(LogCategory, $"Clarification requested: '{clarificationMsg}' (Pending={intent.Target})");

                    ClarificationRequested?.Invoke(intent);
                    TokenReceived?.Invoke(clarificationMsg);
                    GenerationCompleted?.Invoke(clarificationMsg);

                    lock (_stateLock) { IsGenerating = false; }
                    _lifecycleManager.NotifyGenerationCompleted();
                    return clarificationMsg;
                }

                // Case B: Deterministic Action Execution (Fast-path with confirmed truth, zero split-brain)
                if (intent.Type != CommandType.Unknown && intent.Confidence >= 0.60f && _commandExecutor != null)
                {
                    // Clear any previous pending clarification and conversational context on deterministic command
                    PendingClarification = null;
                    lock (_stateLock) { _conversationHistory.Clear(); }

                    OrbitLogger.Info(LogCategory, $"Executing deterministic command intent: {intent.Type} -> '{intent.Target}'");
                    var execResult = await _commandExecutor.ExecuteAsync(intent, linkedCts.Token).ConfigureAwait(false);
                    string feedbackText = execResult.Message;

                    OrbitLogger.Info(LogCategory, $"Command executed: success={execResult.Success}, feedback='{feedbackText}'");
                    TokenReceived?.Invoke(feedbackText);
                    GenerationCompleted?.Invoke(feedbackText);

                    lock (_stateLock) { IsGenerating = false; }
                    _lifecycleManager.NotifyGenerationCompleted();
                    return feedbackText;
                }
            }
            catch (Exception ex)
            {
                OrbitLogger.Warn(LogCategory, $"Command intent execution failed: {ex.Message}");
            }
        }

        // Stage 2: Pure Conversational / Generative Knowledge Query (Qwen 2.5: 3B)
        var accumulatedResponse = new StringBuilder();

        try
        {
            // Verify Ollama health first
            bool healthy = await _ollamaClient.CheckServiceHealthAsync(linkedCts.Token).ConfigureAwait(false);
            if (!healthy)
            {
                string offlineMsg = "Ollama is currently offline. Please ensure Ollama is running on your machine.";
                OrbitLogger.Warn(LogCategory, offlineMsg);
                GenerationFailed?.Invoke(offlineMsg);
                return offlineMsg;
            }

            var messages = new List<AiChatMessage>
            {
                new("system", BuildSystemPrompt())
            };

            lock (_stateLock)
            {
                // Preserve conversational context so replies build directly upon previous questions
                messages.AddRange(_conversationHistory);
            }

            messages.Add(new("user", transcription));

            await _ollamaClient.StreamChatAsync(
                messages,
                chunk =>
                {
                    if (linkedCts.IsCancellationRequested) return;
                    accumulatedResponse.Append(chunk);
                    TokenReceived?.Invoke(chunk);
                },
                "qwen2.5:3b",
                linkedCts.Token).ConfigureAwait(false);

            string fullResponse = accumulatedResponse.ToString().Trim();
            OrbitLogger.Info(LogCategory, $"AI generation completed ({fullResponse.Length} chars).");

            lock (_stateLock)
            {
                _conversationHistory.Add(new("user", transcription));
                _conversationHistory.Add(new("assistant", fullResponse));
                while (_conversationHistory.Count > 6)
                {
                    _conversationHistory.RemoveAt(0);
                }
            }

            GenerationCompleted?.Invoke(fullResponse);
            return fullResponse;
        }
        catch (OperationCanceledException)
        {
            OrbitLogger.Info(LogCategory, "AI generation was cancelled.");
            GenerationFailed?.Invoke("Generation cancelled.");
            return string.Empty;
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, $"AI generation failed: {ex.Message}", ex);
            string errorMsg = "Sorry, I ran into an issue connecting to my local AI engine.";
            GenerationFailed?.Invoke(errorMsg);
            return errorMsg;
        }
        finally
        {
            lock (_stateLock)
            {
                IsGenerating = false;
            }
            _lifecycleManager.NotifyGenerationCompleted();
        }
    }

    public async Task<CommandResult?> ConfirmClarificationAsync()
    {
        CommandIntent? pending;
        lock (_stateLock)
        {
            pending = _pendingClarification;
            _pendingClarification = null;
        }

        if (pending == null) return null;

        var actionToRun = pending.PendingAction ?? pending;
        OrbitLogger.Info(LogCategory, $"Clarification confirmed (YES). Dispatching pending action: {actionToRun.Type} -> '{actionToRun.Target}'");

        ClarificationDismissed?.Invoke();

        if (_commandExecutor != null)
        {
            var result = await _commandExecutor.ExecuteAsync(actionToRun).ConfigureAwait(false);
            TokenReceived?.Invoke(result.Message);
            GenerationCompleted?.Invoke(result.Message);
            return result;
        }

        return null;
    }

    public void RejectClarification()
    {
        lock (_stateLock)
        {
            _pendingClarification = null;
        }
        OrbitLogger.Info(LogCategory, "Clarification rejected (NO). Clearing pending action.");
        ClarificationDismissed?.Invoke();
    }

    public void CancelActiveGeneration()
    {
        lock (_stateLock)
        {
            _currentCts?.Cancel();
            _pendingClarification = null;
        }
    }

    public void ResetConversation()
    {
        lock (_stateLock)
        {
            _conversationHistory.Clear();
        }
        OrbitLogger.Debug(LogCategory, "Conversation context reset.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        CancelActiveGeneration();
        _currentCts?.Dispose();
        _currentCts = null;
    }
}

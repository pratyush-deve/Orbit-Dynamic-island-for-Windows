using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Core;
using Orbit.Services.CodeGeneration;
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
    private readonly ICodeGenerationService _codeGenService;
    private readonly IOllamaServerManager? _ollamaServerManager;
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
        Orbit.Services.SystemMonitor.ISystemMonitorService? systemMonitorService = null,
        ICodeGenerationService? codeGenService = null,
        IOllamaServerManager? ollamaServerManager = null)
    {
        _ollamaClient = ollamaClient ?? throw new ArgumentNullException(nameof(ollamaClient));
        _lifecycleManager = lifecycleManager ?? throw new ArgumentNullException(nameof(lifecycleManager));
        _appRegistry = appRegistry ?? throw new ArgumentNullException(nameof(appRegistry));
        _commandParser = commandParser;
        _commandExecutor = commandExecutor;
        _systemMonitorService = systemMonitorService;
        _codeGenService = codeGenService ?? new CodeGenerationService();
        _ollamaServerManager = ollamaServerManager;
    }

    private string BuildSystemPrompt()
    {
        var sb = new StringBuilder(@"You are ORBIT, an intelligent, helpful, and concise desktop companion for Windows 11.
Guidelines:
1. Speak warmly, casually, and concisely. Keep responses under 2-3 friendly sentences.
2. Voice transcriptions may have phonetic typos or imperfect phrasing (e.g. 'memories' for memory/RAM, 'studios' for Android Studio, 'lappy' for laptop). Infer the user's intent naturally.
3. If the user asks or mentions their laptop's RAM, memory, battery, CPU, performance, or whether their laptop can handle/run an app (such as Android Studio, games, or IDEs), ALWAYS use the Live Laptop Status below to give a direct, helpful, and accurate answer with real numbers!
4. You also help with general knowledge, questions, computer science, and programming (such as DAA lab algorithms: insertion sort, binary search, recursion).
5. Do not invent or execute system actions on your own. System actions are managed by ORBIT's command engine.
6. Code-generation file requests are a special controlled ORBIT workflow. Only C and Python are supported for file generation. You must not claim files have been saved before actual execution succeeds, must not invent filesystem access, and must not write or execute files directly. When asked to generate code, generate clean, complete, beginner-friendly code with necessary imports and entry points, without conversational filler or unnecessary explanations inside source code.");

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

        // Stage 0: Active Multi-Step Code Generation Session Response
        if (PendingClarification != null && PendingClarification.Type == CommandType.GenerateCode && PendingClarification.CodeGenData != null)
        {
            try
            {
                string sessionResult = await HandleCodeGenerationResponseAsync(transcription, PendingClarification, linkedCts.Token).ConfigureAwait(false);
                lock (_stateLock) { IsGenerating = false; }
                _lifecycleManager.NotifyGenerationCompleted();
                return sessionResult;
            }
            catch (Exception ex)
            {
                OrbitLogger.Error(LogCategory, $"Code generation session error: {ex.Message}", ex);
                string err = "Encountered an issue processing your response.";
                GenerationFailed?.Invoke(err);
                lock (_stateLock) { IsGenerating = false; }
                _lifecycleManager.NotifyGenerationCompleted();
                return err;
            }
        }

        // Stage 1: Deterministic Command Classification
        if (_commandParser != null)
        {
            try
            {
                var intent = _commandParser.Parse(transcription);

                // Case 0: Code Generation & File Creation
                if (intent.Type == CommandType.GenerateCode && intent.CodeGenData != null)
                {
                    return await StartCodeGenerationWorkflowAsync(intent, linkedCts.Token).ConfigureAwait(false);
                }

                // Case A: Clarification needed (Ambiguous app name, phonetic uncertainty, compound command)
                if (intent.Type == CommandType.Clarification)
                {
                    PendingClarification = intent;
                    string clarificationMsg = intent.ClarificationPrompt ?? $"Did you mean {intent.Target}?";
                    OrbitLogger.Info(LogCategory, $"Clarification requested: '{clarificationMsg}' (Pending={intent.Target})");

                    ClarificationRequested?.Invoke(intent);
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
            // Verify Ollama health first, attempting server startup or recovery if offline
            bool healthy = await _ollamaClient.CheckServiceHealthAsync(linkedCts.Token).ConfigureAwait(false);
            if (!healthy && _ollamaServerManager != null)
            {
                OrbitLogger.Info(LogCategory, "Ollama client reported offline. Triggering server startup/recovery check...");
                healthy = await _ollamaServerManager.EnsureServerRunningAsync(linkedCts.Token).ConfigureAwait(false);
            }

            if (!healthy)
            {
                string offlineMsg = "Ollama is currently offline. Please ensure Ollama is running on your machine.";
                OrbitLogger.Warn(LogCategory, offlineMsg);
                GenerationFailed?.Invoke(offlineMsg);
                return offlineMsg;
            }

            // Verify model readiness
            if (_ollamaServerManager != null && !_ollamaServerManager.IsModelReady)
            {
                string modelMsg = _ollamaServerManager.ModelDiagnosticMessage ?? "Model 'qwen2.5:3b' is not installed in local Ollama library.";
                OrbitLogger.Warn(LogCategory, modelMsg);
                GenerationFailed?.Invoke(modelMsg);
                return modelMsg;
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
        }

        if (pending == null) return null;

        // If this is an active multi-step code generation confirmation step
        if (pending.Type == CommandType.GenerateCode && pending.CodeGenData != null)
        {
            return await AdvanceCodeGenerationConfirmationAsync(pending).ConfigureAwait(false);
        }

        lock (_stateLock)
        {
            _pendingClarification = null;
        }

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

    private async Task<string> StartCodeGenerationWorkflowAsync(CommandIntent intent, CancellationToken cancellationToken)
    {
        var data = intent.CodeGenData!;

        // 1. Check unsupported language
        if (data.Language == CodeLanguage.Unsupported)
        {
            string unsupportedMsg = $"File generation currently supports C and Python only ({data.UnsupportedLanguageName} is not supported).";
            OrbitLogger.Info(LogCategory, unsupportedMsg);
            TokenReceived?.Invoke(unsupportedMsg);
            GenerationCompleted?.Invoke(unsupportedMsg);
            lock (_stateLock) { IsGenerating = false; }
            _lifecycleManager.NotifyGenerationCompleted();
            return unsupportedMsg;
        }

        // 2. Check omitted / unknown language
        if (data.Language == CodeLanguage.Unknown)
        {
            var updatedData = data with { Stage = CodeGenerationStage.AwaitingLanguageClarification };
            var updatedIntent = intent with { CodeGenData = updatedData, ClarificationPrompt = "Which language would you like to use, C or Python?" };
            PendingClarification = updatedIntent;

            string prompt = updatedIntent.ClarificationPrompt;
            OrbitLogger.Info(LogCategory, $"Prompting language choice: '{prompt}'");
            ClarificationRequested?.Invoke(updatedIntent);
            lock (_stateLock) { IsGenerating = false; }
            _lifecycleManager.NotifyGenerationCompleted();
            return prompt;
        }

        // 3. Validate Destination
        var (isValid, canonical, displayLocation, error) = _codeGenService.ValidateAndResolveDestination(data.DestinationFolder);
        if (!isValid || string.IsNullOrEmpty(canonical))
        {
            string rejectMsg = $"I can only save files to your Desktop or Documents folders for security. ({error ?? "Invalid path"})";
            OrbitLogger.Warn(LogCategory, $"Rejected destination '{data.DestinationFolder}': {rejectMsg}");
            TokenReceived?.Invoke(rejectMsg);
            GenerationCompleted?.Invoke(rejectMsg);
            lock (_stateLock) { IsGenerating = false; }
            _lifecycleManager.NotifyGenerationCompleted();
            return rejectMsg;
        }

        // 4. Check if directory exists
        if (!_codeGenService.DirectoryExists(canonical))
        {
            string folderName = Path.GetFileName(canonical.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string rootName = displayLocation?.Split('→')[0].Trim() ?? "Desktop";
            string prompt = $"The {folderName} folder doesn't exist on your {rootName}. Should I create it?";

            var updatedData = data with
            {
                Stage = CodeGenerationStage.AwaitingFolderCreationConfirmation,
                ResolvedFolderPath = canonical,
                DisplayLocation = displayLocation
            };
            var updatedIntent = intent with { CodeGenData = updatedData, ClarificationPrompt = prompt };
            PendingClarification = updatedIntent;

            OrbitLogger.Info(LogCategory, $"Prompting folder creation: '{prompt}'");
            ClarificationRequested?.Invoke(updatedIntent);
            lock (_stateLock) { IsGenerating = false; }
            _lifecycleManager.NotifyGenerationCompleted();
            return prompt;
        }

        // 5. Directory exists -> Prompt destination confirmation
        {
            string prompt = $"Save it in {displayLocation}. Is that correct?";
            var updatedData = data with
            {
                Stage = CodeGenerationStage.AwaitingDestinationConfirmation,
                ResolvedFolderPath = canonical,
                DisplayLocation = displayLocation
            };
            var updatedIntent = intent with { CodeGenData = updatedData, ClarificationPrompt = prompt };
            PendingClarification = updatedIntent;

            OrbitLogger.Info(LogCategory, $"Prompting destination confirmation: '{prompt}'");
            ClarificationRequested?.Invoke(updatedIntent);
            lock (_stateLock) { IsGenerating = false; }
            _lifecycleManager.NotifyGenerationCompleted();
            return prompt;
        }
    }

    private async Task<CommandResult?> AdvanceCodeGenerationConfirmationAsync(CommandIntent pending)
    {
        var data = pending.CodeGenData!;

        switch (data.Stage)
        {
            case CodeGenerationStage.AwaitingFolderCreationConfirmation:
            {
                bool created = _codeGenService.CreateDirectorySafely(data.ResolvedFolderPath!);
                if (!created)
                {
                    string failMsg = "Could not create the folder. Where would you like to save it instead?";
                    TokenReceived?.Invoke(failMsg);
                    GenerationCompleted?.Invoke(failMsg);
                    return new CommandResult(false, failMsg, pending);
                }

                string fileName = data.ProposedFileName ?? _codeGenService.SuggestFileName(data.Purpose, data.Language);
                string prompt = $"Folder created. I'll name it `{fileName}`. Is that okay?";

                var nextData = data with
                {
                    Stage = CodeGenerationStage.AwaitingFileNameConfirmation,
                    ProposedFileName = fileName
                };
                var nextIntent = pending with { CodeGenData = nextData, ClarificationPrompt = prompt };
                PendingClarification = nextIntent;

                ClarificationRequested?.Invoke(nextIntent);
                return new CommandResult(true, prompt, nextIntent);
            }

            case CodeGenerationStage.AwaitingDestinationConfirmation:
            {
                string fileName = data.ProposedFileName ?? _codeGenService.SuggestFileName(data.Purpose, data.Language);
                string prompt = $"I'll name it `{fileName}`. Is that okay?";

                var nextData = data with
                {
                    Stage = CodeGenerationStage.AwaitingFileNameConfirmation,
                    ProposedFileName = fileName
                };
                var nextIntent = pending with { CodeGenData = nextData, ClarificationPrompt = prompt };
                PendingClarification = nextIntent;

                ClarificationRequested?.Invoke(nextIntent);
                return new CommandResult(true, prompt, nextIntent);
            }

            case CodeGenerationStage.AwaitingFileNameConfirmation:
            {
                string targetFile = Path.Combine(data.ResolvedFolderPath!, data.ProposedFileName!);
                if (_codeGenService.FileExists(targetFile))
                {
                    string conflictPrompt = $"`{data.ProposedFileName}` already exists. Do you want to overwrite it?";
                    var nextData = data with { Stage = CodeGenerationStage.AwaitingOverwriteConfirmation };
                    var nextIntent = pending with { CodeGenData = nextData, ClarificationPrompt = conflictPrompt };
                    PendingClarification = nextIntent;

                    ClarificationRequested?.Invoke(nextIntent);
                    return new CommandResult(true, conflictPrompt, nextIntent);
                }

                PendingClarification = null;
                ClarificationDismissed?.Invoke();
                return await ExecuteCodeGenerationAsync(data).ConfigureAwait(false);
            }

            case CodeGenerationStage.AwaitingOverwriteConfirmation:
            {
                PendingClarification = null;
                ClarificationDismissed?.Invoke();
                return await ExecuteCodeGenerationAsync(data with { OverwriteApproved = true }).ConfigureAwait(false);
            }

            default:
                return null;
        }
    }

    private async Task<string> HandleCodeGenerationResponseAsync(string text, CommandIntent pending, CancellationToken cancellationToken)
    {
        var data = pending.CodeGenData!;
        string cleanSpoken = text.Trim().ToLowerInvariant();

        OrbitLogger.Info(LogCategory, $"Handling code generation response in stage {data.Stage}: '{text}'");

        // 1. Redirection check
        if (_commandParser != null)
        {
            var checkIntent = _commandParser.Parse(text);
            if (checkIntent.Type != CommandType.Unknown && checkIntent.Type != CommandType.GenerateCode && checkIntent.Confidence >= 0.85f)
            {
                OrbitLogger.Info(LogCategory, $"User redirected conversation away from code generation to: {checkIntent.Type}");
                PendingClarification = null;
                ClarificationDismissed?.Invoke();
                if (_commandExecutor != null)
                {
                    var res = await _commandExecutor.ExecuteAsync(checkIntent, cancellationToken).ConfigureAwait(false);
                    TokenReceived?.Invoke(res.Message);
                    GenerationCompleted?.Invoke(res.Message);
                    return res.Message;
                }
            }
        }

        // 2. Affirmative check
        if (IsAffirmativeResponse(text))
        {
            var res = await AdvanceCodeGenerationConfirmationAsync(pending).ConfigureAwait(false);
            return res?.Message ?? string.Empty;
        }

        // 3. Stage-specific logic:
        switch (data.Stage)
        {
            case CodeGenerationStage.AwaitingLanguageClarification:
            {
                var lang = _codeGenService.ParseLanguage(text, out string? unsupp);
                if (lang == CodeLanguage.Unsupported)
                {
                    string msg = $"File generation currently supports C and Python only ({unsupp} is not supported). Which would you like?";
                    TokenReceived?.Invoke(msg);
                    GenerationCompleted?.Invoke(msg);
                    return msg;
                }
                if (lang == CodeLanguage.C || lang == CodeLanguage.Python)
                {
                    var nextIntent = pending with { CodeGenData = data with { Language = lang } };
                    PendingClarification = null;
                    return await StartCodeGenerationWorkflowAsync(nextIntent, cancellationToken).ConfigureAwait(false);
                }

                string reprompt = "Please specify C or Python.";
                TokenReceived?.Invoke(reprompt);
                GenerationCompleted?.Invoke(reprompt);
                return reprompt;
            }

            case CodeGenerationStage.AwaitingDestinationConfirmation:
            case CodeGenerationStage.AwaitingFolderCreationConfirmation:
            {
                if (IsNegativeResponse(text))
                {
                    string askWhere = "Where would you like to save it instead?";
                    var nextIntent = pending with { ClarificationPrompt = askWhere };
                    PendingClarification = nextIntent;
                    ClarificationRequested?.Invoke(nextIntent);
                    return askWhere;
                }

                string corrected = CleanDestinationCorrection(text);
                var (isValid, canonical, displayLocation, error) = _codeGenService.ValidateAndResolveDestination(corrected);
                if (!isValid || string.IsNullOrEmpty(canonical))
                {
                    string invalidMsg = $"Invalid location: {error} Please choose Desktop or Documents.";
                    TokenReceived?.Invoke(invalidMsg);
                    GenerationCompleted?.Invoke(invalidMsg);
                    return invalidMsg;
                }

                if (!_codeGenService.DirectoryExists(canonical))
                {
                    string folderName = Path.GetFileName(canonical.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    string rootName = displayLocation?.Split('→')[0].Trim() ?? "Desktop";
                    string prompt = $"The {folderName} folder doesn't exist on your {rootName}. Should I create it?";

                    var nextData = data with
                    {
                        Stage = CodeGenerationStage.AwaitingFolderCreationConfirmation,
                        DestinationFolder = corrected,
                        ResolvedFolderPath = canonical,
                        DisplayLocation = displayLocation
                    };
                    var nextIntent = pending with { CodeGenData = nextData, ClarificationPrompt = prompt };
                    PendingClarification = nextIntent;

                    ClarificationRequested?.Invoke(nextIntent);
                    return prompt;
                }
                else
                {
                    string prompt = $"Save it in {displayLocation}. Is that correct?";
                    var nextData = data with
                    {
                        Stage = CodeGenerationStage.AwaitingDestinationConfirmation,
                        DestinationFolder = corrected,
                        ResolvedFolderPath = canonical,
                        DisplayLocation = displayLocation
                    };
                    var nextIntent = pending with { CodeGenData = nextData, ClarificationPrompt = prompt };
                    PendingClarification = nextIntent;

                    ClarificationRequested?.Invoke(nextIntent);
                    return prompt;
                }
            }

            case CodeGenerationStage.AwaitingFileNameConfirmation:
            {
                if (IsNegativeResponse(text))
                {
                    string askName = "What filename would you like to use?";
                    var nextIntent = pending with { ClarificationPrompt = askName };
                    PendingClarification = nextIntent;
                    ClarificationRequested?.Invoke(nextIntent);
                    return askName;
                }

                string candidateName = CleanFileNameCorrection(text);
                var (isValid, sanitized, error) = _codeGenService.ValidateFileName(candidateName, data.Language);
                if (!isValid || string.IsNullOrEmpty(sanitized))
                {
                    string invalidMsg = $"Invalid filename: {error}";
                    TokenReceived?.Invoke(invalidMsg);
                    GenerationCompleted?.Invoke(invalidMsg);
                    return invalidMsg;
                }

                string targetFile = Path.Combine(data.ResolvedFolderPath!, sanitized);
                if (_codeGenService.FileExists(targetFile))
                {
                    string conflictPrompt = $"`{sanitized}` already exists. Do you want to overwrite it?";
                    var nextData = data with { ProposedFileName = sanitized, Stage = CodeGenerationStage.AwaitingOverwriteConfirmation };
                    var nextIntent = pending with { CodeGenData = nextData, ClarificationPrompt = conflictPrompt };
                    PendingClarification = nextIntent;

                    ClarificationRequested?.Invoke(nextIntent);
                    return conflictPrompt;
                }
                else
                {
                    string prompt = $"I'll name it `{sanitized}`. Is that okay?";
                    var nextData = data with { ProposedFileName = sanitized, Stage = CodeGenerationStage.AwaitingFileNameConfirmation };
                    var nextIntent = pending with { CodeGenData = nextData, ClarificationPrompt = prompt };
                    PendingClarification = nextIntent;

                    ClarificationRequested?.Invoke(nextIntent);
                    return prompt;
                }
            }

            case CodeGenerationStage.AwaitingOverwriteConfirmation:
            {
                if (cleanSpoken.Contains("overwrite") || cleanSpoken.Contains("replace") || IsAffirmativeResponse(text))
                {
                    PendingClarification = null;
                    ClarificationDismissed?.Invoke();
                    var res = await ExecuteCodeGenerationAsync(data with { OverwriteApproved = true }).ConfigureAwait(false);
                    return res.Message;
                }

                if (cleanSpoken.Contains("save as new") || cleanSpoken.Contains("new") || cleanSpoken.Contains("another") || cleanSpoken.Contains("rename"))
                {
                    string newName = _codeGenService.GetNextAvailableFileName(data.ResolvedFolderPath!, data.ProposedFileName!);
                    PendingClarification = null;
                    ClarificationDismissed?.Invoke();
                    var res = await ExecuteCodeGenerationAsync(data with { ProposedFileName = newName }).ConfigureAwait(false);
                    return res.Message;
                }

                PendingClarification = null;
                ClarificationDismissed?.Invoke();
                string cancelMsg = "File creation cancelled.";
                TokenReceived?.Invoke(cancelMsg);
                GenerationCompleted?.Invoke(cancelMsg);
                return cancelMsg;
            }

            default:
                PendingClarification = null;
                ClarificationDismissed?.Invoke();
                return string.Empty;
        }
    }

    private static bool IsAffirmativeResponse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        string clean = text.Trim().ToLowerInvariant();
        char[] toTrim = ['.', '?', '!', ',', '"', '\'', ';', ':', '-', '—', '~', '`'];
        clean = clean.Trim(toTrim).Trim();

        var exacts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "yes", "yeah", "yep", "sure", "correct", "okay", "ok", "confirm",
            "proceed", "do it", "create it", "create the folder", "create folder",
            "overwrite", "replace"
        };
        if (exacts.Contains(clean)) return true;

        // Starts with affirmative prefix: e.g. "yes please", "yes create the folder", "sure go ahead", "yeah do that"
        if (Regex.IsMatch(clean, @"^(?:yes|yeah|yep|sure|okay|ok|correct|confirm)\b", RegexOptions.IgnoreCase))
        {
            if (!Regex.IsMatch(clean, @"\b(?:no|not|don't|dont|cancel|stop)\b", RegexOptions.IgnoreCase))
            {
                return true;
            }
        }

        // Action confirmation phrases: "please create the folder", "create the required folder", "go ahead and create it"
        if (Regex.IsMatch(clean, @"^(?:please\s+)?(?:go\s+ahead\s+(?:and\s+)?)?create\s+(?:the\s+)?(?:required\s+)?folder\b", RegexOptions.IgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool IsNegativeResponse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        string clean = text.Trim().ToLowerInvariant();
        char[] toTrim = ['.', '?', '!', ',', '"', '\'', ';', ':', '-', '—', '~', '`'];
        clean = clean.Trim(toTrim).Trim();

        var exacts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "no", "nope", "nah", "cancel", "stop", "don't", "dont", "abort",
            "never mind", "nevermind", "no cancel", "no stop", "no don't"
        };
        if (exacts.Contains(clean)) return true;

        // If it starts with "no" / "nope", check if it provides an alternative destination/filename
        if (Regex.IsMatch(clean, @"^(?:no|nope|nah)\b", RegexOptions.IgnoreCase))
        {
            // If it mentions saving, paths, directories, or renaming, it's a correction, NOT a bare negative
            if (Regex.IsMatch(clean, @"\b(?:save|put|desktop|documents|folder|directory|drive|name|call|[a-z]:)\b", RegexOptions.IgnoreCase))
            {
                return false;
            }

            // Pure cancellation phrase: e.g. "no don't", "no stop"
            if (Regex.IsMatch(clean, @"^(?:no|nope|nah)\s+(?:don't|dont|stop|cancel|abort)\b", RegexOptions.IgnoreCase))
            {
                return true;
            }

            // Very short phrase (e.g. "no please", "no thanks", "no thank you")
            if (Regex.IsMatch(clean, @"^(?:no|nope|nah)(?:\s+(?:please|thanks|thank\s+you|bro|buddy))?$", RegexOptions.IgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string CleanDestinationCorrection(string text)
    {
        string cleaned = Regex.Replace(text, @"^(?:no[, ]*|nope[, ]*|nah[, ]*|actually[, ]*|instead[, ]*)", "", RegexOptions.IgnoreCase).Trim();
        cleaned = Regex.Replace(cleaned, @"^(?:save(?:\s+it)?\s+(?:in|into|to|on)\s+|put(?:\s+it)?\s+(?:in|into|to|on)\s+|in\s+|on\s+)", "", RegexOptions.IgnoreCase).Trim();
        cleaned = Regex.Replace(cleaned, @"\s+instead$", "", RegexOptions.IgnoreCase).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? text.Trim() : cleaned;
    }

    private static string CleanFileNameCorrection(string text)
    {
        string cleaned = Regex.Replace(text, @"^(?:no[, ]*|nope[, ]*|nah[, ]*|actually[, ]*)", "", RegexOptions.IgnoreCase).Trim();
        cleaned = Regex.Replace(cleaned, @"^(?:name\s+it\s+|call\s+it\s+|save(?:\s+it)?\s+as\s+)", "", RegexOptions.IgnoreCase).Trim();
        cleaned = Regex.Replace(cleaned, @"\s+instead$", "", RegexOptions.IgnoreCase).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? text.Trim() : cleaned;
    }

    private async Task<CommandResult> ExecuteCodeGenerationAsync(CodeGenerationIntentData data)
    {
        lock (_stateLock)
        {
            IsGenerating = true;
        }

        _lifecycleManager.NotifyGenerationStarted();
        GenerationStarted?.Invoke();

        var resultIntent = new CommandIntent(CommandType.GenerateCode, data.Purpose, 1.0f, Scope: data.DestinationFolder, Target: data.ProposedFileName);
        try
        {
            var result = await _codeGenService.GenerateAndWriteFileAsync(
                data.ResolvedFolderPath!,
                data.ProposedFileName!,
                data.DisplayLocation ?? data.DestinationFolder,
                data.Purpose,
                data.Language,
                _ollamaClient,
                CancellationToken.None).ConfigureAwait(false);

            if (result.Success)
            {
                TokenReceived?.Invoke(result.Message);
                GenerationCompleted?.Invoke(result.Message);
                return new CommandResult(true, result.Message, resultIntent, result.FilePath);
            }
            else
            {
                GenerationFailed?.Invoke(result.Message);
                return new CommandResult(false, result.Message, resultIntent, result.Error);
            }
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, $"Code generation failed: {ex.Message}", ex);
            string errMsg = $"Failed to create file: {ex.Message}";
            GenerationFailed?.Invoke(errMsg);
            return new CommandResult(false, errMsg, resultIntent, ex.Message);
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

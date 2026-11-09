namespace Orbit.Services.Commands;

/// <summary>
/// Broad classification of voice command intent.
/// </summary>
public enum CommandType
{
    Unknown,
    LaunchApp,
    WebSearch,
    WebsiteSearch,
    FileSearch,
    SystemControl,
    Screenshot,
    TakeNote,
    Clarification,
    SystemMetrics,
    GenerateCode
}

/// <summary>
/// Supported system volume and audio control actions.
/// </summary>
public enum SystemControlAction
{
    SetVolume,
    IncreaseVolume,
    DecreaseVolume,
    Mute,
    Unmute
}

/// <summary>
/// Subtype of system metrics query (RAM, CPU, Battery, All).
/// </summary>
public enum SystemMetricsSubtype
{
    All,
    Ram,
    Cpu,
    Battery,
    Gpu
}

/// <summary>
/// Deterministic, structured voice command intent parsed from speech transcription.
/// </summary>
public record CommandIntent(
    CommandType Type,
    string RawText,
    float Confidence,
    string? Target = null,
    SystemControlAction? ControlAction = null,
    int? Value = null,
    string? Scope = null,
    string? Query = null,
    CommandIntent? PendingAction = null,
    string? ClarificationPrompt = null,
    SystemMetricsSubtype? MetricsSubtype = null,
    Orbit.Services.CodeGeneration.CodeGenerationIntentData? CodeGenData = null
);


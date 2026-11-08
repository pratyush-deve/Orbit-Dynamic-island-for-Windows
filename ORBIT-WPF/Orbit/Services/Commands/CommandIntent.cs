namespace Orbit.Services.Commands;

/// <summary>
/// Broad classification of voice command intent.
/// </summary>
public enum CommandType
{
    Unknown,
    LaunchApp,
    WebSearch,
    FileSearch,
    SystemControl,
    Screenshot,
    TakeNote
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
/// Deterministic, structured voice command intent parsed from speech transcription.
/// </summary>
public record CommandIntent(
    CommandType Type,
    string RawText,
    float Confidence,
    string? Target = null,
    SystemControlAction? ControlAction = null,
    int? Value = null,
    string? Scope = null
);


namespace Orbit.Services.Voice;

/// <summary>
/// Lifecycle states for an ORBIT voice interaction session.
/// </summary>
public enum VoiceSessionState
{
    /// <summary>
    /// Microphone is inactive; companion is not in voice mode.
    /// </summary>
    Idle,

    /// <summary>
    /// Actively recording from the default audio capture endpoint and analyzing intensity/silence.
    /// </summary>
    Listening,

    /// <summary>
    /// Speech detected and followed by silence; utterance capture completed.
    /// </summary>
    Completed,

    /// <summary>
    /// Speech detected and captured; offline Whisper transcription in progress.
    /// </summary>
    Processing,

    /// <summary>
    /// Session was explicitly aborted or companion was hidden via HP key/relaunch.
    /// </summary>
    Cancelled,

    /// <summary>
    /// Audio hardware was unavailable, permission was denied, or initialization failed.
    /// </summary>
    Error
}


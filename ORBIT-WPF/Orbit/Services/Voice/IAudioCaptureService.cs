using System;
using System.IO;

namespace Orbit.Services.Voice;

/// <summary>
/// Service contract for in-memory microphone audio capture and live RMS metering.
/// Audio is kept strictly in volatile memory and never written to disk or transmitted.
/// </summary>
public interface IAudioCaptureService : IDisposable
{
    /// <summary>
    /// Whether audio is actively capturing from the default microphone endpoint.
    /// </summary>
    bool IsCapturing { get; }

    /// <summary>
    /// Event raised periodically when a chunk of audio has been processed, providing the latest normalized RMS intensity (0.0 to 1.0).
    /// </summary>
    event EventHandler<float>? AudioLevelUpdated;

    /// <summary>
    /// Event raised when an error occurs during capture (e.g. device disconnected or access denied).
    /// </summary>
    event EventHandler<string>? CaptureErrorOccurred;

    /// <summary>
    /// Begins capturing from the default microphone into an internal memory buffer.
    /// </summary>
    /// <returns>True if capture started successfully, false otherwise.</returns>
    bool StartCapture();

    /// <summary>
    /// Stops audio capture and returns the buffered 16kHz 16-bit mono PCM audio in memory.
    /// </summary>
    /// <returns>A read-only copy of the captured PCM bytes.</returns>
    byte[] StopCapture();

    /// <summary>
    /// Stops capture and immediately clears/discards any recorded audio from memory.
    /// </summary>
    void CancelCapture();
}


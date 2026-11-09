using System;
using System.Threading;
using System.Threading.Tasks;

namespace Orbit.Services.Voice;

/// <summary>
/// Service providing local, offline speech-to-text inference using Whisper.net and whisper.cpp.
/// </summary>
public interface IWhisperTranscriptionService : IDisposable
{
    /// <summary>
    /// Gets whether the Whisper model is currently loaded in memory and ready for inference.
    /// </summary>
    bool IsModelLoaded { get; }

    /// <summary>
    /// Gets the filename of the currently loaded Whisper GGML model (e.g. "ggml-small.en.bin").
    /// </summary>
    string LoadedModelName { get; }

    /// <summary>
    /// Preloads and initializes the Whisper factory and processor asynchronously if not already loaded.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Transcribes a raw 16 kHz 16-bit mono PCM audio buffer held in RAM.
    /// Returns the cleaned English text recognized from speech, or empty string if no speech was detected.
    /// </summary>
    Task<string> TranscribePcm16Async(byte[] pcmData, CancellationToken cancellationToken = default);
}


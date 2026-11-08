using System.Threading;
using System.Threading.Tasks;

namespace Orbit.Services.Voice;

/// <summary>
/// Manages the local lifecycle, storage location, and verification of the Whisper GGML speech model.
/// </summary>
public interface IWhisperModelManager
{
    /// <summary>
    /// Gets the absolute local file path where the default Whisper Base.en GGML model is stored.
    /// </summary>
    string ModelPath { get; }

    /// <summary>
    /// Gets the absolute local file path where the fallback Whisper Tiny.en GGML model is stored.
    /// </summary>
    string FallbackModelPath { get; }

    /// <summary>
    /// Checks whether the default Base model exists on local disk and is fully downloaded.
    /// </summary>
    bool IsModelAvailable { get; }

    /// <summary>
    /// Checks whether the fallback Tiny model exists on local disk and is valid.
    /// </summary>
    bool IsFallbackAvailable { get; }

    /// <summary>
    /// Ensures the default Base model is present on disk (downloading it if missing).
    /// If Base model download fails, falls back gracefully to Tiny if available.
    /// Returns the verified absolute path to the model file to use.
    /// </summary>
    Task<string> EnsureModelAvailableAsync(CancellationToken cancellationToken = default);
}


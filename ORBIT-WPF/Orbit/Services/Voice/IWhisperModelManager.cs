using System.Threading;
using System.Threading.Tasks;

namespace Orbit.Services.Voice;

/// <summary>
/// Manages the local lifecycle, storage location, and verification of the Whisper GGML speech model.
/// </summary>
public interface IWhisperModelManager
{
    /// <summary>
    /// Gets the absolute local file path where the primary Whisper Small.en GGML model is stored.
    /// </summary>
    string ModelPath { get; }

    /// <summary>
    /// Gets the absolute local file path where the fallback Whisper Base.en or Tiny.en GGML model is stored.
    /// </summary>
    string FallbackModelPath { get; }

    /// <summary>
    /// Checks whether the primary Small model exists on local disk and is valid.
    /// </summary>
    bool IsModelAvailable { get; }

    /// <summary>
    /// Checks whether any fallback model (Base or Tiny) exists on local disk and is valid.
    /// </summary>
    bool IsFallbackAvailable { get; }

    /// <summary>
    /// Ensures the primary Small model is present on disk (downloading it if missing).
    /// If Small model download fails, falls back gracefully to Base or Tiny if available.
    /// Returns the verified absolute path to the model file to use.
    /// </summary>
    Task<string> EnsureModelAvailableAsync(CancellationToken cancellationToken = default);
}


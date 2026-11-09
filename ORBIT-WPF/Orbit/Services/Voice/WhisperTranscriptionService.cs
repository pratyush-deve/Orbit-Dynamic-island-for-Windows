using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Core;
using Whisper.net;

namespace Orbit.Services.Voice;

/// <summary>
/// Dedicated offline speech-to-text service integrating Whisper.net and whisper.cpp.
/// Uses a cached WhisperFactory and processor on the CPU, running inference off the UI thread.
/// </summary>
public sealed class WhisperTranscriptionService : IWhisperTranscriptionService
{
    private const string LogCategory = "WHISPER_SERVICE";

    private readonly IWhisperModelManager _modelManager;
    private readonly SemaphoreSlim _inferenceLock = new(1, 1);

    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private string _loadedModelName = "None";
    private string _loadedModelPath = string.Empty;
    private bool _disposed;

    public bool IsModelLoaded => _processor != null && !_disposed;
    public string LoadedModelName => _loadedModelName;
    public string LoadedModelPath => _loadedModelPath;

    public WhisperTranscriptionService(IWhisperModelManager modelManager)
    {
        _modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (IsModelLoaded) return;

        await _inferenceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await InitializeInternalAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _inferenceLock.Release();
        }
    }

    private async Task InitializeInternalAsync(CancellationToken cancellationToken)
    {
        if (IsModelLoaded || _disposed) return;

        var sw = Stopwatch.StartNew();
        OrbitLogger.Info(LogCategory, $"Starting Whisper speech recognition engine initialization (Target: {_modelManager.ModelPath})...");

        string modelPath = await _modelManager.EnsureModelAvailableAsync(cancellationToken).ConfigureAwait(false);

        // Run factory and processor creation on thread pool to avoid blocking callers
        await Task.Run(() =>
        {
            _processor?.Dispose();
            _factory?.Dispose();

            try
            {
                _factory = WhisperFactory.FromPath(modelPath);
            }
            catch (Exception ex) when (!modelPath.Equals(_modelManager.FallbackModelPath, StringComparison.OrdinalIgnoreCase) && _modelManager.IsFallbackAvailable)
            {
                OrbitLogger.Warn(LogCategory, $"Failed to load primary model '{modelPath}': {ex.Message}. Falling back to '{_modelManager.FallbackModelPath}'.");
                modelPath = _modelManager.FallbackModelPath;
                _factory = WhisperFactory.FromPath(modelPath);
            }

            int threadCount = Math.Clamp(Environment.ProcessorCount / 2, 2, 4);

            _processor = _factory.CreateBuilder()
                .WithLanguage("en")
                .WithSingleSegment()
                .WithNoContext()
                .WithThreads(threadCount)
                .WithGreedySamplingStrategy(s => s.WithBestOf(1))
                .Build();

            _loadedModelName = Path.GetFileName(modelPath);
            _loadedModelPath = modelPath;

        }, cancellationToken).ConfigureAwait(false);

        sw.Stop();
        OrbitLogger.Info(LogCategory, $"Whisper engine ({_loadedModelName}) initialized successfully in {sw.ElapsedMilliseconds}ms. Path: {_loadedModelPath}");
    }

    public async Task<string> TranscribePcm16Async(byte[] pcmData, CancellationToken cancellationToken = default)
    {
        if (pcmData == null || pcmData.Length < 3200) // Less than ~100ms of audio (16kHz * 2 bytes * 0.1s)
        {
            return string.Empty;
        }

        await _inferenceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WhisperTranscriptionService));

            if (!IsModelLoaded)
            {
                await InitializeInternalAsync(cancellationToken).ConfigureAwait(false);
            }

            if (_processor == null)
            {
                OrbitLogger.Warn(LogCategory, "Whisper processor is unavailable for transcription.");
                return string.Empty;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var floatSamples = ConvertPcm16ToFloat(pcmData);
            if (floatSamples.Length == 0) return string.Empty;

            var sb = new StringBuilder();
            var sw = Stopwatch.StartNew();

            await foreach (var segment in _processor.ProcessAsync(floatSamples, cancellationToken).ConfigureAwait(false))
            {
                if (!string.IsNullOrWhiteSpace(segment.Text))
                {
                    sb.Append(segment.Text).Append(' ');
                }
            }

            sw.Stop();
            string raw = sb.ToString();
            string cleaned = CleanRecognizedText(raw);
            OrbitLogger.Info(LogCategory, $"[Model: {_loadedModelName}] Transcription completed in {sw.ElapsedMilliseconds}ms: '{cleaned}' (raw: '{raw.Trim()}', Audio length: {floatSamples.Length / 16000.0:F2}s)");

            return cleaned;
        }
        catch (OperationCanceledException)
        {
            OrbitLogger.Debug(LogCategory, "Whisper transcription cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, $"Whisper transcription encountered error: {ex.Message}", ex);
            return string.Empty;
        }
        finally
        {
            _inferenceLock.Release();
        }
    }

    /// <summary>
    /// Converts a 16 kHz 16-bit signed PCM byte array to normalized float samples (-1.0 to 1.0)
    /// without creating intermediate audio files or writing to disk.
    /// </summary>
    public static float[] ConvertPcm16ToFloat(byte[] pcmData)
    {
        if (pcmData == null || pcmData.Length < 2) return Array.Empty<float>();

        int sampleCount = pcmData.Length / 2;
        var samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            short pcm = (short)(pcmData[i * 2] | (pcmData[i * 2 + 1] << 8));
            samples[i] = pcm / 32768.0f;
        }

        return samples;
    }

    /// <summary>
    /// Cleans recognized Whisper text, removing hallucinated blank tokens, quotes, and leading/trailing punctuation.
    /// </summary>
    public static string CleanRecognizedText(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        string trimmed = raw.Trim();

        // Suppress common Whisper hallucination artifacts on silence/short noise
        if (trimmed.Equals("[BLANK_AUDIO]", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("[MUSIC]", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("(silence)", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("you", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Thank you.", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Thanks for watching!", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        // Clean leading/trailing quotes and punctuation for crisp command matching
        trimmed = trimmed.Trim('"', '\'', '`', '.', ',', '!', '?', ' ');

        return trimmed;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _processor?.Dispose();
            _factory?.Dispose();
            _inferenceLock.Dispose();
        }
        catch (Exception ex)
        {
            OrbitLogger.Warn(LogCategory, $"Error disposing Whisper resources: {ex.Message}");
        }
    }
}


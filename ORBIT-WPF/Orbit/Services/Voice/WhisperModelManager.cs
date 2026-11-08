using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Core;
using Whisper.net.Ggml;

namespace Orbit.Services.Voice;

/// <summary>
/// Persistent local storage manager for Whisper GGML model files.
/// Guarantees that models are stored in %LOCALAPPDATA%\ORBIT\models, downloaded only once,
/// and verified before use.
/// </summary>
public sealed class WhisperModelManager : IWhisperModelManager
{
    private const string LogCategory = "WHISPER_MODEL";
    private const string BaseFileName = "ggml-base.en.bin";
    private const string TinyFileName = "ggml-tiny.en.bin";
    private const long MinExpectedBytesBase = 120 * 1024 * 1024; // ~120MB minimum sanity check for base.en (actual ~142MB)
    private const long MinExpectedBytesTiny = 50 * 1024 * 1024;  // ~50MB minimum sanity check for tiny.en (actual ~75MB)

    private readonly string _modelsDirectory;
    private readonly string _baseModelPath;
    private readonly string _tinyModelPath;
    private readonly SemaphoreSlim _downloadLock = new(1, 1);

    public string ModelPath => _baseModelPath;
    public string FallbackModelPath => _tinyModelPath;

    public bool IsModelAvailable
    {
        get
        {
            try
            {
                if (!File.Exists(_baseModelPath)) return false;
                var info = new FileInfo(_baseModelPath);
                return info.Length >= MinExpectedBytesBase;
            }
            catch
            {
                return false;
            }
        }
    }

    public bool IsFallbackAvailable
    {
        get
        {
            try
            {
                if (!File.Exists(_tinyModelPath)) return false;
                var info = new FileInfo(_tinyModelPath);
                return info.Length >= MinExpectedBytesTiny;
            }
            catch
            {
                return false;
            }
        }
    }

    public WhisperModelManager()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _modelsDirectory = Path.Combine(localAppData, "ORBIT", "models");
        _baseModelPath = Path.Combine(_modelsDirectory, BaseFileName);
        _tinyModelPath = Path.Combine(_modelsDirectory, TinyFileName);
    }

    public async Task<string> EnsureModelAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (IsModelAvailable)
        {
            OrbitLogger.Debug(LogCategory, $"Whisper Base model already exists locally at: {_baseModelPath}");
            return _baseModelPath;
        }

        await _downloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsModelAvailable)
            {
                return _baseModelPath;
            }

            Directory.CreateDirectory(_modelsDirectory);
            string tempPath = _baseModelPath + ".tmp";

            OrbitLogger.Info(LogCategory, $"Whisper Base English model missing. Initiating download to {tempPath}...");

            try
            {
                using (var downloadStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(
                           GgmlType.BaseEn,
                           cancellationToken: cancellationToken).ConfigureAwait(false))
                using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    await downloadStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
                }

                var tempInfo = new FileInfo(tempPath);
                if (tempInfo.Length < MinExpectedBytesBase)
                {
                    throw new InvalidOperationException($"Downloaded Base model file is incomplete ({tempInfo.Length} bytes).");
                }

                if (File.Exists(_baseModelPath))
                {
                    File.Delete(_baseModelPath);
                }

                File.Move(tempPath, _baseModelPath);
                OrbitLogger.Info(LogCategory, $"Whisper Base English model successfully downloaded and stored at: {_baseModelPath} ({tempInfo.Length} bytes).");

                return _baseModelPath;
            }
            catch (Exception ex) when (IsFallbackAvailable)
            {
                OrbitLogger.Warn(LogCategory, $"Failed to download Base model: {ex.Message}. Using existing Tiny fallback model at {_tinyModelPath}");
                CleanupTempFile(tempPath);
                return _tinyModelPath;
            }
        }
        catch (OperationCanceledException)
        {
            OrbitLogger.Warn(LogCategory, "Model download operation was cancelled.");
            CleanupTempFile(_baseModelPath + ".tmp");
            throw;
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, $"Failed to download or verify Whisper Base model: {ex.Message}", ex);
            CleanupTempFile(_baseModelPath + ".tmp");
            throw;
        }
        finally
        {
            _downloadLock.Release();
        }
    }

    private void CleanupTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }
}


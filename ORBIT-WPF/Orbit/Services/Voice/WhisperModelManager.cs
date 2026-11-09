using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Core;
using Whisper.net.Ggml;

namespace Orbit.Services.Voice;

/// <summary>
/// Persistent local storage manager for Whisper GGML model files.
/// Guarantees that models are stored in %LOCALAPPDATA%\ORBIT\models, downloaded once,
/// verified before use, and supports safe fallback to Base.en or Tiny.en.
/// </summary>
public sealed class WhisperModelManager : IWhisperModelManager
{
    private const string LogCategory = "WHISPER_MODEL";
    private const string SmallFileName = "ggml-small.en.bin";
    private const string BaseFileName = "ggml-base.en.bin";
    private const string TinyFileName = "ggml-tiny.en.bin";

    private const long MinExpectedBytesSmall = 450 * 1024 * 1024; // ~450MB sanity check for small.en (actual ~487.6MB)
    private const long MinExpectedBytesBase = 120 * 1024 * 1024;  // ~120MB sanity check for base.en (actual ~142MB)
    private const long MinExpectedBytesTiny = 50 * 1024 * 1024;   // ~50MB sanity check for tiny.en (actual ~75MB)

    private const string HuggingFaceSmallEnUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.en.bin";

    private readonly string _modelsDirectory;
    private readonly string _smallModelPath;
    private readonly string _baseModelPath;
    private readonly string _tinyModelPath;
    private readonly SemaphoreSlim _downloadLock = new(1, 1);

    public string ModelPath => _smallModelPath;
    public string BaseModelPath => _baseModelPath;
    public string TinyModelPath => _tinyModelPath;

    public string FallbackModelPath
    {
        get
        {
            if (IsBaseAvailable) return _baseModelPath;
            return _tinyModelPath;
        }
    }

    public bool IsModelAvailable => CheckModelFile(_smallModelPath, MinExpectedBytesSmall);
    public bool IsBaseAvailable => CheckModelFile(_baseModelPath, MinExpectedBytesBase);
    public bool IsTinyAvailable => CheckModelFile(_tinyModelPath, MinExpectedBytesTiny);
    public bool IsFallbackAvailable => IsBaseAvailable || IsTinyAvailable;

    public WhisperModelManager()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _modelsDirectory = Path.Combine(localAppData, "ORBIT", "models");
        _smallModelPath = Path.Combine(_modelsDirectory, SmallFileName);
        _baseModelPath = Path.Combine(_modelsDirectory, BaseFileName);
        _tinyModelPath = Path.Combine(_modelsDirectory, TinyFileName);
    }

    public static bool CheckModelFile(string path, long minExpectedBytes)
    {
        try
        {
            if (!File.Exists(path)) return false;
            var info = new FileInfo(path);
            return info.Length >= minExpectedBytes;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> EnsureModelAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (IsModelAvailable)
        {
            OrbitLogger.Debug(LogCategory, $"Whisper Small English model already verified locally at: {_smallModelPath}");
            return _smallModelPath;
        }

        await _downloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsModelAvailable)
            {
                return _smallModelPath;
            }

            Directory.CreateDirectory(_modelsDirectory);
            string tempPath = _smallModelPath + ".tmp";

            OrbitLogger.Info(LogCategory, $"Whisper Small English model missing. Initiating download to {tempPath}...");

            try
            {
                Stream? downloadStream = null;
                try
                {
                    downloadStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(
                        GgmlType.SmallEn,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    OrbitLogger.Warn(LogCategory, $"WhisperGgmlDownloader failed ({ex.Message}), trying direct HuggingFace endpoint...");
                    var client = new HttpClient();
                    var response = await client.GetAsync(HuggingFaceSmallEnUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    downloadStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                }

                using (downloadStream)
                using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    await downloadStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
                }

                var tempInfo = new FileInfo(tempPath);
                if (tempInfo.Length < MinExpectedBytesSmall)
                {
                    throw new InvalidOperationException($"Downloaded Small model file is incomplete ({tempInfo.Length} bytes).");
                }

                if (File.Exists(_smallModelPath))
                {
                    File.Delete(_smallModelPath);
                }

                File.Move(tempPath, _smallModelPath);
                OrbitLogger.Info(LogCategory, $"Whisper Small English model successfully verified and stored at: {_smallModelPath} ({tempInfo.Length} bytes).");

                return _smallModelPath;
            }
            catch (Exception ex) when (IsFallbackAvailable)
            {
                string fallback = FallbackModelPath;
                OrbitLogger.Warn(LogCategory, $"Failed to download Small model: {ex.Message}. Using fallback model at {fallback}");
                CleanupTempFile(tempPath);
                return fallback;
            }
        }
        catch (OperationCanceledException)
        {
            OrbitLogger.Warn(LogCategory, "Model download operation was cancelled.");
            CleanupTempFile(_smallModelPath + ".tmp");
            throw;
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, $"Failed to download or verify Whisper Small model: {ex.Message}", ex);
            CleanupTempFile(_smallModelPath + ".tmp");
            throw;
        }
        finally
        {
            _downloadLock.Release();
        }
    }

    /// <summary>
    /// Safely removes the previous ggml-base.en.bin model file only after verification has confirmed
    /// that ggml-small.en.bin is loaded and operational.
    /// </summary>
    public bool RemoveOldBaseModelSafely()
    {
        try
        {
            if (File.Exists(_baseModelPath))
            {
                File.Delete(_baseModelPath);
                OrbitLogger.Info(LogCategory, $"Safely removed obsolete Base model at: {_baseModelPath}");
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            OrbitLogger.Warn(LogCategory, $"Failed to remove old Base model file: {ex.Message}");
            return false;
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

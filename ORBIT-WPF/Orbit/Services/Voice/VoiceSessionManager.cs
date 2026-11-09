using System;
using Orbit.Core;

namespace Orbit.Services.Voice;

/// <summary>
/// Service managing the voice session lifecycle, coordinating audio capture, VAD, and UI state.
/// </summary>
public interface IVoiceSessionManager : IDisposable
{
    VoiceSessionState CurrentState { get; }
    bool IsActive { get; }

    event EventHandler<VoiceSessionState>? StateChanged;
    event EventHandler<float>? AudioLevelUpdated;
    event EventHandler<byte[]>? UtteranceCompleted;
    event EventHandler<string>? TranscriptionReceived;

    bool StartSession();
    void StopSession(string reason = "Manual");
    void CancelSession();
}

public sealed class VoiceSessionManager : IVoiceSessionManager
{
    private const string LogCategory = "VOICE_SESSION";

    private readonly IAudioCaptureService _captureService;
    private readonly IWhisperTranscriptionService? _transcriptionService;
    private readonly VoiceActivityDetector _vad;
    private readonly object _stateLock = new();

    private VoiceSessionState _currentState = VoiceSessionState.Idle;
    private CancellationTokenSource? _transcriptionCts;
    private int _sessionCounter = 0;
    private bool _disposed;

    public VoiceSessionState CurrentState
    {
        get
        {
            lock (_stateLock)
            {
                return _currentState;
            }
        }
        private set
        {
            lock (_stateLock)
            {
                if (_currentState == value) return;
                _currentState = value;
            }
            OrbitLogger.Info(LogCategory, $"VoiceSessionState -> {value}");
            StateChanged?.Invoke(this, value);
        }
    }

    public bool IsActive
    {
        get
        {
            lock (_stateLock)
            {
                return _currentState == VoiceSessionState.Listening || _currentState == VoiceSessionState.Processing;
            }
        }
    }

    public event EventHandler<VoiceSessionState>? StateChanged;
    public event EventHandler<float>? AudioLevelUpdated;
    public event EventHandler<byte[]>? UtteranceCompleted;
    public event EventHandler<string>? TranscriptionReceived;

    public VoiceSessionManager(IAudioCaptureService captureService, IWhisperTranscriptionService? transcriptionService = null)
    {
        _captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
        _transcriptionService = transcriptionService;
        _vad = new VoiceActivityDetector();

        _captureService.AudioLevelUpdated += OnAudioLevelUpdated;
        _captureService.CaptureErrorOccurred += OnCaptureErrorOccurred;

        _vad.SpeechStarted += OnVadSpeechStarted;
        _vad.UtteranceCompleted += OnVadUtteranceCompleted;
    }

    public bool StartSession()
    {
        lock (_stateLock)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VoiceSessionManager));
            if (_currentState == VoiceSessionState.Listening || _currentState == VoiceSessionState.Processing)
            {
                OrbitLogger.Warn(LogCategory, "Voice session is already active; ignoring activation.");
                return false;
            }

            OrbitLogger.Info(LogCategory, "Starting Voice Mode session...");
            Interlocked.Increment(ref _sessionCounter);
            _transcriptionCts?.Cancel();
            _transcriptionCts?.Dispose();
            _transcriptionCts = null;

            _vad.Reset();
            _vad.Start();

            if (!_captureService.StartCapture())
            {
                CurrentState = VoiceSessionState.Error;
                return false;
            }

            CurrentState = VoiceSessionState.Listening;
            return true;
        }
    }

    public void StopSession(string reason = "Manual")
    {
        byte[] recordedPcm;
        int sessionId;

        lock (_stateLock)
        {
            if (_currentState != VoiceSessionState.Listening) return;

            OrbitLogger.Info(LogCategory, $"Stopping voice recording and finalizing utterance (reason: {reason})...");
            _vad.Stop();
            recordedPcm = _captureService.StopCapture();
            sessionId = _sessionCounter;

            UtteranceCompleted?.Invoke(this, recordedPcm);

            // If initial silence timeout (no speech detected) or audio is too short, complete immediately without wasting 6+ seconds of CPU on Whisper!
            if (reason == "InitialSilenceTimeout" || _transcriptionService == null || recordedPcm.Length < 3200)
            {
                OrbitLogger.Info(LogCategory, $"Skipping Whisper transcription (initial silence timeout or short buffer: reason='{reason}', bytes={recordedPcm.Length}).");
                CurrentState = VoiceSessionState.Completed;
                CurrentState = VoiceSessionState.Idle;
                return;
            }

            CurrentState = VoiceSessionState.Processing;

            _transcriptionCts?.Cancel();
            _transcriptionCts?.Dispose();
            _transcriptionCts = new CancellationTokenSource();
        }

        var cts = _transcriptionCts;
        var token = cts.Token;

        // Run Whisper inference off the UI thread
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                string recognized = await _transcriptionService.TranscribePcm16Async(recordedPcm, token).ConfigureAwait(false);

                lock (_stateLock)
                {
                    if (token.IsCancellationRequested || sessionId != _sessionCounter || _currentState != VoiceSessionState.Processing)
                    {
                        OrbitLogger.Debug(LogCategory, "Discarded transcription for cancelled or superseded session.");
                        return;
                    }

                    OrbitLogger.Info(LogCategory, $"Transcription received: '{recognized}'");
                    TranscriptionReceived?.Invoke(this, recognized);
                    CurrentState = VoiceSessionState.Completed;
                    CurrentState = VoiceSessionState.Idle;
                }
            }
            catch (OperationCanceledException)
            {
                OrbitLogger.Debug(LogCategory, "Transcription was cancelled.");
            }
            catch (Exception ex)
            {
                OrbitLogger.Error(LogCategory, $"Transcription failed: {ex.Message}", ex);
                lock (_stateLock)
                {
                    if (sessionId == _sessionCounter)
                    {
                        CurrentState = VoiceSessionState.Error;
                        CurrentState = VoiceSessionState.Idle;
                    }
                }
            }
        });
    }

    public void CancelSession()
    {
        lock (_stateLock)
        {
            if (_currentState == VoiceSessionState.Idle) return;

            OrbitLogger.Info(LogCategory, "Cancelling voice session immediately and purging buffers...");
            Interlocked.Increment(ref _sessionCounter);
            _transcriptionCts?.Cancel();
            _transcriptionCts?.Dispose();
            _transcriptionCts = null;

            _vad.Stop();
            _captureService.CancelCapture();

            CurrentState = VoiceSessionState.Cancelled;
            CurrentState = VoiceSessionState.Idle;
        }
    }

    private void OnAudioLevelUpdated(object? sender, float intensity)
    {
        if (CurrentState != VoiceSessionState.Listening) return;

        _vad.ProcessIntensity(intensity);
        AudioLevelUpdated?.Invoke(this, intensity);
    }

    private void OnCaptureErrorOccurred(object? sender, string errorMessage)
    {
        OrbitLogger.Error(LogCategory, $"Capture error encountered: {errorMessage}");
        lock (_stateLock)
        {
            _vad.Stop();
            CurrentState = VoiceSessionState.Error;
        }
    }

    private void OnVadSpeechStarted(object? sender, EventArgs e)
    {
        OrbitLogger.Info(LogCategory, "Voice utterance actively receiving speech.");
    }

    private void OnVadUtteranceCompleted(object? sender, string reason)
    {
        OrbitLogger.Info(LogCategory, $"VAD signaled completion ({reason}). Finalizing session.");
        StopSession(reason);
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            if (_disposed) return;
            _disposed = true;

            _captureService.AudioLevelUpdated -= OnAudioLevelUpdated;
            _captureService.CaptureErrorOccurred -= OnCaptureErrorOccurred;

            _vad.SpeechStarted -= OnVadSpeechStarted;
            _vad.UtteranceCompleted -= OnVadUtteranceCompleted;

            _vad.Stop();
            _captureService.Dispose();
        }
    }
}


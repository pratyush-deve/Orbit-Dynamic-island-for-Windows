using System;
using System.Diagnostics;
using Orbit.Core;

namespace Orbit.Services.Voice;

/// <summary>
/// Voice Activity Detector (VAD) that measures short-term RMS intensity against an adaptive noise floor.
/// Handles:
/// 1. Initial 200ms background ambient noise sampling.
/// 2. Speech onset detection.
/// 3. Trailing silence timeout (~2.0 seconds of continuous silence after speech).
/// 4. Initial silence timeout (~3.5 seconds if no speech occurs).
/// 5. Maximum recording duration safety cap (~15.0 seconds).
/// </summary>
public sealed class VoiceActivityDetector
{
    private const string LogCategory = "VAD";

    public double InitialSilenceTimeoutSeconds { get; set; } = 4.5;
    public double TrailingSilenceTimeoutSeconds { get; set; } = 1.8;
    public double MaxDurationSeconds { get; set; } = 15.0;

    private readonly Stopwatch _sessionStopwatch = new();
    private readonly Stopwatch _silenceStopwatch = new();
    private readonly System.Timers.Timer _checkTimer = new(50);

    private bool _isActive;
    private bool _isCalibrating;
    private float _noiseFloor = 0.02f;
    private float _speechThreshold = 0.06f;
    private int _calibrationSampleCount;
    private float _calibrationSum;
    private bool _hasSpeechStarted;

    public bool HasSpeechStarted => _hasSpeechStarted;
    public float CurrentNoiseFloor => _noiseFloor;
    public float CurrentThreshold => _speechThreshold;

    public VoiceActivityDetector()
    {
        _checkTimer.AutoReset = true;
        _checkTimer.Elapsed += (s, e) => CheckTimeouts();
    }

    /// <summary>
    /// Event triggered when speech activity is first detected.
    /// </summary>
    public event EventHandler? SpeechStarted;

    /// <summary>
    /// Event triggered when speech has completed (due to trailing silence or max duration cap).
    /// </summary>
    public event EventHandler<string>? UtteranceCompleted;

    public void Reset()
    {
        _checkTimer.Stop();
        _sessionStopwatch.Reset();
        _silenceStopwatch.Reset();
        _isActive = false;
        _isCalibrating = false;
        _hasSpeechStarted = false;
        _calibrationSampleCount = 0;
        _calibrationSum = 0f;
        _noiseFloor = 0.02f;
        _speechThreshold = 0.06f;
    }

    public void Start()
    {
        Reset();
        _isActive = true;
        _isCalibrating = true;
        _sessionStopwatch.Start();
        _checkTimer.Start();
        OrbitLogger.Info(LogCategory, "VAD started with 200ms ambient noise calibration.");
    }

    public void Stop()
    {
        _isActive = false;
        _checkTimer.Stop();
        _sessionStopwatch.Stop();
        _silenceStopwatch.Stop();
    }

    private void CheckTimeouts()
    {
        if (!_isActive) return;

        double elapsedSeconds = _sessionStopwatch.Elapsed.TotalSeconds;

        if (elapsedSeconds >= MaxDurationSeconds)
        {
            OrbitLogger.Info(LogCategory, $"VAD reached maximum recording cap of {MaxDurationSeconds}s.");
            Stop();
            UtteranceCompleted?.Invoke(this, "MaxDurationReached");
            return;
        }

        if (!_hasSpeechStarted)
        {
            if (elapsedSeconds >= InitialSilenceTimeoutSeconds)
            {
                OrbitLogger.Info(LogCategory, $"Initial silence timeout ({InitialSilenceTimeoutSeconds}s) reached without speech. Finalizing voice mode.");
                Stop();
                UtteranceCompleted?.Invoke(this, "InitialSilenceTimeout");
            }
            return;
        }

        if (_silenceStopwatch.IsRunning && _silenceStopwatch.Elapsed.TotalSeconds >= TrailingSilenceTimeoutSeconds)
        {
            OrbitLogger.Info(LogCategory, $"Continuous trailing silence of {TrailingSilenceTimeoutSeconds}s detected after speech. Utterance complete.");
            Stop();
            UtteranceCompleted?.Invoke(this, "SilenceDetected");
        }
    }

    /// <summary>
    /// Feeds a new normalized audio intensity sample (0.0 to 1.0) into the detector.
    /// </summary>
    public void ProcessIntensity(float intensity)
    {
        if (!_isActive) return;

        double elapsedSeconds = _sessionStopwatch.Elapsed.TotalSeconds;

        // 1. Initial 200ms calibration
        if (_isCalibrating)
        {
            _calibrationSum += intensity;
            _calibrationSampleCount++;

            if (elapsedSeconds >= 0.200)
            {
                _isCalibrating = false;
                float avgNoise = _calibrationSampleCount > 0 ? _calibrationSum / _calibrationSampleCount : 0.02f;
                _noiseFloor = Math.Clamp(avgNoise, 0.01f, 0.20f);
                // Dynamic speech threshold is established above the ambient noise floor
                _speechThreshold = Math.Max(_noiseFloor * 1.8f, _noiseFloor + 0.04f);

                OrbitLogger.Info(LogCategory, $"VAD calibrated: NoiseFloor={_noiseFloor:F3}, SpeechThreshold={_speechThreshold:F3}");
            }
            return;
        }

        // 2. Maximum duration safety cap check
        if (elapsedSeconds >= MaxDurationSeconds)
        {
            OrbitLogger.Info(LogCategory, $"VAD reached maximum recording cap of {MaxDurationSeconds}s.");
            Stop();
            UtteranceCompleted?.Invoke(this, "MaxDurationReached");
            return;
        }

        // 3. Pre-speech state: Check for initial silence timeout or speech trigger
        if (!_hasSpeechStarted)
        {
            if (intensity >= _speechThreshold)
            {
                _hasSpeechStarted = true;
                _silenceStopwatch.Reset();
                OrbitLogger.Info(LogCategory, $"Speech detected at {elapsedSeconds:F2}s with intensity {intensity:F3}.");
                SpeechStarted?.Invoke(this, EventArgs.Empty);
            }
            else if (elapsedSeconds >= InitialSilenceTimeoutSeconds)
            {
                OrbitLogger.Info(LogCategory, $"Initial silence timeout ({InitialSilenceTimeoutSeconds}s) reached without speech. Finalizing voice mode.");
                Stop();
                UtteranceCompleted?.Invoke(this, "InitialSilenceTimeout");
            }
            return;
        }

        // 4. Post-speech state: Monitor trailing silence
        if (intensity >= _speechThreshold)
        {
            // Active speech continuing: reset trailing silence timer
            if (_silenceStopwatch.IsRunning)
            {
                _silenceStopwatch.Reset();
            }
        }
        else
        {
            // Below threshold: accumulate silence time
            if (!_silenceStopwatch.IsRunning)
            {
                _silenceStopwatch.Restart();
            }
            else if (_silenceStopwatch.Elapsed.TotalSeconds >= TrailingSilenceTimeoutSeconds)
            {
                OrbitLogger.Info(LogCategory, $"Continuous trailing silence of {TrailingSilenceTimeoutSeconds}s detected after speech. Utterance complete.");
                Stop();
                UtteranceCompleted?.Invoke(this, "SilenceDetected");
            }
        }
    }
}

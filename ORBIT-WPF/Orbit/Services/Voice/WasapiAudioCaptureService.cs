using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Orbit.Core;
using Orbit.Platform.Windows.Audio;

namespace Orbit.Services.Voice;

/// <summary>
/// Native Windows Core Audio WASAPI microphone capture service.
/// Uses pure Win32 COM interfaces directly to eliminate external dependency cast errors.
/// Buffers audio into memory only and computes real-time RMS for the waveform visualizer.
/// </summary>
public sealed class WasapiAudioCaptureService : IAudioCaptureService
{
    private const string LogCategory = "AUDIO_CAPTURE";

    private readonly object _lock = new();
    private IMMDeviceEnumerator? _enumerator;
    private IMMDevice? _device;
    private IAudioClient? _audioClient;
    private IAudioCaptureClient? _captureClient;

    private Thread? _captureThread;
    private EventWaitHandle? _audioEvent;
    private CancellationTokenSource? _cts;
    private MemoryStream? _pcmStream;
    private WaveFormatEx _mixFormat;
    private bool _isEventDriven;

    private const int TargetSampleRate = 16000;
    private double _resamplePos;
    private int _resamplePhase;
    private float _resampleSum;

    private bool _isCapturing;
    private bool _disposed;

    public bool IsCapturing
    {
        get
        {
            lock (_lock)
            {
                return _isCapturing;
            }
        }
    }

    public event EventHandler<float>? AudioLevelUpdated;
    public event EventHandler<string>? CaptureErrorOccurred;

    public bool StartCapture()
    {
        lock (_lock)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WasapiAudioCaptureService));
            if (_isCapturing) return true;

            try
            {
                _pcmStream?.Dispose();
                _pcmStream = new MemoryStream(64 * 1024);
                _resamplePos = 0;
                _resamplePhase = 0;
                _resampleSum = 0f;

                var enumeratorType = Type.GetTypeFromCLSID(CoreAudioGuids.CLSID_MMDeviceEnumerator);
                if (enumeratorType == null)
                {
                    throw new InvalidOperationException("MMDeviceEnumerator COM class not found.");
                }

                _enumerator = (IMMDeviceEnumerator?)Activator.CreateInstance(enumeratorType);
                if (_enumerator == null)
                {
                    throw new InvalidOperationException("Failed to instantiate MMDeviceEnumerator.");
                }

                int hr = _enumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eCommunications, out _device);
                if (hr != 0 || _device == null)
                {
                    hr = _enumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eConsole, out _device);
                }

                if (hr != 0 || _device == null)
                {
                    throw new InvalidOperationException($"No default microphone capture endpoint found (hr=0x{hr:X8}).");
                }

                var iidAudioClient = CoreAudioGuids.IID_IAudioClient;
                hr = _device.Activate(ref iidAudioClient, CoreAudioGuids.CLSCTX_ALL, IntPtr.Zero, out var comObj);
                if (hr != 0 || comObj == null)
                {
                    throw new InvalidOperationException($"Failed to activate IAudioClient (hr=0x{hr:X8}).");
                }

                _audioClient = (IAudioClient)comObj;

                IntPtr pMixFormat = IntPtr.Zero;
                try
                {
                    hr = _audioClient.GetMixFormat(out pMixFormat);
                    if (hr != 0 || pMixFormat == IntPtr.Zero)
                    {
                        throw new InvalidOperationException($"Failed to get audio mix format (hr=0x{hr:X8}).");
                    }

                    _mixFormat = Marshal.PtrToStructure<WaveFormatEx>(pMixFormat);
                    _audioEvent = new EventWaitHandle(false, EventResetMode.AutoReset);

                    // Initialize in Shared mode with EventCallback
                    long hnsBufferDuration = 10000000; // 1 second
                    hr = _audioClient.Initialize(
                        AudioClientShareMode.Shared,
                        AudioClientStreamFlags.EventCallback,
                        hnsBufferDuration,
                        0,
                        pMixFormat,
                        IntPtr.Zero);

                    if (hr == 0)
                    {
                        _isEventDriven = true;
                        _audioClient.SetEventHandle(_audioEvent.SafeWaitHandle.DangerousGetHandle());
                    }
                    else
                    {
                        OrbitLogger.Warn(LogCategory, $"Event-driven WASAPI init returned 0x{hr:X8}, falling back to timer-polling mode.");
                        hr = _audioClient.Initialize(
                            AudioClientShareMode.Shared,
                            AudioClientStreamFlags.None,
                            hnsBufferDuration,
                            0,
                            pMixFormat,
                            IntPtr.Zero);

                        if (hr != 0)
                        {
                            throw new InvalidOperationException($"Failed to initialize IAudioClient (hr=0x{hr:X8}).");
                        }
                        _isEventDriven = false;
                    }
                }
                finally
                {
                    if (pMixFormat != IntPtr.Zero)
                    {
                        Marshal.FreeCoTaskMem(pMixFormat);
                    }
                }

                var iidCaptureClient = CoreAudioGuids.IID_IAudioCaptureClient;
                hr = _audioClient.GetService(ref iidCaptureClient, out var captureObj);
                if (hr != 0 || captureObj == null)
                {
                    throw new InvalidOperationException($"Failed to get IAudioCaptureClient (hr=0x{hr:X8}).");
                }

                _captureClient = (IAudioCaptureClient)captureObj;

                _cts = new CancellationTokenSource();
                _isCapturing = true;

                hr = _audioClient.Start();
                if (hr != 0)
                {
                    throw new InvalidOperationException($"Failed to start IAudioClient (hr=0x{hr:X8}).");
                }

                _captureThread = new Thread(CaptureLoop)
                {
                    IsBackground = true,
                    Name = "OrbitWasapiCaptureThread"
                };
                _captureThread.Start();

                OrbitLogger.Info(LogCategory, $"Native WASAPI capture started successfully: {_mixFormat.nSamplesPerSec}Hz, {_mixFormat.nChannels}ch, {_mixFormat.wBitsPerSample}bit.");
                return true;
            }
            catch (Exception ex)
            {
                OrbitLogger.Error(LogCategory, $"Failed to initialize native WASAPI audio capture: {ex.Message}", ex);
                CleanupCapture();
                CaptureErrorOccurred?.Invoke(this, ex.Message);
                return false;
            }
        }
    }

    private void CaptureLoop()
    {
        var token = _cts?.Token ?? CancellationToken.None;

        while (!token.IsCancellationRequested && _isCapturing && _captureClient != null)
        {
            try
            {
                if (_isEventDriven && _audioEvent != null)
                {
                    _audioEvent.WaitOne(40);
                }
                else
                {
                    Thread.Sleep(20);
                }

                while (_captureClient.GetNextPacketSize(out uint packetFrames) == 0 && packetFrames > 0)
                {
                    int hr = _captureClient.GetBuffer(
                        out IntPtr pData,
                        out uint numFramesToRead,
                        out AudioClientBufferFlags flags,
                        out _,
                        out _);

                    if (hr != 0 || pData == IntPtr.Zero) break;

                    try
                    {
                        if ((flags & AudioClientBufferFlags.Silent) != 0)
                        {
                            AudioLevelUpdated?.Invoke(this, 0.0f);
                        }
                        else
                        {
                            ProcessAudioBuffer(pData, numFramesToRead);
                        }
                    }
                    finally
                    {
                        _captureClient.ReleaseBuffer(numFramesToRead);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    OrbitLogger.Warn(LogCategory, $"Capture loop frame error: {ex.Message}");
                }
                break;
            }
        }
    }

    private void ProcessAudioBuffer(IntPtr pData, uint numFrames)
    {
        if (numFrames == 0 || _pcmStream == null) return;

        int channels = Math.Max(1, (int)_mixFormat.nChannels);
        int bitsPerSample = _mixFormat.wBitsPerSample;
        int totalSamples = (int)numFrames * channels;

        float sumSquares = 0f;
        float[] monoFrames = new float[numFrames];

        if (bitsPerSample == 32)
        {
            // IEEE Float 32-bit
            float[] buffer = new float[totalSamples];
            Marshal.Copy(pData, buffer, 0, totalSamples);

            for (int f = 0; f < numFrames; f++)
            {
                int baseIdx = f * channels;
                float mono = 0f;
                for (int ch = 0; ch < channels; ch++)
                {
                    mono += buffer[baseIdx + ch];
                }
                mono /= channels;
                monoFrames[f] = mono;
                sumSquares += mono * mono;
            }
        }
        else if (bitsPerSample == 16)
        {
            short[] buffer = new short[totalSamples];
            Marshal.Copy(pData, buffer, 0, totalSamples);

            for (int f = 0; f < numFrames; f++)
            {
                int baseIdx = f * channels;
                int mono = 0;
                for (int ch = 0; ch < channels; ch++)
                {
                    mono += buffer[baseIdx + ch];
                }
                mono /= channels;
                float norm = mono / 32768f;
                monoFrames[f] = norm;
                sumSquares += norm * norm;
            }
        }

        // Resample mono audio frames to 16 kHz mono PCM16 stream for Whisper
        using var bw = new BinaryWriter(_pcmStream, System.Text.Encoding.Default, leaveOpen: true);
        int inRate = (int)_mixFormat.nSamplesPerSec;
        ResampleAndWrite(monoFrames, inRate, bw);

        float rms = numFrames > 0 ? MathF.Sqrt(sumSquares / numFrames) : 0f;
        float normalizedIntensity = Math.Clamp(rms * 12.0f, 0f, 1f); // Generous headroom for speech

        AudioLevelUpdated?.Invoke(this, normalizedIntensity);
    }

    private void ResampleAndWrite(float[] monoFrames, int inRate, BinaryWriter bw)
    {
        if (inRate <= 0) inRate = TargetSampleRate;

        if (inRate == TargetSampleRate)
        {
            for (int i = 0; i < monoFrames.Length; i++)
            {
                short pcm = (short)(Math.Clamp(monoFrames[i], -1.0f, 1.0f) * 32767f);
                bw.Write(pcm);
            }
            return;
        }

        if (inRate == 48000)
        {
            // 3:1 decimation with box-car anti-aliasing filter
            int i = 0;
            while (_resamplePhase > 0 && i < monoFrames.Length)
            {
                _resampleSum += monoFrames[i++];
                _resamplePhase++;
                if (_resamplePhase == 3)
                {
                    float avg = _resampleSum / 3.0f;
                    bw.Write((short)(Math.Clamp(avg, -1.0f, 1.0f) * 32767f));
                    _resamplePhase = 0;
                    _resampleSum = 0f;
                }
            }

            while (i + 2 < monoFrames.Length)
            {
                float avg = (monoFrames[i] + monoFrames[i + 1] + monoFrames[i + 2]) / 3.0f;
                bw.Write((short)(Math.Clamp(avg, -1.0f, 1.0f) * 32767f));
                i += 3;
            }

            while (i < monoFrames.Length)
            {
                _resampleSum += monoFrames[i++];
                _resamplePhase++;
            }
            return;
        }

        // General linear interpolation fallback for any other sample rate (e.g. 44100, 96000)
        double step = (double)inRate / TargetSampleRate;
        while (_resamplePos < monoFrames.Length)
        {
            int idx = (int)_resamplePos;
            double frac = _resamplePos - idx;

            float s0 = monoFrames[idx];
            float s1 = (idx + 1 < monoFrames.Length) ? monoFrames[idx + 1] : s0;
            float sample = (float)(s0 + (s1 - s0) * frac);

            short pcm = (short)(Math.Clamp(sample, -1.0f, 1.0f) * 32767f);
            bw.Write(pcm);

            _resamplePos += step;
        }

        _resamplePos -= monoFrames.Length;
    }

    public byte[] StopCapture()
    {
        lock (_lock)
        {
            if (!_isCapturing)
            {
                return _pcmStream?.ToArray() ?? Array.Empty<byte>();
            }

            _isCapturing = false;
            _cts?.Cancel();

            try
            {
                _audioClient?.Stop();
            }
            catch (Exception ex)
            {
                OrbitLogger.Warn(LogCategory, $"Exception while stopping native audio client: {ex.Message}");
            }

            byte[] data = _pcmStream?.ToArray() ?? Array.Empty<byte>();
            CleanupCapture();

            OrbitLogger.Info(LogCategory, $"Native audio capture stopped. Staged {data.Length} bytes PCM in RAM.");
            return data;
        }
    }

    public void CancelCapture()
    {
        lock (_lock)
        {
            _isCapturing = false;
            _cts?.Cancel();

            try
            {
                _audioClient?.Stop();
            }
            catch (Exception ex)
            {
                OrbitLogger.Warn(LogCategory, $"Exception while stopping native audio client: {ex.Message}");
            }

            CleanupCapture();
            OrbitLogger.Info(LogCategory, "Native audio capture cancelled and memory flushed.");
        }
    }

    private void CleanupCapture()
    {
        _isCapturing = false;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        _audioEvent?.Dispose();
        _audioEvent = null;

        if (_captureClient != null)
        {
            Marshal.ReleaseComObject(_captureClient);
            _captureClient = null;
        }

        if (_audioClient != null)
        {
            Marshal.ReleaseComObject(_audioClient);
            _audioClient = null;
        }

        if (_device != null)
        {
            Marshal.ReleaseComObject(_device);
            _device = null;
        }

        if (_enumerator != null)
        {
            Marshal.ReleaseComObject(_enumerator);
            _enumerator = null;
        }

        if (_pcmStream != null)
        {
            _pcmStream.Dispose();
            _pcmStream = null;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            CleanupCapture();
        }
    }
}

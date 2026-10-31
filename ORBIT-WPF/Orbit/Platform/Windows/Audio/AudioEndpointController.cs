using System.Runtime.InteropServices;
using Orbit.Core;

namespace Orbit.Platform.Windows.Audio;

public sealed class AudioEndpointController : IAudioEndpointVolumeCallback, IDisposable
{
    private const string LogCategory = "AUDIO_CONTROLLER";

    private readonly EDataFlow _dataFlow;
    private readonly ERole _preferredRole;
    private readonly object _syncLock = new();

    private IMMDeviceEnumerator? _enumerator;
    private IMMDevice? _device;
    private IAudioEndpointVolume? _endpointVolume;
    private bool _isCallbackRegistered;
    private bool _disposed;

    public event Action<bool>? MuteChanged;
    public event Action<float>? VolumeChanged;

    public bool IsAvailable { get; private set; }

    public AudioEndpointController(EDataFlow dataFlow, ERole preferredRole = ERole.eMultimedia)
    {
        _dataFlow = dataFlow;
        _preferredRole = preferredRole;

        InitializeEndpoint();
    }

    public void InitializeEndpoint()
    {
        lock (_syncLock)
        {
            Cleanup();

            try
            {
                var enumeratorType = Type.GetTypeFromCLSID(CoreAudioGuids.CLSID_MMDeviceEnumerator);
                if (enumeratorType == null)
                {
                    OrbitLogger.Warn(LogCategory, $"MMDeviceEnumerator type could not be loaded for {_dataFlow}.");
                    IsAvailable = false;
                    return;
                }

                _enumerator = (IMMDeviceEnumerator?)Activator.CreateInstance(enumeratorType);
                if (_enumerator == null)
                {
                    OrbitLogger.Warn(LogCategory, $"Failed to instantiate MMDeviceEnumerator for {_dataFlow}.");
                    IsAvailable = false;
                    return;
                }

                // Try preferred role first
                int hr = _enumerator.GetDefaultAudioEndpoint(_dataFlow, _preferredRole, out _device);
                if (hr != 0 || _device == null)
                {
                    // Fallback to eConsole
                    hr = _enumerator.GetDefaultAudioEndpoint(_dataFlow, ERole.eConsole, out _device);
                }

                if (hr != 0 || _device == null)
                {
                    OrbitLogger.Warn(LogCategory, $"No default audio endpoint found for {_dataFlow} (hr=0x{hr:X8}).");
                    IsAvailable = false;
                    return;
                }

                var iid = CoreAudioGuids.IID_IAudioEndpointVolume;
                hr = _device.Activate(ref iid, CoreAudioGuids.CLSCTX_ALL, IntPtr.Zero, out var comObj);
                if (hr != 0 || comObj == null)
                {
                    OrbitLogger.Warn(LogCategory, $"Failed to activate IAudioEndpointVolume for {_dataFlow} (hr=0x{hr:X8}).");
                    IsAvailable = false;
                    return;
                }

                _endpointVolume = (IAudioEndpointVolume)comObj;

                // Register event callback for live external changes
                hr = _endpointVolume.RegisterControlChangeNotify(this);
                if (hr == 0)
                {
                    _isCallbackRegistered = true;
                }
                else
                {
                    OrbitLogger.Warn(LogCategory, $"Failed to register audio endpoint callback for {_dataFlow} (hr=0x{hr:X8}).");
                }

                IsAvailable = true;
                OrbitLogger.Info(LogCategory, $"Audio endpoint initialized successfully for {_dataFlow}.");
            }
            catch (Exception ex)
            {
                OrbitLogger.Error(LogCategory, $"Error initializing audio endpoint for {_dataFlow}.", ex);
                IsAvailable = false;
            }
        }
    }

    public bool GetMute()
    {
        lock (_syncLock)
        {
            if (_endpointVolume == null) return false;

            try
            {
                int hr = _endpointVolume.GetMute(out bool muted);
                return hr == 0 && muted;
            }
            catch (Exception ex)
            {
                OrbitLogger.Warn(LogCategory, $"GetMute failed for {_dataFlow}: {ex.Message}");
                return false;
            }
        }
    }

    public bool SetMute(bool mute)
    {
        lock (_syncLock)
        {
            if (_endpointVolume == null)
            {
                InitializeEndpoint();
                if (_endpointVolume == null) return false;
            }

            try
            {
                var context = Guid.Empty;
                int hr = _endpointVolume.SetMute(mute, ref context);
                if (hr == 0)
                {
                    OrbitLogger.Info(LogCategory, $"SetMute({mute}) succeeded for {_dataFlow}.");
                    return true;
                }
                OrbitLogger.Warn(LogCategory, $"SetMute({mute}) failed for {_dataFlow} (hr=0x{hr:X8}).");
                return false;
            }
            catch (Exception ex)
            {
                OrbitLogger.Error(LogCategory, $"Exception setting mute on {_dataFlow}.", ex);
                return false;
            }
        }
    }

    public bool ToggleMute()
    {
        bool currentMute = GetMute();
        return SetMute(!currentMute);
    }

    public float GetVolumeScalar()
    {
        lock (_syncLock)
        {
            if (_endpointVolume == null) return 0f;

            try
            {
                int hr = _endpointVolume.GetMasterVolumeLevelScalar(out float level);
                return hr == 0 ? level : 0f;
            }
            catch (Exception ex)
            {
                OrbitLogger.Warn(LogCategory, $"GetMasterVolumeLevelScalar failed for {_dataFlow}: {ex.Message}");
                return 0f;
            }
        }
    }

    public bool SetVolumeScalar(float level)
    {
        lock (_syncLock)
        {
            if (_endpointVolume == null) return false;

            try
            {
                var clamped = Math.Clamp(level, 0f, 1f);
                var context = Guid.Empty;
                int hr = _endpointVolume.SetMasterVolumeLevelScalar(clamped, ref context);
                return hr == 0;
            }
            catch (Exception ex)
            {
                OrbitLogger.Error(LogCategory, $"Exception setting volume on {_dataFlow}.", ex);
                return false;
            }
        }
    }

    int IAudioEndpointVolumeCallback.OnNotify(IntPtr pNotify)
    {
        try
        {
            if (pNotify == IntPtr.Zero) return 0;

            var data = Marshal.PtrToStructure<AudioVolumeNotificationData>(pNotify);
            MuteChanged?.Invoke(data.bMuted);
            VolumeChanged?.Invoke(data.fMasterVolume);
        }
        catch (Exception ex)
        {
            OrbitLogger.Warn(LogCategory, $"Error processing audio notification for {_dataFlow}: {ex.Message}");
        }

        return 0;
    }

    private void Cleanup()
    {
        try
        {
            if (_endpointVolume != null)
            {
                if (_isCallbackRegistered)
                {
                    _endpointVolume.UnregisterControlChangeNotify(this);
                    _isCallbackRegistered = false;
                }

                Marshal.ReleaseComObject(_endpointVolume);
                _endpointVolume = null;
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
        }
        catch (Exception ex)
        {
            OrbitLogger.Warn(LogCategory, $"Error cleaning up audio endpoint for {_dataFlow}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_syncLock)
        {
            Cleanup();
        }
    }
}


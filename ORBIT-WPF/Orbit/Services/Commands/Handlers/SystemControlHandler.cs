using System;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Core;
using Orbit.Platform.Windows.Audio;

namespace Orbit.Services.Commands.Handlers;

public sealed class SystemControlHandler : ICommandHandler
{
    private const string LogCategory = "SYSTEM_CONTROL_HANDLER";
    private readonly AudioEndpointController _audioController;

    public CommandType SupportedType => CommandType.SystemControl;

    public SystemControlHandler()
    {
        _audioController = new AudioEndpointController(EDataFlow.eRender, ERole.eMultimedia);
    }

    public Task<CommandResult> ExecuteAsync(CommandIntent intent, CancellationToken cancellationToken = default)
    {
        if (intent.ControlAction == null)
        {
            return Task.FromResult(new CommandResult(false, "Unspecified system control action", intent));
        }

        try
        {
            switch (intent.ControlAction.Value)
            {
                case SystemControlAction.SetVolume:
                {
                    int targetPercent = intent.Value ?? 50;
                    targetPercent = Math.Clamp(targetPercent, 0, 100);
                    float scalar = targetPercent / 100.0f;

                    if (_audioController.GetMute() && targetPercent > 0)
                    {
                        _audioController.SetMute(false);
                    }

                    bool ok = _audioController.SetVolumeScalar(scalar);
                    if (ok)
                    {
                        OrbitLogger.Info(LogCategory, $"Master volume set to {targetPercent}%");
                        return Task.FromResult(new CommandResult(true, $"Volume set to {targetPercent}%", intent));
                    }
                    return Task.FromResult(new CommandResult(false, "Failed to adjust volume", intent));
                }

                case SystemControlAction.IncreaseVolume:
                {
                    int step = intent.Value ?? 10;
                    float current = _audioController.GetVolumeScalar();
                    float next = Math.Clamp(current + (step / 100.0f), 0.0f, 1.0f);
                    int nextPercent = (int)Math.Round(next * 100.0f);

                    if (_audioController.GetMute())
                    {
                        _audioController.SetMute(false);
                    }

                    bool ok = _audioController.SetVolumeScalar(next);
                    if (ok)
                    {
                        OrbitLogger.Info(LogCategory, $"Master volume increased to {nextPercent}%");
                        return Task.FromResult(new CommandResult(true, $"Volume set to {nextPercent}%", intent));
                    }
                    return Task.FromResult(new CommandResult(false, "Failed to increase volume", intent));
                }

                case SystemControlAction.DecreaseVolume:
                {
                    int step = intent.Value ?? 10;
                    float current = _audioController.GetVolumeScalar();
                    float next = Math.Clamp(current - (step / 100.0f), 0.0f, 1.0f);
                    int nextPercent = (int)Math.Round(next * 100.0f);

                    bool ok = _audioController.SetVolumeScalar(next);
                    if (ok)
                    {
                        OrbitLogger.Info(LogCategory, $"Master volume decreased to {nextPercent}%");
                        return Task.FromResult(new CommandResult(true, $"Volume set to {nextPercent}%", intent));
                    }
                    return Task.FromResult(new CommandResult(false, "Failed to decrease volume", intent));
                }

                case SystemControlAction.Mute:
                {
                    bool ok = _audioController.SetMute(true);
                    if (ok)
                    {
                        OrbitLogger.Info(LogCategory, "Master volume muted");
                        return Task.FromResult(new CommandResult(true, "Volume muted", intent));
                    }
                    return Task.FromResult(new CommandResult(false, "Failed to mute audio", intent));
                }

                case SystemControlAction.Unmute:
                {
                    bool ok = _audioController.SetMute(false);
                    if (ok)
                    {
                        OrbitLogger.Info(LogCategory, "Master volume unmuted");
                        return Task.FromResult(new CommandResult(true, "Volume unmuted", intent));
                    }
                    return Task.FromResult(new CommandResult(false, "Failed to unmute audio", intent));
                }

                default:
                    return Task.FromResult(new CommandResult(false, "Unsupported control action", intent));
            }
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, $"Error executing system control: {intent.ControlAction}", ex);
            return Task.FromResult(new CommandResult(false, "System control error", intent, ex.Message));
        }
    }
}


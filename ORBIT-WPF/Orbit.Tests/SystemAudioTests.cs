using Orbit.Platform.Windows.Audio;
using Xunit;

namespace Orbit.Tests;

[Collection("AudioTests")]
public class SystemAudioTests
{
    [Fact]
    public void AudioEndpointController_Render_InitializesAndTogglesMute()
    {
        using var controller = new AudioEndpointController(EDataFlow.eRender, ERole.eMultimedia);
        Assert.True(controller.IsAvailable);

        var volume = controller.GetVolumeScalar();
        Assert.InRange(volume, 0f, 1f);

        var muted = controller.GetMute();
        // Test toggling and restoring
        bool toggleSuccess = controller.SetMute(!muted);
        Assert.True(toggleSuccess);
        Assert.Equal(!muted, controller.GetMute());

        // Restore original
        controller.SetMute(muted);
        Assert.Equal(muted, controller.GetMute());
    }

    [Fact]
    public void AudioEndpointController_Capture_InitializesAndTogglesMute()
    {
        using var controller = new AudioEndpointController(EDataFlow.eCapture, ERole.eCommunications);
        Assert.True(controller.IsAvailable);

        var muted = controller.GetMute();
        bool toggleSuccess = controller.SetMute(!muted);
        Assert.True(toggleSuccess);
        Assert.Equal(!muted, controller.GetMute());

        // Restore original
        controller.SetMute(muted);
        Assert.Equal(muted, controller.GetMute());
    }
}

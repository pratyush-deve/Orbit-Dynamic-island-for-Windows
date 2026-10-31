using System.IO;
using Orbit.Models;
using Orbit.Services.DevWorkspace;
using Xunit;

namespace Orbit.Tests;

public class DevWorkspaceServiceTests
{
    [Fact]
    public async Task AppDiscovery_DiscoversInstalledDeveloperTools()
    {
        var iconExtractor = new IconExtractorService();
        var discovery = new AppDiscoveryService(iconExtractor);

        var apps = await discovery.DiscoverDeveloperAppsAsync();

        Assert.NotNull(apps);
        Assert.NotEmpty(apps);

        // Verify core apps are present
        Assert.Contains(apps, a => a.Id == "android-studio");
        Assert.Contains(apps, a => a.Id == "antigravity-ide");
        Assert.Contains(apps, a => a.Id == "antigravity");
        Assert.Contains(apps, a => a.Id == "google-chrome");

        var regular = apps.First(a => a.Id == "antigravity");
        var ide = apps.First(a => a.Id == "antigravity-ide");
        Assert.Equal("Antigravity", regular.Name);
        Assert.Equal("Antigravity IDE", ide.Name);
        Assert.NotEqual(regular.ExecutablePath, ide.ExecutablePath);

        var chrome = apps.First(a => a.Id == "google-chrome");
        Assert.Equal("https://chatgpt.com/", chrome.Arguments);
    }

    [Fact]
    public async Task DevWorkspaceService_InitializesWithDefaultThreeApps()
    {
        var iconExtractor = new IconExtractorService();
        var discovery = new AppDiscoveryService(iconExtractor);
        using var service = new DevWorkspaceService(discovery);

        // Run initialization
        await service.InitializeAsync();

        var selected = service.SelectedApps;
        Assert.NotNull(selected);
        Assert.True(selected.Count >= 3);

        Assert.Contains(selected, a => a.Id == "android-studio" && a.IsSelected);
        Assert.Contains(selected, a => a.Id == "antigravity-ide" && a.IsSelected);
        Assert.Contains(selected, a => a.Id == "google-chrome" && a.IsSelected);
    }

    [Fact]
    public async Task DevWorkspaceService_TogglesSelection_AndPersists()
    {
        var iconExtractor = new IconExtractorService();
        var discovery = new AppDiscoveryService(iconExtractor);
        using var service = new DevWorkspaceService(discovery);
        await service.InitializeAsync();

        int initialCount = service.SelectedApps.Count;

        // Toggle an app OFF
        service.ToggleAppSelection("google-chrome", false);
        Assert.DoesNotContain(service.SelectedApps, a => a.Id == "google-chrome");
        Assert.Equal(initialCount - 1, service.SelectedApps.Count);

        // Toggle it back ON
        service.ToggleAppSelection("google-chrome", true);
        Assert.Contains(service.SelectedApps, a => a.Id == "google-chrome");
        Assert.Equal(initialCount, service.SelectedApps.Count);
    }

    [Fact]
    public void IconExtractor_ExtractsOrCachesIcon()
    {
        var extractor = new IconExtractorService();

        // Test with shell32 or existing exe
        string chromePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
        if (File.Exists(chromePath))
        {
            var icon = extractor.GetOrExtractIcon(chromePath);
            Assert.NotNull(icon);

            // Second call should return cached instance
            var cached = extractor.GetOrExtractIcon(chromePath);
            Assert.Same(icon, cached);
        }
    }
}

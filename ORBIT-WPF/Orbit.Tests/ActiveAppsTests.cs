using Orbit.Core;
using Orbit.Integrations.Desktop;
using Orbit.Models;
using Orbit.Platform.Windows;
using Orbit.UI.Panels;
using Xunit;

namespace Orbit.Tests;

public class ActiveAppsTests
{
    [Theory]
    [InlineData(0, 230.0)]
    [InlineData(1, 230.0)]
    [InlineData(2, 230.0)]
    [InlineData(3, 230.0)]
    [InlineData(4, 310.0)]
    [InlineData(5, 310.0)]
    [InlineData(6, 310.0)]
    [InlineData(7, 390.0)]
    [InlineData(8, 390.0)]
    [InlineData(9, 390.0)]
    [InlineData(10, 420.0)]
    [InlineData(15, 420.0)]
    [InlineData(50, 420.0)]
    public void CalculateDesiredHeight_CalculatesRowHeightsAccurately(int windowCount, double expectedHeight)
    {
        double height = ActiveAppsView.CalculateDesiredHeight(windowCount);
        Assert.Equal(expectedHeight, height);
    }

    [Fact]
    public void DI_Container_Provides_IWindowManager()
    {
        using var app = new OrbitApplication();
        var windowManager = app.GetService<IWindowManager>();
        Assert.NotNull(windowManager);
        Assert.IsType<WindowManager>(windowManager);
    }

    [Fact]
    public async Task WindowManager_EnumeratesRealWindowsWithoutSelf()
    {
        using var windowManager = new WindowManager();
        var windows = await windowManager.GetOpenWindowsAsync();

        Assert.NotNull(windows);
        Assert.NotEmpty(windows);

        int currentPid = Environment.ProcessId;
        foreach (var win in windows)
        {
            System.Diagnostics.Trace.WriteLine($"FOUND WINDOW: '{win.Title}' | App: '{win.AppName}' | Exe: '{win.ExecutablePath}' | Icon: {(win.Icon != null ? "Loaded" : "Null")}");
            Assert.NotEqual(IntPtr.Zero, win.Handle);
            Assert.False(string.IsNullOrWhiteSpace(win.Title), $"Window 0x{win.Handle:X} had empty title.");
            Assert.NotEqual(currentPid, win.ProcessId);
            Assert.True(win.Bounds.Width > 0);
            Assert.True(win.Bounds.Height > 0);
        }
    }

    [Fact]
    public void WindowInfo_UpdateFrom_UpdatesPropertiesAndFiresPropertyChanged()
    {
        var handle = new IntPtr(0x1234);
        var initial = new WindowInfo
        {
            Handle = handle,
            ProcessName = "chrome",
            ProcessId = 1234,
            Title = "Initial Title",
            AppName = "Google Chrome",
            IsMinimized = false,
            IsVisible = true,
            Bounds = new MonitorBounds(10, 10, 800, 600)
        };

        var changedProperties = new List<string>();
        initial.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != null) changedProperties.Add(e.PropertyName);
        };

        var fresh = new WindowInfo
        {
            Handle = handle,
            ProcessName = "chrome",
            ProcessId = 1234,
            Title = "Updated Page - Google Chrome",
            AppName = "Google Chrome",
            IsMinimized = true,
            IsVisible = true,
            Bounds = new MonitorBounds(0, 0, 800, 600)
        };

        initial.UpdateFrom(fresh);

        Assert.Equal("Updated Page - Google Chrome", initial.Title);
        Assert.True(initial.IsMinimized);
        Assert.Contains(nameof(WindowInfo.Title), changedProperties);
        Assert.Contains(nameof(WindowInfo.IsMinimized), changedProperties);
        Assert.Contains(nameof(WindowInfo.Bounds), changedProperties);
    }

    [Fact]
    public void WindowInfo_Equality_IsBasedOnHandle()
    {
        var handle = new IntPtr(0xABCD);
        var win1 = new WindowInfo
        {
            Handle = handle,
            ProcessName = "notepad",
            ProcessId = 100,
            Title = "Untitled - Notepad"
        };
        var win2 = new WindowInfo
        {
            Handle = handle,
            ProcessName = "notepad",
            ProcessId = 100,
            Title = "Notes.txt - Notepad"
        };

        Assert.Equal(win1, win2);
        Assert.Equal(win1.GetHashCode(), win2.GetHashCode());
    }

    [Fact]
    public void IncrementalUpdateLogic_PreservesExistingCards_AndModifiesInPlace()
    {
        var existingCollection = new System.Collections.ObjectModel.ObservableCollection<WindowInfo>();
        var handle1 = new IntPtr(0x1001);
        var handle2 = new IntPtr(0x1002);

        var card1 = new WindowInfo
        {
            Handle = handle1,
            ProcessName = "notepad",
            ProcessId = 111,
            Title = "Document1 - Notepad",
            AppName = "Notepad",
            IsMinimized = false
        };
        var card2 = new WindowInfo
        {
            Handle = handle2,
            ProcessName = "notepad",
            ProcessId = 222,
            Title = "Document2 - Notepad",
            AppName = "Notepad",
            IsMinimized = false
        };

        existingCollection.Add(card1);
        existingCollection.Add(card2);

        // Simulate snapshot:
        // card1 title changed & minimized
        // card2 closed (not in snapshot)
        // card3 new window opened
        var handle3 = new IntPtr(0x1003);
        var snapshot = new List<WindowInfo>
        {
            new WindowInfo
            {
                Handle = handle1,
                ProcessName = "notepad",
                ProcessId = 111,
                Title = "Document1 (Saved) - Notepad",
                AppName = "Notepad",
                IsMinimized = true
            },
            new WindowInfo
            {
                Handle = handle3,
                ProcessName = "explorer",
                ProcessId = 333,
                Title = "File Explorer",
                AppName = "File Explorer",
                IsMinimized = false
            }
        };

        // Run the differential update algorithm:
        var freshByHwnd = snapshot.ToDictionary(w => w.Handle);

        // 1. Remove closed
        for (int i = existingCollection.Count - 1; i >= 0; i--)
        {
            var existing = existingCollection[i];
            if (!freshByHwnd.TryGetValue(existing.Handle, out var fresh) || existing.ProcessId != fresh.ProcessId)
            {
                existingCollection.RemoveAt(i);
            }
        }

        // 2. Update existing
        var existingHandles = new HashSet<IntPtr>();
        foreach (var existing in existingCollection)
        {
            existingHandles.Add(existing.Handle);
            if (freshByHwnd.TryGetValue(existing.Handle, out var fresh))
            {
                existing.UpdateFrom(fresh);
            }
        }

        // 3. Add new
        foreach (var win in snapshot)
        {
            if (!existingHandles.Contains(win.Handle))
            {
                existingCollection.Add(win);
            }
        }

        // Assertions:
        // Total count should be 2 (card1 updated, card3 added)
        Assert.Equal(2, existingCollection.Count);

        // Card1 reference MUST be identical (the exact same C# object was preserved!)
        Assert.Same(card1, existingCollection[0]);
        Assert.Equal("Document1 (Saved) - Notepad", existingCollection[0].Title);
        Assert.True(existingCollection[0].IsMinimized);

        // Card2 must have been removed
        Assert.DoesNotContain(existingCollection, w => w.Handle == handle2);

        // Card3 must have been added
        Assert.Contains(existingCollection, w => w.Handle == handle3);
    }
}

using System.IO;
using System.Threading;
using System.Windows;
using Orbit.Models;
using Orbit.Services.FileDock;
using Orbit.UI.Panels;
using Xunit;

namespace Orbit.Tests;

[Collection("UITests")]
public class FileDockTests
{
    private void RunOnSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current == null)
                {
                    try { _ = new System.Windows.Application(); } catch { }
                }

                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception != null)
        {
            throw new Exception("STA test execution failed: " + exception.ToString());
        }
    }

    [Fact]
    public void FileDockItem_FormatsMetadataCorrectly()
    {
        // 1. Byte formatting checks
        Assert.Equal("0 B", FileDockItem.FormatBytes(0));
        Assert.Equal("500 B", FileDockItem.FormatBytes(500));
        Assert.Equal("1.5 KB", FileDockItem.FormatBytes(1536));
        Assert.Equal("2.0 MB", FileDockItem.FormatBytes(2 * 1024 * 1024));

        // 2. Real file item check
        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "Orbit File Dock Unit Test");

            var item = new FileDockItem(tempFile);
            Assert.Equal(Path.GetFileName(tempFile), item.FileName);
            Assert.False(item.IsDirectory);
            Assert.True(item.FileSizeBytes > 0);
            Assert.Contains("B", item.FileSizeFormatted);
            Assert.Equal(".tmp", item.Extension);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void FileDockService_SessionOnlyPersistence_AndFileSafety()
    {
        RunOnSta(() =>
        {
            var service = new FileDockService();

            // 1. Fresh startup begins completely empty
            Assert.Empty(service.Items);
            Assert.Equal(FileDockMode.Closed, service.CurrentMode);

            string tempFile1 = Path.GetTempFileName();
            string tempFile2 = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tempFile1, "File 1 Content");
                File.WriteAllText(tempFile2, "File 2 Content");

                // 2. Add files to service
                service.AddFiles(new[] { tempFile1, tempFile2 });
                Assert.Equal(2, service.Items.Count);

                // 3. Duplicate paths should be ignored
                service.AddFiles(new[] { tempFile1 });
                Assert.Equal(2, service.Items.Count);

                // 4. Remove file reference: original file must remain untouched on disk!
                var itemToRemove = service.Items[0];
                bool removed = service.RemoveFile(itemToRemove);
                Assert.True(removed);
                Assert.Single(service.Items);
                Assert.True(File.Exists(tempFile1), "Original physical file must NOT be deleted when removing from File Dock!");

                // 5. Clear all: original physical files must remain untouched!
                service.Clear();
                Assert.Empty(service.Items);
                Assert.True(File.Exists(tempFile2), "Original physical file must NOT be deleted when clearing File Dock!");
            }
            finally
            {
                if (File.Exists(tempFile1)) File.Delete(tempFile1);
                if (File.Exists(tempFile2)) File.Delete(tempFile2);
            }
        });
    }

    [Fact]
    public void FileDockService_ModeTransitions_RaiseEvents()
    {
        RunOnSta(() =>
        {
            var service = new FileDockService();
            FileDockMode? recordedMode = null;

            service.ModeChanged += mode => recordedMode = mode;

            service.CurrentMode = FileDockMode.Temporary;
            Assert.Equal(FileDockMode.Temporary, service.CurrentMode);
            Assert.Equal(FileDockMode.Temporary, recordedMode);

            service.CurrentMode = FileDockMode.Pinned;
            Assert.Equal(FileDockMode.Pinned, service.CurrentMode);
            Assert.Equal(FileDockMode.Pinned, recordedMode);

            service.CurrentMode = FileDockMode.Closed;
            Assert.Equal(FileDockMode.Closed, service.CurrentMode);
            Assert.Equal(FileDockMode.Closed, recordedMode);
        });
    }

    [Fact]
    public void FileDockView_BindsToService_AndUpdatesUiStates()
    {
        RunOnSta(() =>
        {
            var service = new FileDockService();
            var view = new FileDockView();
            view.Initialize(service);

            // 1. Initial empty state
            Assert.Equal(Visibility.Visible, view.EmptyStateBorder.Visibility);
            Assert.Equal(Visibility.Collapsed, view.FileScrollViewer.Visibility);
            Assert.Equal("0", view.TxtItemCount.Text);

            // 2. Add an item
            string tempFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tempFile, "Test staging");
                service.AddFiles(new[] { tempFile });

                Assert.Equal(Visibility.Collapsed, view.EmptyStateBorder.Visibility);
                Assert.Equal(Visibility.Visible, view.FileScrollViewer.Visibility);
                Assert.Equal("1", view.TxtItemCount.Text);

                // 3. Test drag preview overlay toggle
                view.SetDragPreviewActive(true);
                Assert.Equal(Visibility.Visible, view.DragPreviewOverlay.Visibility);

                view.SetDragPreviewActive(false);
                Assert.Equal(Visibility.Collapsed, view.DragPreviewOverlay.Visibility);

                // 4. Verify pin toggle button is present
                Assert.NotNull(view.BtnPinToggle);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        });
    }
}

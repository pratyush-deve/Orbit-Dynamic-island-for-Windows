using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Orbit.Core;

namespace Orbit.Services.Commands.Handlers;

public sealed class ScreenshotHandler : ICommandHandler
{
    private const string LogCategory = "SCREENSHOT_HANDLER";

    public CommandType SupportedType => CommandType.Screenshot;

    public Task<CommandResult> ExecuteAsync(CommandIntent intent, CancellationToken cancellationToken = default)
    {
        try
        {
            var bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
            using var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size);
            }

            string picturesDir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            string screenshotsDir = Path.Combine(picturesDir, "Screenshots");
            Directory.CreateDirectory(screenshotsDir);

            string fileName = $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            string filePath = Path.Combine(screenshotsDir, fileName);

            bitmap.Save(filePath, ImageFormat.Png);

            OrbitLogger.Info(LogCategory, $"Screenshot captured and saved to: {filePath}");
            return Task.FromResult(new CommandResult(true, "Screenshot saved to Pictures", intent, filePath));
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, "Failed to capture screenshot", ex);
            return Task.FromResult(new CommandResult(false, "Failed to capture screenshot", intent, ex.Message));
        }
    }
}


using Orbit.Core;
using Orbit.Platform.Windows;
using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

public class MainWindowCreationTests
{
    [Fact]
    public void MainWindow_CanBeConstructedOnStaThread()
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                // Ensure Application exists
                if (System.Windows.Application.Current == null)
                {
                    _ = new System.Windows.Application();
                }

                var app = new OrbitApplication();
                var window = app.GetService<MainWindow>();
                Assert.NotNull(window);
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
            throw new Exception("STA construction failed: " + exception.ToString());
        }
    }
}

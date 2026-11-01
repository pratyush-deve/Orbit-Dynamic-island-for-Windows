using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using Orbit.Core;

namespace Orbit.Platform.Windows;

public interface ISystemTrayService : IDisposable
{
    void Initialize(
        Action onShowRequested,
        Action onHideRequested,
        Action onResetPositionRequested,
        Action onExitRequested);

    void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info);
}

/// <summary>
/// Manages Windows taskbar notification area (System Tray) icon and context menu.
/// </summary>
public sealed class SystemTrayService : ISystemTrayService
{
    private const string LogCategory = "SYSTEM_TRAY";
    private NotifyIcon? _notifyIcon;
    private ContextMenuStrip? _contextMenu;
    private Icon? _generatedIcon;
    private bool _disposed;

    public void Initialize(
        Action onShowRequested,
        Action onHideRequested,
        Action onResetPositionRequested,
        Action onExitRequested)
    {
        try
        {
            _generatedIcon = GetOrbitTrayIcon();

            _contextMenu = new ContextMenuStrip();
            _contextMenu.RenderMode = ToolStripRenderMode.System;

            var showItem = new ToolStripMenuItem("Show ORBIT", null, (s, e) => onShowRequested());
            showItem.Font = new Font(showItem.Font, FontStyle.Bold);

            var hideItem = new ToolStripMenuItem("Hide ORBIT", null, (s, e) => onHideRequested());
            var resetItem = new ToolStripMenuItem("Reset Position", null, (s, e) => onResetPositionRequested());
            var exitItem = new ToolStripMenuItem("Exit ORBIT", null, (s, e) => onExitRequested());

            _contextMenu.Items.Add(showItem);
            _contextMenu.Items.Add(hideItem);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add(resetItem);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add(exitItem);

            _notifyIcon = new NotifyIcon
            {
                Icon = _generatedIcon,
                Text = "ORBIT 2.0 — Desktop Companion",
                Visible = true,
                ContextMenuStrip = _contextMenu
            };

            // Double click or single click to restore
            _notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    onShowRequested();
                }
            };

            OrbitLogger.Info(LogCategory, "System tray icon initialized and visible.");
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, "Failed to initialize system tray icon.", ex);
        }
    }

    public void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _notifyIcon?.ShowBalloonTip(3000, title, text, icon);
    }

    /// <summary>
    /// Loads the custom multi-resolution ORBIT icon from embedded resources or executable with programmatic fallback.
    /// </summary>
    private static Icon GetOrbitTrayIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/ORBIT.ico", UriKind.Absolute);
            var streamInfo = System.Windows.Application.GetResourceStream(uri);
            if (streamInfo != null)
            {
                using var stream = streamInfo.Stream;
                return new Icon(stream, new System.Drawing.Size(32, 32));
            }
        }
        catch (Exception ex)
        {
            OrbitLogger.Warn(LogCategory, $"Failed to load ORBIT.ico from resources: {ex.Message}");
        }

        try
        {
            var procPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(procPath) && File.Exists(procPath))
            {
                var icon = Icon.ExtractAssociatedIcon(procPath);
                if (icon != null) return icon;
            }
        }
        catch { }

        return CreateOrbitIcon();
    }

    /// <summary>
    /// Generates a crisp, vector-rendered 32x32 icon depicting ORBIT's glowing pill and twin eyes.
    /// </summary>
    private static Icon CreateOrbitIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            // Purple outer glow / border pill
            using (var glowBrush = new SolidBrush(Color.FromArgb(200, 138, 43, 226)))
            {
                g.FillEllipse(glowBrush, 2, 8, 28, 16);
            }

            // Dark charcoal core
            using (var bgBrush = new SolidBrush(Color.FromArgb(255, 20, 20, 28)))
            {
                g.FillEllipse(bgBrush, 4, 10, 24, 12);
            }

            // White eye sclera left & right
            using (var eyeBrush = new SolidBrush(Color.White))
            {
                g.FillEllipse(eyeBrush, 9, 13, 5, 6);
                g.FillEllipse(eyeBrush, 18, 13, 5, 6);
            }

            // Dark pupils
            using (var pupilBrush = new SolidBrush(Color.FromArgb(255, 17, 17, 24)))
            {
                g.FillEllipse(pupilBrush, 11, 14, 3, 4);
                g.FillEllipse(pupilBrush, 20, 14, 3, 4);
            }

            // Pupil highlights
            using (var hlBrush = new SolidBrush(Color.FromArgb(220, 255, 255, 255)))
            {
                g.FillEllipse(hlBrush, 11, 14, 1, 1);
                g.FillEllipse(hlBrush, 20, 14, 1, 1);
            }
        }

        var hIcon = bitmap.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }

            _contextMenu?.Dispose();
            _generatedIcon?.Dispose();

            OrbitLogger.Info(LogCategory, "System tray icon disposed.");
        }
        catch (Exception ex)
        {
            OrbitLogger.Warn(LogCategory, $"Error disposing tray service: {ex.Message}");
        }
    }
}

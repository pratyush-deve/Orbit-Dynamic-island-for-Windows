using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using Orbit.Core;
using Orbit.Platform.Windows;
using MediaColor = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;

namespace Orbit.UI;

/// <summary>
/// Fullscreen transparent topmost overlay window hosting the InkCanvas drawing engine.
/// Spans the entire virtual desktop to allow temporary drawing over any application.
/// Provides smooth pen drawing, stroke-based erasing, and click-through cursor mode.
/// </summary>
public partial class DesktopAnnotationOverlayWindow : Window
{
    private const string LogCategory = "ANNOTATION_OVERLAY";

    private readonly IntPtr _mainWindowHwnd;
    private IntPtr _hwnd = IntPtr.Zero;
    private bool _isClickThrough = false;
    private AnnotationToolMode _currentTool = AnnotationToolMode.Cursor;
    private MediaColor _currentColor = MediaColor.FromRgb(0xEF, 0x44, 0x44);

    public IntPtr Hwnd => _hwnd;
    public bool IsClickThrough => _isClickThrough;
    public AnnotationToolMode CurrentTool => _currentTool;
    public MediaColor CurrentColor => _currentColor;
    public Func<NativeMethods.POINT, bool>? IsOverToolbarOrPaletteFunc { get; set; }
    public Action? RequestCursorModeAction { get; set; }

    public DesktopAnnotationOverlayWindow() : this(IntPtr.Zero)
    {
    }

    public DesktopAnnotationOverlayWindow(IntPtr mainWindowHwnd)
    {
        _mainWindowHwnd = mainWindowHwnd;
        InitializeComponent();

        UpdateBounds();
        ApplyDrawingAttributes();

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(_hwnd);
        source?.AddHook(WndProc);

        ApplyWindowStyles();
        UpdateBounds();
        SetTool(_currentTool);
        OrbitLogger.Info(LogCategory, $"Annotation overlay initialized with HWND 0x{_hwnd:X}.");
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateBounds();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.InvokeAsync(UpdateBounds);
    }

    /// <summary>
    /// Resizes and positions the overlay to span all active monitors (Virtual Screen).
    /// </summary>
    public void UpdateBounds()
    {
        try
        {
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;

            OrbitLogger.Debug(LogCategory, $"Overlay bounds updated: Left={Left}, Top={Top}, Width={Width}, Height={Height}");
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, $"Failed to update overlay bounds: {ex.Message}");
        }
    }

    /// <summary>
    /// Applies WS_EX_TOOLWINDOW and WS_EX_NOACTIVATE styles so the overlay
    /// does not appear in Alt-Tab, taskbar, or steal focus from ORBIT or active apps.
    /// </summary>
    private void ApplyWindowStyles()
    {
        if (_hwnd == IntPtr.Zero) return;

        try
        {
            var exStyle = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            exStyle |= NativeMethods.WS_EX_TOOLWINDOW;
            exStyle |= NativeMethods.WS_EX_NOACTIVATE;
            exStyle &= ~NativeMethods.WS_EX_APPWINDOW;

            NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle));

            // Place overlay behind MainWindow in topmost band if known, otherwise HWND_TOPMOST
            var insertAfter = (_mainWindowHwnd != IntPtr.Zero && NativeMethods.IsWindow(_mainWindowHwnd))
                ? _mainWindowHwnd
                : NativeMethods.HWND_TOPMOST;

            NativeMethods.SetWindowPos(_hwnd, insertAfter, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, $"Failed to apply initial window styles: {ex.Message}");
        }
    }

    /// <summary>
    /// Configures the active drawing attributes (color, thickness, smoothing) on the InkCanvas.
    /// </summary>
    private void ApplyDrawingAttributes()
    {
        if (AnnotationCanvas == null) return;

        var da = new DrawingAttributes
        {
            Color = _currentColor,
            Width = 3.5,
            Height = 3.5,
            FitToCurve = true,
            StylusTip = StylusTip.Ellipse,
            IgnorePressure = true
        };

        AnnotationCanvas.DefaultDrawingAttributes = da;
    }

    /// <summary>
    /// Updates the overlay tool mode (Cursor, Pen, Eraser).
    /// </summary>
    public void SetTool(AnnotationToolMode mode)
    {
        _currentTool = mode;

        switch (mode)
        {
            case AnnotationToolMode.Cursor:
                SetClickThrough(true);
                break;

            case AnnotationToolMode.Pen:
                SetClickThrough(false);
                if (AnnotationCanvas != null)
                {
                    ApplyDrawingAttributes();
                    AnnotationCanvas.EditingMode = InkCanvasEditingMode.Ink;
                }
                break;

            case AnnotationToolMode.Eraser:
                SetClickThrough(false);
                if (AnnotationCanvas != null)
                {
                    AnnotationCanvas.EditingMode = InkCanvasEditingMode.EraseByStroke;
                }
                break;
        }

        OrbitLogger.Info(LogCategory, $"Overlay mode set to: {mode} (ClickThrough={_isClickThrough})");
    }

    /// <summary>
    /// Sets the drawing color for subsequent pen strokes.
    /// </summary>
    public void SetPenColor(MediaColor color)
    {
        _currentColor = color;
        ApplyDrawingAttributes();
        OrbitLogger.Info(LogCategory, $"Overlay drawing color updated to #{color.R:X2}{color.G:X2}{color.B:X2}.");
    }

    /// <summary>
    /// Toggles WS_EX_TRANSPARENT on the overlay window.
    /// When true: clicks pass straight through to underlying applications.
    /// When false: clicks are captured by InkCanvas for drawing or erasing.
    /// </summary>
    public void SetClickThrough(bool enable)
    {
        _isClickThrough = enable;

        if (AnnotationCanvas != null)
        {
            if (enable)
            {
                AnnotationCanvas.EditingMode = InkCanvasEditingMode.None;
                AnnotationCanvas.IsHitTestVisible = false;
                AnnotationCanvas.Background = Brushes.Transparent;
                if (OverlayRootGrid != null) OverlayRootGrid.Background = Brushes.Transparent;
            }
            else
            {
                AnnotationCanvas.IsHitTestVisible = true;
                AnnotationCanvas.EditingMode = _currentTool == AnnotationToolMode.Eraser
                    ? InkCanvasEditingMode.EraseByStroke
                    : InkCanvasEditingMode.Ink;

                // Essential for Win32 layered window hit-testing:
                // Non-zero alpha (alpha = 1 / 255) ensures Windows DWM treats the surface as hit-testable,
                // while remaining 100% imperceptible and visually clear to human eyes!
                var hitTestBrush = new SolidColorBrush(MediaColor.FromArgb(1, 0, 0, 0));
                AnnotationCanvas.Background = hitTestBrush;
                if (OverlayRootGrid != null) OverlayRootGrid.Background = hitTestBrush;
            }
        }

        if (_hwnd == IntPtr.Zero) return;

        try
        {
            var exStyle = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            if (enable)
            {
                exStyle |= NativeMethods.WS_EX_TRANSPARENT;
            }
            else
            {
                exStyle &= ~NativeMethods.WS_EX_TRANSPARENT;
            }

            NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle));

            NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER |
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, $"Failed to toggle click-through ({enable}): {ex.Message}");
        }
    }

    /// <summary>
    /// Clears all drawn strokes immediately.
    /// </summary>
    public void ClearAllStrokes()
    {
        AnnotationCanvas?.Strokes.Clear();
        OrbitLogger.Info(LogCategory, "All annotation strokes cleared.");
    }

    /// <summary>
    /// Displays a momentary camera-shutter flash across the screen for visual screenshot feedback.
    /// </summary>
    public void TriggerShutterFlash()
    {
        if (ShutterFlashBorder == null) return;

        ShutterFlashBorder.Opacity = 0.65;
        var anim = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(320))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ShutterFlashBorder.BeginAnimation(OpacityProperty, anim);
    }

    private NativeMethods.POINT _lastWheelPt;
    private DateTime _lastWheelTime = DateTime.MinValue;
    private IntPtr _lastTargetHwnd = IntPtr.Zero;

    /// <summary>
    /// Forwards WM_MOUSEWHEEL messages received by this overlay window directly to the underlying
    /// window beneath the cursor, ensuring smooth, independent scrolling across all desktop applications
    /// (browsers, IDEs, PDFs, Word, documents) without having to switch back to Cursor mode.
    /// </summary>
    private void ForwardMouseWheel(IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (!NativeMethods.GetCursorPos(out var pt))
            {
                int raw = lParam.ToInt32();
                pt = new NativeMethods.POINT(unchecked((short)(raw & 0xFFFF)), unchecked((short)((raw >> 16) & 0xFFFF)));
            }

            // If mouse is over ORBIT's dynamic island or color palette, let ORBIT handle it
            if (IsOverOrbitUI(pt))
            {
                return;
            }

            IntPtr targetHwnd = IntPtr.Zero;
            var now = DateTime.UtcNow;

            // Cache target window if cursor position hasn't changed within 250ms for performance
            if (pt.X == _lastWheelPt.X && pt.Y == _lastWheelPt.Y &&
                (now - _lastWheelTime).TotalMilliseconds < 250 &&
                _lastTargetHwnd != IntPtr.Zero &&
                NativeMethods.IsWindow(_lastTargetHwnd))
            {
                targetHwnd = _lastTargetHwnd;
            }
            else
            {
                targetHwnd = FindUnderlyingWindow(pt);
                _lastWheelPt = pt;
                _lastTargetHwnd = targetHwnd;
            }

            _lastWheelTime = now;

            if (targetHwnd == IntPtr.Zero || targetHwnd == _hwnd || targetHwnd == _mainWindowHwnd)
                return;

            // Deliver WM_MOUSEWHEEL to the target window
            NativeMethods.PostMessage(targetHwnd, NativeMethods.WM_MOUSEWHEEL, wParam, lParam);
            NativeMethods.SendMessageTimeout(
                targetHwnd,
                NativeMethods.WM_MOUSEWHEEL,
                wParam,
                lParam,
                NativeMethods.SMTO_ABORTIFHUNG,
                30,
                out _);
        }
        catch (Exception ex)
        {
            OrbitLogger.Warn(LogCategory, $"Failed to forward mouse wheel: {ex.Message}");
        }
    }

    private IntPtr FindUnderlyingWindow(NativeMethods.POINT pt)
    {
        IntPtr foundTopLevel = IntPtr.Zero;

        NativeMethods.EnumWindows((hWnd, lParam) =>
        {
            // Skip Orbit windows
            if (hWnd == _hwnd || hWnd == _mainWindowHwnd)
                return true;

            // Skip invisible or minimized windows
            if (!NativeMethods.IsWindowVisible(hWnd) || NativeMethods.IsIconic(hWnd))
                return true;

            // Skip cloaked windows (virtual desktops, minimized UWP apps)
            if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;

            if (NativeMethods.GetWindowRect(hWnd, out var rect))
            {
                if (pt.X >= rect.Left && pt.X < rect.Right &&
                    pt.Y >= rect.Top && pt.Y < rect.Bottom)
                {
                    foundTopLevel = hWnd;
                    return false;
                }
            }

            return true;
        }, IntPtr.Zero);

        if (foundTopLevel == IntPtr.Zero)
            return IntPtr.Zero;

        // Drill down to the deepest visible child window under the cursor
        IntPtr current = foundTopLevel;
        NativeMethods.POINT clientPt = pt;
        NativeMethods.ScreenToClient(current, ref clientPt);

        for (int depth = 0; depth < 8; depth++)
        {
            IntPtr child = NativeMethods.ChildWindowFromPointEx(
                current,
                clientPt,
                NativeMethods.CWP_SKIPINVISIBLE | NativeMethods.CWP_SKIPDISABLED | NativeMethods.CWP_SKIPTRANSPARENT);

            if (child == IntPtr.Zero || child == current)
                break;

            current = child;
            clientPt = pt;
            NativeMethods.ScreenToClient(current, ref clientPt);
        }

        return current != IntPtr.Zero ? current : foundTopLevel;
    }

    protected override void OnPreviewMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseRightButtonDown(e);

        // When in Pen or Eraser mode, right-clicking auto-selects Cursor mode
        if (_currentTool != AnnotationToolMode.Cursor)
        {
            var ptScreen = PointToScreen(e.GetPosition(this));
            var pt = new NativeMethods.POINT((int)ptScreen.X, (int)ptScreen.Y);
            if (!IsOverOrbitUI(pt))
            {
                e.Handled = true;
                OrbitLogger.Info(LogCategory, "PreviewMouseRightButtonDown in overlay window. Requesting Cursor mode.");
                RequestCursorModeAction?.Invoke();
            }
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // 1. Right-click auto-selects Cursor mode so user can instantly interact with desktop
        if (msg == (int)NativeMethods.WM_RBUTTONDOWN || msg == (int)NativeMethods.WM_RBUTTONUP)
        {
            if (_currentTool != AnnotationToolMode.Cursor)
            {
                if (msg == (int)NativeMethods.WM_RBUTTONDOWN)
                {
                    if (!NativeMethods.GetCursorPos(out var pt))
                    {
                        int raw = lParam.ToInt32();
                        pt = new NativeMethods.POINT(unchecked((short)(raw & 0xFFFF)), unchecked((short)((raw >> 16) & 0xFFFF)));
                    }

                    if (!IsOverOrbitUI(pt))
                    {
                        OrbitLogger.Info(LogCategory, "Right-click detected in overlay window via Win32. Requesting Cursor mode.");
                        RequestCursorModeAction?.Invoke();
                    }
                }

                handled = true;
                return IntPtr.Zero;
            }
        }

        // 2. Intercept WM_MOUSEWHEEL directly in Win32 message loop
        if (msg == (int)NativeMethods.WM_MOUSEWHEEL)
        {
            handled = true;
            ForwardMouseWheel(wParam, lParam);
            return IntPtr.Zero;
        }

        // 3. WM_NCHITTEST for drawing, erasing, and click-through
        if (msg == NativeMethods.WM_NCHITTEST)
        {
            if (!NativeMethods.GetCursorPos(out var pt))
            {
                int raw = lParam.ToInt32();
                pt = new NativeMethods.POINT(unchecked((short)(raw & 0xFFFF)), unchecked((short)((raw >> 16) & 0xFFFF)));
            }

            // Cursor mode: let all clicks pass through everywhere
            if (_isClickThrough || _currentTool == AnnotationToolMode.Cursor)
            {
                handled = true;
                return new IntPtr(NativeMethods.HTTRANSPARENT);
            }

            // In Pen or Eraser mode:
            // If the mouse is over ORBIT's toolbar or color palette, pass clicks to ORBIT!
            if (IsOverOrbitUI(pt))
            {
                handled = true;
                return new IntPtr(NativeMethods.HTTRANSPARENT);
            }

            // Otherwise, mouse is over desktop drawing area. Deliver to this overlay window!
            handled = true;
            return new IntPtr(NativeMethods.HTCLIENT);
        }

        return IntPtr.Zero;
    }

    private bool IsOverOrbitUI(NativeMethods.POINT pt)
    {
        if (IsOverToolbarOrPaletteFunc != null)
        {
            try
            {
                return IsOverToolbarOrPaletteFunc(pt);
            }
            catch
            {
                // Fall through to fallback
            }
        }

        if (_mainWindowHwnd != IntPtr.Zero && NativeMethods.IsWindow(_mainWindowHwnd))
        {
            if (NativeMethods.GetWindowRect(_mainWindowHwnd, out var mainRect))
            {
                return pt.X >= mainRect.Left && pt.X < mainRect.Right &&
                       pt.Y >= mainRect.Top && pt.Y < mainRect.Bottom;
            }
        }

        return false;
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        base.OnClosed(e);
        OrbitLogger.Info(LogCategory, "Annotation overlay closed cleanly.");
    }
}

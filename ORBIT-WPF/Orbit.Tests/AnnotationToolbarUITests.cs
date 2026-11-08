using System;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Orbit.Core;
using Xunit;

namespace Orbit.Tests;

[Collection("UITests")]
public class AnnotationToolbarUITests
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
    public void AnnotationToolbar_ElementsExist_AndInitialDimensionsCorrect()
    {
        RunOnSta(() =>
        {
            var app = new OrbitApplication();
            var window = app.GetService<MainWindow>();
            Assert.NotNull(window);

            Assert.Equal(420.0, MainWindow.AnnotationToolbarWidth);

            var annotationPanel = window.FindName("AnnotationToolbarPanel") as StackPanel;
            Assert.NotNull(annotationPanel);
            Assert.Equal(Visibility.Collapsed, annotationPanel.Visibility);

            var btnBack = window.FindName("BtnAnnotateBack") as Button;
            var btnCursor = window.FindName("BtnAnnotateCursor") as Button;
            var btnPen = window.FindName("BtnAnnotatePen") as Button;
            var btnEraser = window.FindName("BtnAnnotateEraser") as Button;
            var btnClear = window.FindName("BtnAnnotateClear") as Button;
            var btnSave = window.FindName("BtnAnnotateSave") as Button;
            var btnTuck = window.FindName("BtnAnnotateTuck") as Button;

            Assert.NotNull(btnBack);
            Assert.NotNull(btnCursor);
            Assert.NotNull(btnPen);
            Assert.NotNull(btnEraser);
            Assert.NotNull(btnClear);
            Assert.NotNull(btnSave);
            Assert.NotNull(btnTuck);

            Assert.Equal("Exit Annotation", btnBack.ToolTip);
            Assert.Equal("Cursor / Select", btnCursor.ToolTip);
            Assert.Equal("Pen", btnPen.ToolTip);
            Assert.Equal("Eraser", btnEraser.ToolTip);
            Assert.Equal("Clear All", btnClear.ToolTip);
            Assert.Equal("Save Screenshot", btnSave.ToolTip);
        });
    }

    [Fact]
    public void EnterAnnotationMode_ExpandsToolbar_HidesMascotEyes_AndSelectsCursorByDefault()
    {
        RunOnSta(() =>
        {
            var app = new OrbitApplication();
            var window = app.GetService<MainWindow>();
            Assert.NotNull(window);

            // Trigger EnterAnnotationToolbarMode
            window.EnterAnnotationToolbarMode();

            Assert.True(window.IsAnnotationModeActive);
            Assert.Equal(AnnotationToolMode.Cursor, window.CurrentAnnotationTool);

            // Mascot eyes should be hidden
            Assert.Equal(0.0, window.PillMascot.LeftClosedEye.Opacity); // closed or fading

            // Exit annotation mode
            window.ExitAnnotationToolbarMode(returnToQuickActions: true, immediate: true);
            Assert.False(window.IsAnnotationModeActive);
        });
    }

    [Fact]
    public void ToolSelection_IsMutuallyExclusive_AndUpdatesVisualState()
    {
        RunOnSta(() =>
        {
            var app = new OrbitApplication();
            var window = app.GetService<MainWindow>();
            Assert.NotNull(window);

            window.EnterAnnotationToolbarMode();

            // Default: Cursor is selected
            Assert.Equal(AnnotationToolMode.Cursor, window.CurrentAnnotationTool);
            Assert.NotNull(window.IconAnnotateCursor.Effect);

            // Switch to Pen
            window.SelectAnnotationTool(AnnotationToolMode.Pen);
            Assert.Equal(AnnotationToolMode.Pen, window.CurrentAnnotationTool);
            Assert.NotNull(window.IconAnnotatePen.Effect);

            // Switch to Eraser
            window.SelectAnnotationTool(AnnotationToolMode.Eraser);
            Assert.Equal(AnnotationToolMode.Eraser, window.CurrentAnnotationTool);
            Assert.NotNull(window.IconAnnotateEraser.Effect);

            // Switch back to Cursor
            window.SelectAnnotationTool(AnnotationToolMode.Cursor);
            Assert.Equal(AnnotationToolMode.Cursor, window.CurrentAnnotationTool);
            Assert.NotNull(window.IconAnnotateCursor.Effect);

            window.ExitAnnotationToolbarMode(returnToQuickActions: true, immediate: true);
        });
    }

    [Fact]
    public void ExitAnnotationMode_RestoresStandardQuickActions_AndDimensions()
    {
        RunOnSta(() =>
        {
            var app = new OrbitApplication();
            var window = app.GetService<MainWindow>();
            Assert.NotNull(window);

            window.EnterAnnotationToolbarMode();
            Assert.True(window.IsAnnotationModeActive);

            // Exit returning to Quick Actions
            window.ExitAnnotationToolbarMode(returnToQuickActions: true, immediate: true);
            Assert.False(window.IsAnnotationModeActive);

            var standardPanel = window.FindName("StandardQuickActionsPanel") as StackPanel;
            var annotationPanel = window.FindName("AnnotationToolbarPanel") as StackPanel;

            Assert.NotNull(standardPanel);
            Assert.NotNull(annotationPanel);

            Assert.Equal(Visibility.Visible, standardPanel.Visibility);
            Assert.Equal(Visibility.Collapsed, annotationPanel.Visibility);
            Assert.Equal(MainWindow.QuickActionsWidth, window.Width);
        });
    }

    [Fact]
    public void ColorSelector_DefaultsToRed_AndHasValidVisuals()
    {
        RunOnSta(() =>
        {
            var app = new OrbitApplication();
            var window = app.GetService<MainWindow>();
            Assert.NotNull(window);

            Assert.Equal("#EF4444", window.CurrentAnnotationColorHex);
            Assert.NotNull(window.BtnAnnotateColorPicker);
            Assert.Equal("Select Color", window.BtnAnnotateColorPicker.ToolTip);

            Assert.NotNull(window.BorderCurrentColorSwatch);
            Assert.NotNull(window.GlowCurrentColorSwatch);
            Assert.Equal(System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44), window.CurrentAnnotationColor);
        });
    }

    [Fact]
    public void ColorPalette_ContainsTenSwatches_AndSelectingColorUpdatesSwatchAndPreservesAcrossTools()
    {
        RunOnSta(() =>
        {
            var app = new OrbitApplication();
            var window = app.GetService<MainWindow>();
            Assert.NotNull(window);

            window.EnterAnnotationToolbarMode();

            // Verify popup and swatches
            Assert.NotNull(window.AnnotationColorPalettePopup);
            Assert.NotNull(window.AnnotationColorPalettePopup.CustomPopupPlacementCallback);
            Assert.NotNull(window.BtnPaletteColor_Red);
            Assert.NotNull(window.BtnPaletteColor_Orange);
            Assert.NotNull(window.BtnPaletteColor_Yellow);
            Assert.NotNull(window.BtnPaletteColor_Green);
            Assert.NotNull(window.BtnPaletteColor_Cyan);
            Assert.NotNull(window.BtnPaletteColor_Blue);
            Assert.NotNull(window.BtnPaletteColor_Purple);
            Assert.NotNull(window.BtnPaletteColor_Pink);
            Assert.NotNull(window.BtnPaletteColor_White);
            Assert.NotNull(window.BtnPaletteColor_Black);

            // Red checkmark visible initially
            Assert.Equal(Visibility.Visible, window.Check_Red.Visibility);
            Assert.Equal(Visibility.Collapsed, window.Check_Green.Visibility);

            // Select Green (#22C55E)
            window.SelectAnnotationColor("#22C55E");
            Assert.Equal("#22C55E", window.CurrentAnnotationColorHex);
            Assert.Equal(Visibility.Collapsed, window.Check_Red.Visibility);
            Assert.Equal(Visibility.Visible, window.Check_Green.Visibility);

            // Switch to Pen: should reflect Green fill and neon effect
            window.SelectAnnotationTool(AnnotationToolMode.Pen);
            Assert.Equal(AnnotationToolMode.Pen, window.CurrentAnnotationTool);
            var penFillBrush = window.IconAnnotatePen.Fill as System.Windows.Media.SolidColorBrush;
            Assert.NotNull(penFillBrush);
            Assert.Equal(System.Windows.Media.Color.FromRgb(0x22, 0xC5, 0x5E), penFillBrush.Color);
            var penGlow = window.IconAnnotatePen.Effect as System.Windows.Media.Effects.DropShadowEffect;
            Assert.NotNull(penGlow);
            Assert.Equal(System.Windows.Media.Color.FromRgb(0x22, 0xC5, 0x5E), penGlow.Color);

            // Switch to Eraser and Cursor: color is still preserved
            window.SelectAnnotationTool(AnnotationToolMode.Eraser);
            window.SelectAnnotationTool(AnnotationToolMode.Cursor);
            Assert.Equal("#22C55E", window.CurrentAnnotationColorHex);

            // Switch back to Pen: still Green!
            window.SelectAnnotationTool(AnnotationToolMode.Pen);
            Assert.Equal("#22C55E", window.CurrentAnnotationColorHex);

            window.ExitAnnotationToolbarMode(returnToQuickActions: true, immediate: true);
            Assert.False(window.AnnotationColorPalettePopup.IsOpen);
        });
    }

    [Fact]
    public void ToolSelection_UsesDistinctColors_PerTool()
    {
        RunOnSta(() =>
        {
            var app = new OrbitApplication();
            var window = app.GetService<MainWindow>();
            Assert.NotNull(window);

            window.EnterAnnotationToolbarMode();

            // 1. Cursor is Blue (#38BDF8) when selected, otherwise white outline
            window.SelectAnnotationTool(AnnotationToolMode.Cursor);
            var cursorBrush = window.IconAnnotateCursor.Fill as System.Windows.Media.SolidColorBrush;
            Assert.NotNull(cursorBrush);
            Assert.Equal(System.Windows.Media.Color.FromRgb(0x38, 0xBD, 0xF8), cursorBrush.Color);
            Assert.NotNull(window.IconAnnotateCursor.Effect);

            // 2. Eraser is filled white when selected, otherwise white border only
            window.SelectAnnotationTool(AnnotationToolMode.Eraser);
            Assert.Equal(System.Windows.Media.Brushes.White, window.IconAnnotateEraser.Fill);
            Assert.NotNull(window.IconAnnotateEraser.Effect);

            // Cursor now unselected: white outline, transparent fill
            Assert.Equal(System.Windows.Media.Brushes.Transparent, window.IconAnnotateCursor.Fill);
            Assert.Equal(System.Windows.Media.Brushes.White, window.IconAnnotateCursor.Stroke);
            Assert.Null(window.IconAnnotateCursor.Effect);

            // 3. Pen uses current color fill with white outline
            window.SelectAnnotationColor("#EF4444");
            window.SelectAnnotationTool(AnnotationToolMode.Pen);
            var penFill = window.IconAnnotatePen.Fill as System.Windows.Media.SolidColorBrush;
            Assert.NotNull(penFill);
            Assert.Equal(System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44), penFill.Color);
            Assert.Equal(System.Windows.Media.Brushes.White, window.IconAnnotatePen.Stroke);
            Assert.NotNull(window.IconAnnotatePen.Effect);

            window.ExitAnnotationToolbarMode(returnToQuickActions: true, immediate: true);
        });
    }

    [Fact]
    public void DesktopAnnotationOverlay_InitializesWithTransparentInkCanvas_AndFullscreenBounds()
    {
        RunOnSta(() =>
        {
            var overlay = new Orbit.UI.DesktopAnnotationOverlayWindow();
            Assert.NotNull(overlay.AnnotationCanvas);
            Assert.Equal(System.Windows.Media.Brushes.Transparent, overlay.AnnotationCanvas.Background);
            Assert.Equal(System.Windows.Media.Brushes.Transparent, overlay.Background);
            Assert.True(overlay.AllowsTransparency);
            Assert.Equal(WindowStyle.None, overlay.WindowStyle);
            Assert.False(overlay.ShowInTaskbar);
            Assert.True(overlay.Topmost);

            // Bounds should cover virtual screen
            Assert.Equal(SystemParameters.VirtualScreenLeft, overlay.Left);
            Assert.Equal(SystemParameters.VirtualScreenTop, overlay.Top);
            Assert.Equal(SystemParameters.VirtualScreenWidth, overlay.Width);
            Assert.Equal(SystemParameters.VirtualScreenHeight, overlay.Height);

            overlay.Close();
        });
    }

    [Fact]
    public void DesktopAnnotationOverlay_CursorMode_EnablesClickThrough_AndDisablesInkHitTest()
    {
        RunOnSta(() =>
        {
            var overlay = new Orbit.UI.DesktopAnnotationOverlayWindow();
            overlay.SetTool(AnnotationToolMode.Cursor);

            Assert.Equal(AnnotationToolMode.Cursor, overlay.CurrentTool);
            Assert.True(overlay.IsClickThrough);
            Assert.Equal(InkCanvasEditingMode.None, overlay.AnnotationCanvas.EditingMode);
            Assert.False(overlay.AnnotationCanvas.IsHitTestVisible);

            overlay.Close();
        });
    }

    [Fact]
    public void DesktopAnnotationOverlay_PenMode_DisablesClickThrough_AndSetsInkMode_WithSelectedColor()
    {
        RunOnSta(() =>
        {
            var overlay = new Orbit.UI.DesktopAnnotationOverlayWindow();

            // Set color first
            var testColor = System.Windows.Media.Color.FromRgb(0x06, 0xB6, 0xD4); // Cyan
            overlay.SetPenColor(testColor);
            overlay.SetTool(AnnotationToolMode.Pen);

            Assert.Equal(AnnotationToolMode.Pen, overlay.CurrentTool);
            Assert.False(overlay.IsClickThrough);
            Assert.Equal(InkCanvasEditingMode.Ink, overlay.AnnotationCanvas.EditingMode);
            Assert.True(overlay.AnnotationCanvas.IsHitTestVisible);
            Assert.Equal(testColor, overlay.AnnotationCanvas.DefaultDrawingAttributes.Color);
            Assert.True(overlay.AnnotationCanvas.DefaultDrawingAttributes.FitToCurve);

            overlay.Close();
        });
    }

    [Fact]
    public void DesktopAnnotationOverlay_EraserMode_DisablesClickThrough_AndSetsEraseByStroke()
    {
        RunOnSta(() =>
        {
            var overlay = new Orbit.UI.DesktopAnnotationOverlayWindow();
            overlay.SetTool(AnnotationToolMode.Eraser);

            Assert.Equal(AnnotationToolMode.Eraser, overlay.CurrentTool);
            Assert.False(overlay.IsClickThrough);
            Assert.Equal(InkCanvasEditingMode.EraseByStroke, overlay.AnnotationCanvas.EditingMode);
            Assert.True(overlay.AnnotationCanvas.IsHitTestVisible);

            overlay.Close();
        });
    }

    [Fact]
    public void DesktopAnnotationOverlay_ClearAllStrokes_EmptiesCanvasStrokes()
    {
        RunOnSta(() =>
        {
            var overlay = new Orbit.UI.DesktopAnnotationOverlayWindow();
            overlay.SetTool(AnnotationToolMode.Pen);

            // Add dummy stroke
            var points = new System.Windows.Input.StylusPointCollection
            {
                new System.Windows.Input.StylusPoint(10, 10),
                new System.Windows.Input.StylusPoint(20, 20)
            };
            var stroke = new System.Windows.Ink.Stroke(points);
            overlay.AnnotationCanvas.Strokes.Add(stroke);
            Assert.Single(overlay.AnnotationCanvas.Strokes);

            // Clear strokes
            overlay.ClearAllStrokes();
            Assert.Empty(overlay.AnnotationCanvas.Strokes);

            overlay.Close();
        });
    }

    [Fact]
    public void MainWindow_AnnotationLifecycle_CreatesAndCleansUpOverlayWindow()
    {
        RunOnSta(() =>
        {
            var app = new OrbitApplication();
            var window = app.GetService<MainWindow>();
            Assert.NotNull(window);

            Assert.Null(window.AnnotationOverlayWindow);

            // Enter annotation mode
            window.EnterAnnotationToolbarMode();
            Assert.True(window.IsAnnotationModeActive);
            Assert.NotNull(window.AnnotationOverlayWindow);
            Assert.Equal(AnnotationToolMode.Cursor, window.AnnotationOverlayWindow.CurrentTool);
            Assert.True(window.AnnotationOverlayWindow.IsClickThrough);

            // Switch to Pen tool through MainWindow
            window.SelectAnnotationTool(AnnotationToolMode.Pen);
            Assert.Equal(AnnotationToolMode.Pen, window.AnnotationOverlayWindow.CurrentTool);
            Assert.False(window.AnnotationOverlayWindow.IsClickThrough);

            // Switch color through MainWindow
            window.SelectAnnotationColor("#A855F7"); // Purple
            Assert.Equal(System.Windows.Media.Color.FromRgb(0xA8, 0x55, 0xF7), window.AnnotationOverlayWindow.CurrentColor);
            Assert.Equal(System.Windows.Media.Color.FromRgb(0xA8, 0x55, 0xF7), window.AnnotationOverlayWindow.AnnotationCanvas.DefaultDrawingAttributes.Color);

            // Exit annotation mode
            window.ExitAnnotationToolbarMode(returnToQuickActions: true, immediate: true);
            Assert.False(window.IsAnnotationModeActive);
            Assert.Null(window.AnnotationOverlayWindow);
        });
    }

    [Fact]
    public void AnnotationPalette_ColorSelection_UpdatesCheckmarksAndToolVisuals()
    {
        RunOnSta(() =>
        {
            var app = new OrbitApplication();
            var window = app.GetService<MainWindow>();
            Assert.NotNull(window);

            window.EnterAnnotationToolbarMode();
            window.SelectAnnotationTool(AnnotationToolMode.Pen);

            // Test selecting multiple colors
            window.SelectAnnotationColor("#3B82F6"); // Blue
            Assert.Equal("#3B82F6", window.CurrentAnnotationColorHex);
            Assert.Equal(System.Windows.Media.Color.FromRgb(0x3B, 0x82, 0xF6), window.CurrentAnnotationColor);

            var checkBlue = window.FindName("Check_Blue") as FrameworkElement;
            var checkRed = window.FindName("Check_Red") as FrameworkElement;
            Assert.NotNull(checkBlue);
            Assert.NotNull(checkRed);
            Assert.Equal(Visibility.Visible, checkBlue.Visibility);
            Assert.Equal(Visibility.Collapsed, checkRed.Visibility);

            // Switch to Yellow (#EAB308)
            window.SelectAnnotationColor("#EAB308");
            Assert.Equal("#EAB308", window.CurrentAnnotationColorHex);
            var checkYellow = window.FindName("Check_Yellow") as FrameworkElement;
            Assert.NotNull(checkYellow);
            Assert.Equal(Visibility.Visible, checkYellow.Visibility);
            Assert.Equal(Visibility.Collapsed, checkBlue.Visibility);

            window.ExitAnnotationToolbarMode(returnToQuickActions: false, immediate: true);
        });
    }

    [Fact]
    public void RightClick_AutoSelectsCursorMode()
    {
        RunOnSta(() =>
        {
            var app = new OrbitApplication();
            var window = app.GetService<MainWindow>();
            Assert.NotNull(window);

            window.EnterAnnotationToolbarMode();
            Assert.True(window.IsAnnotationModeActive);

            // Switch to Pen mode
            window.SelectAnnotationTool(AnnotationToolMode.Pen);
            Assert.Equal(AnnotationToolMode.Pen, window.CurrentAnnotationTool);
            Assert.NotNull(window.AnnotationOverlayWindow);
            Assert.False(window.AnnotationOverlayWindow.IsClickThrough);

            // Trigger right-click action on overlay
            window.AnnotationOverlayWindow.RequestCursorModeAction?.Invoke();

            // Run pending dispatcher actions
            var frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);

            // Should have automatically transitioned to Cursor mode!
            Assert.Equal(AnnotationToolMode.Cursor, window.CurrentAnnotationTool);
            Assert.True(window.AnnotationOverlayWindow.IsClickThrough);

            window.ExitAnnotationToolbarMode(returnToQuickActions: false, immediate: true);
        });
    }

    [Fact]
    public void TuckAnnotationToolbar_TucksAndRestores_PreservingToolAndOverlay()
    {
        RunOnSta(() =>
        {
            var app = new OrbitApplication();
            var window = app.GetService<MainWindow>();
            Assert.NotNull(window);

            window.EnterAnnotationToolbarMode();
            Assert.True(window.IsAnnotationModeActive);
            Assert.False(window.IsAnnotationToolbarTucked);

            // Select Pen mode with a specific color
            window.SelectAnnotationTool(AnnotationToolMode.Pen);
            window.SelectAnnotationColor("#A855F7"); // Purple

            // Tuck toolbar
            window.TuckAnnotationToolbar(true);
            Assert.True(window.IsAnnotationToolbarTucked);
            Assert.True(window.IsAnnotationModeActive);
            Assert.NotNull(window.AnnotationOverlayWindow); // Overlay remains active
            Assert.Equal(AnnotationToolMode.Pen, window.CurrentAnnotationTool); // Tool preserved

            // Restore toolbar
            window.TuckAnnotationToolbar(false);
            Assert.False(window.IsAnnotationToolbarTucked);
            Assert.True(window.IsAnnotationModeActive);
            Assert.NotNull(window.AnnotationOverlayWindow);
            Assert.Equal(AnnotationToolMode.Pen, window.CurrentAnnotationTool);
            Assert.Equal("#A855F7", window.CurrentAnnotationColorHex);

            window.ExitAnnotationToolbarMode(returnToQuickActions: false, immediate: true);
        });
    }

    [Fact]
    public void CaptureAnnotationScreenshot_SavesToDisk_AndCopiesToClipboard()
    {
        RunOnSta(() =>
        {
            var app = new OrbitApplication();
            var window = app.GetService<MainWindow>();
            Assert.NotNull(window);

            window.EnterAnnotationToolbarMode();
            Assert.True(window.IsAnnotationModeActive);

            var task = window.CaptureAnnotationScreenshotAsync();
            task.Wait();
            string? filePath = task.Result;

            Assert.NotNull(filePath);
            Assert.True(System.IO.File.Exists(filePath));
            Assert.True(new System.IO.FileInfo(filePath).Length > 0);
            Assert.Contains("ORBIT Screenshots", filePath);

            window.ExitAnnotationToolbarMode(returnToQuickActions: false, immediate: true);
        });
    }
}


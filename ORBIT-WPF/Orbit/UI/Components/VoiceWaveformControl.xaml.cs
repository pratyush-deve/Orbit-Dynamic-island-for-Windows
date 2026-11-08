using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using MediaColor = System.Windows.Media.Color;

namespace Orbit.UI.Components;

/// <summary>
/// Lightweight, smooth 30-60 FPS horizontal audio waveform visualizer.
/// Symmetrically renders vertical rounded capsule bars reacting to real microphone RMS intensity.
/// Reuses pre-created UI elements to prevent garbage collection churn.
/// </summary>
public partial class VoiceWaveformControl : UserControl
{
    private const int BarCount = 20;
    private const double BarWidth = 3.5;
    private const double BarSpacing = 3.5;
    private const double MinBarHeight = 4.5;
    private const double MaxBarHeight = 22.0;

    private readonly WpfRectangle[] _bars = new WpfRectangle[BarCount];
    private readonly double[] _currentHeights = new double[BarCount];
    private readonly double[] _targetHeights = new double[BarCount];

    private static readonly SolidColorBrush CenterBarBrush = CreateCenterBarBrush();
    private static readonly SolidColorBrush OuterBarBrush = CreateOuterBarBrush();

    private float _latestIntensity = 0f;
    private bool _isRendering = false;
    private double _phase = 0.0;

    private static SolidColorBrush CreateCenterBarBrush()
    {
        var brush = new SolidColorBrush(MediaColor.FromRgb(0xC0, 0x84, 0xFC)); // Neon purple
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush CreateOuterBarBrush()
    {
        var brush = new SolidColorBrush(MediaColor.FromArgb(0x99, 0xA8, 0x55, 0xF7)); // Soft translucent purple
        brush.Freeze();
        return brush;
    }

    public VoiceWaveformControl()
    {
        InitializeComponent();
        InitializeBars();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
    }

    private void InitializeBars()
    {
        WaveformCanvas.Children.Clear();

        for (int i = 0; i < BarCount; i++)
        {
            // Center bars are brighter, outer bars fade softly
            bool isCenter = i >= (BarCount / 2 - 5) && i <= (BarCount / 2 + 4);
            var rect = new WpfRectangle
            {
                Width = BarWidth,
                Height = MinBarHeight,
                RadiusX = 1.75,
                RadiusY = 1.75,
                Fill = isCenter ? CenterBarBrush : OuterBarBrush
            };

            _bars[i] = rect;
            _currentHeights[i] = MinBarHeight;
            _targetHeights[i] = MinBarHeight;
            WaveformCanvas.Children.Add(rect);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        StartRendering();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopRendering();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        LayoutBars();
    }

    public void StartRendering()
    {
        if (_isRendering) return;
        _isRendering = true;
        CompositionTarget.Rendering += OnRenderingFrame;
    }

    public void StopRendering()
    {
        if (!_isRendering) return;
        _isRendering = false;
        CompositionTarget.Rendering -= OnRenderingFrame;

        // Reset bars to minimum resting height
        for (int i = 0; i < BarCount; i++)
        {
            _currentHeights[i] = MinBarHeight;
            _targetHeights[i] = MinBarHeight;
            _bars[i].Height = MinBarHeight;
        }
        LayoutBars();
    }

    /// <summary>
    /// Updates the visualizer with the latest normalized RMS audio intensity (0.0 to 1.0).
    /// </summary>
    public void SetAudioIntensity(float intensity)
    {
        // Smooth instantaneous intensity updates
        _latestIntensity = Math.Clamp(intensity, 0f, 1f);
    }

    private void OnRenderingFrame(object? sender, EventArgs e)
    {
        if (!_isRendering) return;

        double canvasHeight = ActualHeight > 0 ? ActualHeight : 30.0;
        _phase += 0.09;

        int half = BarCount / 2;

        for (int i = 0; i < BarCount; i++)
        {
            // Symmetrical distance from center (0 at center, 1 at ends)
            double distFromCenter = Math.Abs(i - (half - 0.5)) / (double)half;
            double envelope = Math.Cos(distFromCenter * (Math.PI / 2.2)); // Smooth bell-curve

            // Elegant flowing idle breathing wave: continuous organic motion
            double idleBreathing = (Math.Sin(_phase + (i * 0.45)) * 0.5 + 0.5) * 4.0 * envelope;

            // When user speaks, react dynamically and proportionally to RMS intensity
            double speechFactor = Math.Min(1.0, _latestIntensity * 3.5);
            double voiceJitter = Math.Sin(_phase * 2.8 + i * 0.75) * 0.2 + 0.8;
            double speechHeight = speechFactor * (MaxBarHeight - MinBarHeight) * envelope * voiceJitter;

            double target = MinBarHeight + Math.Max(idleBreathing, speechHeight);
            _targetHeights[i] = Math.Clamp(target, MinBarHeight, MaxBarHeight);

            // Responsive exponential smoothing (lerp) towards target height for fluid animation
            _currentHeights[i] += (_targetHeights[i] - _currentHeights[i]) * 0.42;

            _bars[i].Height = _currentHeights[i];
            Canvas.SetTop(_bars[i], (canvasHeight - _currentHeights[i]) / 2.0);
        }
    }

    private void LayoutBars()
    {
        double canvasWidth = ActualWidth > 0 ? ActualWidth : 220.0;
        double canvasHeight = ActualHeight > 0 ? ActualHeight : 30.0;

        double totalBarsWidth = (BarCount * BarWidth) + ((BarCount - 1) * BarSpacing);
        double startX = Math.Max(0, (canvasWidth - totalBarsWidth) / 2.0);

        for (int i = 0; i < BarCount; i++)
        {
            Canvas.SetLeft(_bars[i], startX + (i * (BarWidth + BarSpacing)));
            Canvas.SetTop(_bars[i], (canvasHeight - _bars[i].Height) / 2.0);
        }
    }
}

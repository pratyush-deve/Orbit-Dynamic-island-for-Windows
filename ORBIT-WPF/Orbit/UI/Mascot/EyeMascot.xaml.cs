using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Orbit.UI.Mascot;

/// <summary>
/// Interaction logic for EyeMascot.xaml.
/// Controls expressive pupil tracking within the window and organic idle gaze/blinking.
/// </summary>
public partial class EyeMascot : UserControl
{
    private readonly DispatcherTimer _blinkTimer;
    private readonly DispatcherTimer _glanceTimer;
    private readonly Random _random = new();

    private bool _isGlowEnabled = true;
    private bool _isTrackingCursor = false;

    private double _lastTargetLeftX = 0.0;
    private double _lastTargetLeftY = 0.0;
    private double _lastTargetRightX = 0.0;
    private double _lastTargetRightY = 0.0;

    public EyeMascot()
    {
        InitializeComponent();

        _blinkTimer = new DispatcherTimer();
        _blinkTimer.Interval = TimeSpan.FromSeconds(3.5);
        _blinkTimer.Tick += (s, e) => TriggerBlink();

        _glanceTimer = new DispatcherTimer();
        _glanceTimer.Interval = TimeSpan.FromSeconds(2.8);
        _glanceTimer.Tick += (s, e) => TriggerGlance();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        StartAnimations();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopAnimations();
    }

    public void StartAnimations()
    {
        _blinkTimer.Start();
        _glanceTimer.Start();
        StartIdleGlow();
    }

    public void StopAnimations()
    {
        _blinkTimer.Stop();
        _glanceTimer.Stop();
    }

    public void SetGlowEnabled(bool enabled)
    {
        _isGlowEnabled = enabled;
        if (PillGlow != null)
        {
            PillGlow.Opacity = enabled ? 0.5 : 0.0;
        }
    }

    /// <summary>
    /// Smooth organic blinking using cubic ease animations on vertical sclera scale.
    /// Operates concurrently with pupil translation.
    /// </summary>
    public void TriggerBlink(bool isDoubleBlink = false)
    {
        var blinkAnimation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(180)
        };

        // Keyframe sequence: Open -> Closed -> Open
        blinkAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(0.0)));
        blinkAnimation.KeyFrames.Add(new SplineDoubleKeyFrame(0.06, KeyTime.FromPercent(0.45), new KeySpline(0.4, 0.0, 0.2, 1.0)));
        blinkAnimation.KeyFrames.Add(new SplineDoubleKeyFrame(1.0, KeyTime.FromPercent(1.0), new KeySpline(0.1, 0.0, 0.2, 1.0)));

        LeftEyeScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, blinkAnimation);
        RightEyeScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, blinkAnimation);

        // Reset timer with organic variation
        _blinkTimer.Interval = TimeSpan.FromSeconds(2.5 + (_random.NextDouble() * 3.5));

        // Occasional natural double-blink (15% chance)
        if (!isDoubleBlink && _random.NextDouble() < 0.15)
        {
            var doubleBlinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
            doubleBlinkTimer.Tick += (s, e) =>
            {
                doubleBlinkTimer.Stop();
                TriggerBlink(true);
            };
            doubleBlinkTimer.Start();
        }
    }

    /// <summary>
    /// Follows the mouse cursor smoothly while inside the ORBIT window.
    /// Calculates direction vector from each eye's centre and clamps within the sclera.
    /// Fast and responsive with micro-easing to eliminate lag while avoiding jitter.
    /// </summary>
    public void TrackCursor(Point cursorRelativeToMascot)
    {
        bool wasIdle = !_isTrackingCursor;
        _isTrackingCursor = true;
        _glanceTimer.Stop();

        // Eye centres relative to EyeMascot surface (Left eye at ~28,24; Right eye at ~64,24)
        var (leftX, leftY) = CalculatePupilOffset(cursorRelativeToMascot.X - 28.0, cursorRelativeToMascot.Y - 24.0);
        var (rightX, rightY) = CalculatePupilOffset(cursorRelativeToMascot.X - 64.0, cursorRelativeToMascot.Y - 24.0);

        if (wasIdle)
        {
            // Initial acquisition: smooth 100ms glide from wherever the idle gaze was
            _lastTargetLeftX = leftX;
            _lastTargetLeftY = leftY;
            _lastTargetRightX = rightX;
            _lastTargetRightY = rightY;
            AnimatePupilsIndividual(leftX, leftY, rightX, rightY, TimeSpan.FromMilliseconds(100));
            return;
        }

        // Active tracking: fast, fluid response with micro-threshold to prevent redundant redraws
        if (Math.Abs(leftX - _lastTargetLeftX) > 0.04 || Math.Abs(leftY - _lastTargetLeftY) > 0.04)
        {
            _lastTargetLeftX = leftX;
            _lastTargetLeftY = leftY;
            _lastTargetRightX = rightX;
            _lastTargetRightY = rightY;

            AnimatePupilsIndividual(leftX, leftY, rightX, rightY, TimeSpan.FromMilliseconds(20));
        }
    }

    /// <summary>
    /// Smoothly transitions away from cursor tracking back to playful idle gaze.
    /// </summary>
    public void OnWindowMouseLeave()
    {
        if (!_isTrackingCursor) return;

        _isTrackingCursor = false;
        _lastTargetLeftX = 0.0;
        _lastTargetLeftY = 0.0;
        _lastTargetRightX = 0.0;
        _lastTargetRightY = 0.0;

        // Smoothly glide pupils to resting center
        AnimatePupilsIndividual(0.0, 0.0, 0.0, 0.0, TimeSpan.FromMilliseconds(280));

        // Resume natural idle glance timer
        _glanceTimer.Interval = TimeSpan.FromSeconds(2.0 + (_random.NextDouble() * 2.5));
        _glanceTimer.Start();
    }

    /// <summary>
    /// Clamps pupil displacement within an elliptic boundary inside the eyeball.
    /// Mathematically guarantees that neither pupil can ever leave the sclera.
    /// Rapid distance curve provides immediate gaze direction even for small cursor shifts.
    /// </summary>
    private static (double x, double y) CalculatePupilOffset(double deltaX, double deltaY)
    {
        const double maxRadiusX = 4.5;
        const double maxRadiusY = 5.2;

        double dist = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (dist < 0.1) return (0.0, 0.0);

        // Normalize direction vector
        double dirX = deltaX / dist;
        double dirY = deltaY / dist;

        // Snappy distance response: reaches full gaze deflection within 35 DIPs
        double intensity = Math.Min(1.0, dist / 35.0);

        // Elliptically clamped offsets
        double targetX = dirX * maxRadiusX * intensity;
        double targetY = dirY * maxRadiusY * intensity;

        return (targetX, targetY);
    }

    /// <summary>
    /// Playful randomized idle glances when mouse is outside the window.
    /// </summary>
    private void TriggerGlance()
    {
        if (_isTrackingCursor) return;

        int roll = _random.Next(100);
        double targetX;
        double targetY;

        if (roll < 42)
        {
            // 42% chance: Look straight ahead
            targetX = 0.0;
            targetY = 0.0;
        }
        else if (roll < 62)
        {
            // 20% chance: Look left
            targetX = -2.8 - (_random.NextDouble() * 1.4);
            targetY = (_random.NextDouble() * 1.6) - 0.8;
        }
        else if (roll < 82)
        {
            // 20% chance: Look right
            targetX = 2.8 + (_random.NextDouble() * 1.4);
            targetY = (_random.NextDouble() * 1.6) - 0.8;
        }
        else if (roll < 92)
        {
            // 10% chance: Look slightly up
            targetX = (_random.NextDouble() * 2.0) - 1.0;
            targetY = -3.0 - (_random.NextDouble() * 1.4);
        }
        else
        {
            // 8% chance: Look slightly down
            targetX = (_random.NextDouble() * 2.0) - 1.0;
            targetY = 2.0 + (_random.NextDouble() * 1.6);
        }

        AnimatePupilsIndividual(targetX, targetY, targetX, targetY, TimeSpan.FromMilliseconds(260));

        // Randomize next glance interval
        _glanceTimer.Interval = TimeSpan.FromSeconds(2.2 + (_random.NextDouble() * 2.8));
    }

    private void AnimatePupilsIndividual(double leftX, double leftY, double rightX, double rightY, TimeSpan duration)
    {
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };

        var animLeftX = new DoubleAnimation(leftX, duration) { EasingFunction = ease };
        var animLeftY = new DoubleAnimation(leftY, duration) { EasingFunction = ease };
        var animRightX = new DoubleAnimation(rightX, duration) { EasingFunction = ease };
        var animRightY = new DoubleAnimation(rightY, duration) { EasingFunction = ease };

        LeftPupilTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, animLeftX);
        LeftPupilTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, animLeftY);
        LeftCatchlightTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, animLeftX);
        LeftCatchlightTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, animLeftY);

        RightPupilTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, animRightX);
        RightPupilTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, animRightY);
        RightCatchlightTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, animRightX);
        RightCatchlightTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, animRightY);
    }

    private void StartIdleGlow()
    {
        if (!_isGlowEnabled || PillGlow == null) return;

        var glowAnimation = new DoubleAnimation
        {
            From = 0.35,
            To = 0.65,
            Duration = TimeSpan.FromSeconds(2.5),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };

        PillGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, glowAnimation);
    }
}

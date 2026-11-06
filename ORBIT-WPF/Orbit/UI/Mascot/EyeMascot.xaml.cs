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

    private static readonly System.Windows.Media.Color ColorIdle = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#A855F7");
    private static readonly System.Windows.Media.Color ColorWorking = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#C084FC");
    private static readonly System.Windows.Media.Color ColorCompleted = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#10B981");
    private static readonly System.Windows.Media.Color ColorAttention = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#EF4444");
    private static readonly System.Windows.Media.Color ColorEyePurple = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#A855F7");
    private static readonly System.Windows.Media.Color ColorGlowPurple = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#C084FC");
    private static readonly System.Windows.Media.Color ColorEyeWhite = System.Windows.Media.Colors.White;

    private static readonly System.Windows.Media.SolidColorBrush BrushWorkingBorder = new(ColorWorking);
    private static readonly System.Windows.Media.SolidColorBrush BrushCompletedBorder = new(ColorCompleted);
    private static readonly System.Windows.Media.SolidColorBrush BrushAttentionBorder = new(ColorAttention);
    private static readonly System.Windows.Media.SolidColorBrush BrushWorkingMouth = new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E9D5FF"));

    // Expressions for ORBIT Mascot's tiny expressive mouth
    private static readonly System.Windows.Media.Geometry GeometryMouthIdle = System.Windows.Media.Geometry.Parse("M 0,0 Q 4,2.2 8,0");
    private static readonly System.Windows.Media.Geometry GeometryMouthWorking = System.Windows.Media.Geometry.Parse("M 0,2 L 8,2");
    private static readonly System.Windows.Media.Geometry GeometryMouthError = System.Windows.Media.Geometry.Parse("M 0,2.5 Q 4,0.2 8,2.5");
    private static readonly System.Windows.Media.Geometry GeometryMouthAttention = System.Windows.Media.Geometry.Parse("M 0,2 L 2,0.5 L 4,2 L 6,0.5 L 8,2");
    private static readonly System.Windows.Media.Geometry GeometryMouthCompleted = System.Windows.Media.Geometry.Parse("M 0,0 Q 4,2.8 8,0");

    private Orbit.Models.AgentState _currentMouthState = Orbit.Models.AgentState.Idle;

    private DispatcherTimer? _donePersistenceTimer;
    private int _doneTimerToken = 0;
    private bool _isHoldingDoneState = false;
    private bool _isDoneExpired = false;
    private string? _lastCompletedMessage;

    private bool _isGlowEnabled = true;
    private bool _isTrackingCursor = false;

    /// <summary>
    /// Pure facial features mascot component (eyes, purple iris glow, and tiny smile).
    /// </summary>
    public bool IsEmbeddedInNotch { get; set; } = true;

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
        if (MouthPath != null && MouthPath.Data == null)
        {
            MouthPath.Data = GeometryMouthIdle;
        }
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
        CancelDoneHold();
    }

    public void SetGlowEnabled(bool enabled)
    {
        _isGlowEnabled = enabled;
    }

    /// <summary>
    /// Smooth organic blinking using cubic ease animations on vertical sclera scale.
    /// Operates concurrently with pupil translation.
    /// </summary>
    public void TriggerBlink(bool isDoubleBlink = false)
    {
        if (_currentMouthState == Orbit.Models.AgentState.Working)
        {
            return; // Maintain rock-solid, focused concentration during active work
        }

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

        // Eye centres relative to EyeMascot surface (Left eye at ~11,13; Right eye at ~43,13)
        var (leftX, leftY) = CalculatePupilOffset(cursorRelativeToMascot.X - 11.0, cursorRelativeToMascot.Y - 13.0);
        var (rightX, rightY) = CalculatePupilOffset(cursorRelativeToMascot.X - 43.0, cursorRelativeToMascot.Y - 13.0);

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
        const double maxRadiusX = 4.0;
        const double maxRadiusY = 2.6;

        double dist = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (dist < 0.1) return (0.0, 0.0);

        // Normalize direction vector
        double dirX = deltaX / dist;
        double dirY = deltaY / dist;

        // Snappy distance response: reaches full gaze deflection within 30 DIPs
        double intensity = Math.Min(1.0, dist / 30.0);

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
            targetX = -3.0 - (_random.NextDouble() * 1.0);
            targetY = (_random.NextDouble() * 1.2) - 0.6;
        }
        else if (roll < 82)
        {
            // 20% chance: Look right
            targetX = 3.0 + (_random.NextDouble() * 1.0);
            targetY = (_random.NextDouble() * 1.2) - 0.6;
        }
        else if (roll < 92)
        {
            // 10% chance: Look slightly up
            targetX = (_random.NextDouble() * 1.8) - 0.9;
            targetY = -2.4 - (_random.NextDouble() * 0.8);
        }
        else
        {
            // 8% chance: Look slightly down
            targetX = (_random.NextDouble() * 1.8) - 0.9;
            targetY = 1.4 + (_random.NextDouble() * 0.8);
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

        LeftEyeTransform?.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, animLeftX);
        LeftEyeTransform?.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, animLeftY);

        RightEyeTransform?.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, animRightX);
        RightEyeTransform?.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, animRightY);

        // Move the mouth smoothly in unison with eye gaze so eyes never overlap the mouth
        double mouthTargetX = (leftX + rightX) * 0.5 * 0.70;
        double mouthTargetY = (leftY + rightY) * 0.5 * 0.55;
        var animMouthX = new DoubleAnimation(mouthTargetX, duration) { EasingFunction = ease };
        var animMouthY = new DoubleAnimation(mouthTargetY, duration) { EasingFunction = ease };

        MouthTransform?.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, animMouthX);
        MouthTransform?.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, animMouthY);
    }

    private void StartIdleGlow()
    {
        // Standalone facial features: glow is managed by the surrounding notch container
    }

    /// <summary>
    /// Updates the mascot eye visual state according to real-time AI agent status.
    /// Manages Working eye rings, Completed smiling eyes, and 15–20s DONE persistence.
    /// </summary>
    public void SetAiState(Orbit.Models.AgentState state, string message = "")
    {
        var colorEase = new SineEase { EasingMode = EasingMode.EaseInOut };

        // Handle Attention Badge (!)
        if (state == Orbit.Models.AgentState.Attention)
        {
            AttentionBadge.Visibility = Visibility.Visible;
            var badgeFade = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200));
            AttentionBadge.BeginAnimation(OpacityProperty, badgeFade);

            var pulseAnim = new DoubleAnimation
            {
                From = 1.0,
                To = 1.25,
                Duration = TimeSpan.FromMilliseconds(700),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = colorEase
            };
            AttentionBadgeScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, pulseAnim);
            AttentionBadgeScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, pulseAnim);
        }
        else
        {
            if (AttentionBadge.Visibility == Visibility.Visible)
            {
                var badgeFadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(200));
                badgeFadeOut.Completed += (s, e) =>
                {
                    AttentionBadge.Visibility = Visibility.Collapsed;
                    AttentionBadgeScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
                    AttentionBadgeScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
                };
                AttentionBadge.BeginAnimation(OpacityProperty, badgeFadeOut);
            }
        }

        // Manage DONE (Completed) State Persistence & Transitions
        if (state == Orbit.Models.AgentState.Working)
        {
            // Any new working task immediately cancels previous DONE persistence
            CancelDoneHold();
            _isDoneExpired = false;
            _lastCompletedMessage = null;
            ApplyVisualState(Orbit.Models.AgentState.Working);
            return;
        }

        if (state == Orbit.Models.AgentState.Attention || state == Orbit.Models.AgentState.Error)
        {
            // Attention/Error cancels any DONE hold immediately
            CancelDoneHold();
            ApplyVisualState(state);
            return;
        }

        if (state == Orbit.Models.AgentState.Completed)
        {
            // Avoid re-triggering if this specific completion already timed out into Idle
            if (_isDoneExpired && _lastCompletedMessage == message)
            {
                ApplyVisualState(Orbit.Models.AgentState.Idle);
                return;
            }

            _lastCompletedMessage = message;
            _isDoneExpired = false;
            _isHoldingDoneState = true;
            StartDoneTimer();
            ApplyVisualState(Orbit.Models.AgentState.Completed);
            return;
        }

        // State is Idle (or Default)
        if (_isHoldingDoneState)
        {
            // Keep green DONE indicator visible for the remainder of the 15-20s timeout
            return;
        }

        CancelDoneHold();
        ApplyVisualState(Orbit.Models.AgentState.Idle);
    }

    private void StartDoneTimer()
    {
        _donePersistenceTimer?.Stop();
        int token = ++_doneTimerToken;

        _donePersistenceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(18) // ~18 seconds persistence (within 15–20s requirement)
        };
        _donePersistenceTimer.Tick += (s, e) =>
        {
            _donePersistenceTimer?.Stop();
            _donePersistenceTimer = null;

            // Stale timer verification
            if (_doneTimerToken != token) return;

            _isHoldingDoneState = false;
            _isDoneExpired = true;

            // Return to Idle state
            ApplyVisualState(Orbit.Models.AgentState.Idle);
        };
        _donePersistenceTimer.Start();
    }

    private void CancelDoneHold()
    {
        _doneTimerToken++;
        if (_donePersistenceTimer != null)
        {
            _donePersistenceTimer.Stop();
            _donePersistenceTimer = null;
        }
        _isHoldingDoneState = false;
    }

    /// <summary>
    /// Smoothly transitions ORBIT Mascot's tiny expressive mouth between AgentState expressions.
    /// </summary>
    private void SetMouthExpression(Orbit.Models.AgentState state)
    {
        if (MouthPath == null) return;

        var targetGeometry = state switch
        {
            Orbit.Models.AgentState.Working => GeometryMouthWorking,
            Orbit.Models.AgentState.Completed => GeometryMouthCompleted,
            Orbit.Models.AgentState.Attention => GeometryMouthAttention,
            Orbit.Models.AgentState.Error => GeometryMouthError,
            _ => GeometryMouthIdle
        };

        if (_currentMouthState == state && MouthPath.Data != null)
        {
            return;
        }

        _currentMouthState = state;

        if (!IsLoaded || MouthPath.Data == null)
        {
            MouthPath.Data = targetGeometry;
            return;
        }

        // Smooth subtle fade/scale transition
        var fadeOut = new DoubleAnimation(0.2, TimeSpan.FromMilliseconds(90))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        var scaleOut = new DoubleAnimation(0.85, TimeSpan.FromMilliseconds(90))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        fadeOut.Completed += (s, e) =>
        {
            MouthPath.Data = targetGeometry;

            var fadeIn = new DoubleAnimation(0.85, TimeSpan.FromMilliseconds(130))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            var scaleIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(130))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            MouthPath.BeginAnimation(OpacityProperty, fadeIn);
            MouthScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleIn);
            MouthScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleIn);
        };

        MouthPath.BeginAnimation(OpacityProperty, fadeOut);
        MouthScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleOut);
        MouthScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleOut);
    }

    private void ApplyVisualState(Orbit.Models.AgentState state)
    {
        SetMouthExpression(state);
        ApplyEyeMicroInteractions(state);
    }

    private void ApplyEyeMicroInteractions(Orbit.Models.AgentState state)
    {
        if (LeftSmilingEye == null || RightSmilingEye == null ||
            LeftEyePill == null || RightEyePill == null)
        {
            return;
        }

        var fadeEase = new QuadraticEase { EasingMode = EasingMode.EaseOut };

        switch (state)
        {
            case Orbit.Models.AgentState.Working:
            {
                // WORKING: Determined focused eye expression (narrowed, inward-angled upper lid) with vibrant neon purple glow
                _glanceTimer.Stop();
                AnimatePupilsIndividual(0.0, 0.0, 0.0, 0.0, TimeSpan.FromMilliseconds(180));

                LeftSmilingEye.BeginAnimation(OpacityProperty, null);
                RightSmilingEye.BeginAnimation(OpacityProperty, null);
                LeftSmilingEye.Opacity = 0.0;
                RightSmilingEye.Opacity = 0.0;

                // Hide relaxed normal pill eyes
                var pillFadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150)) { EasingFunction = fadeEase };
                LeftEyePill.BeginAnimation(OpacityProperty, pillFadeOut);
                RightEyePill.BeginAnimation(OpacityProperty, pillFadeOut);

                // Reveal focused working eyes
                if (LeftWorkingEye != null && RightWorkingEye != null)
                {
                    var workingFadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(180)) { EasingFunction = fadeEase };
                    LeftWorkingEye.BeginAnimation(OpacityProperty, workingFadeIn);
                    RightWorkingEye.BeginAnimation(OpacityProperty, workingFadeIn);

                    // Subtle, premium glow breathing pulse on drop shadow
                    var glowPulse = new DoubleAnimation
                    {
                        From = 10.0,
                        To = 14.0,
                        Duration = TimeSpan.FromMilliseconds(1200),
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                    };
                    LeftWorkingGlow?.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, glowPulse);
                    RightWorkingGlow?.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, glowPulse);
                }

                // Concentrated flat horizontal mouth stroke
                if (MouthPath != null) MouthPath.Stroke = BrushWorkingMouth;
                break;
            }

            case Orbit.Models.AgentState.Completed:
            {
                // COMPLETED: Smiling eye arches (◠ ◠) in emerald green (#10B981) + uplifted friendly green smile
                StopEyeRings();
                ResetWorkingEyeState();

                var pillFadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(180)) { EasingFunction = fadeEase };
                LeftEyePill.BeginAnimation(OpacityProperty, pillFadeOut);
                RightEyePill.BeginAnimation(OpacityProperty, pillFadeOut);

                var smileFadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(220)) { EasingFunction = fadeEase };
                LeftSmilingEye.BeginAnimation(OpacityProperty, smileFadeIn);
                RightSmilingEye.BeginAnimation(OpacityProperty, smileFadeIn);

                // Uplift mouth slightly in Done state
                MouthTransform?.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(-4.0, TimeSpan.FromMilliseconds(200)) { EasingFunction = fadeEase });

                if (MouthPath != null) MouthPath.Stroke = BrushCompletedBorder;
                break;
            }

            case Orbit.Models.AgentState.Attention:
            case Orbit.Models.AgentState.Error:
            {
                // ATTENTION / ERROR: Normal eyes with red attention badge/glow
                StopEyeRings();
                ResetWorkingEyeState();
                RestoreNormalEyes();
                if (MouthPath != null) MouthPath.Stroke = System.Windows.Media.Brushes.White;
                break;
            }

            default:
            {
                // IDLE: Standard expressive white eyes, gentle smile, organic glance
                StopEyeRings();
                ResetWorkingEyeState();
                RestoreNormalEyes();
                if (MouthPath != null) MouthPath.Stroke = System.Windows.Media.Brushes.White;
                if (!_isTrackingCursor)
                {
                    _glanceTimer.Start();
                }
                break;
            }
        }
    }

    private void StopEyeRings()
    {
    }

    private void RestoreNormalEyes()
    {
        if (LeftSmilingEye == null || RightSmilingEye == null ||
            LeftEyePill == null || RightEyePill == null) return;

        LeftSmilingEye.BeginAnimation(OpacityProperty, null);
        RightSmilingEye.BeginAnimation(OpacityProperty, null);
        LeftSmilingEye.Opacity = 0.0;
        RightSmilingEye.Opacity = 0.0;

        if (LeftWorkingEye != null)
        {
            LeftWorkingEye.BeginAnimation(OpacityProperty, null);
            LeftWorkingEye.Opacity = 0.0;
        }
        if (RightWorkingEye != null)
        {
            RightWorkingEye.BeginAnimation(OpacityProperty, null);
            RightWorkingEye.Opacity = 0.0;
        }

        var pillFadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200));
        LeftEyePill.BeginAnimation(OpacityProperty, pillFadeIn);
        RightEyePill.BeginAnimation(OpacityProperty, pillFadeIn);
    }

    private void ResetWorkingEyeState()
    {
        // Stop pulse animation and fade out working eyes
        if (LeftWorkingEye != null && RightWorkingEye != null)
        {
            LeftWorkingEye.BeginAnimation(OpacityProperty, null);
            RightWorkingEye.BeginAnimation(OpacityProperty, null);
            LeftWorkingEye.Opacity = 0.0;
            RightWorkingEye.Opacity = 0.0;

            LeftWorkingGlow?.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, null);
            RightWorkingGlow?.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, null);
        }

        // Smoothly restore eye colors to normal white
        var whiteColorAnim = new ColorAnimation(ColorEyeWhite, TimeSpan.FromMilliseconds(200));
        LeftEyeBrush?.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty, whiteColorAnim);
        RightEyeBrush?.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty, whiteColorAnim);
        LeftEyeGlow?.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.ColorProperty, whiteColorAnim);
        RightEyeGlow?.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.ColorProperty, whiteColorAnim);

        // Reset any mouth uplift animation
        MouthTransform?.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
    }
}

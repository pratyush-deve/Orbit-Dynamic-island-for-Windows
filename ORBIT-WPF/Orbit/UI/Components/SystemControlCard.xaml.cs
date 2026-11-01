using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Orbit.Services.SystemControls;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;

namespace Orbit.UI.Components;

public partial class SystemControlCard : UserControl
{
    public static readonly DependencyProperty ControlTypeProperty =
        DependencyProperty.Register(
            nameof(ControlType),
            typeof(SystemControlType),
            typeof(SystemControlCard),
            new PropertyMetadata(SystemControlType.Microphone, OnControlTypeChanged));

    public static readonly RoutedEvent ClickEvent =
        EventManager.RegisterRoutedEvent(
            nameof(Click),
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(SystemControlCard));

    public event RoutedEventHandler Click
    {
        add => AddHandler(ClickEvent, value);
        remove => RemoveHandler(ClickEvent, value);
    }

    public SystemControlType ControlType
    {
        get => (SystemControlType)GetValue(ControlTypeProperty);
        set => SetValue(ControlTypeProperty, value);
    }

    private readonly WpfBrush _enabledBackground;
    private readonly WpfBrush _enabledHoverBackground;
    private readonly WpfBrush _disabledBackground;
    private readonly WpfBrush _disabledHoverBackground;
    private readonly WpfBrush _pressedBackground;

    private readonly WpfBrush _enabledBorder = new SolidColorBrush(WpfColor.FromRgb(0xA8, 0x55, 0xF7));
    private readonly WpfBrush _disabledBorder = new SolidColorBrush(WpfColor.FromRgb(0x3D, 0x29, 0x5F));
    private readonly WpfBrush _purpleAccent = new SolidColorBrush(WpfColor.FromRgb(0xC0, 0x84, 0xFC));
    private readonly WpfBrush _mutedGrey = new SolidColorBrush(WpfColor.FromRgb(0x64, 0x74, 0x8B));
    private readonly WpfBrush _dimGrey = new SolidColorBrush(WpfColor.FromRgb(0x47, 0x55, 0x69));

    private bool _isEnabled;
    private bool _isAvailable = true;
    private DateTime _lastClickTime = DateTime.MinValue;

    public SystemControlCard()
    {
        InitializeComponent();

        _enabledBackground = (WpfBrush)Resources["CardBrush.Enabled"];
        _enabledHoverBackground = (WpfBrush)Resources["CardBrush.EnabledHover"];
        _disabledBackground = (WpfBrush)Resources["CardBrush.Disabled"];
        _disabledHoverBackground = (WpfBrush)Resources["CardBrush.DisabledHover"];
        _pressedBackground = (WpfBrush)Resources["CardBrush.Pressed"];

        UpdateIconForType(ControlType);
    }

    private static void OnControlTypeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SystemControlCard card && e.NewValue is SystemControlType type)
        {
            card.UpdateIconForType(type);
        }
    }

    public void UpdateIconForType(SystemControlType type)
    {
        IconMicrophone.Visibility = type == SystemControlType.Microphone ? Visibility.Visible : Visibility.Collapsed;
        IconWiFi.Visibility = type == SystemControlType.WiFi ? Visibility.Visible : Visibility.Collapsed;
        IconBluetooth.Visibility = type == SystemControlType.Bluetooth ? Visibility.Visible : Visibility.Collapsed;
        IconSystemVolume.Visibility = type == SystemControlType.SystemVolume ? Visibility.Visible : Visibility.Collapsed;
        IconSettings.Visibility = type == SystemControlType.Settings ? Visibility.Visible : Visibility.Collapsed;

        // Configure which status element to use (Badge vs Device Info Text vs None)
        bool isDeviceControl = type == SystemControlType.WiFi || type == SystemControlType.Bluetooth;
        bool isSettings = type == SystemControlType.Settings;

        BadgeStatus.Visibility = (!isDeviceControl && !isSettings) ? Visibility.Visible : Visibility.Collapsed;
        TxtDeviceInfo.Visibility = isDeviceControl ? Visibility.Visible : Visibility.Collapsed;
        TxtControlName.Margin = isSettings ? new Thickness(0, 3, 0, 0) : new Thickness(0, 3, 0, 2.5);

        TxtControlName.Text = type switch
        {
            SystemControlType.Microphone => "Microphone",
            SystemControlType.WiFi => "Wi-Fi",
            SystemControlType.Bluetooth => "Bluetooth",
            SystemControlType.SystemVolume => "Volume",
            SystemControlType.Settings => "Settings",
            _ => type.ToString()
        };

        if (type == SystemControlType.Settings)
        {
            ApplySettingsActionVisuals();
        }
    }

    /// <summary>
    /// Applies action-button visual styling to the Settings card.
    /// It is NOT a toggle and has no ON/OFF or enabled/disabled states or badges.
    /// </summary>
    public void ApplySettingsActionVisuals()
    {
        _isAvailable = true;
        _isEnabled = false;

        BadgeStatus.Visibility = Visibility.Collapsed;
        TxtDeviceInfo.Visibility = Visibility.Collapsed;
        TxtControlName.Margin = new Thickness(0, 3, 0, 0);

        CardBorder.Background = _enabledBackground;
        CardBorder.BorderBrush = _enabledBorder;

        var glowAnim = new DoubleAnimation(0.35, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        CardGlow.BeginAnimation(DropShadowEffect.OpacityProperty, glowAnim);

        SettingsGearGlyph.Fill = _purpleAccent;
    }

    public void UpdateStatus(SystemControlStatus status)
    {
        if (ControlType == SystemControlType.Settings)
        {
            ApplySettingsActionVisuals();
            return;
        }

        _isAvailable = status.IsAvailable;
        _isEnabled = status.IsEnabled;

        bool isDeviceControl = ControlType == SystemControlType.WiFi || ControlType == SystemControlType.Bluetooth;

        if (!_isAvailable)
        {
            CardBorder.Background = _disabledBackground;
            CardBorder.BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0x28, 0x20, 0x38));
            CardGlow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
            CardGlow.Opacity = 0.0;

            if (isDeviceControl)
            {
                BadgeStatus.Visibility = Visibility.Collapsed;
                TxtDeviceInfo.Visibility = Visibility.Visible;
                TxtDeviceInfo.Text = "Unavailable";
                TxtDeviceInfo.Foreground = _dimGrey;
            }
            else
            {
                BadgeStatus.Visibility = Visibility.Visible;
                TxtDeviceInfo.Visibility = Visibility.Collapsed;
                DotStatus.Fill = _dimGrey;
                TxtStatus.Foreground = _dimGrey;
                TxtStatus.Text = "N/A";
            }

            ApplyIconMuteVisuals(false, isUnavailable: true);
            return;
        }

        if (_isEnabled)
        {
            CardBorder.Background = _enabledBackground;
            CardBorder.BorderBrush = _enabledBorder;

            var glowAnim = new DoubleAnimation(0.45, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            CardGlow.BeginAnimation(DropShadowEffect.OpacityProperty, glowAnim);

            if (isDeviceControl)
            {
                BadgeStatus.Visibility = Visibility.Collapsed;
                TxtDeviceInfo.Visibility = Visibility.Visible;
                TxtDeviceInfo.Text = status.StatusLabel;

                bool isConnected = !status.StatusLabel.Equals("Not Connected", StringComparison.OrdinalIgnoreCase) &&
                                   !status.StatusLabel.Equals("No Devices", StringComparison.OrdinalIgnoreCase) &&
                                   !status.StatusLabel.Equals("Disabled", StringComparison.OrdinalIgnoreCase);

                TxtDeviceInfo.Foreground = isConnected ? _purpleAccent : new SolidColorBrush(WpfColor.FromRgb(0x94, 0xA3, 0xB8));
                TxtDeviceInfo.FontWeight = isConnected ? FontWeights.SemiBold : FontWeights.Normal;
            }
            else
            {
                BadgeStatus.Visibility = Visibility.Visible;
                TxtDeviceInfo.Visibility = Visibility.Collapsed;
                BadgeStatus.Background = new SolidColorBrush(WpfColor.FromRgb(0x2E, 0x18, 0x50));
                BadgeStatus.BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0x7C, 0x3A, 0xED));
                DotStatus.Fill = _purpleAccent;
                TxtStatus.Foreground = _purpleAccent;
                TxtStatus.Text = status.StatusLabel;
            }

            ApplyIconMuteVisuals(true, isUnavailable: false);
        }
        else
        {
            CardBorder.Background = _disabledBackground;
            CardBorder.BorderBrush = _disabledBorder;

            var glowAnim = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            CardGlow.BeginAnimation(DropShadowEffect.OpacityProperty, glowAnim);

            if (isDeviceControl)
            {
                BadgeStatus.Visibility = Visibility.Collapsed;
                TxtDeviceInfo.Visibility = Visibility.Visible;
                TxtDeviceInfo.Text = status.StatusLabel;
                TxtDeviceInfo.Foreground = _mutedGrey;
                TxtDeviceInfo.FontWeight = FontWeights.Normal;
            }
            else
            {
                BadgeStatus.Visibility = Visibility.Visible;
                TxtDeviceInfo.Visibility = Visibility.Collapsed;
                BadgeStatus.Background = new SolidColorBrush(WpfColor.FromRgb(0x1B, 0x16, 0x26));
                BadgeStatus.BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0x35, 0x27, 0x4D));
                DotStatus.Fill = _mutedGrey;
                TxtStatus.Foreground = _mutedGrey;
                TxtStatus.Text = status.StatusLabel;
            }

            ApplyIconMuteVisuals(false, isUnavailable: false);
        }
    }

    private void ApplyIconMuteVisuals(bool isEnabled, bool isUnavailable)
    {
        var strokeColor = isEnabled ? _purpleAccent : (isUnavailable ? _dimGrey : _mutedGrey);

        switch (ControlType)
        {
            case SystemControlType.Microphone:
                MicCapsule.Stroke = strokeColor;
                MicStand.Stroke = strokeColor;
                MicSlash.Visibility = isEnabled ? Visibility.Collapsed : Visibility.Visible;
                break;

            case SystemControlType.WiFi:
                WiFiWave3.Stroke = strokeColor;
                WiFiWave2.Stroke = strokeColor;
                WiFiWave1.Stroke = strokeColor;
                WiFiDot.Fill = strokeColor;
                WiFiSlash.Visibility = isEnabled ? Visibility.Collapsed : Visibility.Visible;
                break;

            case SystemControlType.Bluetooth:
                BluetoothRune.Stroke = strokeColor;
                BluetoothSlash.Visibility = isEnabled ? Visibility.Collapsed : Visibility.Visible;
                break;

            case SystemControlType.SystemVolume:
                VolumeCone.Stroke = strokeColor;
                VolumeWave1.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
                VolumeWave2.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
                VolumeWave1.Stroke = strokeColor;
                VolumeWave2.Stroke = strokeColor;
                VolumeMuteCross.Visibility = isEnabled ? Visibility.Collapsed : Visibility.Visible;
                break;

            case SystemControlType.Settings:
                SettingsGearGlyph.Fill = strokeColor;
                break;
        }
    }

    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        if (ControlType == SystemControlType.Settings)
        {
            CardBorder.Background = _enabledHoverBackground;
            CardBorder.BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0xC0, 0x84, 0xFC));

            var glowAnim = new DoubleAnimation(0.65, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            CardGlow.BeginAnimation(DropShadowEffect.OpacityProperty, glowAnim);

            var scaleAnim = new DoubleAnimation(1.02, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
            return;
        }

        if (!_isAvailable) return;

        CardBorder.Background = _isEnabled ? _enabledHoverBackground : _disabledHoverBackground;
        CardBorder.BorderBrush = _isEnabled ? new SolidColorBrush(WpfColor.FromRgb(0xC0, 0x84, 0xFC)) : _enabledBorder;

        var targetGlow = _isEnabled ? 0.70 : 0.30;
        var glowAnimRegular = new DoubleAnimation(targetGlow, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        CardGlow.BeginAnimation(DropShadowEffect.OpacityProperty, glowAnimRegular);

        var scaleAnimRegular = new DoubleAnimation(1.02, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimRegular);
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimRegular);
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (ControlType == SystemControlType.Settings)
        {
            CardBorder.Background = _enabledBackground;
            CardBorder.BorderBrush = _enabledBorder;

            var glowAnim = new DoubleAnimation(0.35, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            CardGlow.BeginAnimation(DropShadowEffect.OpacityProperty, glowAnim);

            var scaleAnim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
            return;
        }

        CardBorder.Background = _isEnabled ? _enabledBackground : _disabledBackground;
        CardBorder.BorderBrush = _isEnabled ? _enabledBorder : _disabledBorder;

        var targetGlow = _isEnabled ? 0.45 : 0.0;
        var glowAnimLeave = new DoubleAnimation(targetGlow, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        CardGlow.BeginAnimation(DropShadowEffect.OpacityProperty, glowAnimLeave);

        var scaleAnimLeave = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimLeave);
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimLeave);
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CardBorder.Background = _pressedBackground;

        var scaleAnim = new DoubleAnimation(0.96, TimeSpan.FromMilliseconds(70))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        TriggerClick();
    }

    private void OnCardMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        TriggerClick();
    }

    private void TriggerClick()
    {
        if (ControlType == SystemControlType.Settings)
        {
            CardBorder.Background = IsMouseOver ? _enabledHoverBackground : _enabledBackground;
        }
        else if (IsMouseOver && _isAvailable)
        {
            CardBorder.Background = _isEnabled ? _enabledHoverBackground : _disabledHoverBackground;
        }
        else
        {
            CardBorder.Background = _isEnabled ? _enabledBackground : _disabledBackground;
        }

        var scaleAnimation = new DoubleAnimation(IsMouseOver ? 1.02 : 1.0, TimeSpan.FromMilliseconds(100))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);

        if (DateTime.UtcNow - _lastClickTime > TimeSpan.FromMilliseconds(200))
        {
            _lastClickTime = DateTime.UtcNow;
            RaiseEvent(new RoutedEventArgs(ClickEvent, this));
        }
    }
}


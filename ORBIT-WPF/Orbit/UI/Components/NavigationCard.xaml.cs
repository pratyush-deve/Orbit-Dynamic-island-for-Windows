using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;

namespace Orbit.UI.Components;

/// <summary>
/// Reusable interactive navigation card component for ORBIT 2.0.
/// Implements tactile micro-scale on press and neon-glow on hover inspired by Android Jetpack Compose.
/// </summary>
[ContentProperty(nameof(IconContent))]
public partial class NavigationCard : UserControl
{
    public static readonly DependencyProperty IconContentProperty =
        DependencyProperty.Register(
            nameof(IconContent),
            typeof(object),
            typeof(NavigationCard),
            new PropertyMetadata(null, OnIconContentChanged));

    public static readonly DependencyProperty CardBadgeProperty =
        DependencyProperty.Register(
            nameof(CardBadge),
            typeof(string),
            typeof(NavigationCard),
            new PropertyMetadata(string.Empty, OnCardBadgeChanged));

    public static readonly DependencyProperty CardLabelProperty =
        DependencyProperty.Register(
            nameof(CardLabel),
            typeof(string),
            typeof(NavigationCard),
            new PropertyMetadata(string.Empty, OnCardLabelChanged));

    public static readonly RoutedEvent ClickEvent =
        EventManager.RegisterRoutedEvent(
            nameof(Click),
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(NavigationCard));

    public event RoutedEventHandler Click
    {
        add => AddHandler(ClickEvent, value);
        remove => RemoveHandler(ClickEvent, value);
    }

    public object? IconContent
    {
        get => GetValue(IconContentProperty);
        set => SetValue(IconContentProperty, value);
    }

    public string CardBadge
    {
        get => (string)GetValue(CardBadgeProperty);
        set => SetValue(CardBadgeProperty, value);
    }

    public string CardLabel
    {
        get => (string)GetValue(CardLabelProperty);
        set => SetValue(CardLabelProperty, value);
    }

    private readonly WpfBrush _normalBackground;
    private readonly WpfBrush _hoverBackground;
    private readonly WpfBrush _pressedBackground;
    private readonly WpfBrush _normalBorder = new SolidColorBrush(WpfColor.FromRgb(0x3D, 0x29, 0x5F));
    private readonly WpfBrush _hoverBorder = new SolidColorBrush(WpfColor.FromRgb(0xA8, 0x55, 0xF7));
    private readonly WpfBrush _pressedBorder = new SolidColorBrush(WpfColor.FromRgb(0xC0, 0x84, 0xFC));

    public NavigationCard()
    {
        InitializeComponent();

        _normalBackground = (WpfBrush)Resources["CardBrush.Normal"];
        _hoverBackground = (WpfBrush)Resources["CardBrush.Hover"];
        _pressedBackground = (WpfBrush)Resources["CardBrush.Pressed"];
    }

    private static void OnIconContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NavigationCard card)
        {
            card.IconPresenter.Content = e.NewValue;
        }
    }

    private static void OnCardBadgeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NavigationCard card)
        {
            var text = e.NewValue as string;
            card.TxtBadge.Text = text ?? string.Empty;
            card.TxtBadge.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private static void OnCardLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NavigationCard card)
        {
            card.TxtLabel.Text = (e.NewValue as string) ?? string.Empty;
        }
    }

    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        CardBorder.Background = _hoverBackground;
        CardBorder.BorderBrush = _hoverBorder;

        var glowAnim = new DoubleAnimation(0.55, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        CardGlow.BeginAnimation(DropShadowEffect.OpacityProperty, glowAnim);
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        CardBorder.Background = _normalBackground;
        CardBorder.BorderBrush = _normalBorder;

        var scaleAnim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);

        var glowAnim = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        CardGlow.BeginAnimation(DropShadowEffect.OpacityProperty, glowAnim);
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CardBorder.Background = _pressedBackground;
        CardBorder.BorderBrush = _pressedBorder;

        // Subtle tactile press scale
        var scaleAnim = new DoubleAnimation(0.96, TimeSpan.FromMilliseconds(70))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
    }

    private DateTime _lastClickTime = DateTime.MinValue;

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
        if (IsMouseOver)
        {
            CardBorder.Background = _hoverBackground;
            CardBorder.BorderBrush = _hoverBorder;
        }
        else
        {
            CardBorder.Background = _normalBackground;
            CardBorder.BorderBrush = _normalBorder;
        }

        var scaleAnim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(100))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);

        // Debounce double-trigger from preview and standard event
        if (DateTime.UtcNow - _lastClickTime > TimeSpan.FromMilliseconds(150))
        {
            _lastClickTime = DateTime.UtcNow;
            RaiseEvent(new RoutedEventArgs(ClickEvent, this));
        }
    }
}

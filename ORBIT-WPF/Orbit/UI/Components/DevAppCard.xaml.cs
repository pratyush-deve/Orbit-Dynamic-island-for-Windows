using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Orbit.Core;
using Orbit.Models;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;

namespace Orbit.UI.Components;

public partial class DevAppCard : UserControl
{
    private const string LogCategory = "DEV_APP_CARD";

    public static readonly DependencyProperty AppProperty =
        DependencyProperty.Register(
            nameof(App),
            typeof(OrbitApp),
            typeof(DevAppCard),
            new PropertyMetadata(null, OnAppChanged));

    public OrbitApp? App
    {
        get => (OrbitApp?)GetValue(AppProperty);
        set => SetValue(AppProperty, value);
    }

    public static readonly RoutedEvent ClickEvent =
        EventManager.RegisterRoutedEvent(
            nameof(Click),
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(DevAppCard));

    public event RoutedEventHandler Click
    {
        add => AddHandler(ClickEvent, value);
        remove => RemoveHandler(ClickEvent, value);
    }

    private readonly WpfBrush _defaultBackground;
    private readonly WpfBrush _hoverBackground;
    private readonly WpfBrush _pressedBackground;
    private readonly WpfBrush _defaultBorder = new SolidColorBrush(WpfColor.FromRgb(0x43, 0x2B, 0x6E));
    private readonly WpfBrush _hoverBorder = new SolidColorBrush(WpfColor.FromRgb(0xC0, 0x84, 0xFC));

    public DevAppCard()
    {
        InitializeComponent();

        _defaultBackground = (WpfBrush)Resources["CardBrush.Default"];
        _hoverBackground = (WpfBrush)Resources["CardBrush.Hover"];
        _pressedBackground = (WpfBrush)Resources["CardBrush.Pressed"];
    }

    private static void OnAppChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DevAppCard card)
        {
            card.UpdateCard();
        }
    }

    public void UpdateCard()
    {
        if (App == null)
        {
            TxtAppName.Text = string.Empty;
            ImgAppIcon.Source = null;
            ImgAppIcon.Visibility = Visibility.Collapsed;
            FallbackIcon.Visibility = Visibility.Collapsed;
            ToolTip = null;
            return;
        }

        TxtAppName.Text = App.Name;
        ToolTip = $"{App.Name}\n{App.ExecutablePath}";

        if (App.Icon != null)
        {
            ImgAppIcon.Source = App.Icon;
            ImgAppIcon.Visibility = Visibility.Visible;
            FallbackIcon.Visibility = Visibility.Collapsed;
        }
        else
        {
            ImgAppIcon.Source = null;
            ImgAppIcon.Visibility = Visibility.Collapsed;
            FallbackIcon.Visibility = Visibility.Visible;

            // Generate monogram if short
            string glyph = App.Name.Length > 2 ? App.Name[..2].ToUpperInvariant() : "</>";
            TxtFallbackGlyph.Text = glyph;
        }
    }

    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        CardBorder.Background = _hoverBackground;
        CardBorder.BorderBrush = _hoverBorder;

        var glowAnim = new DoubleAnimation(0.65, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        CardGlow.BeginAnimation(DropShadowEffect.OpacityProperty, glowAnim);

        var scaleAnim = new DoubleAnimation(1.03, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        CardBorder.Background = _defaultBackground;
        CardBorder.BorderBrush = _defaultBorder;

        var glowAnim = new DoubleAnimation(0.2, TimeSpan.FromMilliseconds(150))
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
        CardBorder.Background = IsMouseOver ? _hoverBackground : _defaultBackground;

        var scaleAnim = new DoubleAnimation(IsMouseOver ? 1.03 : 1.0, TimeSpan.FromMilliseconds(100))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
    }

    private void OnCardClicked(object sender, MouseButtonEventArgs e)
    {
        if (App == null) return;

        // Raise click event for parent container handling (e.g. window activation in hubs)
        var args = new RoutedEventArgs(ClickEvent, this);
        RaiseEvent(args);
        if (args.Handled) return;

        // Default direct launch fallback
        try
        {
            OrbitLogger.Info(LogCategory, $"Card clicked: Launching single app '{App.Name}'...");

            if (!string.IsNullOrWhiteSpace(App.ExecutablePath) &&
                App.ExecutablePath.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
            {
                var psiShell = new ProcessStartInfo("explorer.exe", App.ExecutablePath)
                {
                    UseShellExecute = true
                };
                Process.Start(psiShell);
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = App.ExecutablePath,
                Arguments = App.Arguments ?? string.Empty,
                UseShellExecute = true
            };
            if (!string.IsNullOrWhiteSpace(App.WorkingDirectory) && Directory.Exists(App.WorkingDirectory))
            {
                psi.WorkingDirectory = App.WorkingDirectory;
            }
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, $"Failed to launch '{App.Name}'.", ex);
        }
    }
}


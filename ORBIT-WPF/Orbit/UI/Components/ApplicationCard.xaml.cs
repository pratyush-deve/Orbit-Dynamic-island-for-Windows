using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Orbit.Models;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;

namespace Orbit.UI.Components;

/// <summary>
/// Interactive application card representing an open desktop window.
/// Displays application icon, application name, window title, and a graceful close button.
/// </summary>
public partial class ApplicationCard : UserControl
{
    public static readonly DependencyProperty WindowInfoProperty =
        DependencyProperty.Register(
            nameof(WindowInfo),
            typeof(WindowInfo),
            typeof(ApplicationCard),
            new PropertyMetadata(null, OnWindowInfoChanged));

    public WindowInfo? WindowInfo
    {
        get => (WindowInfo?)GetValue(WindowInfoProperty);
        set => SetValue(WindowInfoProperty, value);
    }

    public event Action<WindowInfo>? Activated;
    public event Action<WindowInfo>? CloseRequested;

    public ApplicationCard()
    {
        InitializeComponent();
    }

    private static void OnWindowInfoChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ApplicationCard card)
        {
            if (e.OldValue is WindowInfo oldInfo)
            {
                oldInfo.PropertyChanged -= card.OnWindowInfoPropertyChanged;
            }

            if (e.NewValue is WindowInfo newInfo)
            {
                newInfo.PropertyChanged += card.OnWindowInfoPropertyChanged;
                card.UpdateFromInfo(newInfo);
            }
            else
            {
                card.UpdateFromInfo(null);
            }
        }
    }

    private void OnWindowInfoPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (WindowInfo != null)
            {
                UpdateFromInfo(WindowInfo);
            }
        });
    }

    public void UpdateFromInfo(WindowInfo? info)
    {
        if (info == null)
        {
            TxtAppName.Text = string.Empty;
            TxtTitle.Text = string.Empty;
            ToolTip = null;
            ImgIcon.Source = null;
            ImgIcon.Visibility = Visibility.Collapsed;
            FallbackIcon.Visibility = Visibility.Visible;
            return;
        }

        TxtAppName.Text = string.IsNullOrWhiteSpace(info.AppName) ? info.ProcessName : info.AppName;
        TxtTitle.Text = info.Title;
        ToolTip = $"{info.AppName}\n{info.Title}";

        if (info.Icon != null)
        {
            ImgIcon.Source = info.Icon;
            ImgIcon.Visibility = Visibility.Visible;
            FallbackIcon.Visibility = Visibility.Collapsed;
        }
        else
        {
            ImgIcon.Source = null;
            ImgIcon.Visibility = Visibility.Collapsed;
            FallbackIcon.Visibility = Visibility.Visible;
        }
    }

    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        CardBorder.Background = (WpfBrush)FindResource("CardBrush.Hover");
        CardBorder.BorderBrush = (WpfBrush)FindResource("Brush.Accent.PurpleLight");

        var glowAnim = new DoubleAnimation(0.5, TimeSpan.FromMilliseconds(150))
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
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        CardBorder.Background = (WpfBrush)FindResource("CardBrush.Normal");
        CardBorder.BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0x3D, 0x29, 0x5F));

        var glowAnim = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        CardGlow.BeginAnimation(DropShadowEffect.OpacityProperty, glowAnim);

        var scaleAnim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
    }

    private void OnCardPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // If clicking close button, do not depress the card
        if (e.OriginalSource is DependencyObject dep && (dep == BtnClose || IsDescendantOf(dep, BtnClose)))
        {
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            CardBorder.Background = (WpfBrush)FindResource("CardBrush.Pressed");

            var scaleAnim = new DoubleAnimation(0.96, TimeSpan.FromMilliseconds(80))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
        }
    }

    private DateTime _lastActivateTime = DateTime.MinValue;

    private void TriggerActivate()
    {
        if (WindowInfo != null && DateTime.UtcNow - _lastActivateTime > TimeSpan.FromMilliseconds(200))
        {
            _lastActivateTime = DateTime.UtcNow;
            Orbit.Core.OrbitLogger.Info("APP_CARD", $"[CARD_ACTIVATED] Activating HWND 0x{WindowInfo.Handle:X} - '{WindowInfo.Title}'");
            Activated?.Invoke(WindowInfo);
        }
    }

    private void OnCardPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            CardBorder.Background = (WpfBrush)FindResource("CardBrush.Hover");

            var scaleAnim = new DoubleAnimation(1.02, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
        }
    }

    private void OnCardMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled) return;

        // If clicking close button, do not activate card
        if (e.OriginalSource is DependencyObject dep && (dep == BtnClose || IsDescendantOf(dep, BtnClose)))
        {
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            e.Handled = true;
            TriggerActivate();
        }
    }

    private static bool IsDescendantOf(DependencyObject node, DependencyObject parent)
    {
        var current = node;
        while (current != null)
        {
            if (ReferenceEquals(current, parent)) return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        var info = WindowInfo;
        if (info != null)
        {
            bool isWin = Orbit.Platform.Windows.NativeMethods.IsWindow(info.Handle);
            Orbit.Core.OrbitLogger.Info("APP_CARD", $"[CLOSE_BUTTON_CLICKED] HWND=0x{info.Handle:X} | Title='{info.Title}' | IsWindow={isWin}");
            CloseRequested?.Invoke(info);
        }
    }
}

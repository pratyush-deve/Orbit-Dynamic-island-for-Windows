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

public partial class DevAppRow : UserControl
{
    public static readonly DependencyProperty AppProperty =
        DependencyProperty.Register(
            nameof(App),
            typeof(OrbitApp),
            typeof(DevAppRow),
            new PropertyMetadata(null, OnAppChanged));

    public OrbitApp? App
    {
        get => (OrbitApp?)GetValue(AppProperty);
        set => SetValue(AppProperty, value);
    }

    public event Action<OrbitApp, bool>? Toggled;

    private readonly WpfBrush _rowResting = new SolidColorBrush(WpfColor.FromRgb(0x18, 0x11, 0x28));
    private readonly WpfBrush _rowHover = new SolidColorBrush(WpfColor.FromRgb(0x24, 0x17, 0x3D));
    private readonly WpfBrush _borderResting = new SolidColorBrush(WpfColor.FromRgb(0x2D, 0x21, 0x46));
    private readonly WpfBrush _borderHover = new SolidColorBrush(WpfColor.FromRgb(0x56, 0x36, 0x86));

    private readonly WpfBrush _trackEnabled = new SolidColorBrush(WpfColor.FromRgb(0x8B, 0x5C, 0xF6));
    private readonly WpfBrush _trackDisabled = new SolidColorBrush(WpfColor.FromRgb(0x24, 0x1B, 0x38));
    private readonly WpfBrush _trackBorderEnabled = new SolidColorBrush(WpfColor.FromRgb(0xC0, 0x84, 0xFC));
    private readonly WpfBrush _trackBorderDisabled = new SolidColorBrush(WpfColor.FromRgb(0x4A, 0x35, 0x68));

    public DevAppRow()
    {
        InitializeComponent();
    }

    private static void OnAppChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DevAppRow row)
        {
            if (e.OldValue is OrbitApp oldApp)
            {
                oldApp.PropertyChanged -= row.OnAppPropertyChanged;
            }

            if (e.NewValue is OrbitApp newApp)
            {
                newApp.PropertyChanged += row.OnAppPropertyChanged;
                row.UpdateRow();
            }
            else
            {
                row.UpdateRow();
            }
        }
    }

    private void OnAppPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DevApp.IsSelected))
        {
            Dispatcher.BeginInvoke(UpdateToggleVisuals);
        }
    }

    public void UpdateRow()
    {
        if (App == null)
        {
            TxtAppName.Text = string.Empty;
            ImgIcon.Source = null;
            ImgIcon.Visibility = Visibility.Collapsed;
            FallbackIcon.Visibility = Visibility.Collapsed;
            return;
        }

        TxtAppName.Text = App.Name;

        if (App.Icon != null)
        {
            ImgIcon.Source = App.Icon;
            ImgIcon.Visibility = Visibility.Visible;
            FallbackIcon.Visibility = Visibility.Collapsed;
        }
        else
        {
            ImgIcon.Source = null;
            ImgIcon.Visibility = Visibility.Collapsed;
            FallbackIcon.Visibility = Visibility.Visible;
            string glyph = App.Name.Length > 2 ? App.Name[..2].ToUpperInvariant() : "</>";
            TxtFallbackGlyph.Text = glyph;
        }

        UpdateToggleVisuals();
    }

    private void UpdateToggleVisuals()
    {
        bool isEnabled = App?.IsSelected ?? false;

        ToggleTrack.Background = isEnabled ? _trackEnabled : _trackDisabled;
        ToggleTrack.BorderBrush = isEnabled ? _trackBorderEnabled : _trackBorderDisabled;

        ToggleGlow.Opacity = isEnabled ? 0.6 : 0.0;

        if (isEnabled)
        {
            ToggleThumb.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
            ToggleThumb.Margin = new Thickness(0, 0, 2.5, 0);
        }
        else
        {
            ToggleThumb.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            ToggleThumb.Margin = new Thickness(2.5, 0, 0, 0);
        }
    }

    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        RowBorder.Background = _rowHover;
        RowBorder.BorderBrush = _borderHover;
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        RowBorder.Background = _rowResting;
        RowBorder.BorderBrush = _borderResting;
    }

    private void OnRowClicked(object sender, MouseButtonEventArgs e)
    {
        ToggleState();
    }

    private void OnToggleClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ToggleState();
    }

    private void ToggleState()
    {
        if (App == null) return;

        bool newState = !App.IsSelected;
        App.IsSelected = newState;
        UpdateToggleVisuals();
        Toggled?.Invoke(App, newState);
    }
}

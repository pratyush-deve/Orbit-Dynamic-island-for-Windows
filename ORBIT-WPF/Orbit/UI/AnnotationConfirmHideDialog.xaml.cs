using System.Windows;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;

namespace Orbit.UI;

/// <summary>
/// Modal confirmation dialog shown when the user triggers the HP programmable key or hide toggle
/// while Desktop Annotation Mode is active, ensuring accidental key presses do not discard annotations.
/// </summary>
public partial class AnnotationConfirmHideDialog : Window
{
    public AnnotationConfirmHideDialog()
    {
        InitializeComponent();
    }

    private void OnConfirmClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
        }
    }
}

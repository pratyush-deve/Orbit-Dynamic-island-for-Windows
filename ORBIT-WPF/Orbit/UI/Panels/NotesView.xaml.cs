using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Orbit.Core;
using Orbit.Services.Notes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace Orbit.UI.Panels;

/// <summary>
/// Interaction logic for NotesView.xaml.
/// Lightweight, built-in, local-persistent text editor for quick notes in point/bullet form.
/// </summary>
public partial class NotesView : UserControl, IDisposable
{
    private const string LogCategory = "NOTES_VIEW";

    public event Action? BackRequested;

    private INotesService _notesService;
    private readonly DispatcherTimer _autoSaveTimer;
    private bool _isLoaded;
    private bool _hasUnsavedChanges;
    private bool _disposed;

    private static readonly SolidColorBrush BrushSavedDot = new((Color)ColorConverter.ConvertFromString("#10B981"));
    private static readonly SolidColorBrush BrushSavingDot = new((Color)ColorConverter.ConvertFromString("#C084FC"));
    private static readonly SolidColorBrush BrushSavedText = new((Color)ColorConverter.ConvertFromString("#8B7A9F"));
    private static readonly SolidColorBrush BrushSavingText = new((Color)ColorConverter.ConvertFromString("#C084FC"));

    public NotesView()
    {
        InitializeComponent();

        _notesService = new NotesService();

        _autoSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };
        _autoSaveTimer.Tick += OnAutoSaveTimerTick;

        TxtEditor.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnEditorScrollChanged));
        SizeChanged += (s, e) => UpdateScrollIndicator();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public void Initialize(INotesService? notesService = null)
    {
        if (_notesService != null)
        {
            _notesService.NotesUpdated -= OnNotesServiceUpdated;
        }

        if (notesService != null)
        {
            _notesService = notesService;
        }

        if (_notesService != null)
        {
            _notesService.NotesUpdated += OnNotesServiceUpdated;
        }
        LoadSavedContent();
    }

    public void RefreshNotes()
    {
        LoadSavedContent();
    }

    private void OnNotesServiceUpdated(string updatedContent)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!_hasUnsavedChanges)
            {
                TxtEditor.Text = updatedContent;
                TxtPlaceholder.Visibility = string.IsNullOrEmpty(updatedContent) ? Visibility.Visible : Visibility.Collapsed;
                UpdateSaveIndicator(isSaving: false, statusText: "Saved");
            }
        });
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded)
        {
            LoadSavedContent();
            _isLoaded = true;
        }

        // Focus editor for instant note-taking
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            TxtEditor.Focus();
            TxtEditor.CaretIndex = TxtEditor.Text.Length;
        });
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        SaveCurrentNotesSynchronously();
    }

    public void LoadSavedContent()
    {
        try
        {
            string saved = _notesService.LoadNotes();
            TxtEditor.Text = saved;
            _hasUnsavedChanges = false;
            TxtPlaceholder.Visibility = string.IsNullOrEmpty(saved) ? Visibility.Visible : Visibility.Collapsed;
            UpdateSaveIndicator(isSaving: false, statusText: "Saved");
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, $"Failed to load saved notes: {ex.Message}");
        }
    }

    public void SaveCurrentNotesSynchronously()
    {
        if (!_hasUnsavedChanges) return;

        try
        {
            _autoSaveTimer.Stop();
            _notesService.SaveNotes(TxtEditor.Text);
            _hasUnsavedChanges = false;
            UpdateSaveIndicator(isSaving: false, statusText: "Saved");
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, $"Failed to save notes synchronously: {ex.Message}");
        }
    }

    private void OnEditorTextChanged(object sender, TextChangedEventArgs e)
    {
        TxtPlaceholder.Visibility = string.IsNullOrEmpty(TxtEditor.Text) ? Visibility.Visible : Visibility.Collapsed;

        _hasUnsavedChanges = true;
        UpdateSaveIndicator(isSaving: true, statusText: "Saving...");

        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();

        UpdateScrollIndicator();
    }

    private void OnAutoSaveTimerTick(object? sender, EventArgs e)
    {
        _autoSaveTimer.Stop();
        if (!_hasUnsavedChanges) return;

        try
        {
            _notesService.SaveNotes(TxtEditor.Text);
            _hasUnsavedChanges = false;
            UpdateSaveIndicator(isSaving: false, statusText: "Saved");
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, $"Auto-save error: {ex.Message}");
            UpdateSaveIndicator(isSaving: false, statusText: "Save failed");
        }
    }

    private void UpdateSaveIndicator(bool isSaving, string statusText)
    {
        StatusDot.Fill = isSaving ? BrushSavingDot : BrushSavedDot;
        TxtSaveStatus.Text = statusText;
        TxtSaveStatus.Foreground = isSaving ? BrushSavingText : BrushSavedText;
    }

    /// <summary>
    /// Smart bullet point continuation and keyboard navigation helpers.
    /// </summary>
    private void OnEditorPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            SaveCurrentNotesSynchronously();
            BackRequested?.Invoke();
            return;
        }

        // Ctrl + S triggers immediate save
        if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            e.Handled = true;
            SaveCurrentNotesSynchronously();
            UpdateSaveIndicator(isSaving: false, statusText: "Saved");
            return;
        }

        // Smart bullet list continuation on Enter
        if (e.Key == Key.Enter && (Keyboard.Modifiers == ModifierKeys.None))
        {
            int caret = TxtEditor.CaretIndex;
            string text = TxtEditor.Text;

            int lineStart = text.LastIndexOf('\n', Math.Max(0, caret - 1));
            lineStart = lineStart < 0 ? 0 : lineStart + 1;
            string currentLine = text.Substring(lineStart, caret - lineStart);

            string[] bulletPrefixes = ["• ", "- ", "* "];
            foreach (var prefix in bulletPrefixes)
            {
                if (currentLine.StartsWith(prefix))
                {
                    e.Handled = true;

                    // If user pressed Enter on an empty bullet line, remove the bullet
                    if (currentLine.Trim() == prefix.Trim())
                    {
                        TxtEditor.Text = text.Remove(lineStart, caret - lineStart);
                        TxtEditor.CaretIndex = lineStart;
                        return;
                    }

                    // Otherwise continue the list with a new bullet point
                    string insert = "\n" + prefix;
                    TxtEditor.SelectedText = insert;
                    TxtEditor.CaretIndex = caret + insert.Length;
                    TxtEditor.SelectionLength = 0;
                    return;
                }
            }
        }
    }

    private ScrollViewer? _editorScrollViewer;

    private ScrollViewer? GetEditorScrollViewer()
    {
        if (_editorScrollViewer != null) return _editorScrollViewer;
        _editorScrollViewer = FindVisualChild<ScrollViewer>(TxtEditor);
        return _editorScrollViewer;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) return typed;
            var desc = FindVisualChild<T>(child);
            if (desc != null) return desc;
        }
        return null;
    }

    /// <summary>
    /// Handles mouse-wheel and touchpad vertical scrolling smoothly.
    /// Operates even when the editor has keyboard focus and preserves caret/selection.
    /// </summary>
    private void OnEditorPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var sv = GetEditorScrollViewer();
        if (sv != null && sv.ScrollableHeight > 0)
        {
            // Smooth scrolling for both discrete mouse wheel notches (e.g. delta +/-120 -> 42 DIPs)
            // and continuous fine touchpad scrolling deltas (e.g. delta 10-20 -> 3.5-7 DIPs)
            double scrollDelta = -e.Delta * 0.35;
            double targetOffset = Math.Clamp(sv.VerticalOffset + scrollDelta, 0.0, sv.ScrollableHeight);
            sv.ScrollToVerticalOffset(targetOffset);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Dynamically updates the subtle vertical scroll progress thumb as the user scrolls.
    /// </summary>
    private void OnEditorScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        UpdateScrollIndicator(e.ExtentHeight, e.ViewportHeight, e.VerticalOffset);
    }

    private void UpdateScrollIndicator(double? extentOpt = null, double? viewportOpt = null, double? offsetOpt = null)
    {
        var sv = GetEditorScrollViewer();
        double extent = extentOpt ?? sv?.ExtentHeight ?? 0;
        double viewport = viewportOpt ?? sv?.ViewportHeight ?? 0;
        double offset = offsetOpt ?? sv?.VerticalOffset ?? 0;
        double trackHeight = NotesScrollTrack.ActualHeight;

        // Subdue or hide when there is no overflow
        if (trackHeight <= 0 || extent <= viewport || extent <= 0 || (extent - viewport) < 1.0)
        {
            NotesScrollTrack.Visibility = Visibility.Collapsed;
            return;
        }

        NotesScrollTrack.Visibility = Visibility.Visible;

        // Dynamic thumb sizing proportional to visible content ratio (minimum 20 DIPs)
        double thumbHeight = Math.Max(20.0, Math.Min(trackHeight, (viewport / extent) * trackHeight));
        NotesScrollThumb.Height = thumbHeight;

        // Dynamic vertical translation proportional to scroll offset
        double scrollableExtent = extent - viewport;
        double availableThumbTrack = Math.Max(0.0, trackHeight - thumbHeight);
        double scrollRatio = scrollableExtent > 0 ? Math.Clamp(offset / scrollableExtent, 0.0, 1.0) : 0.0;

        NotesScrollThumbTransform.Y = scrollRatio * availableThumbTrack;
    }

    private void OnExportClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        try
        {
            var sfd = new SaveFileDialog
            {
                Title = "Export Notes",
                Filter = "Text Documents (*.txt)|*.txt|Markdown Documents (*.md)|*.md|All Files (*.*)|*.*",
                DefaultExt = ".txt",
                FileName = $"Orbit_Notes_{DateTime.Now:yyyyMMdd_HHmm}.txt"
            };

            if (sfd.ShowDialog() == true)
            {
                bool ok = _notesService.ExportNotes(sfd.FileName, TxtEditor.Text);
                if (ok)
                {
                    UpdateSaveIndicator(isSaving: false, statusText: "Exported");
                }
            }
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, $"Export failed: {ex.Message}");
        }
    }

    private void OnClearClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        ClearConfirmationOverlay.Visibility = Visibility.Visible;
    }

    private void OnConfirmClearClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        ClearConfirmationOverlay.Visibility = Visibility.Collapsed;

        TxtEditor.Text = string.Empty;
        _notesService.ClearNotes();
        _hasUnsavedChanges = false;
        UpdateSaveIndicator(isSaving: false, statusText: "Cleared");
    }

    private void OnCancelClearClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        ClearConfirmationOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnBackClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        SaveCurrentNotesSynchronously();
        BackRequested?.Invoke();
    }

    private void OnViewBackgroundMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        SaveCurrentNotesSynchronously();
        _autoSaveTimer.Stop();
    }
}

using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Orbit.Core;
using Orbit.Models;
using Orbit.Services.FileDock;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using DataObject = System.Windows.DataObject;
using DataFormats = System.Windows.DataFormats;
using PathShape = System.Windows.Shapes.Path;

namespace Orbit.UI.Panels;

/// <summary>
/// Interaction logic for FileDockView.xaml.
/// Implements single-row horizontal file staging, smooth scrolling, drag-in preview, and drag-out interactions.
/// </summary>
public partial class FileDockView : UserControl
{
    private const string LogCategory = "FILE_DOCK_VIEW";

    private IFileDockService? _fileDockService;
    private Point _dragStartPoint;
    private bool _isPotentialDrag;
    private FileDockItem? _dragItem;
    private DispatcherTimer? _smoothScrollTimer;
    private double _targetScrollOffset;

    public bool IsDraggingOut { get; private set; }

    public event Action? PinToggleRequested;
    public event Action? CloseRequested;
    public event Action<string[]>? FilesDropped;

    public FileDockView()
    {
        InitializeComponent();
    }

    public void Initialize(IFileDockService fileDockService)
    {
        if (_fileDockService != null)
        {
            _fileDockService.ModeChanged -= OnServiceModeChanged;
            _fileDockService.Items.CollectionChanged -= OnCollectionChanged;
        }

        _fileDockService = fileDockService;

        if (_fileDockService != null)
        {
            _fileDockService.ModeChanged += OnServiceModeChanged;
            _fileDockService.Items.CollectionChanged += OnCollectionChanged;
            ItemsFileCards.ItemsSource = _fileDockService.Items;
            UpdateUiState();
        }
    }

    private void OnServiceModeChanged(FileDockMode mode)
    {
        Dispatcher.Invoke(() =>
        {
            UpdatePinVisual(mode == FileDockMode.Pinned && (_fileDockService?.Items.Count ?? 0) > 0);
        });
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateUiState();

            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                // Auto-scroll to newly added file
                AutoScrollToEnd();
            }
        });
    }

    public void UpdateUiState()
    {
        int count = _fileDockService?.Items.Count ?? 0;
        TxtItemCount.Text = count.ToString();

        if (count == 0)
        {
            EmptyStateBorder.Visibility = Visibility.Visible;
            FileScrollViewer.Visibility = Visibility.Collapsed;
        }
        else
        {
            EmptyStateBorder.Visibility = Visibility.Collapsed;
            FileScrollViewer.Visibility = Visibility.Visible;
        }

        UpdatePinVisual(_fileDockService?.CurrentMode == FileDockMode.Pinned && count > 0);
    }

    public void UpdatePinVisual(bool isPinned)
    {
        if (BtnPinToggle?.Template?.FindName("PinGlyph", BtnPinToggle) is PathShape glyph)
        {
            if (isPinned)
            {
                glyph.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC0, 0x84, 0xFC));
                glyph.Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE9, 0xD5, 0xFF));
                if (glyph.Effect is DropShadowEffect glow) glow.Opacity = 0.85;
                BtnPinToggle.ToolTip = "Pinned (Click to unpin)";
            }
            else
            {
                glyph.Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x60, 0x7C, 0x3A, 0xED));
                glyph.Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x80, 0xA8, 0x55, 0xF7));
                if (glyph.Effect is DropShadowEffect glow) glow.Opacity = 0.0;
                BtnPinToggle.ToolTip = "Unpinned (Click to pin)";
            }
        }
    }

    public void SetDragPreviewActive(bool active)
    {
        DragPreviewOverlay.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AutoScrollToEnd()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (FileScrollViewer != null)
            {
                FileScrollViewer.UpdateLayout();
                _targetScrollOffset = FileScrollViewer.ScrollableWidth;
                StartSmoothScroll();
            }
        });
    }

    #region Smooth Horizontal Mouse Wheel Scrolling

    private void OnFileScrollViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (FileScrollViewer == null || FileScrollViewer.ScrollableWidth <= 0) return;

        if (_smoothScrollTimer == null || !_smoothScrollTimer.IsEnabled)
        {
            _targetScrollOffset = FileScrollViewer.HorizontalOffset;
        }

        // Horizontal scrolling: standard mouse wheel or Shift+wheel
        double delta = -e.Delta * 0.75;
        _targetScrollOffset = Math.Clamp(_targetScrollOffset + delta, 0, FileScrollViewer.ScrollableWidth);

        StartSmoothScroll();
        e.Handled = true;
    }

    private void StartSmoothScroll()
    {
        if (_smoothScrollTimer == null)
        {
            _smoothScrollTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _smoothScrollTimer.Tick += OnSmoothScrollTick;
        }

        if (!_smoothScrollTimer.IsEnabled)
        {
            _smoothScrollTimer.Start();
        }
    }

    private void OnSmoothScrollTick(object? sender, EventArgs e)
    {
        if (FileScrollViewer == null)
        {
            _smoothScrollTimer?.Stop();
            return;
        }

        double current = FileScrollViewer.HorizontalOffset;
        double diff = _targetScrollOffset - current;

        if (Math.Abs(diff) < 0.8)
        {
            FileScrollViewer.ScrollToHorizontalOffset(_targetScrollOffset);
            _smoothScrollTimer?.Stop();
            return;
        }

        // 60fps exponential easing interpolation
        double next = current + (diff * 0.35);
        FileScrollViewer.ScrollToHorizontalOffset(next);
    }

    #endregion

    #region Drag Out (From File Dock to Windows Explorer / Applications)

    private void OnCardPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is FileDockItem item)
        {
            _dragStartPoint = e.GetPosition(this);
            _dragItem = item;
            _isPotentialDrag = true;
        }
    }

    private void OnCardPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPotentialDrag || _dragItem == null || e.LeftButton != MouseButtonState.Pressed)
        {
            _isPotentialDrag = false;
            return;
        }

        var currentPoint = e.GetPosition(this);
        var diff = _dragStartPoint - currentPoint;

        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            _isPotentialDrag = false;
            var item = _dragItem;
            _dragItem = null;
            InitiateDragOut(item, sender as FrameworkElement);
        }
    }

    private void InitiateDragOut(FileDockItem item, FrameworkElement? sourceElement)
    {
        if (string.IsNullOrWhiteSpace(item.FilePath)) return;

        try
        {
            IsDraggingOut = true;
            SetDragPreviewActive(false);
            OrbitLogger.Info(LogCategory, $"Initiating drag-out for staged file: '{item.FileName}'");

            var dataObject = new DataObject();
            dataObject.SetData(DataFormats.FileDrop, new[] { item.FilePath });

            // Native Windows drag-drop transfer
            DragDropEffects result;
            if (sourceElement != null)
            {
                result = DragDrop.DoDragDrop(sourceElement, dataObject, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            }
            else
            {
                result = DragDrop.DoDragDrop(this, dataObject, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            }

            OrbitLogger.Info(LogCategory, $"Drag-out completed for '{item.FileName}'. Result={result}");

            // Automatically remove the dropped file from the dock and update remaining items
            if (result != DragDropEffects.None)
            {
                _fileDockService?.RemoveFile(item);
                OrbitLogger.Info(LogCategory, $"Auto-removed dropped file from File Dock: '{item.FileName}'");
            }
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, $"Error during drag-out: {ex.Message}");
        }
        finally
        {
            IsDraggingOut = false;
            _dragItem = null;
            SetDragPreviewActive(false);
        }
    }

    #endregion

    #region Drag In and Drop Operations

    private void OnFileDockDragOver(object sender, DragEventArgs e)
    {
        if (IsDraggingOut)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            SetDragPreviewActive(true);
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }
    }

    private void OnFileDockDragLeave(object sender, DragEventArgs e)
    {
        if (IsDraggingOut)
        {
            SetDragPreviewActive(false);
            return;
        }

        var pos = e.GetPosition(this);
        if (pos.X < 0 || pos.Y < 0 || pos.X > ActualWidth || pos.Y > ActualHeight)
        {
            SetDragPreviewActive(false);
        }
    }

    private void OnFileDockDrop(object sender, DragEventArgs e)
    {
        SetDragPreviewActive(false);

        if (IsDraggingOut)
        {
            e.Handled = true;
            return;
        }

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                _fileDockService?.AddFiles(files);
                FilesDropped?.Invoke(files);
                e.Handled = true;
            }
        }
    }

    #endregion

    #region Card Removal & Header Controls

    private void OnRemoveCardClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.Tag is FileDockItem item)
        {
            _fileDockService?.RemoveFile(item);
        }
    }

    private void OnPinToggleClicked(object sender, RoutedEventArgs e)
    {
        PinToggleRequested?.Invoke();
    }

    private void OnCloseDockClicked(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke();
    }

    #endregion
}

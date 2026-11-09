using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CrosshairOverlay.ViewModels;

namespace CrosshairOverlay.Views;

public partial class PixelArtTab : UserControl
{
    private int _lastCenteredGridSize = -1;
    private bool _centerRequestPending;

    public PixelArtTab() => InitializeComponent();

    private void OnGridSizePreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return)) return;
        if (sender is TextBox box)
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        if (DataContext is PixelArtViewModel editor && editor.ApplyGridSizeCommand.CanExecute(null))
            editor.ApplyGridSizeCommand.Execute(null);
        e.Handled = true;
    }

    private void OnShareCodeFocused(object sender, RoutedEventArgs e)
    {
        if (DataContext is PixelArtViewModel editor) editor.IsEditingShareCode = true;
    }

    private void OnShareCodeLostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is PixelArtViewModel editor) editor.IsEditingShareCode = false;
    }

    private void OnCanvasLoaded(object sender, RoutedEventArgs e) => ScheduleCanvasCenter();

    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e) => ScheduleCanvasCenter();

    private void ScheduleCanvasCenter()
    {
        if (_centerRequestPending) return;
        _centerRequestPending = true;
        Dispatcher.BeginInvoke(() =>
        {
            _centerRequestPending = false;
            if (!IsLoaded || CanvasScrollViewer.ViewportWidth <= 0 || CanvasScrollViewer.ViewportHeight <= 0)
                return;

            var size = DrawingCanvas.GridSize;
            if (size == _lastCenteredGridSize) return;
            _lastCenteredGridSize = size;
            CanvasScrollViewer.ScrollToHorizontalOffset(
                Math.Max(0, (CanvasScrollViewer.ExtentWidth - CanvasScrollViewer.ViewportWidth) / 2));
            CanvasScrollViewer.ScrollToVerticalOffset(
                Math.Max(0, (CanvasScrollViewer.ExtentHeight - CanvasScrollViewer.ViewportHeight) / 2));
        }, DispatcherPriority.Loaded);
    }
}

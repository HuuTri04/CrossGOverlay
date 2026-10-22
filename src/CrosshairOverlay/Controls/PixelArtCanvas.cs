using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CrosshairOverlay.ViewModels;

namespace CrosshairOverlay.Controls;

/// <summary>One visual for the bitmap, grid and center guides; no per-cell UI controls.</summary>
public sealed class PixelArtCanvas : FrameworkElement
{
    private static readonly Brush CanvasBrush = FrozenBrush(Color.FromRgb(17, 27, 34));
    private static readonly Pen GridPen = FrozenPen(Color.FromArgb(70, 138, 156, 168), 0.5);
    private static readonly Pen CenterPen = FrozenPen(Color.FromArgb(210, 0, 237, 100), 1.5);
    private bool _drawing;
    private bool _eraseWithRight;
    private (int X, int Y) _lastCell;

    public PixelArtCanvas()
    {
        Focusable = true;
        Cursor = Cursors.Cross;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
    }

    public static readonly DependencyProperty BitmapProperty = DependencyProperty.Register(
        nameof(Bitmap), typeof(WriteableBitmap), typeof(PixelArtCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public WriteableBitmap? Bitmap
    {
        get => (WriteableBitmap?)GetValue(BitmapProperty);
        set => SetValue(BitmapProperty, value);
    }

    public static readonly DependencyProperty GridSizeProperty = DependencyProperty.Register(
        nameof(GridSize), typeof(int), typeof(PixelArtCanvas),
        new FrameworkPropertyMetadata(24, FrameworkPropertyMetadataOptions.AffectsRender));

    public int GridSize
    {
        get => (int)GetValue(GridSizeProperty);
        set => SetValue(GridSizeProperty, value);
    }

    public static readonly DependencyProperty ShowCenterGuideProperty = DependencyProperty.Register(
        nameof(ShowCenterGuide), typeof(bool), typeof(PixelArtCanvas),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool ShowCenterGuide
    {
        get => (bool)GetValue(ShowCenterGuideProperty);
        set => SetValue(ShowCenterGuideProperty, value);
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        context.DrawRectangle(CanvasBrush, null, bounds);
        if (Bitmap is not null) context.DrawImage(Bitmap, bounds);
        if (GridSize <= 0) return;

        var cellWidth = ActualWidth / GridSize;
        var cellHeight = ActualHeight / GridSize;
        for (var index = 1; index < GridSize; index++)
        {
            context.DrawLine(GridPen, new Point(index * cellWidth, 0),
                new Point(index * cellWidth, ActualHeight));
            context.DrawLine(GridPen, new Point(0, index * cellHeight),
                new Point(ActualWidth, index * cellHeight));
        }

        if (!ShowCenterGuide) return;
        context.DrawLine(CenterPen, new Point(ActualWidth / 2, 0),
            new Point(ActualWidth / 2, ActualHeight));
        context.DrawLine(CenterPen, new Point(0, ActualHeight / 2),
            new Point(ActualWidth, ActualHeight / 2));
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton is not (MouseButton.Left or MouseButton.Right) ||
            DataContext is not PixelArtViewModel editor) return;

        Focus();
        _eraseWithRight = e.ChangedButton == MouseButton.Right;
        _lastCell = CellAt(e.GetPosition(this));
        _drawing = CaptureMouse();
        editor.DrawLine(_lastCell.X, _lastCell.Y, _lastCell.X, _lastCell.Y, _eraseWithRight);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_drawing || DataContext is not PixelArtViewModel editor) return;
        if (_eraseWithRight ? e.RightButton != MouseButtonState.Pressed : e.LeftButton != MouseButtonState.Pressed)
        {
            EndStroke();
            return;
        }

        var cell = CellAt(e.GetPosition(this));
        if (cell == _lastCell) return;
        editor.DrawLine(_lastCell.X, _lastCell.Y, cell.X, cell.Y, _eraseWithRight);
        _lastCell = cell;
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_drawing || e.ChangedButton != (_eraseWithRight ? MouseButton.Right : MouseButton.Left)) return;
        EndStroke();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        EndStroke();
    }

    private void EndStroke()
    {
        if (!_drawing) return;
        _drawing = false;
        (DataContext as PixelArtViewModel)?.FinishStroke();
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    private (int X, int Y) CellAt(Point point) =>
        (Math.Clamp((int)(point.X / ActualWidth * GridSize), 0, GridSize - 1),
            Math.Clamp((int)(point.Y / ActualHeight * GridSize), 0, GridSize - 1));

    private static Brush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Color color, double thickness)
    {
        var pen = new Pen(FrozenBrush(color), thickness);
        pen.Freeze();
        return pen;
    }
}

using System.Runtime.InteropServices;
using System.Diagnostics;
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
    private bool _stylusDrawing;
    private int _activeStylusId;
    private bool _lastErase;
    private PixelArtViewModel? _strokeEditor;
    private (int X, int Y) _lastCell;
    private DrawingGroup? _guideDrawing;
    private Size _guideSize;
    private int _guideGridSize;
    private bool _guideShowsCenter;
    private readonly List<PixelStrokeSegment> _pendingSegments = new(128);
    private bool _frameSubscribed;
    private const int MaxPendingSegments = 2048;
    private static readonly bool InputDiagnosticsEnabled =
        Environment.GetEnvironmentVariable("CROSSGOVERLAY_INPUT_DIAGNOSTICS") == "1";

    public PixelArtCanvas()
    {
        Focusable = true;
        Cursor = Cursors.Cross;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        Stylus.SetIsFlicksEnabled(this, false);
        Stylus.SetIsPressAndHoldEnabled(this, false);
        Stylus.SetIsTapFeedbackEnabled(this, false);
        Stylus.SetIsTouchFeedbackEnabled(this, false);
        Unloaded += OnCanvasUnloaded;
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
        if (GridSize <= 0 || (GridSize > 64 && !ShowCenterGuide)) return;

        context.DrawDrawing(GetGuideDrawing());
    }

    // Pixel writes invalidate the canvas, but its grid changes only with size or guide settings.
    // Keep those drawing commands frozen instead of rebuilding every line during a brush stroke.
    private DrawingGroup GetGuideDrawing()
    {
        var size = RenderSize;
        var gridSize = GridSize;
        var showCenter = ShowCenterGuide;
        if (_guideDrawing is not null && _guideSize == size &&
            _guideGridSize == gridSize && _guideShowsCenter == showCenter)
            return _guideDrawing;

        var guides = new DrawingGroup();
        using (var guideContext = guides.Open())
        {
            var cellWidth = size.Width / gridSize;
            var cellHeight = size.Height / gridSize;
            for (var index = 1; gridSize <= 64 && index < gridSize; index++)
            {
                guideContext.DrawLine(GridPen, new Point(index * cellWidth, 0),
                    new Point(index * cellWidth, size.Height));
                guideContext.DrawLine(GridPen, new Point(0, index * cellHeight),
                    new Point(size.Width, index * cellHeight));
            }

            if (showCenter)
            {
                guideContext.DrawLine(CenterPen, new Point(size.Width / 2, 0),
                    new Point(size.Width / 2, size.Height));
                guideContext.DrawLine(CenterPen, new Point(0, size.Height / 2),
                    new Point(size.Width, size.Height / 2));
            }
        }

        guides.Freeze();
        _guideDrawing = guides;
        _guideSize = size;
        _guideGridSize = gridSize;
        _guideShowsCenter = showCenter;
        return guides;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        // WPF can promote pen input to mouse input. Never commit the same stroke twice.
        if (!CanDrawWithMouse(CurrentInputMode, e.StylusDevice is not null) || _stylusDrawing)
        {
            e.Handled = true;
            return;
        }
        if (e.ChangedButton is not (MouseButton.Left or MouseButton.Right) ||
            DataContext is not PixelArtViewModel editor) return;

        EndStroke();
        Focus();
        _eraseWithRight = e.ChangedButton == MouseButton.Right;
        _lastCell = CellAt(e.GetPosition(this));
        _lastErase = _eraseWithRight;
        _drawing = CaptureMouse();
        if (!_drawing) return;
        _strokeEditor = editor;
        editor.DrawLine(_lastCell.X, _lastCell.Y, _lastCell.X, _lastCell.Y, _lastErase);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (e.StylusDevice is not null || _stylusDrawing) return;
        if (CurrentInputMode != DrawingInputMode.Mouse)
        {
            EndStroke();
            return;
        }
        if (!_drawing || _strokeEditor is not PixelArtViewModel editor) return;
        if (_eraseWithRight ? e.RightButton != MouseButtonState.Pressed : e.LeftButton != MouseButtonState.Pressed)
        {
            EndStroke();
            return;
        }

        QueueTo(e.GetPosition(this), _eraseWithRight, editor);
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.StylusDevice is not null || _stylusDrawing)
        {
            e.Handled = true;
            return;
        }
        if (CurrentInputMode != DrawingInputMode.Mouse)
        {
            EndStroke();
            e.Handled = true;
            return;
        }
        if (!_drawing || e.ChangedButton != (_eraseWithRight ? MouseButton.Right : MouseButton.Left)) return;
        EndStroke();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        EndStroke();
    }

    protected override void OnStylusDown(StylusDownEventArgs e)
    {
        base.OnStylusDown(e);
        if (!CanDrawWithStylus(CurrentInputMode, e.StylusDevice.TabletDevice.Type))
        {
            e.Handled = true;
            return;
        }
        if (DataContext is not PixelArtViewModel editor) return;

        EndStroke();
        EndStylusStroke();
        Focus();
        _stylusDrawing = CaptureStylus();
        if (!_stylusDrawing) return;

        _strokeEditor = editor;
        _activeStylusId = e.StylusDevice.Id;
        _lastCell = CellAt(e.GetPosition(this));
        _lastErase = IsStylusEraser(e.StylusDevice);
        editor.DrawLine(_lastCell.X, _lastCell.Y, _lastCell.X, _lastCell.Y, _lastErase);
        e.Handled = true;
    }

    protected override void OnStylusMove(StylusEventArgs e)
    {
        base.OnStylusMove(e);
        if (CurrentInputMode != DrawingInputMode.Stylus)
        {
            if (_stylusDrawing && e.StylusDevice.Id == _activeStylusId) EndStylusStroke();
            e.Handled = true;
            return;
        }
        if (!CanDrawWithStylus(CurrentInputMode, e.StylusDevice.TabletDevice.Type))
        {
            e.Handled = true;
            return;
        }
        if (!_stylusDrawing || e.StylusDevice.Id != _activeStylusId ||
            _strokeEditor is not PixelArtViewModel editor) return;

        var started = InputDiagnosticsEnabled ? Stopwatch.GetTimestamp() : 0;
        QueueTo(e.GetPosition(this), IsStylusEraser(e.StylusDevice), editor);
        if (InputDiagnosticsEnabled)
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            if (elapsed > TimeSpan.FromMilliseconds(8))
                Trace.WriteLine($"[PixelArt StylusMove] grid={GridSize}, handler={elapsed.TotalMilliseconds:F2} ms (> 8 ms)");
        }
        e.Handled = true;
    }

    protected override void OnStylusUp(StylusEventArgs e)
    {
        base.OnStylusUp(e);
        if (CurrentInputMode != DrawingInputMode.Stylus)
        {
            if (_stylusDrawing && e.StylusDevice.Id == _activeStylusId) EndStylusStroke();
            e.Handled = true;
            return;
        }
        if (!CanDrawWithStylus(CurrentInputMode, e.StylusDevice.TabletDevice.Type))
        {
            e.Handled = true;
            return;
        }
        if (!_stylusDrawing || e.StylusDevice.Id != _activeStylusId) return;
        EndStylusStroke();
        e.Handled = true;
    }

    protected override void OnLostStylusCapture(StylusEventArgs e)
    {
        base.OnLostStylusCapture(e);
        EndStylusStroke();
    }

    private void QueueTo(Point point, bool erase, PixelArtViewModel editor)
    {
        var cell = CellAt(point);
        if (cell == _lastCell && erase == _lastErase) return;
        // Changing tip mode must not recolor the previous cell of the same stroke.
        var from = erase == _lastErase ? _lastCell : cell;
        _pendingSegments.Add(new PixelStrokeSegment(
            from.X, from.Y, cell.X, cell.Y, editor.StrokeColor(erase)));
        _lastCell = cell;
        _lastErase = erase;
        if (_pendingSegments.Count >= MaxPendingSegments)
            FlushPending();
        else if (!_frameSubscribed)
        {
            CompositionTarget.Rendering += OnPendingFrame;
            _frameSubscribed = true;
        }
    }

    private void OnPendingFrame(object? sender, EventArgs e) => FlushPending();

    private void FlushPending()
    {
        if (_frameSubscribed)
        {
            CompositionTarget.Rendering -= OnPendingFrame;
            _frameSubscribed = false;
        }
        if (_pendingSegments.Count == 0) return;
        if (_strokeEditor is { } editor)
            editor.DrawSegments(CollectionsMarshal.AsSpan(_pendingSegments));
        _pendingSegments.Clear();
    }

    private DrawingInputMode CurrentInputMode =>
        (DataContext as PixelArtViewModel)?.CurrentInputMode ?? DrawingInputMode.Mouse;

    internal static bool CanDrawWithMouse(DrawingInputMode mode, bool promotedFromStylus) =>
        mode == DrawingInputMode.Mouse && !promotedFromStylus;

    internal static bool CanDrawWithStylus(DrawingInputMode mode, TabletDeviceType deviceType) =>
        mode == DrawingInputMode.Stylus && deviceType == TabletDeviceType.Stylus;

    private static bool IsStylusEraser(StylusDevice stylus)
    {
        if (stylus.Inverted) return true;
        // Devices that expose the barrel button as an eraser can draw without
        // changing the selected toolbar tool. The pen's ordinary tip stays a pen.
        foreach (StylusButton button in stylus.StylusButtons)
        {
            if (button.Guid == StylusPointProperties.BarrelButton.Id &&
                button.StylusButtonState == StylusButtonState.Down)
                return true;
        }
        return false;
    }

    private void EndStylusStroke()
    {
        if (!_stylusDrawing) return;
        FlushPending();
        _stylusDrawing = false;
        _strokeEditor?.FinishStroke();
        _strokeEditor = null;
        if (IsStylusCaptured) ReleaseStylusCapture();
    }

    private void EndStroke()
    {
        if (!_drawing) return;
        FlushPending();
        _drawing = false;
        _strokeEditor?.FinishStroke();
        _strokeEditor = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        if (oldParent is not null)
        {
            FlushPending();
            EndStroke();
            EndStylusStroke();
        }
        base.OnVisualParentChanged(oldParent);
    }

    private void OnCanvasUnloaded(object sender, RoutedEventArgs e)
    {
        FlushPending();
        EndStroke();
        EndStylusStroke();
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

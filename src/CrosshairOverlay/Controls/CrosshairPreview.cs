using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Services.Rendering;

namespace CrosshairOverlay.Controls;

/// <summary>
/// Khung xem trước crosshair, cập nhật ngay khi preset đổi.
/// </summary>
/// <remarks>
/// Dùng CHUNG <see cref="ICrosshairRenderer"/> với overlay, nên preview không thể lệch so với
/// thứ hiển thị thật trong game.
///
/// <para>
/// Phóng to bằng cách render ra bitmap ở đúng độ phân giải thiết bị rồi mới kéo giãn với
/// <see cref="BitmapScalingMode.NearestNeighbor"/> — không kéo giãn hình vector. Nhờ vậy người
/// dùng nhìn thấy đúng từng pixel thật, kể cả kết quả bám lưới pixel của renderer. Nếu scale
/// vector, mọi nét đều mượt đẹp và che mất chính thứ cần kiểm tra.
/// </para>
/// </remarks>
public sealed class CrosshairPreview : FrameworkElement
{
    private static readonly Brush DarkBackdrop = Frozen(new SolidColorBrush(Color.FromRgb(0x12, 0x14, 0x17)));
    private static readonly Brush LightBackdrop = Frozen(new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xAF)));
    private static readonly Pen GuidePen = FrozenPen(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF), 1d);

    private static readonly Brush Checkerboard = CreateCheckerboard();

    private static readonly Brush MarkerFallback = Frozen(new SolidColorBrush(Color.FromRgb(0x00, 0xED, 0x64)));

    private BitmapSource? _cached;
    private bool _dirty = true;

    /// <summary>Crosshair có animation (GIF động): vẽ trực tiếp, không chụp thành ảnh tĩnh.</summary>
    private Drawing? _liveDrawing;

    /// <summary>
    /// Dịch lớp crosshair theo độ lệch của preset — đúng vai trò RenderTransform, nhưng CHỈ cho
    /// crosshair: lưới nền và hai đường dẫn hướng phải đứng yên, vì chúng là "tâm màn hình".
    /// </summary>
    /// <remarks>
    /// Ngoại lệ có chủ ý với quy tắc đóng băng Freezable: đối tượng này được dùng lại và chỉ đổi
    /// X/Y, thay vì cấp phát một transform mới mỗi lần kéo thanh trượt.
    /// </remarks>
    private readonly TranslateTransform _crosshairOffset = new();

    // ---- kéo rê ----

    /// <summary>
    /// Nới vùng bấm quanh crosshair, DIP. Nét 1–2 px phóng lên vẫn mảnh; bắt người dùng nhắm trúng
    /// đúng nét thì kéo rê trở thành trò chơi luyện tay.
    /// </summary>
    private const double GrabTolerance = 10d;

    /// <summary>Vùng crosshair trong khung ở lượt vẽ gần nhất (đã tính zoom và độ lệch).</summary>
    private Rect _crosshairBounds = Rect.Empty;

    /// <summary>Đầu mũi tên chỉ hướng khi crosshair ra ngoài khung; null khi không hiện.</summary>
    private Point? _markerTip;

    private bool _isDragging;
    private Point _dragStartPoint;
    private double _dragStartOffsetX;
    private double _dragStartOffsetY;

    public CrosshairPreview()
    {
        // Cửa sổ Settings bị ẩn xuống khay: dừng GIF trong preview, hiện lại thì dựng mới.
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) return;

            DrawingAnimations.Stop(_liveDrawing);
            _liveDrawing = null;
            _dirty = true;
        };

        // Để nhận phím Esc huỷ cú kéo đang dở.
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
    }

    // ---------------------------------------------------------------- properties

    public static readonly DependencyProperty ProfileProperty = DependencyProperty.Register(
        nameof(Profile), typeof(CrosshairProfile), typeof(CrosshairPreview),
        new PropertyMetadata(null, OnProfileChanged));

    public CrosshairProfile? Profile
    {
        get => (CrosshairProfile?)GetValue(ProfileProperty);
        set => SetValue(ProfileProperty, value);
    }

    public static readonly DependencyProperty RendererProperty = DependencyProperty.Register(
        nameof(Renderer), typeof(ICrosshairRenderer), typeof(CrosshairPreview),
        new PropertyMetadata(null, OnVisualInputChanged));

    public ICrosshairRenderer? Renderer
    {
        get => (ICrosshairRenderer?)GetValue(RendererProperty);
        set => SetValue(RendererProperty, value);
    }

    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom), typeof(double), typeof(CrosshairPreview),
        new PropertyMetadata(4d, OnVisualInputChanged));

    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public static readonly DependencyProperty ShowCheckerboardProperty = DependencyProperty.Register(
        nameof(ShowCheckerboard), typeof(bool), typeof(CrosshairPreview),
        new PropertyMetadata(true, OnRedrawOnlyChanged));

    public bool ShowCheckerboard
    {
        get => (bool)GetValue(ShowCheckerboardProperty);
        set => SetValue(ShowCheckerboardProperty, value);
    }

    public static readonly DependencyProperty UseDarkBackgroundProperty = DependencyProperty.Register(
        nameof(UseDarkBackground), typeof(bool), typeof(CrosshairPreview),
        new PropertyMetadata(true, OnRedrawOnlyChanged));

    public bool UseDarkBackground
    {
        get => (bool)GetValue(UseDarkBackgroundProperty);
        set => SetValue(UseDarkBackgroundProperty, value);
    }

    // ---------------------------------------------------------------- thay đổi

    private static void OnProfileChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var preview = (CrosshairPreview)d;

        if (e.OldValue is CrosshairProfile old)
            ProfileNotifications.Hook(old, preview.OnProfilePartChanged, subscribe: false);

        if (e.NewValue is CrosshairProfile now)
            ProfileNotifications.Hook(now, preview.OnProfilePartChanged, subscribe: true);

        preview.Rebuild();
    }

    private static void OnVisualInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((CrosshairPreview)d).Rebuild();

    private static void OnRedrawOnlyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((CrosshairPreview)d).InvalidateVisual();

    private void OnProfilePartChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Độ lệch chỉ dời vị trí, không đổi hình: vẽ lại là đủ, không dựng lại ảnh chụp. Nhờ vậy
        // kéo thanh Lệch ngang/dọc mượt ngang với overlay thật.
        if (e.PropertyName is nameof(CrosshairProfile.OffsetX) or nameof(CrosshairProfile.OffsetY))
        {
            InvalidateVisual();
            return;
        }

        Rebuild();
    }

    private void Rebuild()
    {
        _dirty = true;
        InvalidateVisual();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Rebuild();
    }

    // ---------------------------------------------------------------- vẽ

    protected override void OnRender(DrawingContext dc)
    {
        var size = RenderSize;
        if (size.Width <= 0 || size.Height <= 0) return;

        var area = new Rect(0, 0, size.Width, size.Height);
        dc.DrawRectangle(UseDarkBackground ? DarkBackdrop : LightBackdrop, null, area);

        if (ShowCheckerboard) dc.DrawRectangle(Checkerboard, null, area);

        var cx = Math.Round(size.Width / 2d);
        var cy = Math.Round(size.Height / 2d);

        // Hai đường dẫn hướng chỉ ra tâm chính xác — để kiểm tra offset và độ cân của hình.
        dc.DrawLine(GuidePen, new Point(0, cy + 0.5), new Point(size.Width, cy + 0.5));
        dc.DrawLine(GuidePen, new Point(cx + 0.5, 0), new Point(cx + 0.5, size.Height));

        if (_dirty)
        {
            _cached = BuildBitmap();
            _dirty = false;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var zoom = Math.Clamp(Zoom, 1d, 40d);

        var offset = Profile is { } profile
            ? CrosshairOffset(profile.EffectiveOffsetX, profile.EffectiveOffsetY, zoom, dpi.DpiScaleX, dpi.DpiScaleY)
            : default;

        _crosshairOffset.X = offset.X;
        _crosshairOffset.Y = offset.Y;

        dc.PushTransform(_crosshairOffset);

        if (_cached is not null)
        {
            // _cached đo bằng device pixel; quy về DIP rồi nhân zoom.
            var width = _cached.PixelWidth * zoom / dpi.DpiScaleX;
            var height = _cached.PixelHeight * zoom / dpi.DpiScaleY;

            var rect = new Rect(cx - (width / 2d), cy - (height / 2d), width, height);
            dc.DrawImage(_cached, rect);
            _crosshairBounds = Rect.Offset(rect, offset);
        }
        else if (_liveDrawing is not null)
        {
            // GIF động: một DIP của crosshair bằng "zoom" DIP trong khung, đúng tỉ lệ của nhánh ảnh tĩnh.
            dc.PushTransform(new TranslateTransform(cx, cy));
            dc.PushTransform(new ScaleTransform(zoom, zoom));
            dc.DrawDrawing(_liveDrawing);
            dc.Pop();
            dc.Pop();

            var bounds = _liveDrawing.Bounds;
            _crosshairBounds = new Rect(
                cx + (bounds.X * zoom) + offset.X,
                cy + (bounds.Y * zoom) + offset.Y,
                bounds.Width * zoom,
                bounds.Height * zoom);
        }
        else
        {
            _crosshairBounds = Rect.Empty;
        }

        dc.Pop();

        DrawOffscreenMarker(dc, size, new Point(cx + offset.X, cy + offset.Y));
    }

    /// <summary>
    /// Độ dịch của crosshair trong khung preview, DIP.
    /// </summary>
    /// <remarks>
    /// Overlay đặt cửa sổ lệch <c>Math.Round(offset × DPI)</c> device pixel. Khung preview phóng mỗi
    /// pixel của crosshair lên <paramref name="zoom"/> lần, nên độ lệch cũng phải phóng đúng như
    /// thế — làm tròn TRƯỚC khi phóng để preview lệch đúng số pixel nguyên như overlay, không lệch
    /// một phần pixel mà overlay không bao giờ lệch được.
    /// </remarks>
    internal static Vector CrosshairOffset(double offsetXDip, double offsetYDip, double zoom, double dpiX, double dpiY)
    {
        dpiX = dpiX > 0 ? dpiX : 1d;
        dpiY = dpiY > 0 ? dpiY : 1d;

        var pixelsX = Math.Round(offsetXDip * dpiX) * zoom;
        var pixelsY = Math.Round(offsetYDip * dpiY) * zoom;

        return new Vector(pixelsX / dpiX, pixelsY / dpiY);
    }

    /// <summary>
    /// Mũi tên ở mép khung khi crosshair đã bị dời ra ngoài.
    /// </summary>
    /// <remarks>
    /// Ở mức phóng 4×, lệch hơn khoảng 80 DIP là crosshair ra khỏi khung. Không có dấu hiệu gì thì
    /// người dùng kéo thanh trượt, thấy crosshair biến mất, và tưởng lại hỏng. Mũi tên chỉ đúng hướng
    /// nó đang nằm; hạ mức Phóng để nhìn thấy lại.
    /// </remarks>
    private void DrawOffscreenMarker(DrawingContext dc, Size size, Point center)
    {
        const double Inset = 12d;

        _markerTip = null;
        if (center.X >= 0 && center.X <= size.Width && center.Y >= 0 && center.Y <= size.Height) return;

        var mid = new Point(size.Width / 2d, size.Height / 2d);
        var direction = center - mid;
        if (direction.Length < 0.001d) return;
        direction.Normalize();

        var tip = new Point(
            Math.Clamp(center.X, Inset, size.Width - Inset),
            Math.Clamp(center.Y, Inset, size.Height - Inset));

        _markerTip = tip;

        var normal = new Vector(-direction.Y, direction.X);
        var back = tip - (direction * 12d);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(tip, isFilled: true, isClosed: true);
            ctx.LineTo(back + (normal * 7d), isStroked: false, isSmoothJoin: false);
            ctx.LineTo(back - (normal * 7d), isStroked: false, isSmoothJoin: false);
        }

        geometry.Freeze();
        dc.DrawGeometry(TryFindResource("AccentBright") as Brush ?? MarkerFallback, null, geometry);
    }

    // ---------------------------------------------------------------- kéo rê

    /// <summary>
    /// Độ lệch mới khi kéo, tính từ ĐIỂM BẮT ĐẦU chứ không cộng dồn từng bước.
    /// </summary>
    /// <remarks>
    /// Crosshair trong khung được phóng lên <paramref name="zoom"/> lần (xem
    /// <see cref="CrosshairOffset"/>), nên chuột đi <c>zoom</c> DIP thì crosshair thật chỉ được lệch
    /// 1 DIP. Chia cho zoom là thứ khiến crosshair bám đúng dưới con trỏ ở MỌI mức phóng.
    ///
    /// <para>
    /// Luôn tính từ điểm nhấn ban đầu: cộng dồn delta của từng lần MouseMove sẽ cộng dồn cả sai số
    /// làm tròn, và sau một cú kéo dài crosshair trôi khỏi con trỏ. Làm tròn về DIP nguyên để ô nhập
    /// số hiện 37 chứ không phải 36.8 — overlay vốn cũng chỉ đặt được ở pixel nguyên.
    /// </para>
    /// </remarks>
    internal static (double X, double Y) DragOffset(
        Point start, Point current, double startOffsetX, double startOffsetY, double zoom)
    {
        zoom = zoom > 0 ? zoom : 1d;
        var delta = current - start;

        // Làm tròn xa số 0: mặc định của Math.Round là làm tròn về số chẵn, khiến nửa DIP lúc thì
        // nhảy, lúc thì không, tuỳ vị trí — kéo sẽ có cảm giác giật cục.
        return (
            Math.Round(startOffsetX + (delta.X / zoom), MidpointRounding.AwayFromZero),
            Math.Round(startOffsetY + (delta.Y / zoom), MidpointRounding.AwayFromZero));
    }

    /// <summary>Điểm này có nằm trên crosshair (hoặc mũi tên chỉ hướng khi nó ở ngoài khung) không.</summary>
    private bool IsOverCrosshair(Point point)
    {
        if (!_crosshairBounds.IsEmpty)
        {
            var grab = _crosshairBounds;
            grab.Inflate(GrabTolerance, GrabTolerance);
            if (grab.Contains(point)) return true;
        }

        return _markerTip is { } tip && (point - tip).Length <= 18d;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Profile is not { } profile) return;

        var point = e.GetPosition(this);
        if (!IsOverCrosshair(point)) return;

        // Giữ chuột: kéo nhanh ra ngoài viền khung, thậm chí ra ngoài cửa sổ, vẫn nhận MouseMove.
        if (!CaptureMouse()) return;

        _isDragging = true;
        _dragStartPoint = point;
        _dragStartOffsetX = profile.EffectiveOffsetX;
        _dragStartOffsetY = profile.EffectiveOffsetY;

        Focus();
        Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var point = e.GetPosition(this);

        if (!_isDragging)
        {
            // Báo trước là kéo được, trước khi người dùng thử bấm.
            Cursor = IsOverCrosshair(point) ? Cursors.SizeAll : null;
            return;
        }

        if (Profile is not { } profile) return;

        var (x, y) = DragOffset(_dragStartPoint, point, _dragStartOffsetX, _dragStartOffsetY, Math.Clamp(Zoom, 1d, 40d));

        // Ghi thẳng vào model: thanh trượt Lệch X/Y (bind hai chiều) nhảy số, overlay dời theo, và
        // khung này vẽ lại — tất cả qua đúng một PropertyChanged, không có đường đồng bộ thứ hai.
        profile.SetEffectiveOffset(x, y);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_isDragging) return;

        EndDrag();
        e.Handled = true;
    }

    /// <summary>Mất capture ngoài ý muốn (Alt+Tab, hộp thoại bật lên): dừng kéo, giữ vị trí hiện tại.</summary>
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_isDragging) EndDrag();
    }

    /// <summary>Esc khi đang kéo: huỷ, trả crosshair về chỗ cũ.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!_isDragging || e.Key != Key.Escape) return;

        Profile?.SetEffectiveOffset(_dragStartOffsetX, _dragStartOffsetY);
        EndDrag();
        e.Handled = true;
    }

    private void EndDrag()
    {
        _isDragging = false;
        Cursor = null;

        // Gọi sau khi đã tắt cờ: ReleaseMouseCapture bắn OnLostMouseCapture ngay lập tức.
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    private BitmapSource? BuildBitmap()
    {
        DrawingAnimations.Stop(_liveDrawing);
        _liveDrawing = null;
        if (Profile is not { } profile || Renderer is not { } renderer) return null;

        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1d;
            var scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1d;

            var options = new CrosshairRenderOptions(
                DpiScale: scaleX,
                SnapToPixels: Math.Abs(profile.Rotation) < 0.01d,
                MaxExtent: 600d);

            var sizeDip = renderer.Measure(profile, options);
            var drawing = renderer.Build(profile, options);

            // Có animation (GIF động) thì chụp thành ảnh tĩnh sẽ đứng hình ở khung đầu.
            if (!drawing.IsFrozen)
            {
                _liveDrawing = drawing;
                return null;
            }

            var pixelWidth = (int)Math.Round(sizeDip.Width * scaleX);
            var pixelHeight = (int)Math.Round(sizeDip.Height * scaleY);
            if (pixelWidth <= 0 || pixelHeight <= 0) return null;

            var visual = new DrawingVisual();

            // Áp ĐÚNG quyết định khử răng cưa mà overlay dùng, nếu không preview sẽ nói dối
            // về độ sắc của crosshair thật.
            RenderOptions.SetEdgeMode(
                visual,
                renderer.PrefersAliasedEdges(profile) ? EdgeMode.Aliased : EdgeMode.Unspecified);

            using (var vdc = visual.RenderOpen())
            {
                vdc.PushTransform(new TranslateTransform(sizeDip.Width / 2d, sizeDip.Height / 2d));
                vdc.DrawDrawing(drawing);
                vdc.Pop();
            }

            var bitmap = new RenderTargetBitmap(
                pixelWidth, pixelHeight, 96d * scaleX, 96d * scaleY, PixelFormats.Pbgra32);

            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            // Preset lỗi chỉ nên làm trống khung preview, không làm sập cửa sổ Settings.
            return null;
        }
    }

    // ---------------------------------------------------------------- tiện ích

    private static Brush CreateCheckerboard()
    {
        var light = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
        var geometry = new GeometryGroup();
        geometry.Children.Add(new RectangleGeometry(new Rect(0, 0, 8, 8)));
        geometry.Children.Add(new RectangleGeometry(new Rect(8, 8, 8, 8)));

        var brush = new DrawingBrush(new GeometryDrawing(light, null, geometry))
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 16, 16),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };

        brush.Freeze();
        return brush;
    }

    private static Brush Frozen(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Color color, double thickness)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();

        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }
}

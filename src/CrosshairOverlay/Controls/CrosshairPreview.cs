using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;

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

    private BitmapSource? _cached;
    private bool _dirty = true;

    public CrosshairPreview()
    {
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

    private void OnProfilePartChanged(object? sender, PropertyChangedEventArgs e) => Rebuild();

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

        if (_cached is null) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        var zoom = Math.Clamp(Zoom, 1d, 40d);

        // _cached đo bằng device pixel; quy về DIP rồi nhân zoom.
        var width = _cached.PixelWidth * zoom / dpi.DpiScaleX;
        var height = _cached.PixelHeight * zoom / dpi.DpiScaleY;

        dc.DrawImage(_cached, new Rect(cx - (width / 2d), cy - (height / 2d), width, height));
    }

    private BitmapSource? BuildBitmap()
    {
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

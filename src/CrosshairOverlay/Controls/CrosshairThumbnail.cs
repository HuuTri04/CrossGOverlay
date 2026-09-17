using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;

namespace CrosshairOverlay.Controls;

/// <summary>
/// Ảnh thu nhỏ tĩnh của một crosshair trên nền ca-rô mờ — cho thẻ trong Thư viện mẫu.
/// </summary>
/// <remarks>
/// <para>
/// Nhẹ hơn <see cref="CrosshairPreview"/> nhiều: không kéo rê, không lắng nghe preset đổi (mẫu trong thư viện
/// không bao giờ đổi), không animation.
/// </para>
/// <para>
/// Vẽ thẳng hình VECTOR đã đóng băng của renderer, không chụp ra bitmap. Đo trên máy thật: chụp
/// <c>RenderTargetBitmap</c> cho mỗi thẻ lần đầu hiện ra làm lần cuộn đầu tiên qua 520 mẫu có khung hình chậm tới
/// 45 ms. Renderer đã bám lưới pixel thiết bị, nên phóng NGUYÊN lần quanh một tâm nằm đúng biên pixel với khử răng
/// cưa theo đúng quyết định của overlay vẫn cho từng pixel sắc nét.
/// </para>
/// <para>
/// Danh sách ảo hoá kiểu Recycling tái dùng thẻ khi cuộn: đổi <see cref="Profile"/> chỉ vẽ lại. Hình vẽ và phép
/// biến đổi của mỗi preset được dựng MỘT lần rồi giữ trong <see cref="ConditionalWeakTable{TKey,TValue}"/> theo chính
/// đối tượng preset — cuộn đi cuộn lại không dựng lại và không cấp phát gì, còn preset bị bỏ (đóng cửa sổ) thì hình
/// đi theo.
/// </para>
/// </remarks>
public sealed class CrosshairThumbnail : FrameworkElement
{
    /// <summary>Phóng tối đa: nét 1 px thành 3 px là đủ nhìn rõ, lớn hơn thì chấm nhỏ thành khối vuông thô.</summary>
    private const double MaxZoom = 3d;

    private static readonly ConditionalWeakTable<CrosshairProfile, Snapshot> Cache = new();

    private static readonly Brush Checkerboard = CreateCheckerboard();

    public static readonly DependencyProperty ProfileProperty = DependencyProperty.Register(
        nameof(Profile), typeof(CrosshairProfile), typeof(CrosshairThumbnail),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RendererProperty = DependencyProperty.Register(
        nameof(Renderer), typeof(ICrosshairRenderer), typeof(CrosshairThumbnail),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public CrosshairThumbnail()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
    }

    public CrosshairProfile? Profile
    {
        get => (CrosshairProfile?)GetValue(ProfileProperty);
        set => SetValue(ProfileProperty, value);
    }

    public ICrosshairRenderer? Renderer
    {
        get => (ICrosshairRenderer?)GetValue(RendererProperty);
        set => SetValue(RendererProperty, value);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var box = RenderSize;
        if (box.Width <= 0 || box.Height <= 0) return;

        dc.DrawRectangle(Checkerboard, null, new Rect(box));

        if (Profile is not { } profile || Renderer is not { } renderer) return;
        if (GetSnapshot(renderer, profile, VisualTreeHelper.GetDpi(this), box) is not { } snapshot) return;

        dc.PushTransform(snapshot.Transform);
        dc.DrawDrawing(snapshot.Drawing);
        dc.Pop();
    }

    /// <summary>
    /// Hệ số phóng: số NGUYÊN lớn nhất (tối đa <see cref="MaxZoom"/>) để hình chiếm không quá 80% khung; hình vốn
    /// to hơn khung thì thu nhỏ vừa khung.
    /// </summary>
    internal static double Zoom(double contentWidth, double contentHeight, Size box)
    {
        if (contentWidth <= 0 || contentHeight <= 0) return 1d;

        var fit = Math.Min(box.Width * 0.8d / contentWidth, box.Height * 0.8d / contentHeight);
        return fit >= 1d ? Math.Clamp(Math.Floor(fit), 1d, MaxZoom) : fit;
    }

    /// <summary>
    /// Tâm khung làm tròn về biên pixel thiết bị: renderer căn nét theo một tâm nằm TRÊN biên pixel (như tâm cửa sổ
    /// overlay), lệch nửa pixel thì mọi nét 1 px bị nhoè thành 2 px mờ.
    /// </summary>
    internal static Point SnappedCenter(Size box, DpiScale dpi) => new(
        Math.Round(box.Width / 2d * dpi.DpiScaleX) / dpi.DpiScaleX,
        Math.Round(box.Height / 2d * dpi.DpiScaleY) / dpi.DpiScaleY);

    private static Snapshot? GetSnapshot(ICrosshairRenderer renderer, CrosshairProfile profile, DpiScale dpi, Size box)
    {
        if (Cache.TryGetValue(profile, out var cached) && cached.Dpi == dpi.DpiScaleX && cached.Box == box)
            return cached.Drawing is null ? null : cached;

        Snapshot snapshot;
        try
        {
            var options = new CrosshairRenderOptions(
                DpiScale: dpi.DpiScaleX,
                SnapToPixels: Math.Abs(profile.Rotation) < 0.01d,
                MaxExtent: 200d);

            var size = renderer.Measure(profile, options);
            var drawing = renderer.Build(profile, options);

            // Áp ĐÚNG quyết định khử răng cưa mà overlay dùng.
            var group = new DrawingGroup();
            group.Children.Add(drawing);
            RenderOptions.SetEdgeMode(group, renderer.PrefersAliasedEdges(profile) ? EdgeMode.Aliased : EdgeMode.Unspecified);
            if (group.CanFreeze) group.Freeze();

            var zoom = Zoom(size.Width, size.Height, box);
            var center = SnappedCenter(box, dpi);
            var transform = new MatrixTransform(zoom, 0, 0, zoom, center.X, center.Y);
            transform.Freeze();

            snapshot = new Snapshot(dpi.DpiScaleX, box, group, transform);
        }
        catch (Exception)
        {
            // Một mẫu hỏng chỉ để trống ô của nó, không làm hỏng cả danh sách.
            snapshot = new Snapshot(dpi.DpiScaleX, box, null!, Transform.Identity);
        }

        Cache.AddOrUpdate(profile, snapshot);
        return snapshot.Drawing is null ? null : snapshot;
    }

    private static Brush CreateCheckerboard()
    {
        // Hai tông xám xanh rất gần nhau: đủ thấy ô ca-rô để nhận ra phần trong suốt, không tranh chỗ với tâm ngắm.
        var light = new SolidColorBrush(Color.FromRgb(0x1C, 0x2D, 0x38));
        var dark = new SolidColorBrush(Color.FromRgb(0x15, 0x23, 0x2C));
        light.Freeze();
        dark.Freeze();

        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        group.Children.Add(new GeometryDrawing(light, null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
        group.Children.Add(new GeometryDrawing(light, null, new RectangleGeometry(new Rect(8, 8, 8, 8))));

        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 16, 16),
            ViewportUnits = BrushMappingMode.Absolute,
        };

        // Lát gạch một DrawingBrush mà không cache thì luồng render dựng lại ô gạch cho MỖI thẻ ở MỖI khung hình
        // khi cuộn. Cache biến ô gạch thành bitmap nhỏ một lần.
        RenderOptions.SetCachingHint(brush, CachingHint.Cache);
        brush.Freeze();
        return brush;
    }

    private sealed record Snapshot(double Dpi, Size Box, Drawing Drawing, Transform Transform);
}

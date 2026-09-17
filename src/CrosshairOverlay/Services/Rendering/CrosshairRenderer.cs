using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay.Services.Rendering;

/// <inheritdoc cref="ICrosshairRenderer"/>
public sealed class CrosshairRenderer : ICrosshairRenderer
{
    /// <summary>Đệm quanh vùng bao để nét ngoài cùng không bị cắt khi làm tròn, device pixel.</summary>
    private const double PaddingPx = 2d;

    private readonly ILogger<CrosshairRenderer> _logger;
    private readonly ICustomImageStore? _images;

    /// <summary>
    /// Cache ảnh tuỳ chỉnh. Thiếu nó thì mỗi lần vẽ lại là một lần đọc đĩa — không chấp nhận
    /// được khi người dùng đang kéo slider. Có giới hạn dung lượng: xem <see cref="ImageCache"/>.
    /// </summary>
    private readonly ImageCache _imageCache = new();

    /// <summary>Số ảnh và dung lượng đang giữ trong cache — cho kiểm thử và chẩn đoán.</summary>
    internal (int Count, long Bytes) ImageCacheUsage => (_imageCache.Count, _imageCache.Bytes);

    /// <param name="images">
    /// Kho ảnh để đổi đường dẫn tương đối trong preset ra file thật. Không có kho (trong test) thì
    /// chỉ đọc được đường dẫn tuyệt đối.
    /// </param>
    public CrosshairRenderer(ILogger<CrosshairRenderer> logger, ICustomImageStore? images = null)
    {
        _logger = logger;
        _images = images;
    }

    public Size Measure(CrosshairProfile profile, CrosshairRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var plan = CreatePlan(profile, options);
        var sideDip = plan.ToDip(plan.ExtentPx * 2d);
        return new Size(sideDip, sideDip);
    }

    public bool PrefersAliasedEdges(CrosshairProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        // Ảnh bitmap có chế độ co giãn riêng (xem CreateImagePlan); tắt khử răng cưa chỉ làm hỏng
        // mép ảnh đã xoay.
        if (profile.Type == CrosshairType.Image) return false;

        // Hình đã xoay: mọi nét đều thành đường xiên.
        if (Math.Abs(profile.Rotation) > 0.01d) return false;

        // Nhánh chéo, đường tròn và ảnh bitmap đều cần khử răng cưa.
        if (profile.Shape is CrosshairShape.XShape or CrosshairShape.Circle
            or CrosshairShape.CircleDot) return false;

        // Vòng tròn bật kèm một hình khác cũng vậy; riêng khung vuông thì không sao.
        if (profile.Ring.Enabled && profile.Shape != CrosshairShape.Square) return false;

        // Đầu nhánh bo tròn là cung tròn — xét từng lớp đang bật.
        if (profile.InnerLines is { Enabled: true, RoundedCaps: true }) return false;
        if (profile.OuterLines is { Enabled: true, RoundedCaps: true }) return false;

        // Chấm giữa lớn được vẽ bằng hình tròn (ngưỡng 3 px trong CreatePlan); chấm nhỏ vẽ
        // bằng hình vuông nên vẫn sắc.
        if (profile.CenterDot.Enabled && profile.CenterDot.Size * profile.Scale > 3d) return false;

        return true;
    }

    public Drawing Build(CrosshairProfile profile, CrosshairRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var plan = CreatePlan(profile, options);
        // Chế độ ảnh dùng độ mờ của CHÍNH ảnh (áp trong DrawImage), không nhân độ mờ tổng thể của
        // chế độ tiêu chuẩn — thanh trượt đó bị ẩn khi chọn ảnh.
        var group = new DrawingGroup
        {
            Opacity = profile.Type == CrosshairType.Image ? 1d : Clamp01(profile.Opacity),
        };

        if (Math.Abs(profile.Rotation) > 0.01d)
            group.Transform = new RotateTransform(profile.Rotation);

        using (var dc = group.Open())
        {
            if (plan.Image is not null)
            {
                DrawImage(dc, plan);
            }
            else
            {
                // Thứ tự: Viền → (vòng) → Chấm giữa → Nhánh trong → Nhánh ngoài.
                //
                // Vẽ TOÀN BỘ viền trước, rồi mới tới lõi. Nếu vẽ xen kẽ theo từng phần tử,
                // viền của phần tử sau sẽ đè lên lõi của phần tử trước — ví dụ viền nhánh
                // trong sẽ cắt ngang chấm giữa khi khoảng cách bằng 0.
                if (plan.HasOutline)
                {
                    DrawRing(dc, plan, outlinePass: true);
                    DrawDot(dc, plan, outlinePass: true);
                    DrawLineLayer(dc, plan, plan.InnerLines, outlinePass: true);
                    DrawLineLayer(dc, plan, plan.OuterLines, outlinePass: true);
                }

                DrawRing(dc, plan, outlinePass: false);
                DrawDot(dc, plan, outlinePass: false);
                DrawLineLayer(dc, plan, plan.InnerLines, outlinePass: false);
                DrawLineLayer(dc, plan, plan.OuterLines, outlinePass: false);
            }
        }

        // GIF động: bộ phát khung sẽ đổi ảnh bên trong, nên KHÔNG được đóng băng. Mọi trường hợp
        // khác vẫn đóng băng như cũ — chính việc không đóng băng là tín hiệu để khung preview biết
        // phải vẽ trực tiếp thay vì chụp thành một ảnh tĩnh.
        if (plan.Animation is null) group.Freeze();
        return group;
    }

    // ------------------------------------------------------------------ dựng plan

    private RenderPlan CreatePlan(CrosshairProfile profile, CrosshairRenderOptions options)
    {
        var dpi = options.DpiScale > 0 ? options.DpiScale : 1d;
        var snap = options.SnapToPixels;
        var scale = Math.Clamp(profile.Scale, 0.1d, 10d);

        double ToPx(double dip) => dip * scale * dpi;
        double SnapPx(double px) => snap ? Math.Round(px) : px;

        // Ảnh tuỳ chỉnh đi đường riêng: không có nhánh, vòng, chấm hay viền.
        if (profile.Type == CrosshairType.Image)
            return CreateImagePlan(profile, dpi, options);

        var shape = profile.Shape;

        // Viền: quy về số nguyên pixel TRƯỚC. Nhờ vậy bề dày tổng (lõi + 2×viền) luôn cùng
        // tính chẵn/lẻ với lõi, nên hai lượt vẽ dùng chung một giá trị căn nửa pixel và
        // luôn đồng tâm.
        var outlinePx = profile.Outline.Enabled && profile.Outline.Thickness > 0
            ? Math.Max(1d, SnapPx(ToPx(profile.Outline.Thickness)))
            : 0d;

        // ---- nhánh ----
        var lineShape = shape is CrosshairShape.Cross or CrosshairShape.TShape or CrosshairShape.XShape;

        // TShape = bỏ nhánh trên ở CẢ HAI lớp, bất kể cờ ShowTop của người dùng.
        var diagonal = shape == CrosshairShape.XShape;
        var tShape = shape == CrosshairShape.TShape;
        var inner = PlanLineLayer(profile.InnerLines, lineShape, diagonal, tShape, ToPx, SnapPx, snap);
        var outer = PlanLineLayer(profile.OuterLines, lineShape, diagonal, tShape, ToPx, SnapPx, snap);

        // ---- vòng / khung ----
        var ringWanted = (profile.Ring.Enabled
                          || shape is CrosshairShape.Circle or CrosshairShape.CircleDot or CrosshairShape.Square)
                         && profile.Ring.Radius > 0;

        var ringCoreT = ringWanted ? Math.Max(snap ? 1d : 0.1d, SnapPx(ToPx(profile.Ring.Thickness))) : 0d;
        var ringRadius = ringWanted ? Math.Max(1d, SnapPx(ToPx(profile.Ring.Radius))) : 0d;

        // ---- chấm giữa ----
        var dotWanted = (profile.CenterDot.Enabled || shape is CrosshairShape.Dot or CrosshairShape.CircleDot)
                        && profile.CenterDot.Size > 0;

        // Chấm giữa gần như luôn nằm trong khoảng 1–4 px, đúng vùng mà khử răng cưa phá hoại
        // nhiều nhất: ở DPI 125% một chấm 2 DIP thành đường tròn đường kính 2.5 px và
        // rasterize ra một vệt xám mờ. Quy ĐƯỜNG KÍNH về số nguyên pixel rồi căn nửa pixel
        // theo đúng luật đã dùng cho nhánh.
        var dotDiameter = dotWanted
            ? Math.Max(snap ? 1d : 0.5d, SnapPx(ToPx(profile.CenterDot.Size)))
            : 0d;

        // ---- vùng bao ----
        var reach = 0d;
        if (inner.Show) reach = Math.Max(reach, inner.ReachPx);
        if (outer.Show) reach = Math.Max(reach, outer.ReachPx);
        if (ringWanted)
        {
            // Khung vuông vươn xa nhất ở góc, không phải ở cạnh.
            var ringReach = ringRadius + (ringCoreT / 2d);
            reach = Math.Max(reach, shape == CrosshairShape.Square ? ringReach * Math.Sqrt(2d) : ringReach);
        }

        if (dotWanted) reach = Math.Max(reach, dotDiameter / 2d);

        reach += outlinePx;

        // Hình xoay quét ra tới đường chéo của vùng bao gốc.
        if (Math.Abs(profile.Rotation) > 0.01d) reach *= Math.Sqrt(2d);

        var maxExtentPx = Math.Max(8d, options.MaxExtent * dpi);
        var extentPx = Math.Ceiling(Math.Clamp(reach + PaddingPx, 4d, maxExtentPx));

        return new RenderPlan
        {
            Dpi = dpi,
            ExtentPx = extentPx,

            ArmsDiagonal = shape == CrosshairShape.XShape,
            InnerLines = inner,
            OuterLines = outer,

            ShowRing = ringWanted,
            RingIsSquare = shape == CrosshairShape.Square,
            RingFilled = profile.Ring.Filled,
            RingRadiusPx = ringRadius,
            RingCoreThicknessPx = ringCoreT,
            RingAlign = AlignFor(ringCoreT, snap),

            ShowDot = dotWanted,
            DotDiameterPx = dotDiameter,
            DotAlign = AlignFor(dotDiameter, snap),
            DotAsRectangle = snap && dotDiameter <= 3d,

            OutlineThicknessPx = outlinePx,
            OutlineColor = profile.Outline.Color,
            OutlineOpacity = Clamp01(profile.Outline.Opacity),

            // Màu ghi đè (khi bắn) áp cho CẢ chấm giữa có màu riêng: chỉ đổi phần nhánh mà chấm vẫn
            // giữ màu cũ thì trông như tính năng chạy nửa vời.
            CoreColor = options.ColorOverride ?? profile.Color,
            DotColor = options.ColorOverride
                ?? (profile.CenterDot.UseProfileColor ? profile.Color : profile.CenterDot.Color),
            DotOpacity = Clamp01(profile.CenterDot.Opacity),
        };
    }

    /// <summary>
    /// Quy đổi một lớp nhánh sang device pixel.
    /// </summary>
    /// <remarks>
    /// Điểm đầu và điểm cuối đều đo từ TÂM rồi mới làm tròn, chứ không làm tròn độ dài rồi cộng
    /// vào điểm đầu. Làm tròn riêng hai số rồi cộng sẽ cộng dồn sai số, và ở DPI lẻ (125%, 175%)
    /// vạch có thể dài hơn hay ngắn hơn một pixel tuỳ vị trí — hai lớp nhánh sẽ lệch nhau rõ rệt.
    /// </remarks>
    private static LineLayerPlan PlanLineLayer(
        LineLayerSettings layer,
        bool lineShape,
        bool diagonal,
        bool hideTop,
        Func<double, double> toPx,
        Func<double, double> snapPx,
        bool snap)
    {
        if (!lineShape || !layer.Enabled || layer.Thickness <= 0 || layer.Opacity <= 0) return default;

        // Hình X không có vạch dọc, nên độ dài dọc riêng không áp dụng.
        var horizontalLength = layer.Length;
        var verticalLength = diagonal ? layer.Length : layer.EffectiveVerticalLength;

        // Trục nào dài 0 thì trục đó không có vạch. Đây là cách Valorant biểu diễn crosshair chỉ
        // có vạch ngang (dọc dài 0) hay chỉ có vạch dọc (ngang dài 0).
        var showLeft = layer.ShowLeft && horizontalLength > 0;
        var showRight = layer.ShowRight && horizontalLength > 0;
        var showTop = layer.ShowTop && !hideTop && verticalLength > 0;
        var showBottom = layer.ShowBottom && verticalLength > 0;

        if (!(showLeft || showRight || showTop || showBottom)) return default;

        var thickness = Math.Max(snap ? 1d : 0.1d, snapPx(toPx(layer.Thickness)));
        var start = Math.Max(0d, snapPx(toPx(layer.Offset)));

        double End(double length)
        {
            if (length <= 0) return start;
            var end = snapPx(toPx(layer.Offset + length));
            return end <= start ? start + 1d : end;
        }

        var horizontalEnd = End(horizontalLength);
        var verticalEnd = End(verticalLength);

        var farthest = Math.Max(
            showLeft || showRight ? horizontalEnd : 0d,
            showTop || showBottom ? verticalEnd : 0d);

        return new LineLayerPlan
        {
            Show = true,
            StartPx = start,
            EndPx = horizontalEnd,
            VerticalEndPx = verticalEnd,
            ReachPx = farthest + (thickness / 2d),
            CoreThicknessPx = thickness,
            Align = AlignFor(thickness, snap),
            Opacity = Clamp01(layer.Opacity),
            ShowTop = showTop,
            ShowBottom = showBottom,
            ShowLeft = showLeft,
            ShowRight = showRight,
            RoundedCaps = layer.RoundedCaps,
        };
    }

    /// <summary>
    /// Kế hoạch vẽ cho chế độ ảnh.
    /// </summary>
    /// <remarks>
    /// Kích thước tính theo PIXEL của ảnh, không theo <c>BitmapSource.Width</c>. <c>Width</c> đổi
    /// theo DPI ghi trong file: một PNG lưu ở 72 DPI sẽ bị WPF phóng lên 133% dù người dùng để tỉ
    /// lệ 1, và phóng lẻ như vậy là nguồn gốc của ảnh nhoè.
    ///
    /// <para>
    /// Khi không xoay, kích thước và mép trái/trên được làm tròn về số nguyên device pixel. Tâm
    /// cửa sổ luôn nằm trên biên pixel, nên ảnh rộng lẻ pixel mà đặt ở -w/2 sẽ lệch nửa pixel và
    /// bị nội suy nhoè toàn bộ.
    /// </para>
    /// </remarks>
    private RenderPlan CreateImagePlan(CrosshairProfile profile, double dpi, CrosshairRenderOptions options)
    {
        var (image, animation) = LoadImage(ResolveImagePath(profile.Image.FilePath));
        var rotated = Math.Abs(profile.Rotation) > 0.01d;
        var snap = options.SnapToPixels && !rotated;

        // Số device pixel cho mỗi pixel ảnh.
        var factor = Math.Clamp(profile.Image.Scale, 0.05d, 20d) * dpi;

        var widthPx = image is null ? 0d : image.PixelWidth * factor;
        var heightPx = image is null ? 0d : image.PixelHeight * factor;

        if (snap && image is not null)
        {
            widthPx = Math.Max(1d, Math.Round(widthPx));
            heightPx = Math.Max(1d, Math.Round(heightPx));
        }

        var left = snap ? -Math.Floor(widthPx / 2d) : -widthPx / 2d;
        var top = snap ? -Math.Floor(heightPx / 2d) : -heightPx / 2d;

        var integerFactor = factor >= 1d && Math.Abs(factor - Math.Round(factor)) < 0.001d;

        var reach = Math.Max(Math.Max(-left, widthPx + left), Math.Max(-top, heightPx + top));
        if (rotated) reach *= Math.Sqrt(2d);

        var maxExtentPx = Math.Max(8d, options.MaxExtent * dpi);
        var extentPx = Math.Ceiling(Math.Clamp(reach + PaddingPx, 4d, maxExtentPx));

        return new RenderPlan
        {
            Dpi = dpi,
            ExtentPx = extentPx,
            Image = image,
            ImageWidthPx = widthPx,
            ImageHeightPx = heightPx,
            ImageLeftPx = left,
            ImageTopPx = top,
            ImageOpacity = Clamp01(profile.Image.Opacity),
            Animation = animation,
            ImageScalingMode = integerFactor && !rotated
                ? BitmapScalingMode.NearestNeighbor
                : BitmapScalingMode.HighQuality,
            CoreColor = profile.Color,
        };
    }

    private string? ResolveImagePath(string? storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath)) return null;
        if (_images is not null) return _images.Resolve(storedPath);

        return Path.IsPathRooted(storedPath) ? storedPath : null;
    }

    /// <summary>
    /// Nét dày lẻ pixel phải đặt tâm ở giữa pixel mới sắc; nét dày chẵn thì đặt trên biên pixel.
    /// </summary>
    private static double AlignFor(double thicknessPx, bool snap)
    {
        if (!snap || thicknessPx <= 0) return 0d;
        return (int)Math.Round(thicknessPx) % 2 != 0 ? 0.5d : 0d;
    }

    // ------------------------------------------------------------------ vẽ

    /// <summary>
    /// Vẽ bốn vạch của một lớp nhánh.
    /// </summary>
    /// <remarks>
    /// Độ mờ của lớp áp bằng <see cref="DrawingContext.PushOpacity(double)"/> cho CẢ lớp, còn
    /// từng vạch vẽ bằng bút đục. Nếu gán độ mờ vào bút của từng vạch, chỗ vạch ngang và vạch
    /// dọc chồng lên nhau gần tâm (khoảng cách nhỏ hơn nửa độ dày) sẽ bị pha màu hai lần và hiện
    /// thành một ô vuông đậm hơn. Gộp thành một nhóm thì cả lớp hoà trộn với nền đúng một lần.
    ///
    /// <para>
    /// Viền của lớp nhân thêm độ mờ của chính lớp đó: nhánh ngoài mờ 35% mà viền vẫn đậm 100%
    /// thì trên màn hình chỉ thấy bốn khung đen.
    /// </para>
    /// </remarks>
    private static void DrawLineLayer(DrawingContext dc, RenderPlan plan, LineLayerPlan layer, bool outlinePass)
    {
        if (!layer.Show) return;

        var opacity = outlinePass ? plan.OutlineOpacity * layer.Opacity : layer.Opacity;
        if (opacity <= 0d) return;

        var thickness = outlinePass
            ? layer.CoreThicknessPx + (plan.OutlineThicknessPx * 2d)
            : layer.CoreThicknessPx;

        var pen = CreatePen(
            outlinePass ? plan.OutlineColor : plan.CoreColor,
            1d,
            plan.ToDip(thickness),
            layer.RoundedCaps);

        var faded = opacity < 0.999d;
        if (faded) dc.PushOpacity(opacity);

        if (plan.ArmsDiagonal)
            DrawDiagonalLines(dc, plan, layer, pen);
        else
            DrawStraightLines(dc, plan, layer, pen);

        if (faded) dc.Pop();
    }

    private static void DrawStraightLines(DrawingContext dc, RenderPlan plan, LineLayerPlan layer, Pen pen)
    {
        var align = plan.ToDip(layer.Align);
        var start = plan.ToDip(layer.StartPx);
        var end = plan.ToDip(layer.EndPx);
        var verticalEnd = plan.ToDip(layer.VerticalEndPx);

        if (layer.ShowTop) dc.DrawLine(pen, new Point(align, -start), new Point(align, -verticalEnd));
        if (layer.ShowBottom) dc.DrawLine(pen, new Point(align, start), new Point(align, verticalEnd));
        if (layer.ShowLeft) dc.DrawLine(pen, new Point(-start, align), new Point(-end, align));
        if (layer.ShowRight) dc.DrawLine(pen, new Point(start, align), new Point(end, align));
    }

    private static void DrawDiagonalLines(DrawingContext dc, RenderPlan plan, LineLayerPlan layer, Pen pen)
    {
        // Nhánh chéo không thể bám lưới pixel, nên bỏ qua Align hoàn toàn.
        const double Diag = 0.70710678118654752d; // cos(45°)

        void Diagonal(bool show, int sx, int sy)
        {
            if (!show) return;
            dc.DrawLine(
                pen,
                new Point(plan.ToDip(layer.StartPx * Diag * sx), plan.ToDip(layer.StartPx * Diag * sy)),
                new Point(plan.ToDip(layer.EndPx * Diag * sx), plan.ToDip(layer.EndPx * Diag * sy)));
        }

        // Với hình X, bốn cờ hướng được ánh xạ theo chiều kim đồng hồ từ góc trên-trái.
        Diagonal(layer.ShowTop, -1, -1);
        Diagonal(layer.ShowRight, +1, -1);
        Diagonal(layer.ShowBottom, +1, +1);
        Diagonal(layer.ShowLeft, -1, +1);
    }

    private static void DrawRing(DrawingContext dc, RenderPlan plan, bool outlinePass)
    {
        if (!plan.ShowRing) return;

        var align = plan.ToDip(plan.RingAlign);
        var center = new Point(align, align);

        if (plan.RingFilled)
        {
            // Vòng tô đặc: viền là một hình lớn hơn vẽ bên dưới, không phải một nét bao.
            var radius = plan.ToDip(plan.RingRadiusPx + (outlinePass ? plan.OutlineThicknessPx : 0d));
            var brush = CreateBrush(
                outlinePass ? plan.OutlineColor : plan.CoreColor,
                outlinePass ? plan.OutlineOpacity : 1d);

            if (plan.RingIsSquare)
                dc.DrawRectangle(brush, null, RectAround(center, radius));
            else
                dc.DrawEllipse(brush, null, center, radius, radius);

            return;
        }

        var thickness = outlinePass
            ? plan.RingCoreThicknessPx + (plan.OutlineThicknessPx * 2d)
            : plan.RingCoreThicknessPx;

        var pen = CreatePen(
            outlinePass ? plan.OutlineColor : plan.CoreColor,
            outlinePass ? plan.OutlineOpacity : 1d,
            plan.ToDip(thickness),
            roundedCaps: false);

        var r = plan.ToDip(plan.RingRadiusPx);

        if (plan.RingIsSquare)
            dc.DrawRectangle(null, pen, RectAround(center, r));
        else
            dc.DrawEllipse(null, pen, center, r, r);
    }

    private static void DrawDot(DrawingContext dc, RenderPlan plan, bool outlinePass)
    {
        if (!plan.ShowDot) return;

        // Viền cộng vào ĐƯỜNG KÍNH (mỗi bên một lần, nên 2×) — tính chẵn/lẻ được giữ nguyên,
        // hai lượt vẽ dùng chung một giá trị căn và chấm luôn đồng tâm với viền của nó.
        var diameter = plan.DotDiameterPx + (outlinePass ? plan.OutlineThicknessPx * 2d : 0d);
        if (diameter <= 0) return;

        var radius = plan.ToDip(diameter / 2d);
        var align = plan.ToDip(plan.DotAlign);
        var center = new Point(align, align);

        var brush = CreateBrush(
            outlinePass ? plan.OutlineColor : plan.DotColor,
            outlinePass ? plan.OutlineOpacity : plan.DotOpacity);

        if (plan.DotAsRectangle)
            dc.DrawRectangle(brush, null, RectAround(center, radius));
        else
            dc.DrawEllipse(brush, null, center, radius, radius);
    }

    private static void DrawImage(DrawingContext dc, RenderPlan plan)
    {
        if (plan.Image is null || plan.ImageWidthPx <= 0 || plan.ImageHeightPx <= 0) return;

        var rect = new Rect(
            plan.ToDip(plan.ImageLeftPx),
            plan.ToDip(plan.ImageTopPx),
            plan.ToDip(plan.ImageWidthPx),
            plan.ToDip(plan.ImageHeightPx));

        // Nhóm con riêng để gắn chế độ co giãn: RenderOptions đặt trên DrawingGroup được WPF áp cho
        // mọi ảnh bên trong, bất kể element chứa nó đặt gì. Độ mờ gắn cùng nhóm luôn.
        //
        // Nền trong suốt của PNG được giữ nguyên: ảnh vẽ thẳng lên cửa sổ layered có alpha, không
        // qua nền trung gian nào. Ảnh bitmap không vẽ viền được — đó là giới hạn có chủ ý.
        var layer = new DrawingGroup { Opacity = plan.ImageOpacity };
        RenderOptions.SetBitmapScalingMode(layer, plan.ImageScalingMode);

        var image = new ImageDrawing(plan.Image, rect);

        if (plan.Animation is { } animation)
            DrawingAnimations.Start(image, animation);

        layer.Children.Add(image);
        dc.DrawDrawing(layer);
    }

    // ------------------------------------------------------------------ tiện ích

    private static Rect RectAround(Point center, double halfSide) =>
        new(center.X - halfSide, center.Y - halfSide, halfSide * 2d, halfSide * 2d);

    private static SolidColorBrush CreateBrush(Color color, double opacity)
    {
        var brush = new SolidColorBrush(color) { Opacity = Clamp01(opacity) };
        brush.Freeze();
        return brush;
    }

    private static Pen CreatePen(Color color, double opacity, double thicknessDip, bool roundedCaps)
    {
        var cap = roundedCaps ? PenLineCap.Round : PenLineCap.Flat;
        var pen = new Pen(CreateBrush(color, opacity), Math.Max(0.1d, thicknessDip))
        {
            StartLineCap = cap,
            EndLineCap = cap,
            LineJoin = PenLineJoin.Miter,
        };
        pen.Freeze();
        return pen;
    }

    private static double Clamp01(double value) =>
        double.IsNaN(value) ? 1d : Math.Clamp(value, 0d, 1d);

    private (BitmapSource? Source, AnimatedImage? Animation) LoadImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return (null, null);

        DateTime stamp;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                _logger.LogWarning("Không tìm thấy ảnh crosshair: {Path}", path);
                return (null, null);
            }

            stamp = info.LastWriteTimeUtc;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.LogWarning(ex, "Không đọc được thông tin file ảnh: {Path}", path);
            return (null, null);
        }

        if (_imageCache.TryGet(path, stamp, out var cachedSource, out var cachedAnimation))
            return (cachedSource, cachedAnimation);

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);

            // OnLoad: đọc hết vào bộ nhớ rồi nhả file, để người dùng vẫn sửa/xoá được ảnh gốc.
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.EndInit();
            bitmap.Freeze();

            // GIF: giải mã đủ khung. Không phải GIF động, hoặc quá lớn để giữ mọi khung trong bộ
            // nhớ, thì trả null và ảnh hiện như ảnh tĩnh (khung đầu).
            var animation = path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)
                ? GifAnimation.TryLoad(path)
                : null;

            if (animation is null && path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                _logger.LogDebug("GIF chỉ hiện khung đầu (một khung, hoặc vượt giới hạn bộ nhớ): {Path}", path);

            // Khung đầu ĐÃ GHÉP có đúng cỡ canvas; khung thô của BitmapImage có thể nhỏ hơn.
            var source = animation?.Frames[0] ?? bitmap;

            _imageCache.Set(path, stamp, source, animation);
            return (source, animation);
        }
        catch (Exception ex)
        {
            // File hỏng hoặc định dạng không hỗ trợ — ghi nhớ thất bại để khỏi thử lại mỗi lần vẽ.
            _logger.LogWarning(ex, "Không tải được ảnh crosshair: {Path}", path);
            _imageCache.Set(path, stamp, null, null);
            return (null, null);
        }
    }

}

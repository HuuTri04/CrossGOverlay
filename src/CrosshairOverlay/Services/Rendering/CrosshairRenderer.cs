using System.Collections.Concurrent;
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

    /// <summary>
    /// Cache ảnh tuỳ chỉnh. Thiếu nó thì mỗi lần vẽ lại là một lần đọc đĩa — không chấp nhận
    /// được khi người dùng đang kéo slider.
    /// </summary>
    private readonly ConcurrentDictionary<string, CachedImage> _imageCache = new(StringComparer.OrdinalIgnoreCase);

    public CrosshairRenderer(ILogger<CrosshairRenderer> logger) => _logger = logger;

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

        // Hình đã xoay: mọi nét đều thành đường xiên.
        if (Math.Abs(profile.Rotation) > 0.01d) return false;

        // Nhánh chéo, đường tròn và ảnh bitmap đều cần khử răng cưa.
        if (profile.Shape is CrosshairShape.XShape or CrosshairShape.Circle
            or CrosshairShape.CircleDot or CrosshairShape.CustomImage) return false;

        // Vòng tròn bật kèm một hình khác cũng vậy; riêng khung vuông thì không sao.
        if (profile.Ring.Enabled && profile.Shape != CrosshairShape.Square) return false;

        // Đầu nhánh bo tròn là cung tròn.
        if (profile.Lines.RoundedCaps) return false;

        // Chấm giữa lớn được vẽ bằng hình tròn (ngưỡng 3 px trong CreatePlan); chấm nhỏ vẽ
        // bằng hình vuông nên vẫn sắc.
        if (profile.CenterDot.Enabled && profile.CenterDot.Size * profile.Scale > 3d) return false;

        return true;
    }

    public Drawing Build(CrosshairProfile profile, CrosshairRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var plan = CreatePlan(profile, options);
        var group = new DrawingGroup { Opacity = Clamp01(profile.Opacity) };

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
                // Vẽ TOÀN BỘ viền trước, rồi mới tới lõi. Nếu vẽ xen kẽ theo từng phần tử,
                // viền của phần tử sau sẽ đè lên lõi của phần tử trước.
                if (plan.HasOutline)
                {
                    DrawArms(dc, plan, outlinePass: true);
                    DrawRing(dc, plan, outlinePass: true);
                    DrawDot(dc, plan, outlinePass: true);
                }

                DrawArms(dc, plan, outlinePass: false);
                DrawRing(dc, plan, outlinePass: false);
                DrawDot(dc, plan, outlinePass: false);
            }
        }

        group.Freeze();
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
        if (profile.Shape == CrosshairShape.CustomImage)
            return CreateImagePlan(profile, dpi, scale, options);

        var shape = profile.Shape;

        // Viền: quy về số nguyên pixel TRƯỚC. Nhờ vậy bề dày tổng (lõi + 2×viền) luôn cùng
        // tính chẵn/lẻ với lõi, nên hai lượt vẽ dùng chung một giá trị căn nửa pixel và
        // luôn đồng tâm.
        var outlinePx = profile.Outline.Enabled && profile.Outline.Thickness > 0
            ? Math.Max(1d, SnapPx(ToPx(profile.Outline.Thickness)))
            : 0d;

        // ---- nhánh ----
        var armsWanted = profile.Lines.Enabled
            && shape is CrosshairShape.Cross or CrosshairShape.TShape or CrosshairShape.XShape
            && profile.Lines.Thickness > 0
            && profile.Lines.Length > 0;

        var armCoreT = armsWanted ? Math.Max(snap ? 1d : 0.1d, SnapPx(ToPx(profile.Lines.Thickness))) : 0d;
        var armInner = armsWanted ? Math.Max(0d, SnapPx(ToPx(profile.Lines.Gap))) : 0d;
        var armOuter = armsWanted ? SnapPx(ToPx(profile.Lines.Gap + profile.Lines.Length)) : 0d;
        if (armOuter <= armInner) armOuter = armInner + (armsWanted ? 1d : 0d);

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
        if (armsWanted) reach = Math.Max(reach, armOuter + (armCoreT / 2d));
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

            ShowArms = armsWanted,
            ArmsDiagonal = shape == CrosshairShape.XShape,
            ArmInnerPx = armInner,
            ArmOuterPx = armOuter,
            ArmCoreThicknessPx = armCoreT,
            ArmAlign = AlignFor(armCoreT, snap),
            // TShape = bỏ nhánh trên, bất kể cờ ShowTop của người dùng.
            ShowTop = profile.Lines.ShowTop && shape != CrosshairShape.TShape,
            ShowBottom = profile.Lines.ShowBottom,
            ShowLeft = profile.Lines.ShowLeft,
            ShowRight = profile.Lines.ShowRight,
            RoundedCaps = profile.Lines.RoundedCaps,

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

            CoreColor = profile.Color,
            DotColor = profile.CenterDot.UseProfileColor ? profile.Color : profile.CenterDot.Color,
            DotOpacity = Clamp01(profile.CenterDot.Opacity),
        };
    }

    private RenderPlan CreateImagePlan(
        CrosshairProfile profile, double dpi, double scale, CrosshairRenderOptions options)
    {
        var image = LoadImage(profile.Image.FilePath);

        var imageScale = Math.Clamp(profile.Image.Scale, 0.05d, 20d) * scale;
        var widthPx = image is null ? 0d : image.Width * imageScale * dpi;
        var heightPx = image is null ? 0d : image.Height * imageScale * dpi;

        var reach = Math.Max(widthPx, heightPx) / 2d;
        if (Math.Abs(profile.Rotation) > 0.01d) reach *= Math.Sqrt(2d);

        var maxExtentPx = Math.Max(8d, options.MaxExtent * dpi);
        var extentPx = Math.Ceiling(Math.Clamp(reach + PaddingPx, 4d, maxExtentPx));

        return new RenderPlan
        {
            Dpi = dpi,
            ExtentPx = extentPx,
            Image = image,
            ImageWidthPx = widthPx,
            ImageHeightPx = heightPx,
            ImageOpacity = Clamp01(profile.Image.Opacity),
            CoreColor = profile.Color,
        };
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

    private static void DrawArms(DrawingContext dc, RenderPlan plan, bool outlinePass)
    {
        if (!plan.ShowArms) return;

        var thickness = outlinePass
            ? plan.ArmCoreThicknessPx + (plan.OutlineThicknessPx * 2d)
            : plan.ArmCoreThicknessPx;

        var pen = CreatePen(
            outlinePass ? plan.OutlineColor : plan.CoreColor,
            outlinePass ? plan.OutlineOpacity : 1d,
            plan.ToDip(thickness),
            plan.RoundedCaps);

        var inner = plan.ArmInnerPx;
        var outer = plan.ArmOuterPx;

        if (plan.ArmsDiagonal)
        {
            // Nhánh chéo không thể bám lưới pixel, nên bỏ qua ArmAlign hoàn toàn.
            const double Diag = 0.70710678118654752d; // cos(45°)

            void Diagonal(bool show, int sx, int sy)
            {
                if (!show) return;
                dc.DrawLine(
                    pen,
                    new Point(plan.ToDip(inner * Diag * sx), plan.ToDip(inner * Diag * sy)),
                    new Point(plan.ToDip(outer * Diag * sx), plan.ToDip(outer * Diag * sy)));
            }

            // Với hình X, bốn cờ hướng được ánh xạ theo chiều kim đồng hồ từ góc trên-trái.
            Diagonal(plan.ShowTop, -1, -1);
            Diagonal(plan.ShowRight, +1, -1);
            Diagonal(plan.ShowBottom, +1, +1);
            Diagonal(plan.ShowLeft, -1, +1);
            return;
        }

        var align = plan.ToDip(plan.ArmAlign);
        var innerDip = plan.ToDip(inner);
        var outerDip = plan.ToDip(outer);

        if (plan.ShowTop) dc.DrawLine(pen, new Point(align, -innerDip), new Point(align, -outerDip));
        if (plan.ShowBottom) dc.DrawLine(pen, new Point(align, innerDip), new Point(align, outerDip));
        if (plan.ShowLeft) dc.DrawLine(pen, new Point(-innerDip, align), new Point(-outerDip, align));
        if (plan.ShowRight) dc.DrawLine(pen, new Point(innerDip, align), new Point(outerDip, align));
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

        var width = plan.ToDip(plan.ImageWidthPx);
        var height = plan.ToDip(plan.ImageHeightPx);
        var rect = new Rect(-width / 2d, -height / 2d, width, height);

        // Ảnh bitmap không vẽ viền được — đó là giới hạn có chủ ý, tài liệu ghi rõ.
        if (plan.ImageOpacity >= 0.999d)
        {
            dc.DrawImage(plan.Image, rect);
            return;
        }

        dc.PushOpacity(plan.ImageOpacity);
        dc.DrawImage(plan.Image, rect);
        dc.Pop();
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

    private BitmapSource? LoadImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        DateTime stamp;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                _logger.LogWarning("Không tìm thấy ảnh crosshair: {Path}", path);
                return null;
            }

            stamp = info.LastWriteTimeUtc;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.LogWarning(ex, "Không đọc được thông tin file ảnh: {Path}", path);
            return null;
        }

        if (_imageCache.TryGetValue(path, out var cached) && cached.Stamp == stamp)
            return cached.Source;

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

            _imageCache[path] = new CachedImage(bitmap, stamp);
            return bitmap;
        }
        catch (Exception ex)
        {
            // File hỏng hoặc định dạng không hỗ trợ — ghi nhớ thất bại để khỏi thử lại mỗi lần vẽ.
            _logger.LogWarning(ex, "Không tải được ảnh crosshair: {Path}", path);
            _imageCache[path] = new CachedImage(null, stamp);
            return null;
        }
    }

    private readonly record struct CachedImage(BitmapSource? Source, DateTime Stamp);
}

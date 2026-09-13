using System.Windows.Media;
using CrosshairOverlay.Core.Models;

namespace CrosshairOverlay.Services.Rendering;

/// <summary>
/// Kết quả quy đổi một <see cref="CrosshairProfile"/> sang toạ độ DEVICE PIXEL đã bám lưới.
/// </summary>
/// <remarks>
/// Tồn tại để <see cref="CrosshairRenderer.Measure"/> và <see cref="CrosshairRenderer.Build"/>
/// dùng chung đúng một phép tính — nếu tách đôi, vùng bao và hình vẽ sẽ trôi lệch nhau và
/// crosshair bị cắt mép ở một số cấu hình.
///
/// <para>
/// Vì sao làm việc bằng device pixel: một nét dày lẻ pixel (1, 3, 5…) chỉ sắc khi tâm nét nằm
/// giữa pixel, còn nét dày chẵn chỉ sắc khi tâm nét nằm trên biên pixel. Quy về số nguyên
/// pixel rồi cộng <see cref="LineLayerPlan.Align"/> nửa pixel khi cần là cách duy nhất bảo đảm điều đó ở
/// mọi mức DPI scaling.
/// </para>
/// </remarks>
internal sealed class RenderPlan
{
    /// <summary>Hệ số DPI đang dùng để quy đổi. Luôn &gt; 0.</summary>
    public required double Dpi { get; init; }

    /// <summary>Nửa cạnh vùng bao, device pixel. Luôn là SỐ NGUYÊN nên bề rộng tổng luôn chẵn.</summary>
    public required double ExtentPx { get; init; }

    // ---- nhánh ----

    /// <summary>Hình X: cả hai lớp nhánh cùng vẽ theo đường chéo.</summary>
    public bool ArmsDiagonal { get; init; }

    public LineLayerPlan InnerLines { get; init; }
    public LineLayerPlan OuterLines { get; init; }

    // ---- vòng / khung ----
    public bool ShowRing { get; init; }
    public bool RingIsSquare { get; init; }
    public bool RingFilled { get; init; }
    public double RingRadiusPx { get; init; }
    public double RingCoreThicknessPx { get; init; }
    public double RingAlign { get; init; }

    // ---- chấm giữa ----
    public bool ShowDot { get; init; }

    /// <summary>Đường KÍNH chấm giữa, device pixel. Đã quy về số nguyên khi bật bám lưới.</summary>
    public double DotDiameterPx { get; init; }

    public double DotAlign { get; init; }

    /// <summary>
    /// Vẽ chấm bằng hình vuông thay vì hình tròn. Bật ở đường kính nhỏ, nơi mắt không phân
    /// biệt được hai hình nhưng hình vuông thì rasterize thành pixel đặc còn hình tròn bị nhoè.
    /// </summary>
    public bool DotAsRectangle { get; init; }

    // ---- ảnh tuỳ chỉnh ----
    public ImageSource? Image { get; init; }
    public double ImageWidthPx { get; init; }
    public double ImageHeightPx { get; init; }
    public double ImageOpacity { get; init; }

    /// <summary>Mép trái/trên của ảnh so với tâm, device pixel — đã làm tròn khi bám lưới.</summary>
    public double ImageLeftPx { get; init; }
    public double ImageTopPx { get; init; }

    /// <summary>
    /// <see cref="BitmapScalingMode.NearestNeighbor"/> khi phóng đúng bội số nguyên (mỗi pixel
    /// ảnh thành một khối pixel vuông, sắc tuyệt đối); <see cref="BitmapScalingMode.HighQuality"/>
    /// cho mọi tỉ lệ khác, nơi láng giềng gần nhất sẽ làm ảnh răng cưa không đều.
    /// </summary>
    public BitmapScalingMode ImageScalingMode { get; init; }

    /// <summary>Các khung của GIF động; null với ảnh tĩnh.</summary>
    public AnimatedImage? Animation { get; init; }

    // ---- viền ----
    /// <summary>Bề dày viền mỗi bên, device pixel. 0 khi tắt viền.</summary>
    public double OutlineThicknessPx { get; init; }
    public Color OutlineColor { get; init; }
    public double OutlineOpacity { get; init; }

    // ---- màu ----
    public Color CoreColor { get; init; }
    public Color DotColor { get; init; }
    public double DotOpacity { get; init; }

    public bool HasOutline => OutlineThicknessPx > 0;

    /// <summary>Đổi device pixel về DIP để đưa vào <see cref="DrawingContext"/>.</summary>
    public double ToDip(double devicePx) => devicePx / Dpi;
}

/// <summary>Một lớp nhánh đã quy đổi sang device pixel và bám lưới.</summary>
/// <remarks>
/// Hai lớp được quy đổi ĐỘC LẬP: mỗi lớp tự quyết định căn nửa pixel theo độ dày của chính nó.
/// Dùng chung một giá trị căn thì lớp có độ dày lẻ và lớp có độ dày chẵn sẽ có một lớp bị nhoè.
/// </remarks>
internal readonly record struct LineLayerPlan
{
    public bool Show { get; init; }

    /// <summary>Khoảng cách từ TÂM tới điểm bắt đầu vạch, device pixel.</summary>
    public double StartPx { get; init; }

    /// <summary>Khoảng cách từ TÂM tới điểm kết thúc vạch NGANG (và vạch chéo), device pixel.</summary>
    public double EndPx { get; init; }

    /// <summary>Khoảng cách từ TÂM tới điểm kết thúc vạch DỌC, device pixel.</summary>
    public double VerticalEndPx { get; init; }

    /// <summary>Nửa cạnh vùng bao cần cho lớp này, device pixel — đã tính nửa độ dày.</summary>
    public double ReachPx { get; init; }

    public double CoreThicknessPx { get; init; }

    /// <summary>Lệch nửa pixel theo trục vuông góc khi nét dày lẻ pixel. 0 hoặc 0.5.</summary>
    public double Align { get; init; }

    public double Opacity { get; init; }

    public bool ShowTop { get; init; }
    public bool ShowBottom { get; init; }
    public bool ShowLeft { get; init; }
    public bool ShowRight { get; init; }
    public bool RoundedCaps { get; init; }
}

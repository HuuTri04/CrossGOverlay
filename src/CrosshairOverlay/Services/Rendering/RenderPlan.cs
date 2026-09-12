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
/// pixel rồi cộng <see cref="ArmAlign"/> nửa pixel khi cần là cách duy nhất bảo đảm điều đó ở
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
    public bool ShowArms { get; init; }
    public bool ArmsDiagonal { get; init; }
    public double ArmInnerPx { get; init; }
    public double ArmOuterPx { get; init; }
    public double ArmCoreThicknessPx { get; init; }

    /// <summary>Lệch nửa pixel theo trục vuông góc khi nét dày lẻ pixel. 0 hoặc 0.5.</summary>
    public double ArmAlign { get; init; }

    public bool ShowTop { get; init; }
    public bool ShowBottom { get; init; }
    public bool ShowLeft { get; init; }
    public bool ShowRight { get; init; }
    public bool RoundedCaps { get; init; }

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

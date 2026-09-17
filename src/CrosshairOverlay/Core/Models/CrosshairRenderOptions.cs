namespace CrosshairOverlay.Core.Models;

/// <summary>
/// Ngữ cảnh vẽ do lớp overlay cung cấp cho renderer. Tách khỏi <see cref="CrosshairProfile"/>
/// vì đây là thuộc tính của thiết bị hiển thị, không phải của preset.
/// </summary>
/// <param name="DpiScale">Hệ số DPI của màn hình đích (1.0 / 1.25 / 1.5 / 2.0).</param>
/// <param name="SnapToPixels">
/// Làm tròn toạ độ về biên pixel vật lý. Bật khi crosshair không xoay để nét sắc, tắt khi
/// <see cref="CrosshairProfile.Rotation"/> khác 0 vì snapping sẽ làm méo hình xoay.
/// </param>
/// <param name="MaxExtent">Giới hạn nửa-cạnh vùng vẽ (DIP), chống preset lỗi tạo hình khổng lồ.</param>
/// <param name="ColorOverride">
/// Vẽ bằng màu này thay cho màu của preset (vd màu khi bắn). Chỉ ảnh hưởng hình vẽ lần này, không đụng
/// tới preset. Viền giữ màu riêng để tâm vẫn nổi trên nền; tâm ngắm ảnh không có màu nên không đổi.
/// </param>
public readonly record struct CrosshairRenderOptions(
    double DpiScale = 1d,
    bool SnapToPixels = true,
    double MaxExtent = 2000d,
    System.Windows.Media.Color? ColorOverride = null)
{
    public static CrosshairRenderOptions Default { get; } = new();
}

using System.Windows.Media;
using CrosshairOverlay.Core.Models;

namespace CrosshairOverlay.Services.Import;

/// <summary>
/// Đổi crosshair đọc từ share code của game sang <see cref="CrosshairProfile"/> của ứng dụng.
/// </summary>
/// <remarks>
/// CẢNH BÁO VỀ ĐỘ CHÍNH XÁC: CS2 và ứng dụng này dùng hệ đơn vị khác nhau, và CS2 còn co giãn
/// crosshair theo độ phân giải lẫn FOV. Cấu trúc (hình dạng, màu, chấm giữa, viền, kiểu chữ T)
/// được chuyển ĐÚNG; còn kích thước chỉ là xấp xỉ theo hệ số tuyến tính bên dưới. Người dùng
/// nên chỉnh lại thanh "Tỉ lệ" sau khi import — giao diện có nhắc điều này.
/// </remarks>
public static class CrosshairCodeConverter
{
    /// <summary>
    /// Một đơn vị của CS2 đổi ra bao nhiêu DIP. Chọn 2.0 để crosshair mặc định của CS2
    /// (size 5) ra độ dài 10 DIP — đúng bằng preset mặc định của ứng dụng.
    /// </summary>
    private const double UnitToDip = 2d;

    /// <summary>
    /// CS2 cho phép gap ÂM (các nhánh lấn vào tâm), còn model của ứng dụng không có khái niệm
    /// đó. Dời gốc lên 4 đơn vị rồi kẹp về 0 là cách xấp xỉ gần nhất.
    /// </summary>
    private const double GapOrigin = 4d;

    public static CrosshairProfile ToProfile(Cs2Crosshair source, string name)
    {
        ArgumentNullException.ThrowIfNull(source);

        var profile = new CrosshairProfile
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Import từ CS2" : name,
            Shape = source.IsTStyle ? CrosshairShape.TShape : CrosshairShape.Cross,
            Color = Color.FromRgb(source.Red, source.Green, source.Blue),

            // CS2 tách riêng alpha của crosshair; ứng dụng gộp vào độ mờ tổng thể.
            Opacity = source.HasAlpha ? Math.Clamp(source.Alpha / 255d, 0.05d, 1d) : 1d,
        };

        profile.Lines = new CrosshairLines
        {
            Enabled = true,
            Length = Math.Max(1d, source.Size * UnitToDip),
            Thickness = Math.Max(1d, source.Thickness * UnitToDip),
            Gap = Math.Max(0d, (source.Gap + GapOrigin) * UnitToDip),

            // Kiểu chữ T bỏ nhánh trên; renderer cũng tự ép điều này theo Shape, đặt ở đây để
            // người dùng mở tab editor ra thấy đúng trạng thái các ô đánh dấu.
            ShowTop = !source.IsTStyle,
        };

        profile.CenterDot = new CenterDotSettings
        {
            Enabled = source.HasCenterDot,
            Size = Math.Max(1d, source.Thickness * UnitToDip),
            UseProfileColor = true,
        };

        profile.Outline = new OutlineSettings
        {
            Enabled = source.HasOutline,
            Thickness = Math.Max(1d, source.OutlineThickness * UnitToDip),
            Color = Colors.Black,
        };

        return profile;
    }

    /// <summary>
    /// Chiều ngược lại: đổi preset của ứng dụng sang giá trị crosshair kiểu Valorant, để
    /// <see cref="ValorantCrosshairCode.Encode"/> dựng thành mã chia sẻ.
    /// </summary>
    /// <remarks>
    /// Chuyển 1:1 vì Valorant dùng đơn vị xấp xỉ pixel. Những thứ Valorant không có — hình
    /// dạng vòng tròn, khung vuông, nhánh chéo, ảnh tuỳ chỉnh, góc xoay — không biểu diễn được
    /// và sẽ mất khi xuất; <see cref="CanExportFaithfully"/> cho biết trước điều đó.
    /// </remarks>
    public static ValorantCrosshair ToValorantCrosshair(CrosshairProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var hasLines = profile.Lines.Enabled
            && profile.Shape is CrosshairShape.Cross or CrosshairShape.TShape or CrosshairShape.XShape;

        return new ValorantCrosshair
        {
            Color = profile.Color,

            HasOutline = profile.Outline.Enabled,
            OutlineThickness = profile.Outline.Thickness,
            OutlineOpacity = profile.Outline.Opacity,

            HasCenterDot = profile.CenterDot.Enabled,
            CenterDotSize = profile.CenterDot.Size,
            CenterDotOpacity = profile.CenterDot.Opacity,

            ShowInnerLines = hasLines,
            InnerLineThickness = profile.Lines.Thickness,
            InnerLineLength = profile.Lines.Length,
            InnerLineOffset = profile.Lines.Gap,
            InnerLineOpacity = profile.Opacity,
        };
    }

    /// <summary>
    /// Preset này có xuất ra mã chia sẻ mà không mất gì không.
    /// </summary>
    /// <remarks>
    /// Định dạng mã chỉ mô tả được chữ thập + chấm giữa + viền. Trả false thì giao diện phải
    /// nói trước cho người dùng biết sẽ mất gì, thay vì đưa ra một mã trông có vẻ đúng.
    /// </remarks>
    public static bool CanExportFaithfully(CrosshairProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.Shape is not (CrosshairShape.Cross or CrosshairShape.Dot)) return false;
        if (profile.Ring.Enabled) return false;
        if (Math.Abs(profile.Rotation) > 0.01d) return false;
        if (Math.Abs(profile.Scale - 1d) > 0.01d) return false;

        return true;
    }

    /// <summary>
    /// Đổi crosshair Valorant sang preset.
    /// </summary>
    /// <remarks>
    /// Khác với CS2, đơn vị của Valorant xấp xỉ PIXEL nên chuyển 1:1, không nhân hệ số.
    /// Ứng dụng chỉ vẽ một lớp nhánh, còn Valorant có cả nhánh trong lẫn nhánh ngoài — chỉ
    /// nhánh trong được chuyển, vì đó là phần quyết định hình dáng crosshair.
    /// </remarks>
    public static CrosshairProfile ToProfile(ValorantCrosshair source, string name)
    {
        ArgumentNullException.ThrowIfNull(source);

        var profile = new CrosshairProfile
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Import từ Valorant" : name,
            Shape = source.ShowInnerLines ? CrosshairShape.Cross : CrosshairShape.Dot,
            Color = source.Color,
            Opacity = Math.Clamp(source.InnerLineOpacity, 0.05d, 1d),
        };

        profile.Lines = new CrosshairLines
        {
            Enabled = source.ShowInnerLines,
            Length = Math.Max(1d, source.InnerLineLength),
            Thickness = Math.Max(1d, source.InnerLineThickness),
            Gap = Math.Max(0d, source.InnerLineOffset),
        };

        profile.CenterDot = new CenterDotSettings
        {
            Enabled = source.HasCenterDot,
            Size = Math.Max(1d, source.CenterDotSize),
            UseProfileColor = true,
            Opacity = Math.Clamp(source.CenterDotOpacity, 0.05d, 1d),
        };

        profile.Outline = new OutlineSettings
        {
            Enabled = source.HasOutline,
            Thickness = Math.Max(1d, source.OutlineThickness),
            Opacity = Math.Clamp(source.OutlineOpacity, 0.05d, 1d),
            Color = Colors.Black,
        };

        return profile;
    }
}

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

        // CS2 chỉ có một lớp nhánh. Nhánh ngoài giữ mặc định của preset, tức là tắt.
        profile.InnerLines = new LineLayerSettings
        {
            Enabled = true,
            Length = Math.Max(1d, source.Size * UnitToDip),
            Thickness = Math.Max(1d, source.Thickness * UnitToDip),
            Offset = Math.Max(0d, (source.Gap + GapOrigin) * UnitToDip),

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
    /// Đổi preset của ứng dụng sang giá trị crosshair của CS2, để
    /// <see cref="Cs2ShareCodeEncoder.Encode"/> dựng thành share code.
    /// </summary>
    /// <remarks>
    /// Đảo lại đúng hệ số của <see cref="ToProfile(Cs2Crosshair, string)"/> nên import rồi export
    /// ra đúng con số ban đầu. CS2 nghèo hơn model của ứng dụng rất nhiều: chỉ có MỘT lớp nhánh,
    /// không có vòng tròn, không xoay, không có độ dài dọc riêng — những thứ đó mất khi xuất, và
    /// <see cref="CanExportToCs2Faithfully"/> báo trước.
    ///
    /// <para>
    /// Mọi giá trị đều bị kẹp vào khoảng CS2 chấp nhận. Kẹp là cố ý: thà ra một crosshair hơi khác
    /// còn hơn một share code mà game từ chối hoặc hiển thị kỳ dị.
    /// </para>
    /// </remarks>
    public static Cs2Crosshair ToCs2Crosshair(CrosshairProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        // Lớp nhánh trong là lớp CS2 mô tả được; nếu nó tắt thì thử lớp ngoài.
        var lines = profile.InnerLines.Enabled ? profile.InnerLines
            : profile.OuterLines.Enabled ? profile.OuterLines
            : profile.InnerLines;

        var visible = lines.Enabled && profile.Shape is not CrosshairShape.Dot;
        var scale = profile.Scale;

        return new Cs2Crosshair
        {
            Size = visible ? Math.Clamp(lines.Length * scale / UnitToDip, 0d, 25d) : 0d,
            Thickness = Math.Clamp(lines.Thickness * scale / UnitToDip, 0d, 25d),
            Gap = visible ? Math.Clamp((lines.Offset * scale / UnitToDip) - GapOrigin, -12.8d, 12.7d) : 0d,

            HasOutline = profile.Outline.Enabled,
            OutlineThickness = profile.Outline.Enabled
                ? Math.Clamp(profile.Outline.Thickness * scale / UnitToDip, 0.5d, 3d)
                : 0d,

            Red = profile.Color.R,
            Green = profile.Color.G,
            Blue = profile.Color.B,

            // CS2 tách alpha riêng; ứng dụng gộp độ mờ tổng thể vào mọi thành phần.
            Alpha = (byte)Math.Clamp(Math.Round(profile.Opacity * 255d), 0d, 255d),
            HasAlpha = true,

            HasCenterDot = profile.CenterDot.Enabled,
            IsTStyle = profile.Shape == CrosshairShape.TShape || (visible && !lines.ShowTop && lines.ShowBottom),

            // Cổ điển, KHÔNG giãn theo bước chân: giống overlay tĩnh của ứng dụng nhất.
            Style = Cs2ShareCodeEncoder.ClassicStaticStyle,
        };
    }

    /// <summary>
    /// Preset này có xuất sang CS2 mà không mất gì không.
    /// </summary>
    /// <remarks>
    /// Khắt khe hơn <see cref="CanExportFaithfully"/> (Valorant) vì CS2 chỉ có một lớp nhánh và
    /// không có độ dài dọc riêng. Kích thước thì LUÔN chỉ là xấp xỉ: CS2 co giãn crosshair theo
    /// độ phân giải và FOV, nên giao diện vẫn nhắc người dùng chỉnh lại trong game.
    /// </remarks>
    public static bool CanExportToCs2Faithfully(CrosshairProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.Shape is not (CrosshairShape.Cross or CrosshairShape.TShape or CrosshairShape.Dot)) return false;
        if (profile.Ring.Enabled) return false;
        if (profile.OuterLines.Enabled && profile.InnerLines.Enabled) return false;
        if (Math.Abs(profile.Rotation) > 0.01d) return false;

        var lines = profile.InnerLines.Enabled ? profile.InnerLines : profile.OuterLines;
        if (!lines.Enabled) return true;

        if (lines.RoundedCaps) return false;
        if (lines.SeparateVerticalLength) return false;

        // CS2 chỉ vẽ đủ bốn nhánh, hoặc bỏ nhánh trên (kiểu chữ T).
        if (lines.ShowLeft != lines.ShowRight) return false;
        if (!lines.ShowBottom) return false;

        return true;
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

        var lineShape = profile.Shape is CrosshairShape.Cross or CrosshairShape.TShape or CrosshairShape.XShape;

        // Mã Valorant không có độ mờ tổng thể. Renderer nhân độ mờ tổng thể vào MỌI thứ, nên để
        // crosshair xuất ra trông y hệt thì phải nhân nó vào từng thành phần.
        var global = profile.Opacity;

        return new ValorantCrosshair
        {
            Color = profile.Color,

            HasOutline = profile.Outline.Enabled,
            OutlineThickness = profile.Outline.Thickness,
            OutlineOpacity = profile.Outline.Opacity * global,

            HasCenterDot = profile.CenterDot.Enabled,
            CenterDotSize = profile.CenterDot.Size,
            CenterDotOpacity = profile.CenterDot.Opacity * global,

            Inner = FromLayer(profile.InnerLines, lineShape, global),
            Outer = FromLayer(profile.OuterLines, lineShape, global),
        };
    }

    /// <summary>
    /// Đổi một lớp nhánh sang dạng Valorant.
    /// </summary>
    /// <remarks>
    /// Valorant không có cờ ẩn từng vạch; ẩn một trục được biểu diễn bằng độ dài 0 của trục đó.
    /// Nên độ dài ngang/dọc xuất ra là độ dài THẬT của trục (0 nếu cả hai vạch của trục bị ẩn),
    /// và cờ tách độ dài dọc bật khi hai con số đó khác nhau. Tổ hợp một trục chỉ có một vạch —
    /// ví dụ ba vạch — không biểu diễn được; <see cref="CanExportFaithfully"/> báo trước điều đó.
    /// </remarks>
    private static ValorantLines FromLayer(LineLayerSettings layer, bool lineShape, double globalOpacity)
    {
        var horizontal = layer.ShowLeft || layer.ShowRight ? layer.Length : 0d;
        var vertical = layer.ShowTop || layer.ShowBottom ? layer.EffectiveVerticalLength : 0d;

        return new ValorantLines
        {
            Show = lineShape && layer.Enabled && (horizontal > 0d || vertical > 0d),
            Thickness = layer.Thickness,
            Length = horizontal,
            Offset = layer.Offset,
            Opacity = layer.Opacity * globalOpacity,
            SeparateVerticalLength = Math.Abs(horizontal - vertical) > 0.0001d,
            VerticalLength = vertical,
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

        if (profile.Shape == CrosshairShape.Cross)
        {
            if (!LayerExportable(profile.InnerLines)) return false;
            if (!LayerExportable(profile.OuterLines)) return false;
        }

        return true;
    }

    /// <summary>Bo tròn đầu vạch, hoặc tổ hợp vạch mà mã không mô tả được (vd chỉ ba vạch).</summary>
    private static bool LayerExportable(LineLayerSettings layer)
    {
        if (!layer.Enabled) return true;
        if (layer.RoundedCaps) return false;

        // Mỗi trục phải hiện đủ hai vạch hoặc ẩn cả hai.
        return layer.ShowLeft == layer.ShowRight && layer.ShowTop == layer.ShowBottom;
    }

    /// <summary>
    /// Đổi crosshair Valorant sang preset.
    /// </summary>
    /// <remarks>
    /// Khác với CS2, đơn vị của Valorant xấp xỉ PIXEL nên chuyển 1:1, không nhân hệ số.
    /// Cả hai lớp nhánh được chuyển độc lập: khoá <c>0*</c> vào <see cref="CrosshairProfile.InnerLines"/>,
    /// khoá <c>1*</c> vào <see cref="CrosshairProfile.OuterLines"/>.
    ///
    /// <para>
    /// Số 0 phải được dịch thành "tắt" TRƯỚC khi gán vào model. Trong Valorant, độ dày 0, độ dài
    /// 0 hay độ mờ 0 nghĩa là phần đó không hiện. Còn model của ứng dụng tự kẹp mọi giá trị vào
    /// khoảng hợp lệ (<see cref="CrosshairLimits"/>) — gán thẳng số 0 vào thì nó bị nâng lên mức
    /// tối thiểu, và một nhánh vốn vô hình trong game lại hiện ra trên overlay.
    /// </para>
    /// </remarks>
    public static CrosshairProfile ToProfile(ValorantCrosshair source, string name)
    {
        ArgumentNullException.ThrowIfNull(source);

        var inner = ToLayer(source.Inner);
        var outer = ToLayer(source.Outer);

        var profile = new CrosshairProfile
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Import từ Valorant" : name,
            Shape = inner.Enabled || outer.Enabled ? CrosshairShape.Cross : CrosshairShape.Dot,
            Color = source.Color,

            // Mỗi lớp nhánh đã có độ mờ riêng, nên độ mờ tổng thể để nguyên 1.
            Opacity = 1d,
            InnerLines = inner,
            OuterLines = outer,
        };

        profile.CenterDot = new CenterDotSettings
        {
            Enabled = source.HasCenterDot && source.CenterDotSize > 0d && source.CenterDotOpacity > 0d,
            Size = source.CenterDotSize,
            UseProfileColor = true,
            Opacity = source.CenterDotOpacity,
        };

        profile.Outline = new OutlineSettings
        {
            Enabled = source.HasOutline && source.OutlineThickness > 0d && source.OutlineOpacity > 0d,
            Thickness = source.OutlineThickness,
            Opacity = source.OutlineOpacity,
            Color = Colors.Black,
        };

        return profile;
    }

    /// <summary>
    /// Đổi một lớp nhánh Valorant sang lớp nhánh của preset.
    /// </summary>
    /// <remarks>
    /// Chuyển 1:1, kể cả độ dài dọc riêng (<c>g</c>/<c>v</c>). Trục có độ dài 0 thì renderer tự
    /// không vẽ trục đó, nên bốn cờ hướng giữ nguyên là bật — chúng là tuỳ chọn riêng của người
    /// dùng, không bị dùng để mô phỏng thứ Valorant đã biểu diễn bằng độ dài.
    /// </remarks>
    private static LineLayerSettings ToLayer(ValorantLines source)
    {
        var vertical = source.SeparateVerticalLength ? source.VerticalLength : source.Length;

        var visible = source.Show
            && source.Thickness > 0d
            && source.Opacity > 0d
            && (source.Length > 0d || vertical > 0d);

        return new LineLayerSettings
        {
            Enabled = visible,
            Opacity = source.Opacity,
            Length = source.Length,
            SeparateVerticalLength = source.SeparateVerticalLength,
            VerticalLength = vertical,
            Thickness = source.Thickness,
            Offset = source.Offset,
        };
    }
}

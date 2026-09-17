using System.Windows.Media;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Services.Import;
using CrosshairOverlay.Services.Storage;

namespace CrosshairOverlay.Services.Presets;

/// <summary>Dựng preset của người dùng từ một mẫu trong thư viện.</summary>
public static class CatalogPresetMapper
{
    /// <summary>
    /// Luôn trả về một <see cref="CrosshairProfile"/> MỚI (Id mới, giờ tạo mới): hai lần "Dùng mẫu" là hai preset
    /// độc lập, sửa cái này không đụng cái kia.
    /// </summary>
    /// <remarks>
    /// Mẫu có mã Valorant thì dựng qua đúng bộ chuyển của tính năng "Nhập mã" — một mẫu trong thư viện và cùng
    /// mã đó dán tay vào phải cho ra cùng một tâm ngắm.
    /// </remarks>
    public static CrosshairProfile ToProfile(CatalogPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        if (!string.IsNullOrWhiteSpace(preset.Code)
            && ValorantCrosshairCode.TryDecode(preset.Code, out var valorant, out _))
        {
            return CrosshairCodeConverter.ToProfile(valorant, preset.Name);
        }

        var shape = preset.ShapeType switch
        {
            "TShape" => CrosshairShape.TShape,
            "XShape" => CrosshairShape.XShape,
            "Dot" => CrosshairShape.Dot,
            "Circle" => CrosshairShape.Circle,
            "Square" => CrosshairShape.Square,
            _ => CrosshairShape.Cross,
        };

        var lineShape = shape is CrosshairShape.Cross or CrosshairShape.TShape or CrosshairShape.XShape;
        var ringShape = shape is CrosshairShape.Circle or CrosshairShape.Square;

        var profile = new CrosshairProfile
        {
            Name = preset.Name,
            Type = CrosshairType.Standard,
            Shape = shape,
            Color = JsonColorConverter.Parse(preset.Color) ?? Colors.White,
            Opacity = 1d,
        };

        profile.InnerLines = new LineLayerSettings
        {
            Enabled = lineShape && preset.Thickness > 0 && preset.Size > 0,
            Opacity = 1d,
            Length = preset.Size,
            VerticalLength = preset.Size,
            Thickness = Math.Max(1d, preset.Thickness),
            Offset = preset.Gap,
        };

        var outer = LineLayerSettings.CreateOuter();
        outer.Enabled = false;
        profile.OuterLines = outer;

        profile.CenterDot = new CenterDotSettings
        {
            Enabled = preset.HasDot || shape == CrosshairShape.Dot,
            Size = preset.DotSize,
            UseProfileColor = true,
            Opacity = 1d,
        };

        profile.Ring = new RingSettings
        {
            Enabled = ringShape,
            Radius = Math.Max(1d, preset.Size),
            Thickness = Math.Max(1d, preset.Thickness),
            Filled = false,
        };

        profile.Outline = new OutlineSettings
        {
            Enabled = preset.HasOutline,
            Thickness = preset.OutlineThickness,
            Opacity = preset.OutlineOpacity,
            Color = Colors.Black,
        };

        return profile;
    }
}

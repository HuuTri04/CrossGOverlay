using System.Windows.Media;
using CrosshairOverlay.Core.Models;

namespace CrosshairOverlay.Services.Storage;

/// <summary>
/// Thư viện preset dựng sẵn, ghi ra đĩa trong lần chạy đầu tiên.
/// </summary>
/// <remarks>
/// Mở app lần đầu mà chỉ có một crosshair trống thì người dùng không biết bắt đầu từ đâu.
/// Vài preset mẫu vừa là điểm khởi đầu, vừa là ví dụ cho thấy từng <see cref="CrosshairShape"/>
/// trông ra sao.
/// </remarks>
internal static class DefaultPresets
{
    public static IReadOnlyList<CrosshairProfile> CreateLibrary() =>
    [
        new()
        {
            Name = "Classic Cross",
            Shape = CrosshairShape.Cross,
            Color = Colors.Lime,
            Lines = new CrosshairLines { Length = 10, Thickness = 2, Gap = 4 },
            CenterDot = new CenterDotSettings { Enabled = true, Size = 2 },
            Outline = new OutlineSettings { Enabled = true, Thickness = 1, Color = Colors.Black },
        },
        new()
        {
            Name = "Tactical T",
            Shape = CrosshairShape.TShape,
            Color = Color.FromRgb(0x00, 0xE5, 0xFF),
            Lines = new CrosshairLines { Length = 7, Thickness = 2, Gap = 3 },
            CenterDot = new CenterDotSettings { Enabled = false },
            Outline = new OutlineSettings { Enabled = true, Thickness = 1, Color = Colors.Black },
        },
        new()
        {
            Name = "Micro Dot",
            Shape = CrosshairShape.Dot,
            Color = Colors.White,
            CenterDot = new CenterDotSettings { Enabled = true, Size = 4 },
            Outline = new OutlineSettings { Enabled = true, Thickness = 1, Color = Colors.Black },
        },
        new()
        {
            Name = "Ring & Dot",
            Shape = CrosshairShape.CircleDot,
            Color = Color.FromRgb(0xFF, 0x3B, 0x30),
            Ring = new RingSettings { Enabled = true, Radius = 11, Thickness = 2 },
            CenterDot = new CenterDotSettings { Enabled = true, Size = 3 },
            Outline = new OutlineSettings { Enabled = true, Thickness = 1, Color = Colors.Black },
        },
        new()
        {
            Name = "Diagonal X",
            Shape = CrosshairShape.XShape,
            Color = Color.FromRgb(0xFF, 0xD6, 0x00),
            Lines = new CrosshairLines { Length = 9, Thickness = 2, Gap = 4 },
            CenterDot = new CenterDotSettings { Enabled = false },
            Outline = new OutlineSettings { Enabled = true, Thickness = 1, Color = Colors.Black },
        },
        new()
        {
            Name = "Open Square",
            Shape = CrosshairShape.Square,
            Color = Color.FromRgb(0xB4, 0x88, 0xFF),
            Ring = new RingSettings { Enabled = true, Radius = 9, Thickness = 2 },
            CenterDot = new CenterDotSettings { Enabled = true, Size = 2 },
            Outline = new OutlineSettings { Enabled = true, Thickness = 1, Color = Colors.Black },
        },
    ];
}

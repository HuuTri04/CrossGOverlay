using System.Text.Json.Serialization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CrosshairOverlay.Core.Models;

/// <summary>
/// Một preset crosshair hoàn chỉnh — đơn vị được lưu/tải/import/export dưới dạng JSON.
/// </summary>
/// <remarks>
/// Model kế thừa <see cref="ObservableObject"/> có chủ đích: editor bind hai chiều trực tiếp vào
/// đây, còn overlay lắng nghe <c>PropertyChanged</c> để redraw. Nhờ vậy real-time preview không
/// cần lớp mapping trung gian, và overlay chỉ vẽ lại đúng lúc có thay đổi (yêu cầu hiệu năng).
/// </remarks>
public sealed partial class CrosshairProfile : ObservableObject
{
    /// <summary>Phiên bản schema JSON, dùng để migrate khi format đổi.</summary>
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyOrder(-2)]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonPropertyOrder(-1)]
    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty] private string _name = "New Crosshair";

    [ObservableProperty] private CrosshairShape _shape = CrosshairShape.Cross;

    /// <summary>Màu chủ đạo của nét vẽ.</summary>
    [ObservableProperty] private Color _color = Colors.Lime;

    /// <summary>Độ mờ tổng thể của toàn bộ crosshair, 0..1.</summary>
    [ObservableProperty] private double _opacity = 1d;

    /// <summary>Hệ số scale áp lên mọi kích thước, 0.1..10.</summary>
    [ObservableProperty] private double _scale = 1d;

    /// <summary>Xoay toàn bộ crosshair quanh tâm, đơn vị độ.</summary>
    [ObservableProperty] private double _rotation;

    /// <summary>Lệch ngang so với tâm màn hình, DIP. Dương = sang phải.</summary>
    [ObservableProperty] private double _offsetX;

    /// <summary>Lệch dọc so với tâm màn hình, DIP. Dương = xuống dưới.</summary>
    [ObservableProperty] private double _offsetY;

    // Property do [ObservableProperty] sinh ra mặc định mang order 0, nên các khối con phải
    // đánh số dương để rơi xuống cuối file JSON — mở preset ra là thấy Name/Shape/Color trước,
    // rồi mới tới chi tiết. File này là thứ người dùng sửa tay và gửi cho nhau.
    [JsonPropertyOrder(10)] public CrosshairLines Lines { get; set; } = new();
    [JsonPropertyOrder(11)] public CenterDotSettings CenterDot { get; set; } = new();
    [JsonPropertyOrder(12)] public OutlineSettings Outline { get; set; } = new();
    [JsonPropertyOrder(13)] public RingSettings Ring { get; set; } = new();
    [JsonPropertyOrder(14)] public CustomImageSettings Image { get; set; } = new();

    [JsonPropertyOrder(20)] public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    [JsonPropertyOrder(21)] public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Bản sao sâu. Dùng khi mở editor (sửa trên bản nháp) và khi nhân bản preset.</summary>
    public CrosshairProfile Clone(bool newIdentity = false) => new()
    {
        SchemaVersion = SchemaVersion,
        Id = newIdentity ? Guid.NewGuid() : Id,
        Name = newIdentity ? $"{Name} {Localization.Tr.Get("Preset_CopySuffix")}" : Name,
        Shape = Shape,
        Color = Color,
        Opacity = Opacity,
        Scale = Scale,
        Rotation = Rotation,
        OffsetX = OffsetX,
        OffsetY = OffsetY,
        Lines = Lines.Clone(),
        CenterDot = CenterDot.Clone(),
        Outline = Outline.Clone(),
        Ring = Ring.Clone(),
        Image = Image.Clone(),
        CreatedUtc = newIdentity ? DateTimeOffset.UtcNow : CreatedUtc,
        ModifiedUtc = DateTimeOffset.UtcNow,
    };

    /// <summary>Preset mặc định khi chạy lần đầu hoặc khi file cấu hình hỏng.</summary>
    public static CrosshairProfile CreateDefault() => new()
    {
        Name = "Default Cross",
        Shape = CrosshairShape.Cross,
        Color = Colors.Lime,
        Lines = new CrosshairLines { Length = 10, Thickness = 2, Gap = 4 },
        CenterDot = new CenterDotSettings { Enabled = true, Size = 2 },
        Outline = new OutlineSettings { Enabled = true, Thickness = 1, Color = Colors.Black },
    };
}

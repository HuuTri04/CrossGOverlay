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
    /// <remarks>
    /// 2: tách <c>Lines</c> thành <see cref="InnerLines"/> + <see cref="OuterLines"/>, đổi
    /// <c>Gap</c> thành <c>Offset</c>. File phiên bản 1 vẫn đọc được — xem <see cref="LegacyLines"/>.
    /// </remarks>
    public const int CurrentSchemaVersion = 2;

    [JsonPropertyOrder(-2)]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonPropertyOrder(-1)]
    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty] private string _name = "New Crosshair";

    /// <summary>Vẽ bằng hình học (<see cref="CrosshairType.Standard"/>) hay bằng ảnh.</summary>
    [ObservableProperty] private CrosshairType _type = CrosshairType.Standard;

    /// <summary>Hình dạng khi ở chế độ tiêu chuẩn.</summary>
    [ObservableProperty] private CrosshairShape _shape = CrosshairShape.Cross;

    /// <summary>
    /// Chuyển giá trị cũ <see cref="CrosshairShape.CustomImage"/> sang chế độ ảnh.
    /// </summary>
    /// <remarks>
    /// Phiên bản trước coi "ảnh" là một hình dạng. Đặt ở setter thay vì ở bước đọc file để MỌI
    /// đường vào — file preset cũ, preset nhận từ người khác, code gán trực tiếp — đều được xử lý
    /// như nhau. Hình dạng trả về chữ thập để khi người dùng quay lại chế độ tiêu chuẩn vẫn thấy
    /// một tâm ngắm hợp lý.
    /// </remarks>
    partial void OnShapeChanged(CrosshairShape value)
    {
        if (value != CrosshairShape.CustomImage) return;

        Type = CrosshairType.Image;
        Shape = CrosshairShape.Cross;
    }

    /// <summary>Màu chủ đạo của nét vẽ.</summary>
    [ObservableProperty] private Color _color = Colors.Lime;

    // Năm thông số dưới đây viết tay thay vì dùng [ObservableProperty] vì setter phải KẸP giá
    // trị. Bộ sinh mã của CommunityToolkit có hook OnXxxChanging nhưng không cho sửa giá trị
    // đang được gán, nên kẹp trong setter tự viết là cách duy nhất bảo đảm model không bao giờ
    // giữ một giá trị ngoài khoảng — kể cả khi giá trị đến từ file JSON sửa tay hay mã chia sẻ.
    // Xem CrosshairLimits.

    /// <summary>Độ mờ tổng thể của toàn bộ crosshair. Xem <see cref="CrosshairLimits.Opacity"/>.</summary>
    public double Opacity
    {
        get => _opacity;
        set => SetProperty(ref _opacity, CrosshairLimits.Opacity.Clamp(value));
    }

    private double _opacity = 1d;

    /// <summary>Hệ số scale áp lên mọi kích thước. Xem <see cref="CrosshairLimits.Scale"/>.</summary>
    public double Scale
    {
        get => _scale;
        set => SetProperty(ref _scale, CrosshairLimits.Scale.Clamp(value));
    }

    private double _scale = 1d;

    /// <summary>Xoay toàn bộ crosshair quanh tâm, đơn vị độ.</summary>
    public double Rotation
    {
        get => _rotation;
        set => SetProperty(ref _rotation, CrosshairLimits.Rotation.Clamp(value));
    }

    private double _rotation;

    /// <summary>Lệch ngang so với tâm màn hình, DIP. Dương = sang phải.</summary>
    public double OffsetX
    {
        get => _offsetX;
        set => SetProperty(ref _offsetX, CrosshairLimits.Offset.Clamp(value));
    }

    private double _offsetX;

    /// <summary>Lệch dọc so với tâm màn hình, DIP. Dương = xuống dưới.</summary>
    public double OffsetY
    {
        get => _offsetY;
        set => SetProperty(ref _offsetY, CrosshairLimits.Offset.Clamp(value));
    }

    private double _offsetY;

    /// <summary>Độ lệch ngang THẬT đang áp dụng: của ảnh khi ở chế độ ảnh, của preset khi tiêu chuẩn.</summary>
    [JsonIgnore]
    public double EffectiveOffsetX => Type == CrosshairType.Image ? Image.OffsetX : OffsetX;

    /// <summary>Độ lệch dọc THẬT đang áp dụng. Xem <see cref="EffectiveOffsetX"/>.</summary>
    [JsonIgnore]
    public double EffectiveOffsetY => Type == CrosshairType.Image ? Image.OffsetY : OffsetY;

    /// <summary>
    /// Ghi độ lệch vào ĐÚNG chỗ theo chế độ hiện tại: độ lệch của ảnh ở chế độ ảnh, của preset ở
    /// chế độ tiêu chuẩn. Chiều ngược của <see cref="EffectiveOffsetX"/>/<see cref="EffectiveOffsetY"/>.
    /// </summary>
    /// <remarks>
    /// Gom về một chỗ để kéo rê trong preview không phải tự biết quy tắc chọn chế độ — tự viết lại
    /// ở chỗ khác là cách chắc chắn nhất để hai nơi trôi lệch nhau.
    /// </remarks>
    public void SetEffectiveOffset(double x, double y)
    {
        if (Type == CrosshairType.Image)
        {
            Image.OffsetX = x;
            Image.OffsetY = y;
        }
        else
        {
            OffsetX = x;
            OffsetY = y;
        }
    }

    // Property do [ObservableProperty] sinh ra mặc định mang order 0, nên các khối con phải
    // đánh số dương để rơi xuống cuối file JSON — mở preset ra là thấy Name/Shape/Color trước,
    // rồi mới tới chi tiết. File này là thứ người dùng sửa tay và gửi cho nhau.
    [JsonPropertyOrder(10)] public CenterDotSettings CenterDot { get; set; } = new();
    [JsonPropertyOrder(11)] public LineLayerSettings InnerLines { get; set; } = LineLayerSettings.CreateInner();
    [JsonPropertyOrder(12)] public LineLayerSettings OuterLines { get; set; } = LineLayerSettings.CreateOuter();
    [JsonPropertyOrder(13)] public OutlineSettings Outline { get; set; } = new();
    [JsonPropertyOrder(14)] public RingSettings Ring { get; set; } = new();
    [JsonPropertyOrder(15)] public CustomImageSettings Image { get; set; } = new();

    /// <summary>
    /// Khối <c>Lines</c> của file phiên bản 1, chỉ để ĐỌC.
    /// </summary>
    /// <remarks>
    /// Phiên bản 1 chỉ có một lớp nhánh; nó chính là nhánh trong của mô hình mới. Getter luôn
    /// trả null nên khối này không bao giờ xuất hiện trong file ghi mới. Khoảng hở cũ
    /// (<c>Gap</c>) được <see cref="LineLayerSettings.LegacyGap"/> chuyển tiếp sang Offset.
    /// </remarks>
    [JsonPropertyName("Lines")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
    public LineLayerSettings? LegacyLines
    {
        get => null;
        set
        {
            if (value is not null) InnerLines = value;
        }
    }

    /// <summary>Ảnh nhúng, chỉ có trong file export. Xem <see cref="EmbeddedImageData"/>.</summary>
    [JsonPropertyOrder(30)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EmbeddedImageData? EmbeddedImage { get; set; }

    [JsonPropertyOrder(20)] public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    [JsonPropertyOrder(21)] public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Tên hiển thị. Danh sách dùng <c>DisplayMemberPath</c> chỉ đổi phần CHỮ vẽ ra; tên mà trình
    /// đọc màn hình (UI Automation) đọc lên vẫn lấy từ <c>ToString()</c> — không ghi đè thì nó đọc
    /// "CrosshairOverlay.Core.Models.CrosshairProfile" cho mọi mục.
    /// </summary>
    public override string ToString() => Name;

    /// <summary>Bản sao sâu. Dùng khi mở editor (sửa trên bản nháp) và khi nhân bản preset.</summary>
    public CrosshairProfile Clone(bool newIdentity = false) => new()
    {
        SchemaVersion = SchemaVersion,
        Id = newIdentity ? Guid.NewGuid() : Id,
        Name = newIdentity ? $"{Name} {Localization.Tr.Get("Preset_CopySuffix")}" : Name,
        Type = Type,
        Shape = Shape,
        Color = Color,
        Opacity = Opacity,
        Scale = Scale,
        Rotation = Rotation,
        OffsetX = OffsetX,
        OffsetY = OffsetY,
        CenterDot = CenterDot.Clone(),
        InnerLines = InnerLines.Clone(),
        OuterLines = OuterLines.Clone(),
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
        InnerLines = new LineLayerSettings { Length = 10, Thickness = 2, Offset = 4 },
        CenterDot = new CenterDotSettings { Enabled = true, Size = 2 },
        Outline = new OutlineSettings { Enabled = true, Thickness = 1, Color = Colors.Black },
    };
}

using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CrosshairOverlay.Core.Models;

/// <summary>
/// Một lớp nhánh: bốn vạch thẳng toả ra từ tâm. Crosshair có HAI lớp độc lập — nhánh trong
/// (<see cref="CrosshairProfile.InnerLines"/>) và nhánh ngoài (<see cref="CrosshairProfile.OuterLines"/>)
/// — giống hệt Valorant.
/// </summary>
/// <remarks>
/// Toàn bộ đơn vị là DIP (device-independent pixel); renderer nhân với DPI scale của màn hình.
///
/// <para>
/// <see cref="Offset"/> của MỖI lớp đều tính từ TÂM crosshair, không tính từ đầu nhánh của lớp
/// kia. Nhánh ngoài có offset 10 nghĩa là bắt đầu cách tâm 10 DIP, bất kể nhánh trong dài bao
/// nhiêu. Đó là cách Valorant định nghĩa, và nhờ vậy chỉnh một lớp không làm xê dịch lớp còn lại.
/// </para>
/// </remarks>
public sealed partial class LineLayerSettings : ObservableObject
{
    /// <summary>Hiển thị lớp nhánh này.</summary>
    [ObservableProperty] private bool _enabled = true;

    /// <summary>Độ mờ riêng của lớp, nhân thêm với độ mờ tổng thể của preset.</summary>
    public double Opacity
    {
        get => _opacity;
        set => SetProperty(ref _opacity, CrosshairLimits.Opacity.Clamp(value));
    }

    private double _opacity = 1d;

    /// <summary>
    /// Chiều dài mỗi vạch, tính từ điểm bắt đầu (<see cref="Offset"/>) ra ngoài. Khi
    /// <see cref="SeparateVerticalLength"/> bật, đây chỉ còn là độ dài của vạch NGANG.
    /// </summary>
    public double Length
    {
        get => _length;
        set => SetProperty(ref _length, CrosshairLimits.LineLength.Clamp(value));
    }

    private double _length = 10d;

    /// <summary>
    /// Vạch dọc (trên/dưới) có độ dài riêng, đọc ở <see cref="VerticalLength"/>; tắt thì vạch dọc
    /// dài bằng vạch ngang. Tương ứng khoá <c>0g</c>/<c>1g</c> của Valorant.
    /// </summary>
    /// <remarks>
    /// Không áp cho hình X: bốn nhánh chéo không có khái niệm ngang hay dọc, nên luôn dùng
    /// <see cref="Length"/>.
    /// </remarks>
    [ObservableProperty] private bool _separateVerticalLength;

    /// <summary>Độ dài vạch dọc. Chỉ có hiệu lực khi <see cref="SeparateVerticalLength"/> bật.</summary>
    public double VerticalLength
    {
        get => _verticalLength;
        set => SetProperty(ref _verticalLength, CrosshairLimits.LineLength.Clamp(value));
    }

    private double _verticalLength = 10d;

    /// <summary>Độ dài thật của vạch dọc, sau khi xét <see cref="SeparateVerticalLength"/>.</summary>
    [JsonIgnore]
    public double EffectiveVerticalLength => SeparateVerticalLength ? VerticalLength : Length;

    /// <summary>Bề dày vạch.</summary>
    public double Thickness
    {
        get => _thickness;
        set => SetProperty(ref _thickness, CrosshairLimits.LineThickness.Clamp(value));
    }

    private double _thickness = 2d;

    /// <summary>Khoảng cách từ TÂM crosshair tới điểm bắt đầu của vạch.</summary>
    public double Offset
    {
        get => _offset;
        set => SetProperty(ref _offset, CrosshairLimits.LineOffset.Clamp(value));
    }

    private double _offset = 4d;

    /// <summary>
    /// Tên cũ của <see cref="Offset"/>, chỉ để ĐỌC preset ghi bởi phiên bản trước.
    /// </summary>
    /// <remarks>
    /// Getter luôn trả null nên thuộc tính này không bao giờ được ghi ra file mới. Không có nó thì
    /// mọi preset người dùng đã lưu hay chia sẻ sẽ mất khoảng hở khi mở ở phiên bản này.
    /// </remarks>
    [JsonPropertyName("Gap")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public double? LegacyGap
    {
        get => null;
        set
        {
            if (value is { } gap) Offset = gap;
        }
    }

    [ObservableProperty] private bool _showTop = true;
    [ObservableProperty] private bool _showBottom = true;
    [ObservableProperty] private bool _showLeft = true;
    [ObservableProperty] private bool _showRight = true;

    /// <summary>Bo tròn đầu vạch thay vì cắt vuông.</summary>
    [ObservableProperty] private bool _roundedCaps;

    public LineLayerSettings Clone() => new()
    {
        Enabled = Enabled,
        Opacity = Opacity,
        Length = Length,
        SeparateVerticalLength = SeparateVerticalLength,
        VerticalLength = VerticalLength,
        Thickness = Thickness,
        Offset = Offset,
        ShowTop = ShowTop,
        ShowBottom = ShowBottom,
        ShowLeft = ShowLeft,
        ShowRight = ShowRight,
        RoundedCaps = RoundedCaps,
    };

    /// <summary>Nhánh trong mặc định: bật, đúng hình dáng preset mặc định của ứng dụng.</summary>
    public static LineLayerSettings CreateInner() => new();

    /// <summary>
    /// Nhánh ngoài mặc định: TẮT.
    /// </summary>
    /// <remarks>
    /// Preset ghi trước khi có nhánh ngoài không có khối <c>OuterLines</c> trong file. Khi đọc,
    /// chúng nhận giá trị này — và phải trông y hệt như trước, nên lớp mới sinh ra phải tắt.
    /// Các số còn lại là nhánh ngoài mặc định của Valorant, để bật lên là có hình hợp lý ngay.
    /// </remarks>
    public static LineLayerSettings CreateOuter() => new()
    {
        Enabled = false,
        Opacity = 0.35d,
        Length = 2d,
        VerticalLength = 2d,
        Thickness = 2d,
        Offset = 10d,
    };
}

/// <summary>Chấm ở tâm crosshair.</summary>
public sealed partial class CenterDotSettings : ObservableObject
{
    [ObservableProperty] private bool _enabled;

    /// <summary>Đường kính chấm.</summary>
    public double Size
    {
        get => _size;
        set => SetProperty(ref _size, CrosshairLimits.DotSize.Clamp(value));
    }

    private double _size = 3d;

    /// <summary>Nếu true, dùng màu chung của profile thay vì <see cref="Color"/>.</summary>
    [ObservableProperty] private bool _useProfileColor = true;

    [ObservableProperty] private Color _color = Colors.Lime;

    /// <summary>Độ mờ riêng của chấm tâm.</summary>
    public double Opacity
    {
        get => _opacity;
        set => SetProperty(ref _opacity, CrosshairLimits.Opacity.Clamp(value));
    }

    private double _opacity = 1d;

    public CenterDotSettings Clone() => new()
    {
        Enabled = Enabled,
        Size = Size,
        UseProfileColor = UseProfileColor,
        Color = Color,
        Opacity = Opacity,
    };
}

/// <summary>Viền bao quanh mọi nét vẽ, giúp crosshair nổi bật trên nền sáng lẫn nền tối.</summary>
public sealed partial class OutlineSettings : ObservableObject
{
    [ObservableProperty] private bool _enabled = true;

    /// <summary>Bề dày viền ở MỖI bên của nét gốc.</summary>
    public double Thickness
    {
        get => _thickness;
        set => SetProperty(ref _thickness, CrosshairLimits.OutlineThickness.Clamp(value));
    }

    private double _thickness = 1d;

    [ObservableProperty] private Color _color = Colors.Black;

    /// <summary>Độ mờ riêng của viền.</summary>
    public double Opacity
    {
        get => _opacity;
        set => SetProperty(ref _opacity, CrosshairLimits.Opacity.Clamp(value));
    }

    private double _opacity = 1d;

    public OutlineSettings Clone() => new()
    {
        Enabled = Enabled,
        Thickness = Thickness,
        Color = Color,
        Opacity = Opacity,
    };
}

/// <summary>Vòng tròn / khung vuông bao quanh tâm.</summary>
public sealed partial class RingSettings : ObservableObject
{
    [ObservableProperty] private bool _enabled;

    /// <summary>Bán kính (với Square: nửa cạnh).</summary>
    public double Radius
    {
        get => _radius;
        set => SetProperty(ref _radius, CrosshairLimits.RingRadius.Clamp(value));
    }

    private double _radius = 12d;

    /// <summary>Bề dày nét vòng.</summary>
    public double Thickness
    {
        get => _thickness;
        set => SetProperty(ref _thickness, CrosshairLimits.RingThickness.Clamp(value));
    }

    private double _thickness = 2d;

    /// <summary>Tô đặc thay vì chỉ vẽ viền.</summary>
    [ObservableProperty] private bool _filled;

    public RingSettings Clone() => new()
    {
        Enabled = Enabled,
        Radius = Radius,
        Thickness = Thickness,
        Filled = Filled,
    };
}

/// <summary>Tâm ngắm dạng ảnh do người dùng cung cấp.</summary>
/// <remarks>
/// Chỉ có hiệu lực khi <see cref="CrosshairProfile.Type"/> là <see cref="CrosshairType.Image"/>.
/// Ở chế độ ảnh, kích thước, độ mờ và vị trí lấy từ ĐÂY, không nhân thêm tỉ lệ, độ mờ hay độ lệch
/// của chế độ tiêu chuẩn — những giá trị đó bị ẩn khỏi giao diện khi chọn ảnh, để chúng âm thầm
/// nhân vào thì người dùng không có cách nào hiểu vì sao ảnh to hay mờ bất thường.
/// </remarks>
public sealed partial class CustomImageSettings : ObservableObject
{
    /// <summary>
    /// Đường dẫn ảnh. Ảnh chọn bằng giao diện luôn được chép vào kho của ứng dụng và lưu ở dạng
    /// TƯƠNG ĐỐI (<c>CustomImages/ten_abc123.png</c>), nên xoá hay di chuyển file gốc không làm
    /// hỏng preset. Đường dẫn tuyệt đối vẫn đọc được để tương thích preset cũ.
    /// </summary>
    [ObservableProperty] private string? _filePath;

    /// <summary>Hệ số phóng to/thu nhỏ; 1 nghĩa là mỗi pixel ảnh bằng một DIP.</summary>
    public double Scale
    {
        get => _scale;
        set => SetProperty(ref _scale, CrosshairLimits.ImageScale.Clamp(value));
    }

    private double _scale = 1d;

    /// <summary>Kích thước hiển thị nhắm tới khi vừa chọn ảnh lớn, DIP — cỡ tâm ngắm to điển hình.</summary>
    public const double TargetDisplaySize = 64d;

    /// <summary>Tâm ngắm nhỏ hơn cỡ này khó nhìn; ảnh bé hơn được phóng lên.</summary>
    public const double MinComfortableSize = 30d;

    /// <summary>
    /// <see cref="Scale"/> đề xuất cho ảnh vừa chọn, để tâm ngắm hiện ra trong khoảng 30–64 DIP thay
    /// vì che nửa màn hình (hay bé tí).
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Cạnh lớn nhất trên 64: thu về 64 (ảnh 256×256 → 0.25). Làm tròn XUỐNG 2 chữ số để
    /// khớp bước của thanh trượt mà không vượt 64.</item>
    /// <item>Dưới 30: phóng lên theo bội số NGUYÊN nhỏ nhất đạt 30 — bội nguyên giữ từng pixel sắc
    /// nét (renderer vẽ nearest-neighbor ở hệ số nguyên), ảnh pixel-art 16×16 thành 32×32.</item>
    /// <item>Từ 30 đến 64: đã đúng cỡ, giữ 1 để hiển thị đúng từng pixel.</item>
    /// </list>
    /// </remarks>
    public static double SuggestScale(int pixelWidth, int pixelHeight)
    {
        var longest = Math.Max(pixelWidth, pixelHeight);
        if (longest <= 0) return 1d;

        double scale;
        if (longest > TargetDisplaySize)
            scale = Math.Floor(TargetDisplaySize / longest * 100d + 1e-9) / 100d;
        else if (longest < MinComfortableSize)
            scale = Math.Ceiling(MinComfortableSize / longest);
        else
            scale = 1d;

        return CrosshairLimits.ImageScale.Clamp(scale);
    }

    /// <summary>Độ mờ của ảnh.</summary>
    public double Opacity
    {
        get => _opacity;
        set => SetProperty(ref _opacity, CrosshairLimits.Opacity.Clamp(value));
    }

    private double _opacity = 1d;

    /// <summary>Lệch ngang so với tâm màn hình, DIP. Dương = sang phải. (0,0) là chính giữa.</summary>
    public double OffsetX
    {
        get => _offsetX;
        set => SetProperty(ref _offsetX, CrosshairLimits.ImageOffset.Clamp(value));
    }

    private double _offsetX;

    /// <summary>Lệch dọc so với tâm màn hình, DIP. Dương = xuống dưới.</summary>
    public double OffsetY
    {
        get => _offsetY;
        set => SetProperty(ref _offsetY, CrosshairLimits.ImageOffset.Clamp(value));
    }

    private double _offsetY;

    public CustomImageSettings Clone() => new()
    {
        FilePath = FilePath,
        Scale = Scale,
        Opacity = Opacity,
        OffsetX = OffsetX,
        OffsetY = OffsetY,
    };
}

/// <summary>
/// Ảnh nhúng thẳng trong file preset XUẤT RA, để người nhận có đủ ảnh chỉ với một file.
/// </summary>
/// <remarks>
/// Chỉ tồn tại trong file export. Preset trong thư viện không bao giờ giữ nó: ngay khi đọc vào,
/// ảnh được đưa vào kho và khối này bị bỏ — giữ vài MB base64 trong bộ nhớ, rồi ghi lại mỗi lần
/// lưu preset, là lãng phí thuần tuý.
/// </remarks>
public sealed class EmbeddedImageData
{
    /// <summary>Tên file gốc, để lấy đuôi và đặt tên dễ nhận ra khi đưa vào kho.</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Nội dung file, mã hoá base64.</summary>
    public string Data { get; init; } = string.Empty;
}

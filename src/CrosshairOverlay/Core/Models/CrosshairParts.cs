using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CrosshairOverlay.Core.Models;

/// <summary>
/// Thông số 4 nhánh đường thẳng của crosshair. Toàn bộ đơn vị là DIP (device-independent pixel),
/// renderer sẽ nhân với DPI scale của màn hình đích.
/// </summary>
public sealed partial class CrosshairLines : ObservableObject
{
    [ObservableProperty] private bool _enabled = true;

    /// <summary>Chiều dài mỗi nhánh, tính từ mép gap ra ngoài.</summary>
    [ObservableProperty] private double _length = 10d;

    /// <summary>Bề dày nhánh.</summary>
    [ObservableProperty] private double _thickness = 2d;

    /// <summary>Khoảng trống từ tâm đến điểm bắt đầu của nhánh.</summary>
    [ObservableProperty] private double _gap = 4d;

    [ObservableProperty] private bool _showTop = true;
    [ObservableProperty] private bool _showBottom = true;
    [ObservableProperty] private bool _showLeft = true;
    [ObservableProperty] private bool _showRight = true;

    /// <summary>Bo tròn đầu nhánh thay vì cắt vuông.</summary>
    [ObservableProperty] private bool _roundedCaps;

    public CrosshairLines Clone() => new()
    {
        Enabled = Enabled,
        Length = Length,
        Thickness = Thickness,
        Gap = Gap,
        ShowTop = ShowTop,
        ShowBottom = ShowBottom,
        ShowLeft = ShowLeft,
        ShowRight = ShowRight,
        RoundedCaps = RoundedCaps,
    };
}

/// <summary>Chấm ở tâm crosshair.</summary>
public sealed partial class CenterDotSettings : ObservableObject
{
    [ObservableProperty] private bool _enabled;

    /// <summary>Đường kính chấm.</summary>
    [ObservableProperty] private double _size = 3d;

    /// <summary>Nếu true, dùng màu chung của profile thay vì <see cref="Color"/>.</summary>
    [ObservableProperty] private bool _useProfileColor = true;

    [ObservableProperty] private Color _color = Colors.Lime;

    /// <summary>0..1.</summary>
    [ObservableProperty] private double _opacity = 1d;

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
    [ObservableProperty] private double _thickness = 1d;

    [ObservableProperty] private Color _color = Colors.Black;

    /// <summary>0..1.</summary>
    [ObservableProperty] private double _opacity = 1d;

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
    [ObservableProperty] private double _radius = 12d;

    [ObservableProperty] private double _thickness = 2d;

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

/// <summary>Crosshair dạng ảnh do người dùng cung cấp.</summary>
public sealed partial class CustomImageSettings : ObservableObject
{
    /// <summary>Đường dẫn tuyệt đối tới file ảnh (PNG khuyến nghị, có alpha).</summary>
    [ObservableProperty] private string? _filePath;

    /// <summary>Hệ số phóng to/thu nhỏ so với kích thước gốc.</summary>
    [ObservableProperty] private double _scale = 1d;

    /// <summary>0..1.</summary>
    [ObservableProperty] private double _opacity = 1d;

    public CustomImageSettings Clone() => new()
    {
        FilePath = FilePath,
        Scale = Scale,
        Opacity = Opacity,
    };
}

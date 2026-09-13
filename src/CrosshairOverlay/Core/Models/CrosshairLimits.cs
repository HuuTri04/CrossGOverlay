namespace CrosshairOverlay.Core.Models;

/// <summary>Khoảng giá trị hợp lệ của một thông số, kèm khả năng tự kẹp.</summary>
public readonly record struct ValueRange(double Min, double Max)
{
    /// <summary>
    /// Kẹp giá trị vào khoảng cho phép.
    /// </summary>
    /// <remarks>
    /// NaN bị quy về <see cref="Min"/> chứ không cho lan truyền: một NaN lọt vào phép tính
    /// hình học sẽ sinh ra Geometry rỗng hoặc vô hạn và làm hỏng cả lượt vẽ, mà lỗi lại hiện
    /// ra ở chỗ khác hẳn nơi nó sinh ra. Vô cực thì <see cref="Math.Clamp(double, double, double)"/>
    /// đã xử lý đúng sẵn — dương vô cực rơi về Max, âm vô cực rơi về Min.
    /// </remarks>
    public double Clamp(double value) => double.IsNaN(value) ? Min : Math.Clamp(value, Min, Max);
}

/// <summary>
/// Khoảng hợp lệ của MỌI thông số crosshair — nguồn chân lý duy nhất.
/// </summary>
/// <remarks>
/// Cả hai phía cùng đọc từ đây, và đó là điểm mấu chốt:
/// <list type="bullet">
///   <item>Slider và ô nhập số lấy Minimum/Maximum qua thuộc tính <c>Range</c>.</item>
///   <item>Chính các property của model tự kẹp trong setter.</item>
/// </list>
/// Chỉ chặn ở giao diện là không đủ. Giá trị còn vào được từ những đường không đi qua giao
/// diện: file preset JSON người dùng sửa tay, preset nhận từ người khác, và mã chia sẻ của
/// CS2/Valorant. Một Thickness âm hay Scale bằng 1e9 từ các đường đó sẽ tạo Geometry khổng lồ
/// làm treo lượt vẽ hoặc ném exception — đúng thứ cần chặn.
/// </remarks>
public static class CrosshairLimits
{
    // ------------------------------------------------------------------ toàn cục

    /// <summary>Độ mờ. Chặn dưới ở 0.05 vì 0 nghĩa là tàng hình — người dùng sẽ tưởng app hỏng.</summary>
    public static readonly ValueRange Opacity = new(0.05d, 1d);

    /// <summary>Hệ số phóng to. Là bội số nên 1 = kích thước gốc; dưới 1 là thu nhỏ.</summary>
    public static readonly ValueRange Scale = new(0.2d, 5d);

    /// <summary>Góc xoay, độ. Quá một vòng là lặp lại nên không cần rộng hơn.</summary>
    public static readonly ValueRange Rotation = new(-180d, 180d);

    /// <summary>Lệch tâm theo mỗi trục, DIP.</summary>
    public static readonly ValueRange Offset = new(-200d, 200d);

    // ------------------------------------------------------------------ nhánh (dùng chung cho nhánh trong lẫn nhánh ngoài)

    public static readonly ValueRange LineLength = new(0d, 100d);
    public static readonly ValueRange LineThickness = new(1d, 20d);
    /// <summary>Khoảng cách từ tâm tới điểm bắt đầu vạch. Valorant cho nhánh ngoài tới 40.</summary>
    public static readonly ValueRange LineOffset = new(0d, 60d);

    // ------------------------------------------------------------------ các khối khác

    public static readonly ValueRange DotSize = new(1d, 30d);
    public static readonly ValueRange RingRadius = new(1d, 120d);
    public static readonly ValueRange RingThickness = new(1d, 20d);
    public static readonly ValueRange OutlineThickness = new(1d, 10d);
    public static readonly ValueRange ImageScale = new(0.1d, 8d);

    /// <summary>
    /// Lệch vị trí của ảnh theo mỗi trục, DIP. Rộng hơn <see cref="Offset"/> vì ảnh thường
    /// dùng làm dấu mốc đặt ở chỗ khác tâm màn hình (vd chỉ báo khoảng cách ném lựu).
    /// </summary>
    public static readonly ValueRange ImageOffset = new(-500d, 500d);
}

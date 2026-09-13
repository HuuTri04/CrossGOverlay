using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace CrosshairOverlay.Converters;

/// <summary>Đổi <see cref="Color"/> sang <see cref="Brush"/> để tô ô xem trước màu.</summary>
public sealed class ColorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not Color color) return Brushes.Transparent;

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is SolidColorBrush brush ? brush.Color : Colors.Transparent;
}

/// <summary>
/// bool → Visibility. Truyền <c>ConverterParameter="Invert"</c> để đảo chiều.
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (IsInverted(parameter)) flag = !flag;

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var visible = value is Visibility.Visible;
        return IsInverted(parameter) ? !visible : visible;
    }

    private static bool IsInverted(object parameter) =>
        string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// null → false. Dùng để tắt các nút thao tác khi chưa chọn preset nào.
/// </summary>
public sealed class NotNullToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// null → Collapsed. Dùng để ẩn cả một panel chi tiết khi chưa chọn mục nào.
/// </summary>
/// <remarks>
/// Tách riêng khỏi <see cref="NotNullToBoolConverter"/> vì WPF KHÔNG tự đổi bool sang
/// <see cref="Visibility"/>: bind một converter trả bool vào Visibility sẽ hỏng lặng lẽ.
/// </remarks>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Hiển thị số thực gọn gàng trên nhãn cạnh slider (1 chữ số thập phân, bỏ đuôi ",0").
/// </summary>
public sealed class RoundedNumberConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double number) return value?.ToString() ?? string.Empty;

        return Math.Abs(number - Math.Round(number)) < 0.05d
            ? Math.Round(number).ToString("0", CultureInfo.CurrentCulture)
            : number.ToString("0.0", CultureInfo.CurrentCulture);
    }

    /// <remarks>
    /// Đọc bằng <see cref="CultureInfo.InvariantCulture"/> sau khi đổi dấu phẩy thành dấu chấm,
    /// giống hệt ô nhập của SliderField. Đọc theo văn hoá hiện tại thì cùng một chuỗi "0.5" ra
    /// 0.5 trên máy tiếng Anh nhưng ra 5 trên máy đặt vùng Việt Nam — dấu chấm ở đó là dấu
    /// phân cách hàng nghìn.
    /// </remarks>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string text
        && double.TryParse(
            text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : DependencyProperty.UnsetValue;
}

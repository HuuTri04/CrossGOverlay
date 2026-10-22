using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace CrosshairOverlay.Controls;

/// <summary>
/// Lăn chuột cuộn đúng vùng đang nằm DƯỚI con trỏ, không cần bấm vào trước.
/// </summary>
/// <remarks>
/// <para>
/// Gắn một lần lên cửa sổ: <c>ctl:WheelScrollAssist.IsEnabled="True"</c>. Khi lăn chuột, đi ngược
/// cây giao diện từ phần tử dưới con trỏ và cuộn <see cref="ScrollViewer"/> gần nhất CÒN cuộn được
/// theo hướng đó.
/// </para>
/// <para>
/// Cần vì mặc định WPF có ba chỗ nuốt mất cú lăn: ô nhập số và ComboBox bắt sự kiện dù chẳng cuộn
/// gì; <see cref="ListBox"/> nằm trong một ScrollViewer khác thì ScrollViewer bên trong luôn đánh
/// dấu đã xử lý dù không cuộn được; và khoảng trống giữa các ô không có nền nên con trỏ "xuyên"
/// qua, trúng thẳng Card phía sau vốn nằm ngoài ScrollViewer. Style ScrollViewer trong Theme đặt
/// nền trong suốt để xử lý chỗ thứ ba; lớp này xử lý hai chỗ đầu.
/// </para>
/// <para>
/// Khi vùng trong cùng đã cuộn tới mép, cú lăn chuyển tiếp ra vùng bao ngoài. Không có vùng nào
/// cuộn được thì để nguyên sự kiện cho hành vi mặc định.
/// </para>
/// </remarks>
public static class WheelScrollAssist
{
    private const double PixelsPerWheelNotch = 48d;

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(WheelScrollAssist),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    /// <summary>Use a fixed DIP offset on page-level scrollers, independent of Windows wheel line count.</summary>
    public static readonly DependencyProperty FixedPixelWheelProperty = DependencyProperty.RegisterAttached(
        "FixedPixelWheel", typeof(bool), typeof(WheelScrollAssist), new PropertyMetadata(false));

    public static bool GetFixedPixelWheel(DependencyObject element) =>
        (bool)element.GetValue(FixedPixelWheelProperty);

    public static void SetFixedPixelWheel(DependencyObject element, bool value) =>
        element.SetValue(FixedPixelWheelProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;

        element.PreviewMouseWheel -= OnPreviewMouseWheel;
        if ((bool)e.NewValue) element.PreviewMouseWheel += OnPreviewMouseWheel;
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || e.Delta == 0) return;

        if (FindScrollable(e.OriginalSource as DependencyObject, scrollDown: e.Delta < 0) is not { } viewer) return;

        Scroll(viewer, e.Delta);

        e.Handled = true;
    }

    /// <summary>
    /// Trang nội dung dùng quán tính 64 DIP/nấc khi được bật; nhánh 48 DIP là fallback.
    /// Danh sách ảo hóa tiếp tục theo thiết lập số dòng của Windows.
    /// </summary>
    private static void Scroll(ScrollViewer viewer, int delta)
    {
        if (SmoothScrollBehavior.TryQueueWheel(viewer, delta)) return;

        if (GetFixedPixelWheel(viewer))
        {
            viewer.ScrollToVerticalOffset(Math.Clamp(
                viewer.VerticalOffset - delta * PixelsPerWheelNotch / Mouse.MouseWheelDeltaForOneLine,
                0d, viewer.ScrollableHeight));
            return;
        }

        var down = delta < 0;
        var lines = SystemParameters.WheelScrollLines;
        if (lines < 0)
        {
            if (down) viewer.PageDown();
            else viewer.PageUp();
            return;
        }

        // Các lệnh được xếp hàng và cộng dồn trong lượt layout kế tiếp, không cuộn giật từng nấc.
        for (var i = 0; i < Math.Max(1, lines); i++)
        {
            if (down) viewer.LineDown();
            else viewer.LineUp();
        }
    }

    /// <summary>ScrollViewer gần phần tử nhất mà còn cuộn được theo hướng yêu cầu.</summary>
    internal static ScrollViewer? FindScrollable(DependencyObject? start, bool scrollDown)
    {
        for (var node = start; node is not null; node = Parent(node))
        {
            if (node is ScrollViewer { IsEnabled: true } viewer && CanScroll(viewer, scrollDown))
                return viewer;
        }

        return null;
    }

    internal static bool CanScroll(ScrollViewer viewer, bool scrollDown)
    {
        // Nửa DIP dung sai: offset dạng số thực có thể dừng ở 199.9999 thay vì 200.
        const double Epsilon = 0.5d;

        if (viewer.ScrollableHeight <= Epsilon) return false;

        var offset = SmoothScrollBehavior.GetEffectiveOffset(viewer);
        return scrollDown
            ? offset < viewer.ScrollableHeight - Epsilon
            : offset > Epsilon;
    }

    /// <summary>
    /// Cha trong cây hiển thị; phần tử không phải Visual (vd đoạn chữ trong TextBlock) thì theo cây
    /// logic.
    /// </summary>
    private static DependencyObject? Parent(DependencyObject node) =>
        node is Visual or Visual3D
            ? VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node)
            : LogicalTreeHelper.GetParent(node);
}

using System.Windows;
using System.Windows.Controls;
using CrosshairOverlay.ViewModels;

namespace CrosshairOverlay.Views;

/// <summary>
/// Nội dung tab "Thư viện": tìm, lọc và thêm mẫu tâm ngắm dựng sẵn vào preset của người dùng.
/// </summary>
/// <remarks>
/// Toàn bộ logic nằm ở <see cref="PresetLibraryViewModel"/>. Ở đây chỉ còn đúng thứ view mới biết: vùng danh sách
/// rộng bao nhiêu thì xếp được mấy thẻ một hàng.
/// </remarks>
public partial class PresetLibraryView : UserControl
{
    /// <summary>Bề ngang một thẻ cộng khoảng cách bên phải (khớp CatalogCard + Margin trong XAML).</summary>
    internal const double CellWidth = 160d + 12d;

    /// <summary>Chừa chỗ cho thanh cuộn dọc để thẻ cuối hàng không bị nó che.</summary>
    private const double ScrollBarAllowance = 14d;

    public PresetLibraryView() => InitializeComponent();

    /// <summary>Số thẻ vừa một hàng. Thẻ cuối hàng không cần khoảng cách bên phải, nên cộng bù 12 DIP đó.</summary>
    internal static int ColumnsFor(double availableWidth) =>
        Math.Max(1, (int)((availableWidth - ScrollBarAllowance + 12d) / CellWidth));

    /// <summary>Đưa con trỏ vào ô tìm kiếm — gọi khi người dùng vừa chuyển sang tab này.</summary>
    public void FocusSearch() => SearchBox.Focus();

    private void OnRowsSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged && DataContext is PresetLibraryViewModel viewModel)
            viewModel.ColumnCount = ColumnsFor(e.NewSize.Width);
    }
}

using System.Windows;
using System.Windows.Input;
using CrosshairOverlay.ViewModels;

namespace CrosshairOverlay.Views;

/// <summary>Cửa sổ "Thư viện mẫu". Logic nằm ở <see cref="PresetLibraryViewModel"/>; ở đây chỉ còn phần thuần giao diện.</summary>
public partial class PresetLibraryWindow : Window
{
    /// <summary>Bề ngang một thẻ cộng khoảng cách bên phải (khớp CatalogCard + Margin trong XAML).</summary>
    internal const double CellWidth = 160d + 12d;

    /// <summary>Chừa chỗ cho thanh cuộn dọc để thẻ cuối hàng không bị nó che.</summary>
    private const double ScrollBarAllowance = 14d;

    private readonly PresetLibraryViewModel _viewModel;

    public PresetLibraryWindow(PresetLibraryViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        Loaded += OnLoaded;
        Closed += (_, _) => _viewModel.Dispose();
        StateChanged += (_, _) => UpdateFrameForState();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        SearchBox.Focus();

        // Lần mở đầu tiên của phiên mới đọc file (trên luồng nền); cửa sổ đã hiện với chữ "Đang tải…".
        await _viewModel.LoadAsync();
    }

    /// <summary>Số thẻ vừa một hàng. Thẻ cuối hàng không cần khoảng cách bên phải, nên cộng bù 12 DIP đó.</summary>
    internal static int ColumnsFor(double availableWidth) =>
        Math.Max(1, (int)((availableWidth - ScrollBarAllowance + 12d) / CellWidth));

    private void OnRowsSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged) _viewModel.ColumnCount = ColumnsFor(e.NewSize.Width);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;

        // Esc lần đầu xoá ô tìm kiếm, lần sau mới đóng — gõ nhầm không mất cả cửa sổ.
        if (!string.IsNullOrEmpty(_viewModel.SearchText))
            _viewModel.SearchText = string.Empty;
        else
            Close();

        e.Handled = true;
    }

    /// <summary>
    /// Cửa sổ không viền phóng to sẽ tràn ra ngoài màn hình đúng bằng độ dày mép kéo giãn; bù lại bằng lề.
    /// </summary>
    private void UpdateFrameForState() =>
        Frame.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
}

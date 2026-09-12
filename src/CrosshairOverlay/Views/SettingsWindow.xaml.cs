using System.Windows;
using CrosshairOverlay.ViewModels;

namespace CrosshairOverlay.Views;

/// <summary>Cửa sổ Settings — editor crosshair, thư viện preset và tuỳ chọn chung.</summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // ViewModel đăng ký sự kiện lên các service singleton (thư viện preset, màn hình,
        // theo dõi foreground). Cửa sổ này được tạo mới mỗi lần mở, nên không giải phóng thì
        // mỗi lần mở lại là một ViewModel nữa bị giữ sống mãi.
        Closed += (_, _) => (DataContext as IDisposable)?.Dispose();
    }
}

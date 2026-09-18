using System.Windows;
using System.Windows.Input;
using CrosshairOverlay.ViewModels;

namespace CrosshairOverlay.Views.Dialogs;

/// <summary>Cửa sổ hỏi cập nhật. Logic nằm ở <see cref="UpdateDialogViewModel"/>.</summary>
public partial class UpdateDialog : Window
{
    private readonly UpdateDialogViewModel _viewModel;

    public UpdateDialog(UpdateDialogViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
        Closed += (_, _) => viewModel.CloseRequested -= OnCloseRequested;
    }

    /// <summary>true khi script cập nhật đã chạy — người gọi phải cho ứng dụng thoát ngay.</summary>
    public bool UpdateApplied { get; private set; }

    private void OnCloseRequested(object? sender, bool applied)
    {
        UpdateApplied = applied;
        DialogResult = applied;
    }

    /// <summary>Cửa sổ không có thanh tiêu đề của Windows, nên thanh tự vẽ phải tự kéo được.</summary>
    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        DragMove();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        // Nút X = "Để sau", và cũng không đóng khi đang tải (xem OnKeyDown).
        if (_viewModel.IsDownloading) return;

        DialogResult = false;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        // Esc = "Để sau", nhưng KHÔNG khi đang tải: đóng giữa chừng thì file tải dở không ai dọn.
        if (e.Key != Key.Escape || _viewModel.IsDownloading) return;

        DialogResult = false;
        e.Handled = true;
    }
}

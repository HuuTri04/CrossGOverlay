using System.ComponentModel;
using System.Windows;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Localization;
using CrosshairOverlay.ViewModels;
using CrosshairOverlay.Views.Dialogs;

namespace CrosshairOverlay.Views;

/// <summary>Cửa sổ Settings — editor crosshair, thư viện preset và tuỳ chọn chung.</summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private readonly ITrayIconController _tray;

    /// <summary>Chỉ nhắc "app vẫn đang chạy" một lần mỗi phiên, không nhắc mỗi lần ẩn.</summary>
    private bool _hintShown;

    public SettingsWindow(SettingsViewModel viewModel, ITrayIconController tray)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _tray = tray;
        DataContext = viewModel;

        // ViewModel đăng ký sự kiện lên các service singleton (thư viện preset, màn hình,
        // theo dõi foreground). Cửa sổ này được tạo mới mỗi lần mở, nên không giải phóng thì
        // mỗi lần mở lại là một ViewModel nữa bị giữ sống mãi.
        Closed += (_, _) => (DataContext as IDisposable)?.Dispose();
    }

    /// <summary>
    /// Quyết định nút X làm gì: thu nhỏ xuống khay, hay hỏi rồi thoát hẳn.
    /// </summary>
    /// <remarks>
    /// Cả hai nhánh đều bắt đầu bằng <c>e.Cancel = true</c>. Ứng dụng chạy ở
    /// <c>ShutdownMode="OnExplicitShutdown"</c>, nên đóng cửa sổ này không tự làm gì tới tiến
    /// trình — nhưng nếu để cửa sổ đóng thật thì ViewModel bị giải phóng và lần mở sau phải
    /// dựng lại từ đầu, chưa kể mất hết trạng thái tab đang chọn.
    ///
    /// <para>
    /// Nhánh thoát KHÔNG tự đi dọn dẹp: <see cref="App.RequestShutdown"/> dẫn tới
    /// <c>App.OnExit</c>, nơi đã ghi nốt hàng đợi lưu file, gỡ hook foreground, gỡ hotkey và
    /// giải phóng toàn bộ container DI. Nhân đôi phần dọn dẹp ở đây chỉ tạo ra một đường thoát
    /// thứ hai để sau này quên cập nhật.
    /// </para>
    /// </remarks>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        // Ứng dụng đang thoát thật (khay, hotkey, hoặc chính lựa chọn "Có" bên dưới) — lần này
        // cửa sổ phải đóng được, nếu không sẽ kẹt lại mãi.
        if (App.IsShuttingDown) return;

        e.Cancel = true;

        if (_viewModel.General.Settings.MinimizeToTrayOnClose)
        {
            HideToTray();
            return;
        }

        if (AppDialogWindow.Confirm(
                this,
                Tr.Get("Exit_ConfirmTitle"),
                Tr.Get("Exit_ConfirmBody"),
                kind: DialogKind.Warning))
        {
            App.RequestShutdown();
        }
    }

    private void HideToTray()
    {
        Hide();

        if (_hintShown) return;
        _hintShown = true;

        // Cửa sổ biến mất mà overlay vẫn vẽ dễ khiến người dùng tưởng app đã thoát và đi tìm
        // cách tắt nó trong Task Manager.
        _tray.ShowNotification(Core.AppInfo.DisplayName, Tr.Get("Tray_HiddenToTray"));
    }
}

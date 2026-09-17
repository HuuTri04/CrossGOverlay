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
    private readonly Microsoft.Extensions.Logging.ILogger<SettingsWindow> _logger;

    /// <summary>Chỉ nhắc "app vẫn đang chạy" một lần mỗi phiên, không nhắc mỗi lần ẩn.</summary>
    private bool _hintShown;

    public SettingsWindow(
        SettingsViewModel viewModel, ITrayIconController tray, Microsoft.Extensions.Logging.ILogger<SettingsWindow> logger)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _tray = tray;
        _logger = logger;
        DataContext = viewModel;

        // ViewModel đăng ký sự kiện lên các service singleton (thư viện preset, màn hình,
        // theo dõi foreground). Cửa sổ này được tạo mới mỗi lần mở, nên không giải phóng thì
        // mỗi lần mở lại là một ViewModel nữa bị giữ sống mãi.
        Closed += (_, _) => (DataContext as IDisposable)?.Dispose();
    }

    /// <summary>
    /// Nạp cột trình chỉnh sửa SAU khi khung hình đầu tiên đã vẽ xong.
    /// </summary>
    /// <remarks>
    /// Đo trên máy thật: dựng cửa sổ Settings tốn ~1000 ms lúc khởi động, trong đó riêng cột chỉnh
    /// sửa (mấy chục thanh trượt, ô chọn màu, nhóm gập) chiếm ~450 ms. Người dùng không cần nó ở
    /// mili-giây đầu tiên — họ cần thấy cửa sổ. Nên cửa sổ hiện ra với danh sách preset và khung xem
    /// trước trước, cột chỉnh sửa điền vào ngay nhịp dispatcher kế tiếp.
    ///
    /// <para>
    /// <see cref="OnContentRendered"/> chạy SAU khung hình đầu tiên, và còn lùi thêm một nhịp ưu tiên
    /// Background để khung hình đó kịp lên màn hình. Gán ContentTemplate là thứ kích hoạt việc dựng
    /// cây giao diện từ DataTemplate.
    /// </para>
    /// </remarks>
    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        var editor = (DataTemplate)FindResource("EditorPanel");
        if (ReferenceEquals(EditorHost.ContentTemplate, editor)) return;

        // Con số người dùng thực sự cảm nhận: từ lúc bấm mở app tới lúc cửa sổ có hình trên màn hình.
        LogFirstFrame();

        // Loaded: ngay sau khung hình đầu tiên, TRƯỚC khi xử lý chuột/bàn phím — khoảng trống chỉ
        // kéo dài đúng thời gian dựng cây, không bị lùi thêm vì người dùng động vào cửa sổ.
        Dispatcher.InvokeAsync(
            () =>
            {
                var watch = global::System.Diagnostics.Stopwatch.StartNew();
                EditorHost.ContentTemplate = editor;
                EditorHost.UpdateLayout();

                Microsoft.Extensions.Logging.LoggerExtensions.LogDebug(
                    _logger, "Cột chỉnh sửa nạp trong {Elapsed:0} ms sau khung hình đầu.", watch.Elapsed.TotalMilliseconds);
            },
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void LogFirstFrame()
    {
        try
        {
            using var process = global::System.Diagnostics.Process.GetCurrentProcess();
            Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(
                _logger,
                "Cửa sổ Settings có khung hình đầu sau {Elapsed:0} ms kể từ khi tiến trình khởi động.",
                (DateTime.Now - process.StartTime).TotalMilliseconds);
        }
        catch (Exception)
        {
            // Chỉ là số đo; không đọc được giờ khởi động của tiến trình thì bỏ qua.
        }
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

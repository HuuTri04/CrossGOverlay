using System.Windows;
using System.Windows.Controls;
using CrosshairOverlay.Core;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Localization;
using CrosshairOverlay.ViewModels;
using CrosshairOverlay.Views;
using H.NotifyIcon;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay.Services.System;

/// <inheritdoc cref="ITrayIconController"/>
public sealed class TrayIconController : ITrayIconController
{
    private readonly ILogger<TrayIconController> _logger;

    private TaskbarIcon? _icon;
    private TrayMenuViewModel? _menu;
    private global::System.Drawing.Icon? _currentIcon;
    private SystemTrayToastWindow? _toast;
    private string _activePresetName = "—";
    private bool _overlayEnabled = true;
    private bool _overlayVisible = true;
    private bool _disposed;
    private int _iconCleanupStarted;

    public TrayIconController(ILogger<TrayIconController> logger) => _logger = logger;

    public event EventHandler? OpenSettingsRequested;
    public event EventHandler? ToggleOverlayRequested;
    public event EventHandler? ExitRequested;

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_icon is not null) return;

        // Menu khai báo trong Resources/TrayMenu.xaml, bind vào ViewModel: nhãn nút bật/tắt tự đổi
        // theo trạng thái thật, các nút gọi lệnh chứ không gắn sự kiện Click.
        _menu = new TrayMenuViewModel(
            toggleOverlay: () => ToggleOverlayRequested?.Invoke(this, EventArgs.Empty),
            openSettings: () => OpenSettingsRequested?.Invoke(this, EventArgs.Empty),
            exit: () => ExitRequested?.Invoke(this, EventArgs.Empty))
        {
            IsOverlayEnabled = _overlayEnabled,
        };

        var menu = (ContextMenu)Application.Current.FindResource("TrayContextMenu");
        menu.DataContext = _menu;

        // Tooltip của icon cũng phải đổi ngôn ngữ; nhãn menu thì ViewModel tự lo.
        TranslationSource.Instance.PropertyChanged += OnLanguageChanged;

        _currentIcon = TrayIconFactory.Create(enabled: true);

        _icon = new TaskbarIcon
        {
            // DataContext PHẢI gán cho chính icon, và TRƯỚC ContextMenu. H.NotifyIcon tự đồng bộ
            // DataContext của icon sang menu: icon không có DataContext thì nó gán menu.DataContext
            // = chính TaskbarIcon, đè mất ViewModel đã gán cho menu. Kiểm chứng trên app thật: menu
            // mở ra với cả ba mục trống chữ vì mọi Header bind vào một TaskbarIcon.
            DataContext = _menu,
            Icon = _currentIcon,
            ToolTipText = AppInfo.DisplayName,
            ContextMenu = menu,
            NoLeftClickDelay = true,
        };

        _icon.TrayMouseDoubleClick += (_, _) => OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
        _icon.ForceCreate();

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        _logger.LogInformation("Tray icon đã khởi tạo.");
    }

    public void UpdateState(bool overlayEnabled, bool overlayVisible, string activePresetName)
    {
        // Nhớ lại trạng thái để dựng lại tooltip khi đổi ngôn ngữ.
        _overlayEnabled = overlayEnabled;
        _overlayVisible = overlayVisible;
        _activePresetName = activePresetName;

        if (_icon is null) return;

        _icon.ToolTipText = Tooltip(overlayEnabled, overlayVisible, activePresetName);

        var previous = _currentIcon;
        _currentIcon = TrayIconFactory.Create(overlayEnabled);
        _icon.Icon = _currentIcon;
        previous?.Dispose();

        // Binding đẩy nhãn mới ("Bật overlay"/"Tắt overlay") lên menu ngay, kể cả khi menu đang mở.
        if (_menu is not null) _menu.IsOverlayEnabled = overlayEnabled;
    }

    /// <summary>
    /// Tooltip của icon: phân biệt ĐƯỢC ba trạng thái mà màu icon gộp làm hai.
    /// </summary>
    /// <remarks>
    /// Icon chỉ xám khi người dùng tắt overlay. Trường hợp đang bật mà overlay tạm ẩn theo game
    /// profile (chế độ "chỉ hiện trong game", hoặc profile ẩn overlay) trông giống lúc đang hiện, nên
    /// tooltip phải nói rõ — nếu không người dùng không có cách nào biết vì sao không thấy crosshair.
    /// </remarks>
    internal static string Tooltip(bool overlayEnabled, bool overlayVisible, string activePresetName)
    {
        if (!overlayEnabled) return Tr.Format("Tray_TooltipOff", AppInfo.DisplayName);
        if (!overlayVisible) return Tr.Format("Tray_TooltipHiddenByProfile", AppInfo.DisplayName);

        return $"{AppInfo.DisplayName} — {activePresetName}";
    }

    /// <summary>Dựng lại tooltip theo ngôn ngữ mới.</summary>
    private void OnLanguageChanged(object? sender, global::System.ComponentModel.PropertyChangedEventArgs e) =>
        UpdateState(_overlayEnabled, _overlayVisible, _activePresetName);

    public void ShowNotification(string title, string message, bool isWarning = false)
    {
        if (_icon is null || _disposed) return;

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return;
        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(() => ShowNotification(title, message, isWarning)));
            return;
        }

        try
        {
            _toast?.Close();
            var toast = new SystemTrayToastWindow(title, message,
                () => OpenSettingsRequested?.Invoke(this, EventArgs.Empty));
            toast.Closed += (_, _) => { if (ReferenceEquals(_toast, toast)) _toast = null; };
            _toast = toast;
            toast.ShowToast();
        }
        catch (Exception ex)
        {
            _toast?.Close();
            _toast = null;
            _logger.LogDebug(ex, "Không hiển thị được thông báo khay tùy chỉnh.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;

        TranslationSource.Instance.PropertyChanged -= OnLanguageChanged;

        _toast?.Close();
        _toast = null;

        _menu?.Dispose();
        _menu = null;

        CleanupTrayIcon();

        _currentIcon?.Dispose();
        _currentIcon = null;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e) => CleanupTrayIcon();

    private void OnProcessExit(object? sender, EventArgs e) => CleanupTrayIcon();

    private void CleanupTrayIcon()
    {
        if (Interlocked.Exchange(ref _iconCleanupStarted, 1) != 0) return;

        var icon = _icon;
        _icon = null;
        if (icon is null) return;

        // H.NotifyIcon dùng Visibility thay cho NotifyIcon.Visible. Dispose gửi NIM_DELETE
        // đến Explorer; cố gắng gọi cả hai kể cả khi quá trình thoát đang ở luồng nền.
        try { icon.Visibility = Visibility.Collapsed; }
        catch (Exception) { /* Dispatcher có thể đã dừng khi ProcessExit chạy. */ }
        try { icon.Dispose(); }
        catch (Exception) { /* Không để lỗi dọn icon cản quá trình thoát. */ }
    }
}

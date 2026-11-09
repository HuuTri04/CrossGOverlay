using System.Windows;
using System.Windows.Controls;
using CrosshairOverlay.Core;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Localization;
using CrosshairOverlay.ViewModels;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay.Services.System;

/// <inheritdoc cref="ITrayIconController"/>
public sealed class TrayIconController : ITrayIconController
{
    private readonly ILogger<TrayIconController> _logger;
    private readonly IPresetLibrary _library;
    private readonly IAppSettingsService _settings;

    private TaskbarIcon? _icon;
    private ContextMenu? _contextMenu;
    private TrayMenuViewModel? _menu;
    private global::System.Drawing.Icon? _currentIcon;
    private WindowsNotificationService? _notifications;
    private string? _lastNotification;
    private DateTimeOffset _lastNotificationAt;
    private string _activePresetName = "—";
    private bool _overlayEnabled = true;
    private bool _overlayVisible = true;
    private bool _disposed;
    private int _iconCleanupStarted;

    public TrayIconController(ILogger<TrayIconController> logger,
        IPresetLibrary library, IAppSettingsService settings)
    {
        _logger = logger;
        _library = library;
        _settings = settings;
    }

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
        menu.Opened += OnMenuOpened;
        _contextMenu = menu;

        // Tooltip của icon cũng phải đổi ngôn ngữ; nhãn menu thì ViewModel tự lo.
        TranslationSource.Instance.PropertyChanged += OnLanguageChanged;

        _currentIcon = TrayIconFactory.Create(enabled: _overlayEnabled);

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
        try { _icon.ForceCreate(); }
        catch
        {
            // The shell may not be ready (or may be absent in a benchmark session).
            // Roll back so the same controller can retry once Settings is visible.
            _icon.Dispose();
            _icon = null;
            _currentIcon?.Dispose();
            _currentIcon = null;
            _menu.Dispose();
            _menu = null;
            menu.Opened -= OnMenuOpened;
            _contextMenu = null;
            TranslationSource.Instance.PropertyChanged -= OnLanguageChanged;
            throw;
        }
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        _logger.LogInformation("Tray icon đã khởi tạo.");
    }

    public void InitializeNotifications()
    {
        if (_disposed || _icon is null || _notifications is not null) return;
        var notifications = new WindowsNotificationService(_logger,
            () => OpenSettingsRequested?.Invoke(this, EventArgs.Empty));
        notifications.Initialize();
        _notifications = notifications;
    }

    private void OnMenuOpened(object? sender, RoutedEventArgs e)
    {
        _menu?.SetFavorites(_library.Presets.Where(preset => preset.IsFavorite)
            .Select(preset => (preset.Id, preset.Name)), id =>
            {
                var preset = _library.Presets.FirstOrDefault(item => item.Id == id && item.IsFavorite);
                if (preset is not null) _library.SetActive(preset);
            });
    }

    public void UpdateState(bool overlayEnabled, bool overlayVisible, string activePresetName)
    {
        // Nhớ lại trạng thái để dựng lại tooltip khi đổi ngôn ngữ.
        _overlayEnabled = overlayEnabled;
        _overlayVisible = overlayVisible;
        _activePresetName = activePresetName;

        if (_icon is null) return;

        _icon.ToolTipText = Tooltip(overlayEnabled, overlayVisible, activePresetName);

        // Tooltip and active-preset changes are common; the GDI icon only has two visual
        // states. Recreate its HICON only when that state actually changes.
        if (_currentIcon is null || _menu?.IsOverlayEnabled != overlayEnabled)
        {
            var next = TrayIconFactory.Create(overlayEnabled);
            var previous = _currentIcon;
            _icon.Icon = next;
            _currentIcon = next;
            previous?.Dispose();
        }

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

    public void ShowNotification(string title, string message, bool isWarning = false,
        NotificationCategory category = NotificationCategory.AppStatus)
    {
        if (_icon is null || _disposed) return;

        var options = _settings.Current;
        if (category switch
            {
                NotificationCategory.MinimizeToTray => !options.NotifyOnMinimizeToTray,
                NotificationCategory.PresetChanged => !options.NotifyOnPresetChanged,
                _ => !options.NotifyOnAppStatus,
            }) return;

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return;
        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(() => ShowNotification(title, message, isWarning, category)));
            return;
        }

        if (string.IsNullOrWhiteSpace(message)) return;
        var key = $"{title}\n{message}";
        var now = DateTimeOffset.UtcNow;
        if (key == _lastNotification && now - _lastNotificationAt < TimeSpan.FromSeconds(10)) return;

        if (_notifications?.TryShow(title, message) == true)
        {
            _lastNotification = key;
            _lastNotificationAt = now;
            return;
        }

        try
        {
            // Native Shell_NotifyIcon fallback for elevated processes (unsupported by
            // Windows App SDK). Never open a second WPF HWND over the game.
            _icon.ShowNotification(title, message,
                isWarning ? NotificationIcon.Warning : NotificationIcon.None,
                customIconHandle: _currentIcon?.Handle,
                largeIcon: true,
                sound: false,
                respectQuietTime: true);
            _lastNotification = key;
            _lastNotificationAt = now;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Không hiển thị được thông báo khay native.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;

        TranslationSource.Instance.PropertyChanged -= OnLanguageChanged;

        if (_contextMenu is not null) _contextMenu.Opened -= OnMenuOpened;
        _contextMenu = null;

        _notifications?.Dispose();
        _notifications = null;

        _menu?.Dispose();
        _menu = null;

        CleanupTrayIcon();

    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e) => CleanupTrayIcon();

    private void OnProcessExit(object? sender, EventArgs e) => CleanupTrayIcon();

    private void CleanupTrayIcon()
    {
        if (Interlocked.Exchange(ref _iconCleanupStarted, 1) != 0) return;

        var icon = Interlocked.Exchange(ref _icon, null);
        if (icon is not null)
        {
            // H.NotifyIcon uses Visibility rather than NotifyIcon.Visible. Dispose sends
            // NIM_DELETE even when ProcessExit arrives on a background thread.
            try { icon.Visibility = Visibility.Collapsed; }
            catch (Exception ex) { global::System.Diagnostics.Trace.WriteLine($"[TrayIcon] Hide failed during cleanup: {ex}"); }
            try { icon.Dispose(); }
            catch (Exception ex) { global::System.Diagnostics.Trace.WriteLine($"[TrayIcon] Dispose failed during cleanup: {ex}"); }
        }

        Interlocked.Exchange(ref _currentIcon, null)?.Dispose();
    }
}

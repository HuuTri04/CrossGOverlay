using System.Windows.Controls;
using CrosshairOverlay.Core;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Localization;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay.Services.System;

/// <inheritdoc cref="ITrayIconController"/>
public sealed class TrayIconController : ITrayIconController
{
    private readonly ILogger<TrayIconController> _logger;

    private TaskbarIcon? _icon;
    private MenuItem? _toggleItem;
    private MenuItem? _openItem;
    private MenuItem? _exitItem;
    private global::System.Drawing.Icon? _currentIcon;
    private string _activePresetName = "—";
    private bool _overlayEnabled = true;
    private bool _disposed;

    public TrayIconController(ILogger<TrayIconController> logger) => _logger = logger;

    public event EventHandler? OpenSettingsRequested;
    public event EventHandler? ToggleOverlayRequested;
    public event EventHandler? ExitRequested;

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_icon is not null) return;

        _toggleItem = new MenuItem
        {
            Header = Tr.Get("Tray_Toggle"),
            IsCheckable = true,
            IsChecked = true,
        };
        _toggleItem.Click += (_, _) => ToggleOverlayRequested?.Invoke(this, EventArgs.Empty);

        _openItem = new MenuItem { Header = Tr.Get("Tray_Open") };
        _openItem.Click += (_, _) => OpenSettingsRequested?.Invoke(this, EventArgs.Empty);

        _exitItem = new MenuItem { Header = Tr.Get("Tray_Exit") };
        _exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        // Menu khay không nằm trong cây XAML nên {loc:Loc} không với tới được; đăng ký thủ công.
        TranslationSource.Instance.PropertyChanged += OnLanguageChanged;

        // Giao diện menu do Style ContextMenu/MenuItem trong Theme.xaml quyết định: nền tối,
        // và quan trọng nhất là KHÔNG còn cột icon sáng màu ở mép trái như template mặc định.
        var menu = new ContextMenu();
        menu.Items.Add(_openItem);
        menu.Items.Add(_toggleItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(_exitItem);

        _currentIcon = TrayIconFactory.Create(enabled: true);

        _icon = new TaskbarIcon
        {
            Icon = _currentIcon,
            ToolTipText = AppInfo.DisplayName,
            ContextMenu = menu,
            NoLeftClickDelay = true,
        };

        _icon.TrayMouseDoubleClick += (_, _) => OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
        _icon.ForceCreate();

        _logger.LogInformation("Tray icon đã khởi tạo.");
    }

    public void UpdateState(bool overlayEnabled, string activePresetName)
    {
        // Nhớ lại trạng thái để dựng lại tooltip khi đổi ngôn ngữ.
        _overlayEnabled = overlayEnabled;
        _activePresetName = activePresetName;

        if (_icon is null) return;

        _icon.ToolTipText = overlayEnabled
            ? $"{AppInfo.DisplayName} — {activePresetName}"
            : Tr.Format("Tray_TooltipOff", AppInfo.DisplayName);

        var previous = _currentIcon;
        _currentIcon = TrayIconFactory.Create(overlayEnabled);
        _icon.Icon = _currentIcon;
        previous?.Dispose();

        if (_toggleItem is not null) _toggleItem.IsChecked = overlayEnabled;
    }

    /// <summary>
    /// Menu khay không nằm trong cây XAML nên markup extension {loc:Loc} không với tới được —
    /// phải cập nhật nhãn bằng tay khi đổi ngôn ngữ.
    /// </summary>
    private void OnLanguageChanged(object? sender, global::System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_toggleItem is not null) _toggleItem.Header = Tr.Get("Tray_Toggle");
        if (_openItem is not null) _openItem.Header = Tr.Get("Tray_Open");
        if (_exitItem is not null) _exitItem.Header = Tr.Get("Tray_Exit");

        UpdateState(_overlayEnabled, _activePresetName);
    }

    public void ShowNotification(string title, string message, bool isWarning = false)
    {
        if (_icon is null) return;

        try
        {
            _icon.ShowNotification(
                title,
                message,
                isWarning ? NotificationIcon.Warning : NotificationIcon.Info);
        }
        catch (Exception ex)
        {
            // Bong bóng thông báo có thể bị chính sách hệ thống hoặc Focus Assist chặn.
            _logger.LogDebug(ex, "Không hiển thị được thông báo khay hệ thống.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        TranslationSource.Instance.PropertyChanged -= OnLanguageChanged;

        _icon?.Dispose();
        _icon = null;

        _currentIcon?.Dispose();
        _currentIcon = null;
    }
}

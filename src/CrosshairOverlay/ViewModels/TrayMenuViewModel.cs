using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrosshairOverlay.Localization;

namespace CrosshairOverlay.ViewModels;

/// <summary>ViewModel của menu chuột phải trên icon khay hệ thống.</summary>
/// <remarks>
/// Menu không biết overlay hay thư viện preset là gì: nó chỉ hiện trạng thái được đẩy vào qua
/// <see cref="IsOverlayEnabled"/> và gọi lại các hành động được truyền vào lúc khởi tạo. Nhờ vậy
/// test được mà không cần dựng icon khay hay cửa sổ nào.
/// </remarks>
public sealed partial class TrayMenuViewModel : ObservableObject, IDisposable
{
    private readonly Action _toggleOverlay;
    private readonly Action _openSettings;
    private readonly Action _exit;
    private bool _disposed;

    public TrayMenuViewModel(Action toggleOverlay, Action openSettings, Action exit)
    {
        _toggleOverlay = toggleOverlay ?? throw new ArgumentNullException(nameof(toggleOverlay));
        _openSettings = openSettings ?? throw new ArgumentNullException(nameof(openSettings));
        _exit = exit ?? throw new ArgumentNullException(nameof(exit));

        // Menu khay nằm ngoài mọi cửa sổ nên {loc:Loc} không với tới; tự báo nhãn đổi khi đổi ngôn ngữ.
        TranslationSource.Instance.PropertyChanged += OnLanguageChanged;
    }

    /// <summary>
    /// Người dùng có BẬT overlay không — dấu tích trong menu.
    /// </summary>
    /// <remarks>
    /// Đây là lựa chọn của người dùng, không phải overlay có đang hiện hay không. Ở chế độ "chỉ
    /// hiện trong game", overlay cố ý ẩn trên desktop nhưng vẫn đang bật; mất dấu tích lúc đó sẽ
    /// khiến người dùng tưởng đã tắt và bấm bật lại. Trạng thái tạm ẩn được báo ở tooltip của icon.
    /// </remarks>
    [ObservableProperty]
    private bool _isOverlayEnabled = true;

    /// <summary>
    /// Nhãn CỐ ĐỊNH của nút bật/tắt. Trạng thái do dấu tích thể hiện, không do chữ: nhãn đổi theo
    /// trạng thái cộng thêm dấu tích ("Tắt overlay ✓") đọc lên rất dễ hiểu nhầm là overlay đang tắt.
    /// </summary>
    public string OverlayText => Tr.Get("Tray_Overlay");

    public string OpenSettingsText => Tr.Get("Tray_Open");

    public string ExitText => Tr.Get("Tray_Exit");

    [RelayCommand]
    private void ToggleOverlay() => _toggleOverlay();

    [RelayCommand]
    private void OpenSettings() => _openSettings();

    [RelayCommand]
    private void Exit() => _exit();

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(OverlayText));
        OnPropertyChanged(nameof(OpenSettingsText));
        OnPropertyChanged(nameof(ExitText));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        TranslationSource.Instance.PropertyChanged -= OnLanguageChanged;
    }
}

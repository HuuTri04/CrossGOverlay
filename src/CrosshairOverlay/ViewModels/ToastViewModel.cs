using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CrosshairOverlay.ViewModels;

/// <summary>
/// Thông báo nhỏ nổi lên ở góc rồi tự tắt ("Đã thêm … vào danh sách của bạn", "Bạn đang dùng phiên bản mới nhất").
/// </summary>
/// <remarks>
/// Dùng cho những việc đã xong xuôi và không cần người dùng làm gì: một hộp thoại phải bấm OK cho mỗi lần thêm preset
/// sẽ chặn đúng thao tác họ đang làm. Timer chỉ sống trong vài giây sau mỗi lần hiện rồi tự dừng — không có nhịp nền
/// nào chạy suốt phiên.
/// </remarks>
public sealed partial class ToastViewModel : ObservableObject, IDisposable
{
    /// <summary>Đủ lâu để đọc một câu, đủ ngắn để không che nội dung.</summary>
    internal static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(2500);

    private DispatcherTimer? _timer;
    private bool _disposed;

    [ObservableProperty] private string? _text;

    [ObservableProperty] private bool _isVisible;

    public void Show(string text)
    {
        if (_disposed) return;

        Text = text;
        IsVisible = true;

        _timer ??= Create();
        _timer.Stop();
        _timer.Start();
    }

    private DispatcherTimer Create()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = Duration };
        timer.Tick += OnTick;
        return timer;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _timer?.Stop();
        IsVisible = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_timer is null) return;

        _timer.Stop();
        _timer.Tick -= OnTick;
        _timer = null;
    }
}

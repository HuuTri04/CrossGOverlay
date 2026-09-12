using System.Windows.Interop;
using CrosshairOverlay.Interop;

namespace CrosshairOverlay.Services.Input;

/// <summary>
/// Cửa sổ message-only để nhận <c>WM_HOTKEY</c>.
/// </summary>
/// <remarks>
/// <c>RegisterHotKey</c> cần một HWND để Windows gửi message tới. Dùng cửa sổ message-only
/// (<c>HWND_MESSAGE</c>) thay vì cửa sổ Settings vì hotkey phải sống độc lập với việc cửa sổ
/// đó đang mở hay đã đóng, và cửa sổ này không bao giờ hiện ra hay xuất hiện trong Alt-Tab.
/// </remarks>
public sealed class HotkeyMessageWindow : IDisposable
{
    private readonly HwndSource _source;
    private bool _disposed;

    public HotkeyMessageWindow()
    {
        var parameters = new HwndSourceParameters("CrosshairOverlay.HotkeySink")
        {
            ParentWindow = Win32Constants.HWND_MESSAGE,
            WindowStyle = 0,
            Width = 0,
            Height = 0,
        };

        _source = new HwndSource(parameters);
    }

    public nint Handle => _source.Handle;

    public void AddHook(HwndSourceHook hook) => _source.AddHook(hook);

    public void RemoveHook(HwndSourceHook hook) => _source.RemoveHook(hook);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _source.Dispose();
    }
}

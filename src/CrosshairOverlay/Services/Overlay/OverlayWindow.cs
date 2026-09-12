using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using CrosshairOverlay.Interop;

namespace CrosshairOverlay.Services.Overlay;

/// <summary>
/// Cửa sổ overlay: trong suốt, không viền, luôn trên cùng, xuyên thấu chuột, không cướp focus.
/// </summary>
/// <remarks>
/// Viết bằng code thay vì XAML vì cửa sổ này không có nội dung khai báo — nó chỉ là một
/// surface đặt <see cref="CrosshairVisualHost"/> lên.
///
/// <para>
/// <c>AllowsTransparency = true</c> khiến WPF render cửa sổ này bằng software và composite qua
/// <c>UpdateLayeredWindow</c>. Đây là đánh đổi có chủ ý: cửa sổ chỉ rộng vài chục pixel và chỉ
/// vẽ lại khi preset đổi, nên chi phí không đáng kể — đổi lại ta có per-pixel alpha đáng tin cậy
/// trên mọi phiên bản Windows 10/11, thứ mà các thủ thuật DWM không bảo đảm được.
/// </para>
/// </remarks>
internal sealed class OverlayWindow : Window
{
    public OverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;

        // Ba thứ này chặn overlay lấy focus ở tầng WPF; WS_EX_NOACTIVATE chặn ở tầng Win32.
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;

        WindowStartupLocation = WindowStartupLocation.Manual;

        // Bố cục phải rơi đúng biên pixel vật lý, nếu không mọi công sức bám lưới pixel trong
        // renderer đều bị một phép làm tròn nửa pixel ở tầng layout phá hỏng.
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        // Đặt ngoài màn hình với kích thước tối thiểu cho tới khi OverlayController
        // canh vị trí thật — tránh một khung nháy ở góc trên trái lúc khởi tạo.
        Left = -32000;
        Top = -32000;
        Width = 1;
        Height = 1;

        Host = new CrosshairVisualHost();
        Content = Host;
    }

    public CrosshairVisualHost Host { get; }

    /// <summary>HWND của cửa sổ. Bằng 0 trước khi handle được tạo.</summary>
    public nint Handle { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        Handle = new WindowInteropHelper(this).Handle;
        ApplyOverlayExtendedStyles(Handle);
    }

    /// <summary>
    /// Tạo HWND mà không hiển thị cửa sổ, để style được áp trước lần Show đầu tiên.
    /// </summary>
    public nint EnsureHandle()
    {
        if (Handle == 0)
            Handle = new WindowInteropHelper(this).EnsureHandle();

        return Handle;
    }

    private static void ApplyOverlayExtendedStyles(nint hwnd)
    {
        if (hwnd == 0) return;

        var exStyle = (long)NativeMethods.GetWindowLongPtr(hwnd, Win32Constants.GWL_EXSTYLE);

        // WS_EX_LAYERED đã do AllowsTransparency đặt; OR thêm cho chắc.
        exStyle |= Win32Constants.WS_EX_LAYERED     // per-pixel alpha
                 | Win32Constants.WS_EX_TRANSPARENT // click xuyên thấu xuống game
                 | Win32Constants.WS_EX_TOOLWINDOW  // biến khỏi Alt-Tab
                 | Win32Constants.WS_EX_NOACTIVATE; // không bao giờ nhận activation

        // Overlay không phải là cửa sổ ứng dụng.
        exStyle &= ~(long)Win32Constants.WS_EX_APPWINDOW;

        NativeMethods.SetWindowLongPtr(hwnd, Win32Constants.GWL_EXSTYLE, (nint)exStyle);
    }
}

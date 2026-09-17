using System.Windows;

namespace CrosshairOverlay.Views;

/// <summary>
/// Màn hình chờ lúc khởi động: hiện càng sớm càng tốt, và chỉ đóng khi cửa sổ chính ĐÃ có hình.
/// </summary>
/// <remarks>
/// Đóng ở sự kiện <c>Loaded</c> của cửa sổ chính là quá sớm: Loaded chạy trước khi khung hình đầu
/// được vẽ, nên giữa lúc màn hình chờ biến mất và lúc cửa sổ chính hiện hình có một khoảng trống —
/// chính là cú "chớp" cần tránh. <see cref="Window.ContentRendered"/> chạy SAU khung hình đầu.
/// </remarks>
public partial class SplashWindow : Window
{
    /// <summary>Lưới an toàn: cửa sổ chính không bao giờ vẽ được thì màn hình chờ cũng không được treo mãi.</summary>
    private static readonly TimeSpan MaxLifetime = TimeSpan.FromSeconds(15);

    public SplashWindow()
    {
        InitializeComponent();
    }

    /// <summary>Có khung hình đầu từ lúc nào — để ghi log thời gian người dùng thấy được thứ gì đó.</summary>
    public event EventHandler? FirstFrameRendered;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        FirstFrameRendered?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Đóng màn hình chờ ngay sau khung hình đầu của <paramref name="next"/>.
    /// </summary>
    /// <remarks>
    /// Cửa sổ chính mở ra phủ lên màn hình chờ trước, vẽ xong khung đầu, rồi màn hình chờ mới đóng — nên
    /// lúc nào trên màn hình cũng có hình, không có khoảnh khắc trống.
    /// </remarks>
    public void CloseAfterFirstFrameOf(Window? next)
    {
        if (next is null || !next.IsVisible)
        {
            CloseQuietly();
            return;
        }

        next.ContentRendered += OnNextRendered;
        next.Closed += OnNextClosed;

        _ = CloseAfterTimeoutAsync();

        void OnNextRendered(object? sender, EventArgs e)
        {
            next.ContentRendered -= OnNextRendered;
            next.Closed -= OnNextClosed;
            CloseQuietly();
        }

        void OnNextClosed(object? sender, EventArgs e)
        {
            next.ContentRendered -= OnNextRendered;
            next.Closed -= OnNextClosed;
            CloseQuietly();
        }
    }

    private async Task CloseAfterTimeoutAsync()
    {
        await Task.Delay(MaxLifetime).ConfigureAwait(true);
        CloseQuietly();
    }

    /// <summary>Đóng được gọi nhiều lần từ nhiều nhánh (đã vẽ, cửa sổ chính đóng, hết giờ, lỗi khởi động).</summary>
    public void CloseQuietly()
    {
        if (!IsLoaded && !IsVisible) return;

        try
        {
            Close();
        }
        catch (InvalidOperationException)
        {
            // Đang trong quá trình đóng rồi.
        }
    }
}

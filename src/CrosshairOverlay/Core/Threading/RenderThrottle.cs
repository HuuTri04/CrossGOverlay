using System.Diagnostics;
using System.Windows.Threading;

namespace CrosshairOverlay.Core.Threading;

/// <summary>
/// Gom nhiều yêu cầu vẽ lại thành tối đa MỘT lần mỗi khung hình.
/// </summary>
/// <remarks>
/// Bài toán: kéo một thanh trượt bắn ra <c>PropertyChanged</c> theo tần số polling của chuột —
/// chuột gaming 1000Hz cho ra tới 1000 sự kiện mỗi giây. Xếp hàng ở
/// <see cref="DispatcherPriority.Render"/> KHÔNG chặn được điều đó: hàng đợi dispatcher được
/// bơm theo message chứ không theo nhịp khung hình, nên mỗi lượt xếp hàng vẫn chạy xong rồi
/// lượt sau lại vào. Kết quả là dựng lại hình học, đo đạc và <c>SetWindowPos</c> cả nghìn lần
/// mỗi giây, trong khi màn hình chỉ hiển thị được 60.
///
/// <para>
/// Cách chặn ở đây là chặn theo THỜI GIAN, mép trước cộng mép sau:
/// yêu cầu đầu tiên chạy ngay lập tức (không thêm độ trễ cảm nhận được), các yêu cầu tới trong
/// vòng 16ms sau đó bị gộp lại thành đúng một lượt chạy ở cuối cửa sổ. Nhờ mép sau, giá trị
/// CUỐI CÙNG của cú kéo không bao giờ bị bỏ rơi — thiếu nó thì thả chuột xong crosshair có thể
/// đứng lại ở giá trị áp chót.
/// </para>
///
/// <para>
/// Bộ đếm thời gian chỉ chạy khi đang có yêu cầu chờ và tự dừng ngay sau đó, nên lúc rảnh
/// không có nhịp đập nền nào — đúng nguyên tắc "không polling" của phần còn lại trong ứng dụng.
/// </para>
/// </remarks>
internal sealed class RenderThrottle : IDisposable
{
    /// <summary>Một khung hình ở 60fps.</summary>
    public static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(16);

    private readonly DispatcherTimer _timer;
    private readonly Action _action;
    private readonly TimeSpan _interval;

    private long _lastRun;
    private bool _pending;
    private bool _disposed;

    public RenderThrottle(Dispatcher dispatcher, Action action, TimeSpan? interval = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(action);

        _action = action;
        _interval = interval ?? FrameInterval;

        _timer = new DispatcherTimer(DispatcherPriority.Render, dispatcher) { Interval = _interval };
        _timer.Tick += OnTick;
    }

    /// <summary>Xin một lượt vẽ lại. Gọi bao nhiêu lần cũng được; chi phí thừa gần bằng không.</summary>
    public void Request()
    {
        if (_disposed) return;

        // Đã có một lượt đang chờ — chỉ cần đánh dấu, timer sẽ lo nốt.
        if (_timer.IsEnabled)
        {
            _pending = true;
            return;
        }

        var remaining = _interval - Stopwatch.GetElapsedTime(_lastRun);
        if (remaining > TimeSpan.Zero)
        {
            _pending = true;
            _timer.Interval = remaining;
            _timer.Start();
            return;
        }

        Run();
    }

    /// <summary>
    /// Chạy ngay phần đang chờ, nếu có. Dùng khi không thể đợi thêm — ví dụ overlay vừa được
    /// bật lên và phải hiện đúng hình ngay lập tức.
    /// </summary>
    public void Flush()
    {
        if (_disposed || !_pending) return;

        _timer.Stop();
        Run();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _timer.Stop();

        // Cửa sổ trôi qua mà không có yêu cầu mới: không có gì để làm, và timer đã dừng nên
        // từ giờ hệ thống hoàn toàn im lặng cho tới lần Request kế tiếp.
        if (!_pending) return;

        Run();
    }

    private void Run()
    {
        _pending = false;
        _lastRun = Stopwatch.GetTimestamp();
        _action();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timer.Stop();
        _timer.Tick -= OnTick;
        _pending = false;
    }
}

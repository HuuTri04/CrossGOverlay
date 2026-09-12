using System.Windows.Threading;
using CrosshairOverlay.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay.Services.System;

/// <inheritdoc cref="ISingleInstanceGuard"/>
/// <remarks>
/// Chạy hai instance cùng lúc sẽ có hai overlay chồng nhau và hai bên tranh nhau đăng ký cùng
/// bộ hotkey — instance thứ hai luôn thua. Thay vì để tình trạng đó xảy ra, instance thứ hai
/// đánh thức instance đang chạy rồi tự thoát.
///
/// <para>
/// Mutex chỉ trả lời được "đã có instance nào chưa". Việc đánh thức cần một kênh riêng, ở đây
/// là <see cref="EventWaitHandle"/> có tên — nhẹ hơn nhiều so với dựng pipe hay socket.
/// </para>
/// </remarks>
public sealed class SingleInstanceGuard : ISingleInstanceGuard
{
    private const string MutexName = @"Local\CrosshairOverlay.SingleInstance";
    private const string SignalName = @"Local\CrosshairOverlay.Activate";

    private readonly ILogger<SingleInstanceGuard> _logger;
    private readonly Dispatcher _dispatcher;
    private readonly ManualResetEvent _stop = new(false);

    private Mutex? _mutex;
    private EventWaitHandle? _signal;
    private Thread? _listener;
    private bool _owned;
    private bool _disposed;

    public SingleInstanceGuard(ILogger<SingleInstanceGuard> logger)
    {
        _logger = logger;
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    public event EventHandler? SecondInstanceLaunched;

    public bool TryAcquire()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
            _owned = createdNew;

            if (!createdNew)
            {
                _logger.LogInformation("Đã có một instance đang chạy.");
                return false;
            }

            _signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
            StartListener();
            return true;
        }
        catch (Exception ex)
        {
            // Không tạo được mutex (môi trường bị hạn chế) thì cho chạy tiếp — thà có nguy cơ
            // hai instance còn hơn không khởi động được.
            _logger.LogWarning(ex, "Không kiểm tra được instance trùng, vẫn khởi động.");
            return true;
        }
    }

    public void SignalExistingInstance()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(SignalName, out var handle))
            {
                using (handle) handle.Set();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không đánh thức được instance đang chạy.");
        }
    }

    private void StartListener()
    {
        _listener = new Thread(ListenLoop)
        {
            IsBackground = true,
            Name = "CrosshairOverlay.InstanceListener",
        };

        _listener.Start();
    }

    private void ListenLoop()
    {
        if (_signal is null) return;

        WaitHandle[] handles = [_signal, _stop];

        while (!_disposed)
        {
            // Chỉ số 1 là _stop — thoát vòng lặp khi app đóng.
            if (WaitHandle.WaitAny(handles) != 0) return;

            _logger.LogInformation("Instance thứ hai vừa khởi chạy, mở cửa sổ Settings.");
            _dispatcher.BeginInvoke(() => SecondInstanceLaunched?.Invoke(this, EventArgs.Empty));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _stop.Set();
        _listener?.Join(TimeSpan.FromSeconds(1));

        _signal?.Dispose();

        if (_mutex is not null)
        {
            if (_owned)
            {
                try { _mutex.ReleaseMutex(); }
                catch (ApplicationException) { /* không sở hữu nữa, bỏ qua */ }
            }

            _mutex.Dispose();
        }

        _stop.Dispose();
    }
}

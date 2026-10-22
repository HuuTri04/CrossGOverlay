using System.Windows.Threading;

namespace CrosshairOverlay.Services.Overlay;

/// <summary>Owns the OLED shift clock for the lifetime of one overlay.</summary>
internal sealed class OledPixelShiftTimer : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly Action<int, int> _shift;
    private readonly TimeSpan _baseInterval;
    private int _step;

    public OledPixelShiftTimer(Dispatcher dispatcher, Action<int, int> shift, TimeSpan interval)
    {
        _shift = shift;
        _baseInterval = interval;
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = interval };
        _timer.Tick += OnTick;
    }

    public void Configure(bool enabled, bool visible)
    {
        if (enabled && visible)
        {
            if (!_timer.IsEnabled)
            {
                ScheduleNextTick();
                _timer.Start();
            }
        }
        else _timer.Stop();
        if (!enabled)
        {
            _step = 0;
            _shift(0, 0);
        }
    }

    internal bool IsRunning => _timer.IsEnabled;

    private void OnTick(object? sender, EventArgs e)
    {
        var (x, y) = OverlayBehaviorController.PixelShiftOffset(_step++);
        _step %= 4;
        _shift(x, y);
        ScheduleNextTick();
    }

    private void ScheduleNextTick()
    {
        // One existing timer; jitter its next tick, while the displacement remains exactly 1 physical pixel.
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, _baseInterval.TotalMilliseconds * Random.Shared.Next(75, 126) / 100d));
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}

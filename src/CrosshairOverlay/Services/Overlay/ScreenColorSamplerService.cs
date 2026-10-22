using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CrosshairOverlay.Interop;

namespace CrosshairOverlay.Services.Overlay;

/// <summary>Samples the desktop near the crosshair only while Smart Contrast is active.</summary>
public sealed class ScreenColorSamplerService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private Func<Point>? _position;
    private Color? _last;

    public ScreenColorSamplerService()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += OnTick;
    }

    public event EventHandler<Color>? ContrastColorChanged;

    public void Start(Func<Point> position)
    {
        _position = position;
        if (!_timer.IsEnabled) _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _position = null;
        _last = null;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_position is null) return;
        var point = _position();
        var hdc = NativeMethods.GetDC(0);
        if (hdc == 0) return;
        try
        {
            // Sample beside the crosshair to avoid reading its own pixels.
            var pixel = NativeMethods.GetPixel(hdc, (int)point.X + 24, (int)point.Y + 24);
            if (pixel == uint.MaxValue) return;
            var red = pixel & 0xFF;
            var green = (pixel >> 8) & 0xFF;
            var blue = (pixel >> 16) & 0xFF;
            var luminance = (red * 299 + green * 587 + blue * 114) / 1000;
            var color = luminance >= 128 ? Colors.Black : Colors.White;
            if (_last == color) return;
            _last = color;
            ContrastColorChanged?.Invoke(this, color);
        }
        finally
        {
            NativeMethods.ReleaseDC(0, hdc);
        }
    }

    public void Dispose()
    {
        Stop();
        _timer.Tick -= OnTick;
    }
}

using System.Windows.Media;

namespace CrosshairOverlay.Services.Hardware;

/// <summary>
/// Owns an optional RGB backend connection. A Razer or OpenRGB adapter can be attached
/// without making overlay startup depend on installed hardware or vendor software.
/// </summary>
public sealed class RgbSyncService : IDisposable
{
    private readonly object _gate = new();
    private IDisposable? _connection;
    private Action<Color>? _setColor;
    private bool _disposed;

    public void Attach(IDisposable connection, Action<Color> setColor)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(setColor);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _connection?.Dispose();
            _connection = connection;
            _setColor = setColor;
        }
    }

    public void SetColor(Color color)
    {
        lock (_gate)
        {
            if (!_disposed) _setColor?.Invoke(color);
        }
    }

    public void Disconnect()
    {
        lock (_gate)
        {
            _setColor = null;
            _connection?.Dispose();
            _connection = null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            Disconnect();
        }
    }
}

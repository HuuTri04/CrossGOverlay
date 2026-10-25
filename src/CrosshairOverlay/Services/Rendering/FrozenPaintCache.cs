using System.Windows.Media;

namespace CrosshairOverlay.Services.Rendering;

/// <summary>Small, bounded cache for immutable paints. Paths are cached separately by the renderer.</summary>
internal sealed class FrozenPaintCache
{
    private const int MaxBrushes = 64;
    private const int MaxPens = 128;
    private readonly object _gate = new();
    private readonly Dictionary<BrushKey, LinkedListNode<(BrushKey Key, SolidColorBrush Value)>> _brushes = new();
    private readonly LinkedList<(BrushKey Key, SolidColorBrush Value)> _brushRecency = new();
    private readonly Dictionary<PenKey, LinkedListNode<(PenKey Key, Pen Value)>> _pens = new();
    private readonly LinkedList<(PenKey Key, Pen Value)> _penRecency = new();

    private readonly record struct BrushKey(Color Color, double Opacity);
    private readonly record struct PenKey(Color Color, double Opacity, double Thickness, bool Rounded);

    internal (int Brushes, int Pens) Count
    {
        get { lock (_gate) return (_brushes.Count, _pens.Count); }
    }

    public SolidColorBrush Brush(Color color, double opacity)
    {
        var key = new BrushKey(color, NormalizeOpacity(opacity));
        lock (_gate)
        {
            if (_brushes.TryGetValue(key, out var cached))
            {
                _brushRecency.Remove(cached);
                _brushRecency.AddFirst(cached);
                return cached.Value.Value;
            }

            var brush = new SolidColorBrush(color) { Opacity = key.Opacity };
            brush.Freeze();
            _brushes[key] = _brushRecency.AddFirst((key, brush));
            if (_brushes.Count > MaxBrushes)
            {
                var last = _brushRecency.Last!;
                _brushes.Remove(last.Value.Key);
                _brushRecency.RemoveLast();
            }
            return brush;
        }
    }

    public Pen Pen(Color color, double opacity, double thickness, bool rounded)
    {
        var key = new PenKey(color, NormalizeOpacity(opacity), Math.Max(0.1d, thickness), rounded);
        lock (_gate)
        {
            if (_pens.TryGetValue(key, out var cached))
            {
                _penRecency.Remove(cached);
                _penRecency.AddFirst(cached);
                return cached.Value.Value;
            }

            var cap = rounded ? PenLineCap.Round : PenLineCap.Flat;
            var pen = new Pen(Brush(color, key.Opacity), key.Thickness)
            {
                StartLineCap = cap,
                EndLineCap = cap,
                LineJoin = PenLineJoin.Miter,
            };
            pen.Freeze();
            _pens[key] = _penRecency.AddFirst((key, pen));
            if (_pens.Count > MaxPens)
            {
                var last = _penRecency.Last!;
                _pens.Remove(last.Value.Key);
                _penRecency.RemoveLast();
            }
            return pen;
        }
    }

    private static double NormalizeOpacity(double value) => double.IsNaN(value) ? 1d : Math.Clamp(value, 0d, 1d);
}

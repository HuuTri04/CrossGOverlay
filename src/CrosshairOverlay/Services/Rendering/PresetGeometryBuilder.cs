using System.Windows;
using System.Windows.Media;

namespace CrosshairOverlay.Services.Rendering;

/// <summary>
/// Compiles each arm layer once per preset revision. Outline and core reuse the same
/// frozen paths with different frozen pens, so rendering does not reconstruct lines.
/// </summary>
internal sealed class PresetGeometryBuilder
{
    private const int MaxEntries = 64;
    private readonly object _gate = new();
    private readonly Dictionary<GeometryKey, LinkedListNode<CacheEntry>> _entries = new();
    private readonly LinkedList<CacheEntry> _recency = new();

    internal readonly record struct Arms(StreamGeometry? Vertical, StreamGeometry? Horizontal, StreamGeometry? Diagonal);

    private readonly record struct GeometryKey(
        double Dpi, bool Diagonal, double Start, double End, double VerticalEnd,
        double Align, double VerticalAlign, bool Top, bool Bottom, bool Left, bool Right);

    private readonly record struct CacheEntry(GeometryKey Key, Arms Geometry);

    internal int Count { get { lock (_gate) return _entries.Count; } }

    public Arms BuildArms(RenderPlan plan, LineLayerPlan layer)
    {
        if (!layer.Show) return default;

        // Colours, opacity, stroke width and shadow belong to the drawing, not the path.
        // Keep only the coordinates that actually affect StreamGeometry in the key.
        var key = new GeometryKey(plan.Dpi, plan.ArmsDiagonal, layer.StartPx,
            layer.EndPx, layer.VerticalEndPx, layer.Align, layer.VerticalAlign,
            layer.ShowTop, layer.ShowBottom, layer.ShowLeft, layer.ShowRight);
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var hit))
            {
                _recency.Remove(hit);
                _recency.AddFirst(hit);
                return hit.Value.Geometry;
            }

            var geometry = CreateArms(plan, layer);
            _entries[key] = _recency.AddFirst(new CacheEntry(key, geometry));
            if (_recency.Count > MaxEntries)
            {
                var oldest = _recency.Last!;
                _entries.Remove(oldest.Value.Key);
                _recency.RemoveLast();
            }
            return geometry;
        }
    }

    private static Arms CreateArms(RenderPlan plan, LineLayerPlan layer)
    {

        var start = plan.ToDip(layer.StartPx);
        var end = plan.ToDip(layer.EndPx);
        if (plan.ArmsDiagonal)
        {
            const double Diag = 0.70710678118654752d;
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                AddDiagonal(context, layer.ShowTop, -1, -1, start, end, Diag);
                AddDiagonal(context, layer.ShowRight, +1, -1, start, end, Diag);
                AddDiagonal(context, layer.ShowBottom, +1, +1, start, end, Diag);
                AddDiagonal(context, layer.ShowLeft, -1, +1, start, end, Diag);
            }
            geometry.Freeze();
            return new Arms(null, null, geometry);
        }

        StreamGeometry? vertical = null;
        if (layer.ShowTop || layer.ShowBottom)
        {
            var align = plan.ToDip(layer.VerticalAlign);
            var verticalEnd = plan.ToDip(layer.VerticalEndPx);
            vertical = new StreamGeometry();
            using (var context = vertical.Open())
            {
                if (layer.ShowTop) AddLine(context, new Point(align, -start), new Point(align, -verticalEnd));
                if (layer.ShowBottom) AddLine(context, new Point(align, start), new Point(align, verticalEnd));
            }
            vertical.Freeze();
        }

        StreamGeometry? horizontal = null;
        if (layer.ShowLeft || layer.ShowRight)
        {
            var align = plan.ToDip(layer.Align);
            horizontal = new StreamGeometry();
            using (var context = horizontal.Open())
            {
                if (layer.ShowLeft) AddLine(context, new Point(-start, align), new Point(-end, align));
                if (layer.ShowRight) AddLine(context, new Point(start, align), new Point(end, align));
            }
            horizontal.Freeze();
        }

        return new Arms(vertical, horizontal, null);
    }

    private static void AddDiagonal(StreamGeometryContext context, bool show, int x, int y,
        double start, double end, double factor)
    {
        if (!show) return;
        AddLine(context, new Point(start * factor * x, start * factor * y),
            new Point(end * factor * x, end * factor * y));
    }

    private static void AddLine(StreamGeometryContext context, Point start, Point end)
    {
        context.BeginFigure(start, isFilled: true, isClosed: false);
        context.LineTo(end, isStroked: true, isSmoothJoin: false);
    }
}

using System.Windows.Media.Imaging;

namespace CrosshairOverlay.Services.Rendering;

/// <summary>
/// Bộ đệm ảnh tuỳ chỉnh đã giải mã, có GIỚI HẠN dung lượng, bỏ ảnh lâu không dùng nhất trước (LRU).
/// </summary>
/// <remarks>
/// <para>
/// Trước đây renderer dùng một dictionary chỉ thêm, không bao giờ xoá: mỗi ảnh từng hiển thị — mà mỗi
/// mã tâm ngắm ảnh dán vào là một file mới — nằm lại trong RAM tới khi thoát app. Một GIF động giữ đủ
/// mọi khung đã giải mã, tới 96 MB (<see cref="GifAnimation.MaxDecodedBytes"/>), nên đổi qua lại vài
/// preset GIF là bộ nhớ tăng mãi.
/// </para>
/// <para>
/// Bỏ một ảnh khỏi đây KHÔNG làm hỏng hình đang hiển thị: Drawing của overlay và khung xem trước vẫn
/// giữ tham chiếu tới BitmapSource của nó. Chỉ là lần dựng sau phải giải mã lại — và bộ nhớ thật sự
/// được trả khi không còn ai dùng ảnh đó.
/// </para>
/// <para>
/// Ảnh vượt ngân sách vẫn được trả về cho caller để hiển thị, nhưng không được giữ trong cache.
/// </para>
/// </remarks>
internal sealed class ImageCache
{
    /// <summary>
    /// Đủ cho vài ảnh tĩnh cỡ lớn cùng một GIF vừa phải — overlay và khung xem trước thường chỉ dùng
    /// một hai ảnh cùng lúc.
    /// </summary>
    public const long DefaultBudgetBytes = 64L * 1024 * 1024;

    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<Entry> _recency = new();
    private readonly long _budgetBytes;

    public ImageCache(long budgetBytes = DefaultBudgetBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(budgetBytes);
        _budgetBytes = budgetBytes;
    }

    public int Count
    {
        get { lock (_gate) return _byPath.Count; }
    }

    public long Bytes
    {
        get { lock (_gate) return _bytes; }
    }

    private long _bytes;

    public bool TryGet(string path, DateTime stamp, out BitmapSource? source, out AnimatedImage? animation)
    {
        lock (_gate)
        {
            if (_byPath.TryGetValue(path, out var node) && node.Value.Stamp == stamp)
            {
                // Vừa dùng: đưa lên đầu để là thứ bị bỏ SAU CÙNG.
                _recency.Remove(node);
                _recency.AddFirst(node);

                source = node.Value.Source;
                animation = node.Value.Animation;
                return true;
            }
        }

        source = null;
        animation = null;
        return false;
    }

    /// <summary>Lưu kết quả giải mã (kể cả thất bại — để khỏi thử lại file hỏng mỗi lần vẽ).</summary>
    public void Set(string path, DateTime stamp, BitmapSource? source, AnimatedImage? animation)
    {
        // Cache entries may be used from another thread after the renderer publishes them.
        // The caller can still display a mutable image, but it must never live in this cache.
        var canCache = source is not { IsFrozen: false } &&
            (animation is null || animation.Frames.All(frame => frame.IsFrozen));
        var entry = new Entry(path, source, animation, stamp, EstimateBytes(source, animation));

        lock (_gate)
        {
            if (_byPath.Remove(path, out var old))
            {
                _recency.Remove(old);
                _bytes -= old.Value.Bytes;
            }

            // An oversized GIF may still be displayed by the caller. Keeping it here would
            // turn the nominal cache budget into an unbounded retained image allocation.
            if (!canCache || entry.Bytes > _budgetBytes) return;

            _byPath[path] = _recency.AddFirst(entry);
            _bytes += entry.Bytes;

            // The new entry fits on its own; evict older entries until the hard cap holds.
            while (_bytes > _budgetBytes)
            {
                var last = _recency.Last!;
                _recency.RemoveLast();
                _byPath.Remove(last.Value.Path);
                _bytes -= last.Value.Bytes;
            }
        }
    }

    /// <summary>Dung lượng pixel đã giải mã (BGRA 4 byte/pixel) — phần chiếm RAM thật của ảnh.</summary>
    internal static long EstimateBytes(BitmapSource? source, AnimatedImage? animation)
    {
        if (animation is not null)
        {
            long total = 0;
            foreach (var frame in animation.Frames) total += (long)frame.PixelWidth * frame.PixelHeight * 4;
            return total;
        }

        return source is null ? 0 : (long)source.PixelWidth * source.PixelHeight * 4;
    }

    private sealed record Entry(string Path, BitmapSource? Source, AnimatedImage? Animation, DateTime Stamp, long Bytes);
}

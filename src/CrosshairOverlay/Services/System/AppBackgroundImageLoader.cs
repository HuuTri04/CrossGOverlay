using System.IO;
using System.Diagnostics;
using System.Windows.Media.Imaging;

namespace CrosshairOverlay.Services.System;

/// <summary>Decodes one Settings backdrop at a bounded size and releases its source file.</summary>
internal static class AppBackgroundImageLoader
{
    internal readonly record struct DecodeTiming(double DiskReadMs, double DecodeMs, long CompletedAt);
    private const long MaxSourceBytes = 50L * 1024 * 1024;
    private const int MaxDecodedSide = 1920;
    private static readonly HashSet<string> Extensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp" };

    public static Task<BitmapSource> DecodeAsync(
        string path, int maxDecodedSide = MaxDecodedSide, CancellationToken cancellationToken = default,
        Action<DecodeTiming>? timing = null) =>
        Task.Run(() => Decode(path, maxDecodedSide, cancellationToken, timing), cancellationToken);

    public static BitmapSource Decode(
        string path, int maxDecodedSide = MaxDecodedSide, CancellationToken cancellationToken = default,
        Action<DecodeTiming>? timing = null)
    {
        var startedAt = timing is null ? 0 : Stopwatch.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();
        if (maxDecodedSide is < 1 or > MaxDecodedSide)
            throw new ArgumentOutOfRangeException(nameof(maxDecodedSide));
        if (!Extensions.Contains(Path.GetExtension(path)))
            throw new NotSupportedException("Unsupported background image format.");

        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("Background image is missing.", path);
        if (file.Length > MaxSourceBytes)
            throw new InvalidDataException("Background image exceeds 50 MB.");

        int width, height;
        double diskReadMs = 0;
        using (var probeFile = new FileStream(path, FileMode.Open, FileAccess.Read,
                   FileShare.ReadWrite | FileShare.Delete))
        {
            using var probe = new CancellableReadStream(probeFile, cancellationToken, timing is not null);
            var decoder = BitmapDecoder.Create(probe, BitmapCreateOptions.DelayCreation,
                BitmapCacheOption.None);
            width = decoder.Frames[0].PixelWidth;
            height = decoder.Frames[0].PixelHeight;
            diskReadMs += probe.ReadMs;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (width <= 0 || height <= 0 || width > 16384 || height > 16384)
            throw new InvalidDataException("Background image dimensions are unsupported.");

        using var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var stream = new CancellableReadStream(fileStream, cancellationToken, timing is not null);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        if (width >= height && width > maxDecodedSide) image.DecodePixelWidth = maxDecodedSide;
        else if (height > maxDecodedSide) image.DecodePixelHeight = maxDecodedSide;
        image.StreamSource = stream;
        image.EndInit();
        cancellationToken.ThrowIfCancellationRequested();
        image.Freeze();
        diskReadMs += stream.ReadMs;
        if (timing is not null)
        {
            var completedAt = Stopwatch.GetTimestamp();
            var totalMs = Stopwatch.GetElapsedTime(startedAt, completedAt).TotalMilliseconds;
            timing(new DecodeTiming(diskReadMs, Math.Max(0, totalMs - diskReadMs), completedAt));
        }
        return image;
    }

    // WIC may read the stream many times during EndInit. Check cancellation at each
    // read boundary; only collect timings when the benchmark flag is enabled.
    private sealed class CancellableReadStream(FileStream inner, CancellationToken cancellationToken,
        bool measureReads) : Stream
    {
        private long _readTicks;
        public double ReadMs => _readTicks * 1000d / Stopwatch.Frequency;
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override long Seek(long offset, SeekOrigin origin)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return inner.Seek(offset, origin);
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!measureReads) return inner.Read(buffer, offset, count);
            var start = Stopwatch.GetTimestamp();
            try { return inner.Read(buffer, offset, count); }
            finally { _readTicks += Stopwatch.GetTimestamp() - start; }
        }
        public override int Read(Span<byte> buffer)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!measureReads) return inner.Read(buffer);
            var start = Stopwatch.GetTimestamp();
            try { return inner.Read(buffer); }
            finally { _readTicks += Stopwatch.GetTimestamp() - start; }
        }
        public override int ReadByte()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!measureReads) return inner.ReadByte();
            var start = Stopwatch.GetTimestamp();
            try { return inner.ReadByte(); }
            finally { _readTicks += Stopwatch.GetTimestamp() - start; }
        }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}

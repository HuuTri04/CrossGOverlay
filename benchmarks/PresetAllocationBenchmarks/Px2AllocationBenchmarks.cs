using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using CrosshairOverlay.Services.Import;

/// <summary>Compare the previous growing MemoryStream/Base64 path with the current PX2 encoder.</summary>
[MemoryDiagnoser]
public class Px2AllocationBenchmarks
{
    [Params(256, 1024)]
    public int Size { get; set; }

    private uint[] _pixels = null!;

    [GlobalSetup]
    public void Setup()
    {
        _pixels = new uint[Size * Size];
        var random = new Random(12345);
        random.NextBytes(MemoryMarshal.AsBytes(_pixels.AsSpan()));
        if (!string.Equals(PreviousGrowingStreamAndBase64(), PixelArtCode.Encode(Size, _pixels),
                StringComparison.Ordinal))
            throw new InvalidOperationException("PX2 benchmark paths produced different payloads.");
    }

    [Benchmark(Baseline = true)]
    public string PreviousGrowingStreamAndBase64()
    {
        using var stream = new MemoryStream();
        using (var gzip = new GZipStream(stream, CompressionLevel.Fastest, leaveOpen: true))
            gzip.Write(MemoryMarshal.AsBytes(_pixels.AsSpan()));
        var base64 = Convert.ToBase64String(stream.GetBuffer(), 0, (int)stream.Length);
        return $"PX2|{Size}|{base64}";
    }

    [Benchmark]
    public string CurrentPooledPx2() => PixelArtCode.Encode(Size, _pixels);
}

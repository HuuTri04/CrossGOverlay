using System.Buffers.Binary;
using System.Runtime.InteropServices;
using CrosshairOverlay.Services.Import;
using Xunit;
using Xunit.Abstractions;

namespace CrosshairOverlay.Tests;

public sealed class PixelArtCodeTests
{
    private readonly ITestOutputHelper _output;

    public PixelArtCodeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void ContentBoundsFindsSparsePixelsAndIgnoresZeroAlpha()
    {
        const int size = 1024;
        var pixels = new uint[size * size];
        pixels[492 * size + 490] = 0xFF00FF00;
        pixels[531 * size + 529] = 0x7F004000;
        pixels[0] = 0x0000FF00;

        Assert.True(PixelArtCode.TryFindContentBounds(size, pixels, out var bounds));
        Assert.Equal(new PixelArtCode.PixelBounds(490, 492, 40, 40), bounds);
        Assert.Equal(6_400, bounds.Width * bounds.Height * sizeof(uint));
    }

    [Fact]
    public void ContentBoundsRejectsEmptyAndIncorrectCanvas()
    {
        Assert.False(PixelArtCode.TryFindContentBounds(1024, new uint[1024 * 1024], out _));
        Assert.False(PixelArtCode.TryFindContentBounds(1024, new uint[24 * 24], out _));
    }

    [Fact]
    public void SparseBoundsReportCurrentPx2AndHypotheticalCroppedSize()
    {
        const int size = 1024;
        const int side = 40;
        const int offset = (size - side) / 2;
        var pixels = new uint[size * size];
        var random = new Random(21);
        for (var y = 0; y < side; y++)
            for (var x = 0; x < side; x++)
                pixels[(offset + y) * size + offset + x] = 0xFF000000u | (uint)random.Next(0x1000000);

        Assert.True(PixelArtCode.TryFindContentBounds(size, pixels, out var bounds));
        var cropped = new uint[bounds.Width * bounds.Height];
        for (var y = 0; y < bounds.Height; y++)
            Array.Copy(pixels, (bounds.Y + y) * size + bounds.X,
                cropped, y * bounds.Width, bounds.Width);

        var fullCode = PixelArtCode.Encode(size, pixels);
        var croppedCode = PixelArtCode.Encode(side, cropped);
        _output.WriteLine($"PX2 full={fullCode.Length} chars; cropped={croppedCode.Length} chars; bounds={bounds}");
        Assert.True(croppedCode.Length < fullCode.Length);
    }

    [Theory]
    [InlineData(24, "PX1|")]
    [InlineData(64, "PX2|")]
    public void Test_PixelArt_EncodeDecode_RoundTrip(int size, string expectedHeader)
    {
        var bytes = new byte[size * size * sizeof(uint)];
        new Random(2026 + size).NextBytes(bytes);
        var pixels = MemoryMarshal.Cast<byte, uint>(bytes).ToArray();

        var code = PixelArtCode.Encode(size, pixels);

        Assert.StartsWith(expectedHeader + size + "|", code);
        Assert.True(PixelArtCode.TryDecode(code, out var decodedSize, out var decodedPixels));
        Assert.Equal(size, decodedSize);
        Assert.Equal(bytes, MemoryMarshal.AsBytes(decodedPixels.AsSpan()).ToArray());
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    public void RoundTripsTransparentAndMulticolorPixels(int size)
    {
        var pixels = new uint[size * size];
        pixels[0] = 0xFF00FF00;
        pixels[size + 1] = 0xFFFF0000;
        pixels[^1] = 0xFF00BFFF;

        var code = PixelArtCode.Encode(size, pixels);

        Assert.True(PixelArtCode.TryDecode(code, out var decodedSize, out var decoded));
        Assert.Equal(size, decodedSize);
        Assert.Equal(pixels, decoded);
    }

    [Theory]
    [InlineData(64)]
    [InlineData(1024)]
    public void CompressedCodeRoundTripsLargeSparseCanvas(int size)
    {
        var pixels = new uint[size * size];
        pixels[0] = 0xFF00FF00;
        pixels[(size / 2) * size + size / 2] = 0xFFFF0000;
        pixels[^1] = 0xFF00BFFF;

        var code = PixelArtCode.Encode(size, pixels);

        Assert.StartsWith($"{PixelArtCode.CompressedPrefix}|{size}|", code);
        Assert.InRange(code.Length, 1, 32_768);
        Assert.True(PixelArtCode.TryDecode(code, out var decodedSize, out var decoded));
        Assert.Equal(size, decodedSize);
        Assert.Equal(pixels, decoded);
    }

    [Fact]
    public void CompressedCodeRejectsTruncatedAndOversizedPayloads()
    {
        var code = PixelArtCode.Encode(64, new uint[64 * 64]);
        Assert.False(PixelArtCode.TryDecode(code.Replace("|64|", "|1024|"), out _, out _));
        Assert.False(PixelArtCode.TryDecode("PX2|64|!!!!", out _, out _));
        Assert.False(PixelArtCode.TryDecode(code + "AAAA", out _, out _));
    }

    [Fact]
    public void CompressedCodeRoundTripsWorstCaseNoisy1024Canvas()
    {
        var pixels = new uint[1024 * 1024];
        for (var index = 0; index < pixels.Length; index++)
            pixels[index] = (uint)Random.Shared.NextInt64();

        var code = PixelArtCode.Encode(1024, pixels);

        Assert.True(code.Length <= PixelArtCode.MaxCompressedCodeLength);
        Assert.True(PixelArtCode.TryDecode(code, out var size, out var decoded));
        Assert.Equal(1024, size);
        Assert.Equal(pixels, decoded);
    }

    [Fact]
    public void LegacyCodeKeepsSizeWhitespaceCompatibility()
    {
        var pixels = new uint[24 * 24];
        pixels[0] = 0xFF00FF00;
        var code = PixelArtCode.Encode(24, pixels).Replace("|24|", "| 24 |", StringComparison.Ordinal);

        Assert.True(PixelArtCode.TryDecode(code, out var size, out var decoded));
        Assert.Equal(24, size);
        Assert.Equal(pixels, decoded);
    }

    [Theory]
    [InlineData(24)]
    [InlineData(1024)]
    public void SignedPixelCodeDecodesOnlyWhenChecksumMatches(int size)
    {
        var pixels = new uint[size * size];
        pixels[0] = 0xFF00FF00;
        pixels[(size / 2) * size + size / 2] = 0xFFFF0000;
        var unsigned = PixelArtCode.Encode(size, pixels);
        var prefix = size <= 32 ? PixelArtCode.Prefix : PixelArtCode.CompressedPrefix;
        var signed = ShareCodeChecksum.Append(unsigned, prefix);

        Assert.True(PixelArtCode.TryDecode(signed, out var decodedSize, out var decoded));
        Assert.Equal(size, decodedSize);
        Assert.Equal(pixels, decoded);

        var wrongChecksum = signed[..^1] + (signed[^1] == '0' ? '1' : '0');
        Assert.False(PixelArtCode.TryDecode(wrongChecksum, out _, out _));
        Assert.False(PixelArtCode.TryDecode(unsigned + ";c123", out _, out _));
    }

    [Fact]
    public void WorstCaseAlternatingPixelsStaysWithinLengthLimit()
    {
        var pixels = Enumerable.Range(0, 32 * 32)
            .Select(index => index % 2 == 0 ? 0xFF000000u : 0xFFFFFFFFu).ToArray();

        var code = PixelArtCode.Encode(32, pixels);

        Assert.True(code.Length <= PixelArtCode.MaxCodeLength);
        Assert.True(PixelArtCode.TryDecode(code, out _, out var decoded));
        Assert.Equal(pixels, decoded);
    }

    [Theory]
    [InlineData("PX1|24|!")]
    [InlineData("PX1|48|AQAAAAAA")]
    [InlineData("PX1|24|")]
    [InlineData("PX1|24|AQAAAAAA|extra")]
    public void RejectsMalformedCodes(string code) =>
        Assert.False(PixelArtCode.TryDecode(code, out _, out _));

    [Fact]
    public void RejectsRunBeyondCanvas()
    {
        var bytes = new byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, 577);
        var code = $"PX1|24|{Convert.ToBase64String(bytes)}";

        Assert.False(PixelArtCode.TryDecode(code, out _, out _));
    }

    [Fact]
    public void RejectsTruncatedCanvas()
    {
        var bytes = new byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, 1);
        var code = $"PX1|24|{Convert.ToBase64String(bytes)}";

        Assert.False(PixelArtCode.TryDecode(code, out _, out _));
    }

    [Fact]
    public void MalformedPixelCodesDoNotThrowUnderRepeatedImport()
    {
        var valid = PixelArtCode.Encode(64, new uint[64 * 64]);
        var random = new Random(12019);
        for (var attempt = 0; attempt < 2_000; attempt++)
        {
            var chars = valid.ToCharArray();
            var index = random.Next(chars.Length);
            chars[index] = "!;|? \u00e9"[random.Next(6)];
            var code = new string(chars);

            var exception = Record.Exception(() => PixelArtCode.TryDecode(code, out _, out _));
            Assert.Null(exception);
        }
    }
}

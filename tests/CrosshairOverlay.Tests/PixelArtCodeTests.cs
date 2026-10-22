using System.Buffers.Binary;
using CrosshairOverlay.Services.Import;
using Xunit;

namespace CrosshairOverlay.Tests;

public sealed class PixelArtCodeTests
{
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
}

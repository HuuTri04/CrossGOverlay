using System.Windows.Media;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Services.Import;
using Xunit;

namespace CrosshairOverlay.Tests;

public sealed class AppCrosshairBinaryCodeTests
{
    [Fact]
    public void BinaryCode_RoundTripsEveryDrawnProfileSetting_AndIsShorterThanLegacy()
    {
        var profile = BuildComplexProfile();
        var oldCode = AppCrosshairCode.EncodeLegacy(profile);
        var newCode = AppCrosshairCode.Encode(profile);
        Console.WriteLine($"CGO-CH legacy: {oldCode.Length} chars; CGO-CH2 binary: {newCode.Length} chars; " +
            $"reduction: {100d * (oldCode.Length - newCode.Length) / oldCode.Length:F1}%");

        Assert.StartsWith(AppCrosshairCode.BinaryPrefix, newCode, StringComparison.Ordinal);
        Assert.True(newCode.Length < oldCode.Length);
        var payload = newCode[AppCrosshairCode.BinaryPrefix.Length..newCode.LastIndexOf(";c", StringComparison.Ordinal)];
        Assert.DoesNotContain('=', payload);
        Assert.True(AppCrosshairCode.TryDecode(newCode, out var decoded, out var reason), reason);
        Assert.NotNull(decoded);
        Assert.NotEqual(profile.Id, decoded.Id); // Import always issues a new identity.
        AssertSameSettings(profile, decoded);
    }

    [Fact]
    public void DefaultCode_IsCompact_AndLegacyJsonGzipStillDecodes()
    {
        var profile = CrosshairProfile.CreateDefault();
        var oldCode = AppCrosshairCode.EncodeLegacy(profile);
        var newCode = AppCrosshairCode.Encode(profile);
        Console.WriteLine($"Default CGO-CH: {oldCode.Length} chars; CGO-CH2: {newCode.Length} chars");

        Assert.StartsWith("CGO-CH;H4sI", oldCode, StringComparison.Ordinal);
        Assert.True(newCode.Length < oldCode.Length);
        Assert.True(AppCrosshairCode.IsAppCode(oldCode));
        Assert.True(AppCrosshairCode.IsAppCode(newCode));
        Assert.True(AppCrosshairCode.TryDecode(oldCode, out var fromOld, out var oldReason), oldReason);
        Assert.True(AppCrosshairCode.TryDecode(newCode, out var fromNew, out var newReason), newReason);
        AssertSameSettings(profile, fromOld!);
        AssertSameSettings(profile, fromNew!);
        Assert.False(AppCrosshairCode.TryDecode(newCode[..^1], out _, out _));
    }

    private static CrosshairProfile BuildComplexProfile()
    {
        var profile = CrosshairProfile.CreateDefault();
        profile.SchemaVersion = 3;
        profile.Name = "Tâm ngắm μ precision";
        profile.IsFavorite = true;
        profile.Shape = CrosshairShape.XShape;
        profile.Color = Color.FromArgb(197, 12, 56, 240);
        profile.Opacity = 0.9123456789012345;
        profile.Scale = 1.23456789012345;
        profile.Rotation = 31.1234567890123;
        profile.OffsetX = -12.1234567890123;
        profile.OffsetY = 19.2345678901234;

        var inner = profile.InnerLines;
        inner.Enabled = false;
        inner.Opacity = 0.8123456789012345;
        inner.Length = 17.1234567890123;
        inner.SeparateVerticalLength = true;
        inner.VerticalLength = 23.1234567890123;
        inner.Thickness = 3.1234567890123;
        inner.SeparateVerticalThickness = true;
        inner.VerticalThickness = 4.1234567890123;
        inner.Offset = 7.1234567890123;
        inner.ShowTop = false;
        inner.ShowLeft = false;
        inner.RoundedCaps = true;

        var outer = profile.OuterLines;
        outer.Enabled = true;
        outer.Opacity = 0.5123456789012345;
        outer.Length = 8.1234567890123;
        outer.SeparateVerticalLength = true;
        outer.VerticalLength = 9.1234567890123;
        outer.Thickness = 1.1234567890123;
        outer.SeparateVerticalThickness = true;
        outer.VerticalThickness = 2.1234567890123;
        outer.Offset = 14.1234567890123;
        outer.ShowBottom = false;
        outer.ShowRight = false;
        outer.RoundedCaps = true;

        profile.CenterDot.Enabled = true;
        profile.CenterDot.UseProfileColor = false;
        profile.CenterDot.Color = Color.FromArgb(160, 240, 20, 90);
        profile.CenterDot.Size = 5.1234567890123;
        profile.CenterDot.Opacity = 0.7123456789012345;
        profile.Outline.Enabled = false;
        profile.Outline.Color = Color.FromArgb(111, 10, 11, 12);
        profile.Outline.Thickness = 2.1234567890123;
        profile.Outline.Opacity = 0.6123456789012345;
        profile.Ring.Enabled = true;
        profile.Ring.Filled = true;
        profile.Ring.Radius = 22.1234567890123;
        profile.Ring.Thickness = 3.1234567890123;
        profile.Shadow.Enabled = true;
        profile.Shadow.Color = Color.FromArgb(90, 45, 67, 89);
        profile.Shadow.BlurRadius = 8.1234567890123;
        profile.Shadow.Opacity = 0.4123456789012345;
        profile.Image.FilePath = "CustomImages/retained-while-standard.png";
        profile.Image.Scale = 1.1234567890123;
        profile.Image.Opacity = 0.3123456789012345;
        profile.Image.OffsetX = -9.1234567890123;
        profile.Image.OffsetY = 8.1234567890123;
        return profile;
    }

    private static void AssertSameSettings(CrosshairProfile expected, CrosshairProfile actual)
    {
        Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.IsFavorite, actual.IsFavorite);
        Assert.Equal(expected.Type, actual.Type);
        Assert.Equal(expected.Shape, actual.Shape);
        Assert.Equal(expected.Color, actual.Color);
        AssertBits(expected.Opacity, actual.Opacity);
        AssertBits(expected.Scale, actual.Scale);
        AssertBits(expected.Rotation, actual.Rotation);
        AssertBits(expected.OffsetX, actual.OffsetX);
        AssertBits(expected.OffsetY, actual.OffsetY);
        AssertLine(expected.InnerLines, actual.InnerLines);
        AssertLine(expected.OuterLines, actual.OuterLines);
        Assert.Equal(expected.CenterDot.Enabled, actual.CenterDot.Enabled);
        Assert.Equal(expected.CenterDot.UseProfileColor, actual.CenterDot.UseProfileColor);
        Assert.Equal(expected.CenterDot.Color, actual.CenterDot.Color);
        AssertBits(expected.CenterDot.Size, actual.CenterDot.Size);
        AssertBits(expected.CenterDot.Opacity, actual.CenterDot.Opacity);
        Assert.Equal(expected.Outline.Enabled, actual.Outline.Enabled);
        Assert.Equal(expected.Outline.Color, actual.Outline.Color);
        AssertBits(expected.Outline.Thickness, actual.Outline.Thickness);
        AssertBits(expected.Outline.Opacity, actual.Outline.Opacity);
        Assert.Equal(expected.Ring.Enabled, actual.Ring.Enabled);
        Assert.Equal(expected.Ring.Filled, actual.Ring.Filled);
        AssertBits(expected.Ring.Radius, actual.Ring.Radius);
        AssertBits(expected.Ring.Thickness, actual.Ring.Thickness);
        Assert.Equal(expected.Shadow.Enabled, actual.Shadow.Enabled);
        Assert.Equal(expected.Shadow.Color, actual.Shadow.Color);
        AssertBits(expected.Shadow.BlurRadius, actual.Shadow.BlurRadius);
        AssertBits(expected.Shadow.Opacity, actual.Shadow.Opacity);
        Assert.Equal(expected.Image.FilePath, actual.Image.FilePath);
        AssertBits(expected.Image.Scale, actual.Image.Scale);
        AssertBits(expected.Image.Opacity, actual.Image.Opacity);
        AssertBits(expected.Image.OffsetX, actual.Image.OffsetX);
        AssertBits(expected.Image.OffsetY, actual.Image.OffsetY);
    }

    private static void AssertLine(LineLayerSettings expected, LineLayerSettings actual)
    {
        Assert.Equal(expected.Enabled, actual.Enabled);
        Assert.Equal(expected.SeparateVerticalLength, actual.SeparateVerticalLength);
        Assert.Equal(expected.SeparateVerticalThickness, actual.SeparateVerticalThickness);
        Assert.Equal(expected.ShowTop, actual.ShowTop);
        Assert.Equal(expected.ShowBottom, actual.ShowBottom);
        Assert.Equal(expected.ShowLeft, actual.ShowLeft);
        Assert.Equal(expected.ShowRight, actual.ShowRight);
        Assert.Equal(expected.RoundedCaps, actual.RoundedCaps);
        AssertBits(expected.Opacity, actual.Opacity);
        AssertBits(expected.Length, actual.Length);
        AssertBits(expected.VerticalLength, actual.VerticalLength);
        AssertBits(expected.Thickness, actual.Thickness);
        AssertBits(expected.VerticalThickness, actual.VerticalThickness);
        AssertBits(expected.Offset, actual.Offset);
    }

    private static void AssertBits(double expected, double actual) =>
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));
}

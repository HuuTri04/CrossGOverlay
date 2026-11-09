using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace CrosshairOverlay.Tests;

/// <summary>Protect the WPF loading window against leaving a repeating animation alive.</summary>
public sealed class SplashAnimationTests
{
    private static string SplashFile(string extension)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "CrosshairOverlay", "Views", "SplashWindow" + extension);
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException("SplashWindow" + extension);
    }

    [Fact]
    public void RepeatingAnimationMustBeControllable()
    {
        var xaml = File.ReadAllText(SplashFile(".xaml"));
        Assert.Contains("<Storyboard x:Key=\"BlinkStatus\"", xaml, StringComparison.Ordinal);
        Assert.Contains("RepeatBehavior=\"Forever\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<BeginStoryboard>", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void CloseQuietlyStopsAndRemovesTheAnimation()
    {
        var code = File.ReadAllText(SplashFile(".xaml.cs"));
        Assert.Matches(new Regex(@"Begin\(this,\s*isControllable:\s*true\)"), code);
        Assert.Matches(new Regex(@"_blink\.Stop\(this\);\s*_blink\.Remove\(this\);"), code);
        var closeQuietly = code[code.IndexOf("public void CloseQuietly", StringComparison.Ordinal)..];
        Assert.Contains("StopBlinking();", closeQuietly, StringComparison.Ordinal);
    }
}

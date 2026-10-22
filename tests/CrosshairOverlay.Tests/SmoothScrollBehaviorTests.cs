using CrosshairOverlay.Controls;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Services.System;
using System.Windows.Media.Animation;
using Xunit;

namespace CrosshairOverlay.Tests;

public sealed class SmoothScrollBehaviorTests
{
    [Theory]
    [InlineData(100, -120, 500, 164)]
    [InlineData(100, 120, 500, 36)]
    [InlineData(100, -60, 500, 132)]
    [InlineData(490, -120, 500, 500)]
    [InlineData(10, 120, 500, 0)]
    public void WheelDeltaAccumulatesAndClampsInPixels(
        double target, int delta, double extent, double expected) =>
        Assert.Equal(expected, SmoothScrollBehavior.CalculateTarget(target, delta, extent));

    [Fact]
    public void CubicEaseMovesQuicklyThenSettlesExactlyAtTarget()
    {
        Assert.Equal(0d, SmoothScrollBehavior.EaseOutCubic(0));
        Assert.True(SmoothScrollBehavior.EaseOutCubic(0.5) > 0.5);
        Assert.Equal(1d, SmoothScrollBehavior.EaseOutCubic(1));
        Assert.Equal(1d, SmoothScrollBehavior.EaseOutCubic(2));
    }

    [Fact]
    public void UiCadenceClampsSettingsAndConfiguresAppAnimations()
    {
        var settings = new AppSettings();
        Assert.Equal(144, settings.UiTargetFps);
        settings.UiTargetFps = 45;
        Assert.Equal(60, settings.UiTargetFps);
        settings.UiTargetFps = 200;
        Assert.Equal(144, settings.UiTargetFps);

        var previous = UiAnimationFrameRate.TargetFps;
        try
        {
            UiAnimationFrameRate.TargetFps = 75;
            Assert.Equal(TimeSpan.FromSeconds(1d / 75), UiAnimationFrameRate.FrameInterval);
            var animation = UiAnimationFrameRate.Configure(new DoubleAnimation());
            Assert.Equal(75, Timeline.GetDesiredFrameRate(animation));
        }
        finally
        {
            UiAnimationFrameRate.TargetFps = previous;
        }
    }
}

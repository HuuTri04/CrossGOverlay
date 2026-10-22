using System.Windows.Media.Animation;

namespace CrosshairOverlay.Services.System;

/// <summary>
/// Cadence for animations owned by the app. WPF's compositor and display refresh rate
/// cannot be changed dynamically through Timeline.DesiredFrameRate metadata.
/// </summary>
public static class UiAnimationFrameRate
{
    private static int _targetFps = 144;

    public static int TargetFps
    {
        get => Volatile.Read(ref _targetFps);
        set => Volatile.Write(ref _targetFps, Math.Clamp(value, 60, 144));
    }

    public static TimeSpan FrameInterval => TimeSpan.FromSeconds(1d / TargetFps);

    /// <summary>Set WPF's advisory frame rate on an app-created root timeline.</summary>
    public static T Configure<T>(T animation) where T : Timeline
    {
        Timeline.SetDesiredFrameRate(animation, TargetFps);
        return animation;
    }
}

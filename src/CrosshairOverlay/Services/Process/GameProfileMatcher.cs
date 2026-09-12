using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;

namespace CrosshairOverlay.Services.Process;

/// <inheritdoc cref="IGameProfileMatcher"/>
/// <remarks>Logic thuần, không phụ thuộc Win32 — đây là phần dễ unit-test nhất của Phase 5.</remarks>
public sealed class GameProfileMatcher : IGameProfileMatcher
{
    public GameProfile? Match(ForegroundWindowInfo window, IEnumerable<GameProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        if (!window.IsValid) return null;

        GameProfile? best = null;

        foreach (var profile in profiles)
        {
            if (!profile.Enabled || string.IsNullOrWhiteSpace(profile.Pattern)) continue;
            if (!Matches(window, profile)) continue;

            // Priority nhỏ hơn được ưu tiên; hoà thì lấy rule gặp trước.
            if (best is null || profile.Priority < best.Priority) best = profile;
        }

        return best;
    }

    private static bool Matches(ForegroundWindowInfo window, GameProfile profile) =>
        profile.MatchMode switch
        {
            ProcessMatchMode.ProcessName =>
                !string.IsNullOrEmpty(window.ProcessName)
                && string.Equals(window.ProcessName, profile.Pattern.Trim(), StringComparison.OrdinalIgnoreCase),

            ProcessMatchMode.WindowTitleContains =>
                !string.IsNullOrEmpty(window.WindowTitle)
                && window.WindowTitle.Contains(profile.Pattern.Trim(), StringComparison.CurrentCultureIgnoreCase),

            ProcessMatchMode.ExecutablePath =>
                !string.IsNullOrEmpty(window.ExecutablePath)
                && string.Equals(window.ExecutablePath, profile.Pattern.Trim(), StringComparison.OrdinalIgnoreCase),

            _ => false,
        };
}

using System.ComponentModel;

namespace CrosshairOverlay.Core.Models;

/// <summary>
/// Đăng ký <c>PropertyChanged</c> trên một preset VÀ mọi khối con của nó.
/// </summary>
/// <remarks>
/// <see cref="CrosshairProfile"/> là <c>ObservableObject</c>, nhưng <see cref="CrosshairProfile.Lines"/>,
/// <see cref="CrosshairProfile.Outline"/>… là những object riêng biệt cũng tự phát sự kiện. Ai muốn
/// biết "preset này có gì đổi không" đều phải nghe cả sáu — overlay, khung preview, và cơ chế
/// tự động lưu. Gom vào đây để ba nơi đó không trôi lệch nhau khi model thêm khối mới.
/// </remarks>
public static class ProfileNotifications
{
    public static void Hook(
        CrosshairProfile profile, PropertyChangedEventHandler handler, bool subscribe)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(handler);

        INotifyPropertyChanged[] targets =
        [
            profile,
            profile.Lines,
            profile.CenterDot,
            profile.Outline,
            profile.Ring,
            profile.Image,
        ];

        foreach (var target in targets)
        {
            if (subscribe) target.PropertyChanged += handler;
            else target.PropertyChanged -= handler;
        }
    }
}

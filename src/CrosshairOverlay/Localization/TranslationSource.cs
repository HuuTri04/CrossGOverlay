using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace CrosshairOverlay.Localization;

/// <summary>
/// Nguồn chuỗi đã dịch cho toàn ứng dụng, cho phép đổi ngôn ngữ NGAY mà không cần khởi động lại.
/// </summary>
/// <remarks>
/// Mấu chốt là bộ chỉ mục <c>this[key]</c>: XAML bind tới <c>[TenKhoa]</c> trên singleton này,
/// nên khi <see cref="CurrentCulture"/> đổi, chỉ cần phát <c>PropertyChanged</c> với tên rỗng —
/// WPF hiểu đó là "mọi thuộc tính đã đổi" và làm mới toàn bộ binding đang sống. Không có cơ chế
/// này thì đổi ngôn ngữ bắt buộc phải mở lại cửa sổ.
/// </remarks>
public sealed class TranslationSource : INotifyPropertyChanged
{
    private static readonly ResourceManager Resources =
        new("CrosshairOverlay.Resources.Strings", typeof(TranslationSource).Assembly);

    public static TranslationSource Instance { get; } = new();

    private CultureInfo _culture = CultureInfo.CurrentUICulture;

    private TranslationSource()
    {
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Thiếu khoá thì trả về chính tên khoá trong dấu chấm than, để lỗi lộ ra ngay trên giao
    /// diện thay vì hiện ô trống khó lần.
    /// </summary>
    public string this[string key] => Resources.GetString(key, _culture) ?? $"!{key}!";

    public CultureInfo CurrentCulture
    {
        get => _culture;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (Equals(_culture, value)) return;

            _culture = value;
            CultureInfo.CurrentUICulture = value;
            CultureInfo.DefaultThreadCurrentUICulture = value;

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }
}

/// <summary>Lối tắt để lấy chuỗi đã dịch từ code C#.</summary>
public static class Tr
{
    public static string Get(string key) => TranslationSource.Instance[key];

    public static string Format(string key, params object?[] args) =>
        string.Format(TranslationSource.Instance.CurrentCulture, Get(key), args);
}

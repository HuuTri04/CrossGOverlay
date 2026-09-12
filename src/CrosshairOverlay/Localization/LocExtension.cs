using System.Windows.Data;
using System.Windows.Markup;

namespace CrosshairOverlay.Localization;

/// <summary>
/// Markup extension cho XAML: <c>Text="{loc:Loc Settings_Title}"</c>.
/// </summary>
/// <remarks>
/// Trả về một <see cref="Binding"/> chứ không phải chuỗi tĩnh — đó là lý do đổi ngôn ngữ có
/// hiệu lực ngay trên cửa sổ đang mở. Nếu ProvideValue trả chuỗi, mọi nhãn sẽ đứng im cho tới
/// khi cửa sổ được dựng lại.
/// </remarks>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = TranslationSource.Instance,
            Mode = BindingMode.OneWay,
        };

        return binding.ProvideValue(serviceProvider);
    }
}

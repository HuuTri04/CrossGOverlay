using System.ComponentModel;

namespace CrosshairOverlay.Localization;

/// <summary>
/// Một mục trong ComboBox có nhãn đổi theo ngôn ngữ.
/// </summary>
/// <remarks>
/// Phải tự phát <c>PropertyChanged</c> cho <see cref="Label"/>. Cách làm ngây thơ — dựng lại
/// cả danh sách khi đổi ngôn ngữ — có hai vấn đề: thay <c>ItemsSource</c> làm ComboBox mất
/// lựa chọn hiện tại, và mục đang chọn vẫn hiện nhãn cũ vì WPF không có lý do gì để gọi lại
/// <c>ToString()</c>. Giữ nguyên danh sách và báo đúng một property đã đổi thì cả hai vấn đề
/// biến mất.
///
/// <para>
/// Dùng kèm <c>DisplayMemberPath="Label"</c> và <c>SelectedValuePath="Value"</c> trong XAML.
/// </para>
/// </remarks>
public sealed class LocalizedOption<TValue> : INotifyPropertyChanged
{
    private readonly Func<string> _label;

    /// <param name="value">Giá trị thật, thứ được lưu và so khớp.</param>
    /// <param name="resourceKey">Khoá resource của nhãn.</param>
    public LocalizedOption(TValue value, string resourceKey)
    {
        Value = value;
        _label = () => Tr.Get(resourceKey);
    }

    private LocalizedOption(TValue value, Func<string> label)
    {
        Value = value;
        _label = label;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public TValue Value { get; }

    public string Label => _label();

    /// <summary>Nhãn cố định, không dịch — dùng cho tên riêng của từng ngôn ngữ.</summary>
    public static LocalizedOption<TValue> Literal(TValue value, string label) =>
        new(value, () => label);

    /// <summary>Báo cho giao diện đọc lại nhãn. Gọi khi ngôn ngữ đổi.</summary>
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));

    public override string ToString() => Label;
}

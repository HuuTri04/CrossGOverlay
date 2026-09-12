using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CrosshairOverlay.Controls;

/// <summary>
/// Một hàng "nhãn — slider — ô nhập số", ràng buộc hai chiều.
/// </summary>
/// <remarks>
/// Kéo slider thì con số nhảy theo; gõ số vào ô thì slider và khung preview đổi ngay từng ký tự.
///
/// <para>
/// <see cref="Text"/> và <see cref="Value"/> được đồng bộ bằng tay thay vì bind trực tiếp ô
/// nhập vào <see cref="Value"/>. Bind trực tiếp kèm định dạng số sẽ viết đè lên thứ người dùng
/// đang gõ (gõ "0." biến ngay thành "0") và đẩy con trỏ về cuối sau mỗi phím. Cờ
/// <see cref="_syncing"/> chặn vòng lặp giữa hai chiều.
/// </para>
/// </remarks>
public partial class SliderField : UserControl
{
    private bool _syncing;

    public SliderField()
    {
        InitializeComponent();
        SyncTextFromValue();
    }

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(SliderField), new PropertyMetadata(string.Empty));

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(SliderField),
        new FrameworkPropertyMetadata(
            0d,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnValueChanged,
            CoerceValue));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(SliderField),
        new PropertyMetadata(0d, OnRangeChanged));

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(SliderField),
        new PropertyMetadata(100d, OnRangeChanged));

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(SliderField),
        new FrameworkPropertyMetadata(
            "0", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextChanged));

    /// <summary>Nội dung ô nhập. Chỉ dùng nội bộ giữa control và XAML của nó.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Kẹp giá trị vào khoảng cho phép, kể cả khi người dùng gõ số ngoài khoảng.</summary>
    private static object CoerceValue(DependencyObject d, object baseValue)
    {
        var field = (SliderField)d;
        var value = (double)baseValue;

        if (double.IsNaN(value) || double.IsInfinity(value)) return field.Minimum;

        return Math.Clamp(value, field.Minimum, field.Maximum);
    }

    private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        d.CoerceValue(ValueProperty);

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var field = (SliderField)d;

        // Đang gõ thì không viết đè lên ô nhập.
        if (field._syncing) return;

        field.SyncTextFromValue();
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var field = (SliderField)d;
        if (field._syncing) return;

        var text = (string?)e.NewValue;
        if (string.IsNullOrWhiteSpace(text)) return;

        // Chấp nhận cả dấu chấm lẫn dấu phẩy: người dùng Việt Nam thường gõ dấu phẩy thập phân
        // trong khi bố cục bàn phím số lại cho dấu chấm.
        var normalized = text.Replace(',', '.');

        if (!double.TryParse(
                normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            // Chuỗi dở dang như "-" hay "0." — chưa phải số, cứ để người dùng gõ tiếp.
            return;
        }

        field._syncing = true;
        try
        {
            field.Value = parsed;
        }
        finally
        {
            field._syncing = false;
        }
    }

    private void SyncTextFromValue()
    {
        _syncing = true;
        try
        {
            Text = Format(Value);
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>
    /// Khoảng giá trị nhỏ (vd độ mờ 0..1) cần hai chữ số thập phân mới chỉnh được; khoảng lớn
    /// thì một chữ số là đủ, và số nguyên thì bỏ hẳn phần thập phân.
    /// </summary>
    private string Format(double value)
    {
        var decimals = Maximum <= 2d ? 2 : 1;

        if (Math.Abs(value - Math.Round(value)) < 0.005d)
            return Math.Round(value).ToString("0", CultureInfo.CurrentCulture);

        return value.ToString(decimals == 2 ? "0.##" : "0.#", CultureInfo.CurrentCulture);
    }

    /// <summary>Rời ô nhập thì chuẩn hoá lại hiển thị, kể cả khi đang dở một chuỗi không hợp lệ.</summary>
    private void OnBoxLostFocus(object sender, RoutedEventArgs e) => SyncTextFromValue();

    private void OnBoxKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                SyncTextFromValue();
                e.Handled = true;
                break;

            // Mũi tên lên/xuống chỉnh từng nấc — nhanh hơn kéo slider khi cần độ chính xác cao.
            case Key.Up:
                Value += Step();
                SyncTextFromValue();
                e.Handled = true;
                break;

            case Key.Down:
                Value -= Step();
                SyncTextFromValue();
                e.Handled = true;
                break;
        }
    }

    private double Step() => Maximum <= 2d ? 0.05d : 1d;
}

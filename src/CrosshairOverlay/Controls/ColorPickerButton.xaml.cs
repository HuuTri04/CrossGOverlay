using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CrosshairOverlay.Services.Storage;

namespace CrosshairOverlay.Controls;

/// <summary>Một ô màu bấm vào mở bảng chọn: swatch dựng sẵn, slider RGBA và ô nhập hex.</summary>
/// <remarks>
/// Tự viết thay vì kéo thêm thư viện: crosshair chỉ cần vài màu tương phản cao, nên bảng swatch
/// cộng slider giải quyết gần hết nhu cầu, và không thêm một dependency chỉ để chọn màu.
/// </remarks>
public partial class ColorPickerButton : UserControl
{
    /// <summary>Chặn vòng lặp khi slider cập nhật màu rồi màu lại cập nhật ngược slider.</summary>
    private bool _updating;

    public ColorPickerButton()
    {
        InitializeComponent();
        SwatchList.ItemsSource = CreateSwatches();
        Loaded += (_, _) => Refresh();
    }

    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
        nameof(SelectedColor), typeof(Color), typeof(ColorPickerButton),
        new FrameworkPropertyMetadata(
            Colors.Lime,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnSelectedColorChanged));

    public Color SelectedColor
    {
        get => (Color)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ColorPickerButton)d).Refresh();

    private void Refresh()
    {
        var color = SelectedColor;

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        Swatch.Background = brush;

        HexLabel.Text = JsonColorConverter.ToHex(color);

        if (_updating) return;

        _updating = true;
        try
        {
            RedSlider.Value = color.R;
            GreenSlider.Value = color.G;
            BlueSlider.Value = color.B;
            AlphaSlider.Value = color.A;
            HexBox.Text = JsonColorConverter.ToHex(color);
            UpdateChannelLabels();
        }
        finally
        {
            _updating = false;
        }
    }

    private void UpdateChannelLabels()
    {
        RedValue.Text = ((int)RedSlider.Value).ToString();
        GreenValue.Text = ((int)GreenSlider.Value).ToString();
        BlueValue.Text = ((int)BlueSlider.Value).ToString();
        AlphaValue.Text = ((int)AlphaSlider.Value).ToString();
    }

    private void OnOpenClick(object sender, RoutedEventArgs e) => PickerPopup.IsOpen = !PickerPopup.IsOpen;

    private void OnChannelChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating) return;

        _updating = true;
        try
        {
            UpdateChannelLabels();

            SelectedColor = Color.FromArgb(
                (byte)AlphaSlider.Value,
                (byte)RedSlider.Value,
                (byte)GreenSlider.Value,
                (byte)BlueSlider.Value);

            HexBox.Text = JsonColorConverter.ToHex(SelectedColor);
        }
        finally
        {
            _updating = false;
        }

        // Refresh bị bỏ qua khi _updating bật, nên cập nhật phần hiển thị ở đây.
        var brush = new SolidColorBrush(SelectedColor);
        brush.Freeze();
        Swatch.Background = brush;
        HexLabel.Text = JsonColorConverter.ToHex(SelectedColor);
    }

    private void OnSwatchClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Color color }) SelectedColor = color;
    }

    private void OnHexKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        CommitHex();
        e.Handled = true;
    }

    private void OnHexCommit(object sender, RoutedEventArgs e) => CommitHex();

    private void CommitHex()
    {
        var parsed = JsonColorConverter.Parse(HexBox.Text);

        // Nhập sai thì lặng lẽ trả về giá trị hiện tại — không quấy rầy bằng hộp thoại lỗi.
        if (parsed is { } color) SelectedColor = color;
        else HexBox.Text = JsonColorConverter.ToHex(SelectedColor);
    }

    private static List<SwatchItem> CreateSwatches()
    {
        Color[] colors =
        [
            Colors.White, Color.FromRgb(0xC8, 0xC8, 0xC8), Color.FromRgb(0x80, 0x80, 0x80), Colors.Black,
            Colors.Lime, Color.FromRgb(0x00, 0xC8, 0x2C), Color.FromRgb(0x7C, 0xFF, 0x00),
            Color.FromRgb(0x00, 0xE5, 0xFF), Color.FromRgb(0x00, 0x9C, 0xFF), Color.FromRgb(0x4C, 0x8D, 0xFF),
            Color.FromRgb(0xFF, 0x00, 0xFF), Color.FromRgb(0xB4, 0x88, 0xFF),
            Colors.Red, Color.FromRgb(0xFF, 0x3B, 0x30), Color.FromRgb(0xFF, 0x8A, 0x00),
            Color.FromRgb(0xFF, 0xD6, 0x00), Colors.Yellow, Color.FromRgb(0xFF, 0x6E, 0xC7),
        ];

        return [.. colors.Select(c => new SwatchItem(c))];
    }

    /// <summary>Một ô màu trong lưới swatch.</summary>
    public sealed class SwatchItem
    {
        public SwatchItem(Color color)
        {
            Color = color;

            var brush = new SolidColorBrush(color);
            brush.Freeze();
            Brush = brush;
        }

        public Color Color { get; }
        public Brush Brush { get; }
    }
}

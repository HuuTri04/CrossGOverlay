using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CrosshairOverlay.Services.Storage;

namespace CrosshairOverlay.Controls;

/// <summary>Một ô màu bấm vào mở bảng chọn: swatch dựng sẵn, slider RGB và ô nhập hex.</summary>
/// <remarks>
/// Tự viết thay vì kéo thêm thư viện: crosshair chỉ cần vài màu tương phản cao, nên bảng swatch
/// cộng slider giải quyết gần hết nhu cầu, và không thêm một dependency chỉ để chọn màu.
/// </remarks>
public partial class ColorPickerButton : UserControl
{
    private static readonly IReadOnlyList<SwatchItem> SharedSwatches = CreateSwatches();
    private readonly SolidColorBrush _previewBrush = new(Colors.Lime);
    private readonly DispatcherTimer _channelUpdateTimer = new(DispatcherPriority.Input)
    {
        Interval = TimeSpan.FromMilliseconds(16),
    };

    /// <summary>Chặn vòng lặp khi slider cập nhật màu rồi màu lại cập nhật ngược slider.</summary>
    private bool _updating;

    public ColorPickerButton()
    {
        InitializeComponent();
        _channelUpdateTimer.Tick += OnChannelUpdateTick;
        RedSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnChannelDragCompleted));
        GreenSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnChannelDragCompleted));
        BlueSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnChannelDragCompleted));
        Swatch.Background = _previewBrush;
        SwatchList.ItemsSource = SharedSwatches;
        Loaded += (_, _) => Refresh();
        Unloaded += (_, _) =>
        {
            if (_channelUpdateTimer.IsEnabled) CommitChannelColor();
            _channelUpdateTimer.Stop();
        };
    }

    private void OnChannelLabelMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || sender is not FrameworkElement label) return;

        var slider = Grid.GetRow(label) switch
        {
            0 => RedSlider,
            1 => GreenSlider,
            2 => BlueSlider,
            _ => null,
        };
        if (slider?.Focus() == true) e.Handled = true;
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

        _previewBrush.Color = Color.FromRgb(color.R, color.G, color.B);

        HexLabel.Text = JsonColorConverter.ToRgbHex(color);

        if (_updating) return;

        _updating = true;
        try
        {
            RedSlider.Value = color.R;
            GreenSlider.Value = color.G;
            BlueSlider.Value = color.B;
            HexBox.Text = JsonColorConverter.ToRgbHex(color);
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
    }

    private void OnOpenClick(object sender, RoutedEventArgs e) => PickerPopup.IsOpen = !PickerPopup.IsOpen;

    private void OnChannelChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating) return;

        UpdateChannelLabels();
        if (!_channelUpdateTimer.IsEnabled) _channelUpdateTimer.Start();
    }

    private void OnChannelUpdateTick(object? sender, EventArgs e)
    {
        _channelUpdateTimer.Stop();
        CommitChannelColor();
    }

    private void OnChannelDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _channelUpdateTimer.Stop();
        CommitChannelColor();
    }

    private void CommitChannelColor()
    {
        if (_updating) return;

        _updating = true;
        try
        {
            SelectedColor = Color.FromRgb(
                (byte)RedSlider.Value,
                (byte)GreenSlider.Value,
                (byte)BlueSlider.Value);

            HexBox.Text = JsonColorConverter.ToRgbHex(SelectedColor);
        }
        finally
        {
            _updating = false;
        }

        // Refresh bị bỏ qua khi _updating bật, nên cập nhật phần hiển thị ở đây.
        _previewBrush.Color = Color.FromRgb(SelectedColor.R, SelectedColor.G, SelectedColor.B);
        HexLabel.Text = JsonColorConverter.ToRgbHex(SelectedColor);
    }

    private void OnSwatchClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Color color })
            SelectedColor = Color.FromRgb(color.R, color.G, color.B);
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
        if (parsed is { } color) SelectedColor = Color.FromRgb(color.R, color.G, color.B);
        else HexBox.Text = JsonColorConverter.ToRgbHex(SelectedColor);
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

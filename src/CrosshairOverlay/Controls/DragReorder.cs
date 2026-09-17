using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace CrosshairOverlay.Controls;

/// <summary>Yêu cầu chuyển <see cref="Item"/> tới vị trí <see cref="NewIndex"/> (chỉ số SAU khi chuyển).</summary>
public sealed record ReorderRequest(object Item, int NewIndex);

/// <summary>
/// Kéo thả để sắp xếp lại các mục của một <see cref="ListBox"/>, thuần WPF.
/// </summary>
/// <remarks>
/// <para>
/// Gắn lên ListBox: <c>ctl:DragReorder.MoveCommand="{Binding MovePresetToCommand}"</c>. Behavior không tự
/// sửa ItemsSource (thường là ReadOnlyObservableCollection) — nó gửi một <see cref="ReorderRequest"/> cho
/// ViewModel, nơi biết cách chuyển và lưu lại.
/// </para>
/// <para>
/// Bấm giữ rồi kéo quá ngưỡng của Windows (<see cref="SystemParameters.MinimumVerticalDragDistance"/>) mới
/// bắt đầu kéo, nên bấm chọn bình thường không bị ảnh hưởng. Trong lúc kéo, một vạch màu nhấn hiện ở mép
/// trên hoặc dưới của mục dưới con trỏ — đúng chỗ mục sẽ nằm khi thả. Esc huỷ (mặc định của DoDragDrop).
/// </para>
/// </remarks>
public static class DragReorder
{
    /// <summary>
    /// Định dạng dữ liệu riêng: thứ kéo từ ngoài vào (file, chữ) hay từ một danh sách khác không bao giờ
    /// được nhận nhầm.
    /// </summary>
    private const string DataFormat = "CrossGOverlay.DragReorder";

    /// <summary>Kéo sát mép trên/dưới vùng cuộn trong khoảng này (DIP) thì danh sách tự cuộn.</summary>
    private const double AutoScrollMargin = 24d;

    public static readonly DependencyProperty MoveCommandProperty = DependencyProperty.RegisterAttached(
        "MoveCommand", typeof(ICommand), typeof(DragReorder),
        new PropertyMetadata(null, OnMoveCommandChanged));

    public static ICommand? GetMoveCommand(DependencyObject element) => (ICommand?)element.GetValue(MoveCommandProperty);

    public static void SetMoveCommand(DependencyObject element, ICommand? value) => element.SetValue(MoveCommandProperty, value);

    // Chỉ một thao tác kéo tại một thời điểm, luôn trên luồng giao diện.
    private static Point _pressPoint;
    private static ListBoxItem? _pressedItem;
    private static InsertionAdorner? _adorner;

    private static void OnMoveCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox list) return;

        list.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        list.PreviewMouseMove -= OnPreviewMouseMove;
        list.DragOver -= OnDragOver;
        list.DragLeave -= OnDragLeave;
        list.Drop -= OnDrop;

        if (e.NewValue is null)
        {
            list.AllowDrop = false;
            return;
        }

        // AllowDrop kế thừa xuống ListBoxItem, nên thả lên mục nào cũng tới được ListBox.
        list.AllowDrop = true;
        list.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        list.PreviewMouseMove += OnPreviewMouseMove;
        list.DragOver += OnDragOver;
        list.DragLeave += OnDragLeave;
        list.Drop += OnDrop;
    }

    /// <summary>
    /// Chỉ số đích cho <c>ObservableCollection.Move</c> khi thả mục đang ở <paramref name="oldIndex"/> vào
    /// trước (hoặc sau, <paramref name="after"/>) mục ở <paramref name="targetIndex"/>.
    /// </summary>
    /// <remarks>
    /// Move lấy mục ra trước rồi mới chèn: chèn phía sau vị trí cũ thì mọi chỉ số phía sau đã lùi 1.
    /// </remarks>
    internal static int ResolveNewIndex(int oldIndex, int targetIndex, bool after)
    {
        var insertAt = targetIndex + (after ? 1 : 0);
        return oldIndex < insertAt ? insertAt - 1 : insertAt;
    }

    // ------------------------------------------------------------------ nguồn kéo

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var list = (ListBox)sender;
        _pressedItem = ContainerFrom(list, e.OriginalSource as DependencyObject);
        _pressPoint = e.GetPosition(list);
    }

    private static void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressedItem is null) return;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _pressedItem = null;
            return;
        }

        var list = (ListBox)sender;
        var delta = e.GetPosition(list) - _pressPoint;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var item = list.ItemContainerGenerator.ItemFromContainer(_pressedItem);
        _pressedItem = null;
        if (item == DependencyProperty.UnsetValue || item is null) return;

        try
        {
            // Chặn tới khi thả hoặc huỷ; DragOver/Drop chạy lồng bên trong.
            DragDrop.DoDragDrop(list, new DataObject(DataFormat, item), DragDropEffects.Move);
        }
        finally
        {
            RemoveAdorner();
        }
    }

    // ------------------------------------------------------------------ đích thả

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        var list = (ListBox)sender;
        e.Handled = true;

        if (!TryResolveDrop(list, e, out var target, out var after, out _))
        {
            e.Effects = DragDropEffects.None;
            RemoveAdorner();
            return;
        }

        e.Effects = DragDropEffects.Move;
        ShowAdorner(target, after);
        AutoScroll(list, e);
    }

    private static void OnDragLeave(object sender, DragEventArgs e)
    {
        // DragLeave cũng nổi lên khi con trỏ chỉ đi từ mục này sang mục khác bên trong danh sách.
        var list = (ListBox)sender;
        var position = e.GetPosition(list);
        if (position.X < 0 || position.Y < 0 || position.X >= list.ActualWidth || position.Y >= list.ActualHeight)
            RemoveAdorner();
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        var list = (ListBox)sender;
        e.Handled = true;
        RemoveAdorner();

        if (!TryResolveDrop(list, e, out var target, out var after, out var item)) return;

        var oldIndex = list.Items.IndexOf(item);
        var targetIndex = list.ItemContainerGenerator.IndexFromContainer(target);
        if (oldIndex < 0 || targetIndex < 0) return;

        var newIndex = ResolveNewIndex(oldIndex, targetIndex, after);
        if (newIndex == oldIndex) return;

        var request = new ReorderRequest(item, newIndex);
        if (GetMoveCommand(list) is { } command && command.CanExecute(request))
        {
            command.Execute(request);
            e.Effects = DragDropEffects.Move;
        }
    }

    /// <summary>
    /// Mục dưới con trỏ và nửa trên/dưới của nó. Con trỏ ở khoảng trống dưới mục cuối thì coi như "sau mục
    /// cuối". Dữ liệu không phải một mục của CHÍNH danh sách này thì từ chối.
    /// </summary>
    private static bool TryResolveDrop(
        ListBox list, DragEventArgs e, out ListBoxItem target, out bool after, out object item)
    {
        target = null!;
        after = false;
        item = null!;

        if (!e.Data.GetDataPresent(DataFormat) || e.Data.GetData(DataFormat) is not { } data) return false;
        if (!list.Items.Contains(data)) return false;
        item = data;

        var container = ContainerFrom(list, list.InputHitTest(e.GetPosition(list)) as DependencyObject);
        if (container is null)
        {
            if (list.Items.Count == 0) return false;
            if (list.ItemContainerGenerator.ContainerFromIndex(list.Items.Count - 1) is not ListBoxItem last) return false;

            // Trên mục đầu tiên (vùng đệm của ListBox) thì không đoán; dưới mục cuối thì là "cuối danh sách".
            if (e.GetPosition(last).Y < 0) return false;
            target = last;
            after = true;
            return true;
        }

        target = container;
        after = e.GetPosition(container).Y > container.ActualHeight / 2d;
        return true;
    }

    private static void AutoScroll(ListBox list, DragEventArgs e)
    {
        // ListBox nằm trong một ScrollViewer bao ngoài (ScrollViewer riêng của nó không bị giới hạn chiều cao
        // nên không cuộn): cuộn ScrollViewer gần nhất còn cuộn được.
        for (DependencyObject? node = list; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is not ScrollViewer viewer || viewer.ScrollableHeight <= 0) continue;

            var y = e.GetPosition(viewer).Y;
            if (y < AutoScrollMargin && WheelScrollAssist.CanScroll(viewer, scrollDown: false)) viewer.LineUp();
            else if (y > viewer.ViewportHeight - AutoScrollMargin && WheelScrollAssist.CanScroll(viewer, scrollDown: true)) viewer.LineDown();
            return;
        }
    }

    private static ListBoxItem? ContainerFrom(ListBox list, DependencyObject? source)
    {
        for (var node = source; node is not null && !ReferenceEquals(node, list); node = Parent(node))
        {
            if (node is ListBoxItem container) return container;
        }

        return null;
    }

    private static DependencyObject? Parent(DependencyObject node) =>
        node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);

    // ------------------------------------------------------------------ vạch chèn

    private static void ShowAdorner(ListBoxItem target, bool after)
    {
        if (_adorner is not null && ReferenceEquals(_adorner.AdornedElement, target))
        {
            _adorner.After = after;
            return;
        }

        RemoveAdorner();
        if (AdornerLayer.GetAdornerLayer(target) is not { } layer) return;

        _adorner = new InsertionAdorner(target, after);
        layer.Add(_adorner);
    }

    private static void RemoveAdorner()
    {
        if (_adorner is null) return;

        AdornerLayer.GetAdornerLayer(_adorner.AdornedElement)?.Remove(_adorner);
        _adorner = null;
    }

    /// <summary>Vạch 2 DIP màu nhấn ở mép trên hoặc dưới của mục, kèm chấm tròn ở đầu vạch.</summary>
    private sealed class InsertionAdorner : Adorner
    {
        private readonly Pen _pen;
        private readonly Brush _brush;
        private bool _after;

        public InsertionAdorner(UIElement adorned, bool after) : base(adorned)
        {
            IsHitTestVisible = false;
            _after = after;

            // Brush trong Theme là StaticResource đã đóng băng; dự phòng khi không tìm thấy thì tự đóng băng.
            _brush = (adorned as FrameworkElement)?.TryFindResource("AccentBright") as Brush ?? Freeze(new SolidColorBrush(Color.FromRgb(0x00, 0xED, 0x64)));
            _pen = new Pen(_brush, 2d) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            _pen.Freeze();
        }

        public bool After
        {
            get => _after;
            set
            {
                if (_after == value) return;
                _after = value;
                InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            var size = AdornedElement.RenderSize;

            // Mục cạnh nhau cách 2 DIP (Margin 0,1): vạch nằm đúng khe giữa hai mục, không đè lên chữ.
            var y = _after ? size.Height + 1d : -1d;
            dc.DrawLine(_pen, new Point(6d, y), new Point(size.Width - 4d, y));
            dc.DrawEllipse(_brush, null, new Point(4d, y), 3d, 3d);
        }

        private static Brush Freeze(Brush brush)
        {
            brush.Freeze();
            return brush;
        }
    }
}

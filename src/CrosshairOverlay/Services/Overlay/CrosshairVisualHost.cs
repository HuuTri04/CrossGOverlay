using System.Windows;
using System.Windows.Media;

namespace CrosshairOverlay.Services.Overlay;

/// <summary>
/// Element rỗng chỉ làm một việc: vẽ <see cref="Drawing"/> đã dựng sẵn, gốc toạ độ đặt ở
/// tâm hình học của element.
/// </summary>
/// <remarks>
/// Không có logic vẽ nào ở đây — hình do <see cref="Core.Abstractions.ICrosshairRenderer"/>
/// dựng và đã <c>Freeze()</c>. Nhờ vậy <c>OnRender</c> chỉ là một lệnh
/// <c>DrawDrawing</c> duy nhất, và WPF chỉ gọi lại nó khi có
/// <see cref="UIElement.InvalidateVisual"/> tường minh.
/// </remarks>
internal sealed class CrosshairVisualHost : FrameworkElement
{
    private Drawing? _drawing;

    public CrosshairVisualHost()
    {
        IsHitTestVisible = false;
        Focusable = false;

        // Ba thứ này cùng phục vụ một mục tiêu: nét crosshair phải rơi đúng biên pixel vật lý.
        // Chúng bổ sung cho việc bám lưới pixel trong RenderPlan chứ không thay thế — bám lưới
        // lo TOẠ ĐỘ, còn chúng lo khâu bố cục và rasterize cuối cùng.
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    /// <summary>
    /// Bật/tắt khử răng cưa. Xem <see cref="Core.Abstractions.ICrosshairRenderer.PrefersAliasedEdges"/>
    /// — tắt cho hình toàn nét thẳng thì sắc tuyệt đối, nhưng tắt cho hình tròn hay nhánh chéo
    /// thì thành răng cưa bậc thang.
    /// </summary>
    public void SetAliasing(bool aliased)
    {
        var mode = aliased ? EdgeMode.Aliased : EdgeMode.Unspecified;
        if (RenderOptions.GetEdgeMode(this) == mode) return;

        RenderOptions.SetEdgeMode(this, mode);
        InvalidateVisual();
    }

    public void SetDrawing(Drawing? drawing)
    {
        if (ReferenceEquals(_drawing, drawing)) return;

        _drawing = drawing;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (_drawing is null) return;

        var size = RenderSize;
        if (size.Width <= 0 || size.Height <= 0) return;

        // Hợp đồng của ICrosshairRenderer: gốc (0,0) của Drawing là TÂM crosshair.
        drawingContext.PushTransform(new TranslateTransform(size.Width / 2d, size.Height / 2d));
        drawingContext.DrawDrawing(_drawing);
        drawingContext.Pop();
    }
}

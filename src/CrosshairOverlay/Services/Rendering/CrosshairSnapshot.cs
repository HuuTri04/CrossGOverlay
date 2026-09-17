using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;

namespace CrosshairOverlay.Services.Rendering;

/// <summary>
/// Chụp crosshair thành bitmap ở đúng độ phân giải thiết bị, bằng CHÍNH renderer của overlay.
/// </summary>
/// <remarks>
/// Dùng chung cho khung xem trước của trình chỉnh sửa và ảnh thu nhỏ trong Thư viện mẫu: phóng bitmap bằng
/// NearestNeighbor thì người dùng thấy đúng từng pixel thật (kể cả kết quả bám lưới pixel), còn phóng hình
/// vector thì mọi nét đều mượt và che mất chính thứ cần xem.
/// </remarks>
public static class CrosshairSnapshot
{
    /// <param name="liveDrawing">
    /// Crosshair có animation (GIF động): trả về hình vẽ sống thay vì bitmap, vì chụp lại sẽ đứng hình ở khung đầu.
    /// </param>
    /// <returns>Bitmap đã đóng băng; null nếu hình rỗng hoặc có animation.</returns>
    public static BitmapSource? Render(
        ICrosshairRenderer renderer,
        CrosshairProfile profile,
        double dpiScaleX,
        double dpiScaleY,
        double maxExtent,
        out Drawing? liveDrawing)
    {
        liveDrawing = null;

        var scaleX = dpiScaleX > 0 ? dpiScaleX : 1d;
        var scaleY = dpiScaleY > 0 ? dpiScaleY : 1d;

        var options = new CrosshairRenderOptions(
            DpiScale: scaleX,
            SnapToPixels: Math.Abs(profile.Rotation) < 0.01d,
            MaxExtent: maxExtent);

        var sizeDip = renderer.Measure(profile, options);
        var drawing = renderer.Build(profile, options);

        if (!drawing.IsFrozen)
        {
            liveDrawing = drawing;
            return null;
        }

        var pixelWidth = (int)Math.Round(sizeDip.Width * scaleX);
        var pixelHeight = (int)Math.Round(sizeDip.Height * scaleY);
        if (pixelWidth <= 0 || pixelHeight <= 0) return null;

        var visual = new DrawingVisual();

        // Áp ĐÚNG quyết định khử răng cưa mà overlay dùng, nếu không ảnh chụp sẽ nói dối về độ sắc.
        RenderOptions.SetEdgeMode(
            visual,
            renderer.PrefersAliasedEdges(profile) ? EdgeMode.Aliased : EdgeMode.Unspecified);

        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new TranslateTransform(sizeDip.Width / 2d, sizeDip.Height / 2d));
            dc.DrawDrawing(drawing);
            dc.Pop();
        }

        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96d * scaleX, 96d * scaleY, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}

using System.Drawing;
using System.Drawing.Imaging;
using CrosshairOverlay.Interop;

namespace CrosshairOverlay.Services.System;

/// <summary>
/// Nạp icon khay hệ thống từ <c>images/app.ico</c> đã nhúng trong assembly.
/// </summary>
/// <remarks>
/// Tách thành file riêng vì ở đây dùng <c>System.Drawing</c>, mà gần như mọi kiểu của nó
/// (<c>Color</c>, <c>Bitmap</c>, <c>Graphics</c>) đều trùng tên với kiểu WPF.
///
/// <para>
/// Cố tình KHÔNG dùng <c>TaskbarIcon.IconSource</c>: H.NotifyIcon chỉ chuyển đổi được vài kiểu
/// <c>ImageSource</c> nhất định và ném <c>NotImplementedException</c> với kiểu khác — bên trong
/// một continuation async, tức là giết luôn tiến trình. Gán thẳng <c>System.Drawing.Icon</c>
/// vào <c>TaskbarIcon.Icon</c> thì bỏ qua hoàn toàn tầng đó.
/// </para>
/// </remarks>
internal static class TrayIconFactory
{
    private const string IconResourcePath = "images/app.ico";

    /// <summary>Khay hệ thống lấy khung 32×32 rồi tự thu nhỏ theo DPI.</summary>
    private const int IconSize = 32;

    public static Icon Create(bool enabled)
    {
        var icon = Load();

        // Không nạp được (rất khó xảy ra vì icon nằm trong assembly) thì vẫn phải có gì đó để
        // hiện, còn hơn không có icon nào trong khay.
        if (icon is null) return (Icon)SystemIcons.Application.Clone();

        if (enabled) return icon;

        using (icon)
        {
            return Desaturate(icon);
        }
    }

    private static Icon? Load()
    {
        try
        {
            var resource = global::System.Windows.Application.GetResourceStream(
                new Uri(IconResourcePath, UriKind.Relative));

            if (resource?.Stream is not { } stream) return null;

            using (stream)
            {
                return new Icon(stream, new Size(IconSize, IconSize));
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Biến thể xám mờ cho trạng thái overlay đang tắt — nhìn vào khay là biết ngay, không
    /// phải rê chuột đọc tooltip.
    /// </summary>
    private static Icon Desaturate(Icon source)
    {
        using var original = source.ToBitmap();
        using var faded = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppArgb);

        using (var graphics = Graphics.FromImage(faded))
        {
            // Hệ số 0.299/0.587/0.114 là trọng số độ sáng cảm nhận được của mắt người; chia
            // đều ba kênh sẽ cho ra ảnh xám trông sai độ sáng.
            var matrix = new ColorMatrix(
            [
                [0.299f, 0.299f, 0.299f, 0f, 0f],
                [0.587f, 0.587f, 0.587f, 0f, 0f],
                [0.114f, 0.114f, 0.114f, 0f, 0f],
                [0f, 0f, 0f, 0.55f, 0f],
                [0f, 0f, 0f, 0f, 1f],
            ]);

            using var attributes = new ImageAttributes();
            attributes.SetColorMatrix(matrix);

            graphics.DrawImage(
                original,
                new Rectangle(0, 0, original.Width, original.Height),
                0, 0, original.Width, original.Height,
                GraphicsUnit.Pixel,
                attributes);
        }

        return ToIcon(faded);
    }

    /// <summary>
    /// <see cref="Icon.FromHandle"/> KHÔNG sở hữu handle được truyền vào, nên phải nhân bản
    /// thành một Icon độc lập rồi tự huỷ handle gốc — nếu không sẽ rò một HICON mỗi lần
    /// icon đổi trạng thái.
    /// </summary>
    private static Icon ToIcon(Bitmap bitmap)
    {
        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }
}

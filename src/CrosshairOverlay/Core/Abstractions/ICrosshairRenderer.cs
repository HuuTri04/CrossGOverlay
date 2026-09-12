using System.Windows;
using System.Windows.Media;
using CrosshairOverlay.Core.Models;

namespace CrosshairOverlay.Core.Abstractions;

/// <summary>
/// Chuyển một <see cref="CrosshairProfile"/> thành hình vẽ WPF.
/// </summary>
/// <remarks>
/// Cài đặt phải là hàm THUẦN (pure): cùng input cho ra cùng output, không giữ state.
/// <see cref="Build"/> trả về <see cref="Drawing"/> đã <c>Freeze()</c> nên có thể cache và
/// dùng lại trên nhiều thread — nền tảng cho yêu cầu "chỉ redraw khi cấu hình đổi".
/// Cùng một renderer phục vụ cả overlay lẫn khung preview trong cửa sổ Settings.
/// </remarks>
public interface ICrosshairRenderer
{
    /// <summary>Dựng hình vẽ, gốc toạ độ (0,0) nằm ở TÂM crosshair.</summary>
    Drawing Build(CrosshairProfile profile, CrosshairRenderOptions options);

    /// <summary>
    /// Kích thước bao (DIP) mà crosshair chiếm. Overlay dùng nó để chỉ tạo surface đủ nhỏ
    /// thay vì phủ toàn màn hình.
    /// </summary>
    Size Measure(CrosshairProfile profile, CrosshairRenderOptions options);

    /// <summary>
    /// Preset này có nên TẮT khử răng cưa hay không.
    /// </summary>
    /// <remarks>
    /// Tắt khử răng cưa cho hình gồm toàn nét thẳng đứng/ngang đã bám lưới pixel sẽ cho tâm
    /// ngắm sắc tuyệt đối. Nhưng bật cờ đó cho hình tròn, nhánh chéo hay hình đã xoay thì các
    /// đường cong và đường xiên bị răng cưa bậc thang — xấu hơn hẳn. Vì vậy quyết định phải
    /// phụ thuộc nội dung preset, không thể bật cứng cho mọi trường hợp.
    /// </remarks>
    bool PrefersAliasedEdges(CrosshairProfile profile);
}

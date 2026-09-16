using CrosshairOverlay.Core.Models;

namespace CrosshairOverlay.Core.Abstractions;

/// <summary>
/// Một ô mã trong hộp thoại "Xuất mã tâm ngắm".
/// </summary>
/// <param name="Title">Tên game hoặc tên định dạng.</param>
/// <param name="Code">Mã dựng được, hoặc null khi định dạng này không mô tả được preset.</param>
/// <param name="Note">
/// Câu nói rõ mã này giữ được gì và mất gì — hoặc lý do không dựng được, khi <paramref name="Code"/> là null.
/// </param>
public sealed record CrosshairExportCode(string Title, string? Code, string? Note);

/// <summary>Toàn bộ mã của một preset, theo đúng thứ tự hiển thị trong hộp thoại.</summary>
public sealed record CrosshairExportCodes(
    string PresetName,
    CrosshairExportCode Valorant,
    CrosshairExportCode Cs2,
    CrosshairExportCode App)
{
    /// <summary>
    /// Bộ lệnh console CS2 tương đương share code, hiện ngay dưới ô CS2.
    /// </summary>
    /// <remarks>
    /// Không phải định dạng thứ tư: cùng một crosshair, chỉ khác cách đưa vào game. Cần khi người
    /// dùng muốn dán vào autoexec.cfg hoặc chỉnh tay từng thông số.
    /// </remarks>
    public CrosshairExportCode? Cs2Console { get; init; }
}

/// <summary>Dịch preset của ứng dụng sang mã chia sẻ của từng game.</summary>
public interface ICrosshairTranslator
{
    string ToValorantCode(CrosshairProfile profile);

    string ToCs2Code(CrosshairProfile profile);

    string ToCs2ConsoleCommands(CrosshairProfile profile);

    string ToAppCode(CrosshairProfile profile);

    /// <param name="imageCode">Mã tâm ngắm ảnh đã dựng sẵn, hoặc null.</param>
    /// <param name="imageCodeError">Lý do khi không dựng được mã ảnh.</param>
    CrosshairExportCodes BuildCodes(CrosshairProfile profile, string? imageCode, string? imageCodeError);
}

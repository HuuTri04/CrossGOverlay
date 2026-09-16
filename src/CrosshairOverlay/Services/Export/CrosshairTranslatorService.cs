using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Localization;
using CrosshairOverlay.Services.Import;

namespace CrosshairOverlay.Services.Export;

/// <summary>
/// Dịch một preset của ứng dụng sang mã chia sẻ của từng game.
/// </summary>
/// <remarks>
/// <para>
/// Mỗi định dạng mô tả được một tập tính năng khác nhau, nên mỗi mã đi kèm MỘT câu nói rõ sẽ mất
/// gì: đưa ra một mã trông có vẻ đúng rồi để người dùng tự phát hiện crosshair trong game khác
/// hẳn là cách tệ nhất. Mã nào không dựng được thì trả về lý do thay vì chuỗi rỗng.
/// </para>
/// <para>
/// Thuần tính toán, không đụng đĩa và không đụng giao diện, nên gọi được từ luồng nền — phần xuất
/// mã tâm ngắm ảnh có thể mất hàng trăm mili giây.
/// </para>
/// </remarks>
public sealed class CrosshairTranslatorService : ICrosshairTranslator
{
    /// <summary>Mã Valorant: <c>0;P;c;8;u;RRGGBBAA;…</c></summary>
    public string ToValorantCode(CrosshairProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return ValorantCrosshairCode.Encode(CrosshairCodeConverter.ToValorantCrosshair(profile));
    }

    /// <summary>Share code của CS2: <c>CSGO-xxxxx-xxxxx-xxxxx-xxxxx-xxxxx</c>, dán thẳng vào game.</summary>
    public string ToCs2Code(CrosshairProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return Cs2ShareCodeEncoder.Encode(CrosshairCodeConverter.ToCs2Crosshair(profile));
    }

    /// <summary>
    /// Bộ lệnh console CS2 tương đương share code.
    /// </summary>
    /// <remarks>
    /// Hữu ích khi người dùng muốn chỉnh tay từng thông số, hoặc dán vào file autoexec.cfg.
    /// </remarks>
    public string ToCs2ConsoleCommands(CrosshairProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var c = CrosshairCodeConverter.ToCs2Crosshair(profile);
        var culture = global::System.Globalization.CultureInfo.InvariantCulture;

        return string.Join(
            "; ",
            "cl_crosshairstyle " + c.Style.ToString(culture),
            "cl_crosshairsize " + c.Size.ToString("0.#", culture),
            "cl_crosshairthickness " + c.Thickness.ToString("0.#", culture),
            "cl_crosshairgap " + c.Gap.ToString("0.#", culture),
            "cl_crosshair_outlinethickness " + c.OutlineThickness.ToString("0.#", culture),
            "cl_crosshair_drawoutline " + (c.HasOutline ? "1" : "0"),
            "cl_crosshairdot " + (c.HasCenterDot ? "1" : "0"),
            "cl_crosshair_t " + (c.IsTStyle ? "1" : "0"),
            "cl_crosshaircolor " + Cs2Crosshair.CustomColorIndex.ToString(culture),
            "cl_crosshaircolor_r " + c.Red.ToString(culture),
            "cl_crosshaircolor_g " + c.Green.ToString(culture),
            "cl_crosshaircolor_b " + c.Blue.ToString(culture),
            "cl_crosshairusealpha 1",
            "cl_crosshairalpha " + c.Alpha.ToString(culture));
    }

    /// <summary>Mã nội bộ của ứng dụng — giữ nguyên MỌI tính năng, kể cả thứ hai game không có.</summary>
    public string ToAppCode(CrosshairProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return AppCrosshairCode.Encode(profile);
    }

    /// <summary>
    /// Dựng cả ba mã cho hộp thoại "Xuất mã tâm ngắm".
    /// </summary>
    /// <param name="profile">Preset cần xuất.</param>
    /// <param name="imageCode">
    /// Mã tâm ngắm ảnh đã dựng sẵn (cần đọc file nên do nơi gọi lo), hoặc null nếu preset không
    /// phải tâm ngắm ảnh hoặc không đọc được ảnh.
    /// </param>
    /// <param name="imageCodeError">Câu giải thích khi <paramref name="imageCode"/> là null.</param>
    public CrosshairExportCodes BuildCodes(CrosshairProfile profile, string? imageCode, string? imageCodeError)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return profile.Type == CrosshairType.Image
            ? BuildForImage(profile, imageCode, imageCodeError)
            : BuildForDrawn(profile);
    }

    private static CrosshairExportCodes BuildForImage(CrosshairProfile profile, string? imageCode, string? imageCodeError)
    {
        // Không game nào nhận ảnh làm crosshair: mã game cho preset ảnh chỉ có thể là một chữ thập
        // bịa ra, không liên quan gì tới ảnh người dùng chọn.
        var unsupported = Tr.Get("Export_ImageNotSupported");

        return new CrosshairExportCodes(
            profile.Name,
            new CrosshairExportCode(Tr.Get("Export_Valorant"), null, unsupported),
            new CrosshairExportCode(Tr.Get("Export_Cs2"), null, unsupported),
            new CrosshairExportCode(
                Tr.Get("Export_App"),
                imageCode,
                imageCode is null ? imageCodeError ?? unsupported : Tr.Get("Export_AppNoteImage")));
    }

    private CrosshairExportCodes BuildForDrawn(CrosshairProfile profile)
    {
        var valorantNote = CrosshairCodeConverter.CanExportFaithfully(profile)
            ? Tr.Get("Export_ValorantNote")
            : Tr.Get("Export_ValorantLossy");

        var cs2Note = CrosshairCodeConverter.CanExportToCs2Faithfully(profile)
            ? Tr.Get("Export_Cs2Note")
            : Tr.Get("Export_Cs2Lossy");

        return new CrosshairExportCodes(
            profile.Name,
            new CrosshairExportCode(Tr.Get("Export_Valorant"), ToValorantCode(profile), valorantNote),
            new CrosshairExportCode(Tr.Get("Export_Cs2"), ToCs2Code(profile), cs2Note),
            new CrosshairExportCode(Tr.Get("Export_App"), ToAppCode(profile), Tr.Get("Export_AppNote")))
        {
            Cs2Console = new CrosshairExportCode(
                Tr.Get("Export_Cs2Console"), ToCs2ConsoleCommands(profile), null),
        };
    }
}

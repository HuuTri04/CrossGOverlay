using System.IO;
using System.IO.Compression;
using System.Text.Json;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Services.Storage;

namespace CrosshairOverlay.Services.Import;

/// <summary>
/// Mã chia sẻ nội bộ: CGO-CH2 dùng binary v1; CGO-CH cũ dùng JSON/GZip.
/// </summary>
/// <remarks>
/// <para>
/// Mã Valorant và CS2 chỉ mô tả được phần giao nhau nghèo nàn giữa hai game: một lớp nhánh, chấm
/// giữa, viền. Preset của ứng dụng còn có vòng tròn, góc xoay, hai lớp nhánh, độ dài dọc riêng,
/// bật/tắt từng vạch, bo đầu vạch — gửi cho người dùng CrossGOverlay khác qua mã game là mất sạch.
/// Mã CGO-CH2 giữ các thông số preset bằng binary không giảm độ chính xác;
/// mã CGO-CH cũ vẫn được đọc từ JSON đã nén.
/// </para>
/// <para>
/// Cùng họ với <see cref="ImageCrosshairCode"/> (tâm ngắm ảnh) và dùng chung cách ký checksum, nên
/// mã cắt cụt hay sửa bậy đều bị phát hiện thay vì dựng ra một preset sai lặng lẽ.
/// </para>
/// </remarks>
public static class AppCrosshairCode
{
    /// <summary>Legacy JSON/GZip prefix, retained for existing shared codes.</summary>
    public const string Prefix = "CGO-CH;";
    public const string BinaryPrefix = "CGO-CH2;";
    public const string CurrentPrefix = BinaryPrefix;

    /// <summary>
    /// Chặn "bom nén" của mã cũ và giới hạn payload binary của mã mới.
    /// </summary>
    public const int MaxJsonBytes = 256 * 1024;

    public static bool IsAppCode(string? text) => text is not null &&
        (text.TrimStart().StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ||
         text.TrimStart().StartsWith(BinaryPrefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>Dựng mã từ preset. Ảnh KHÔNG đi theo mã này — tâm ngắm ảnh dùng <see cref="ImageCrosshairCode"/>.</summary>
    public static string Encode(CrosshairProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        // Image codes use CGO-IMG. Preserve the old rejection path for callers
        // that mistakenly request a drawn-code export from an image profile.
        if (profile.Type != CrosshairType.Standard) return EncodeLegacy(profile);
        var payload = BinaryCrosshairCode.Encode(profile);
        var base64 = Convert.ToBase64String(payload).TrimEnd('=');
        return ShareCodeChecksum.Append(BinaryPrefix + base64, BinaryPrefix);
    }

    /// <summary>Former encoder, kept for compatibility tests and old share codes.</summary>
    public static string EncodeLegacy(CrosshairProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        // Bản sao để không đụng vào preset đang dùng, và để bỏ ảnh nhúng (chỉ có nghĩa ở máy này).
        // KHÔNG dùng Clone(newIdentity: true): nó thêm hậu tố "(Bản sao)" vào tên. Id trong mã bị
        // bỏ qua lúc nhập — bên nhận luôn cấp Id mới.
        var copy = profile.Clone();
        copy.EmbeddedImage = null;

        var json = JsonSerializer.SerializeToUtf8Bytes(copy, AppJson.Options);
        return ShareCodeChecksum.Append(Prefix + Convert.ToBase64String(Gzip(json)), Prefix);
    }

    /// <summary>Đọc mã. Không bao giờ ném exception với dữ liệu hỏng — trả false.</summary>
    /// <param name="reason">Lý do kỹ thuật, chỉ để ghi log; thông báo cho người dùng là một câu chung.</param>
    public static bool TryDecode(string? text, out CrosshairProfile? profile, out string? reason)
    {
        profile = null;

        try
        {
            return TryDecodeCore(text, out profile, out reason);
        }
        catch (Exception ex)
        {
            // Lưới an toàn cuối: dữ liệu ngoài vào không được phép làm sập ứng dụng.
            profile = null;
            reason = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    private static bool TryDecodeCore(string? text, out CrosshairProfile? profile, out string? reason)
    {
        profile = null;

        if (!IsAppCode(text) || text!.Length > 500_000)
        {
            reason = "Không phải mã CGO-CH hợp lệ.";
            return false;
        }

        var normalized = new string([.. text.Where(c => !char.IsWhiteSpace(c))]);
        var binary = normalized.StartsWith(BinaryPrefix, StringComparison.OrdinalIgnoreCase);
        var prefix = binary ? BinaryPrefix : Prefix;

        if (!ShareCodeChecksum.TryVerify(normalized.AsSpan(prefix.Length), out var signedPart, out reason))
            return false;

        CrosshairProfile? decoded;
        if (binary)
        {
            if (signedPart.Length % 4 == 1 || signedPart.Length > 350_000)
            {
                reason = "Độ dài mã binary không hợp lệ.";
                return false;
            }
            var padded = signedPart.PadRight((signedPart.Length + 3) & ~3, '=');
            if (!TryFromBase64(padded, out var payload, out reason)) return false;
            if (payload.Length > MaxJsonBytes)
            {
                reason = "Mã binary vượt giới hạn kích thước.";
                return false;
            }
            decoded = BinaryCrosshairCode.Decode(payload);
        }
        else
        {
            if (!TryFromBase64(signedPart, out var payload, out reason)) return false;
            if (!TryGunzip(payload, out var json, out reason)) return false;
            decoded = JsonSerializer.Deserialize<CrosshairProfile>(json, AppJson.Options);
        }
        if (decoded is null)
        {
            reason = "JSON rỗng.";
            return false;
        }

        // Id mới: mã có thể được nhập nhiều lần, và preset trùng Id sẽ ghi đè lên nhau trong kho.
        decoded.Id = Guid.NewGuid();
        decoded.EmbeddedImage = null;
        decoded.CreatedUtc = DateTimeOffset.UtcNow;
        decoded.ModifiedUtc = decoded.CreatedUtc;

        // Ảnh nằm ở máy người gửi, máy người nhận không có: mã này chỉ chở tâm ngắm VẼ.
        if (decoded.Type == CrosshairType.Image)
        {
            reason = "Mã chứa tâm ngắm ảnh — dạng đó phải dùng " + ImageCrosshairCode.Prefix;
            return false;
        }

        profile = decoded;
        reason = null;
        return true;
    }

    private static bool TryFromBase64(string payload, out byte[] bytes, out string? reason)
    {
        bytes = [];

        var buffer = new byte[(payload.Length / 4 * 3) + 3];
        if (!Convert.TryFromBase64String(payload, buffer, out var written))
        {
            reason = "Phần dữ liệu không phải Base64 hợp lệ.";
            return false;
        }

        bytes = buffer[..written];
        reason = null;
        return true;
    }

    private static byte[] Gzip(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(data);
        return output.ToArray();
    }

    private static bool TryGunzip(byte[] payload, out byte[] data, out string? reason)
    {
        data = [];

        if (payload.Length < 2 || payload[0] != 0x1F || payload[1] != 0x8B)
        {
            reason = "Dữ liệu không phải GZip.";
            return false;
        }

        using var input = new MemoryStream(payload);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();

        var buffer = new byte[8192];
        int read;
        while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + read > MaxJsonBytes)
            {
                reason = "Dữ liệu giải nén vượt quá giới hạn " + MaxJsonBytes + " byte.";
                return false;
            }

            output.Write(buffer, 0, read);
        }

        data = output.ToArray();

        if (data.Length == 0 || data[0] != (byte)'{')
        {
            reason = "Dữ liệu giải nén không phải JSON của preset.";
            return false;
        }

        reason = null;
        return true;
    }
}

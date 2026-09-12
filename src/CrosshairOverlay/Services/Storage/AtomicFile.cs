using System.IO;
using System.Text;
using System.Text.Json;

namespace CrosshairOverlay.Services.Storage;

/// <summary>
/// Ghi file JSON theo kiểu ghi-tạm-rồi-thay-thế.
/// </summary>
/// <remarks>
/// Ghi thẳng đè lên file cũ có nghĩa là mất điện hoặc app bị kill giữa chừng sẽ để lại một
/// file JSON cụt — người dùng mất toàn bộ preset. Ghi ra file tạm rồi <see cref="File.Move"/>
/// đè lên thì thao tác thay thế là nguyên tử ở mức hệ thống file, nên file đích luôn ở một
/// trong hai trạng thái hợp lệ: nội dung cũ, hoặc nội dung mới.
/// </remarks>
internal static class AtomicFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static async Task WriteJsonAsync<T>(
        string path, T value, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(value, AppJson.Options);
        var tempPath = path + ".tmp";

        await File.WriteAllTextAsync(tempPath, json, Utf8NoBom, cancellationToken)
            .ConfigureAwait(false);

        File.Move(tempPath, path, overwrite: true);
    }

    public static async Task<T?> ReadJsonAsync<T>(
        string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return default;

        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return default;

        return JsonSerializer.Deserialize<T>(json, AppJson.Options);
    }

    /// <summary>
    /// Dọn file <c>.tmp</c> còn sót lại từ lần ghi bị ngắt giữa chừng. Lỗi ở đây không quan trọng.
    /// </summary>
    public static void CleanupTemporaries(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return;

            foreach (var file in Directory.EnumerateFiles(directory, "*.tmp"))
            {
                try { File.Delete(file); }
                catch (IOException) { /* file đang bị giữ, để lần sau */ }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

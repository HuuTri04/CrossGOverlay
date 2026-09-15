using System.IO;
using CrosshairOverlay.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay.Services.Storage;

/// <summary>
/// Xoá dữ liệu người dùng để ứng dụng khởi động như lần đầu: <c>settings.json</c>, mọi preset và ảnh
/// trong kho.
/// </summary>
/// <remarks>
/// <para>
/// Chỉ chạy lúc KHỞI ĐỘNG của instance mới, sau khi instance cũ đã thoát hẳn — xem
/// <see cref="Core.StartupArguments"/>. Xoá lúc app còn chạy thì bước tự lưu khi thoát sẽ ghi lại
/// ngay những gì vừa xoá.
/// </para>
/// <para>
/// Chỉ xoá đúng các file của ứng dụng theo tên/đuôi đã biết, không đệ quy, không xoá thư mục gốc:
/// thư mục dữ liệu có thể chứa thứ người dùng tự để vào. Nhật ký (ở LocalAppData) được giữ lại để
/// còn tra cứu nếu có sự cố.
/// </para>
/// </remarks>
public static class FactoryReset
{
    /// <returns>Số file đã xoá.</returns>
    public static int Wipe(IAppPathProvider paths, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logger);

        var removed = 0;
        var root = paths.RootDirectory;
        var settingsName = Path.GetFileName(paths.SettingsFilePath);

        // settings.json cùng các bản đi kèm của nó: file tạm của lần ghi dở, bản .corrupt đã cách ly.
        if (Directory.Exists(root))
        {
            foreach (var file in Directory.EnumerateFiles(root, settingsName + "*"))
                removed += TryDelete(file, logger);
        }

        if (Directory.Exists(paths.PresetsDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(paths.PresetsDirectory))
            {
                var extension = Path.GetExtension(file);
                if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase)
                    || file.EndsWith(".corrupt", StringComparison.OrdinalIgnoreCase))
                {
                    removed += TryDelete(file, logger);
                }
            }
        }

        var images = Path.Combine(root, CustomImageStore.FolderName);
        if (Directory.Exists(images))
        {
            foreach (var file in Directory.EnumerateFiles(images))
                removed += TryDelete(file, logger);
        }

        logger.LogWarning("Khôi phục cài đặt gốc: đã xoá {Count} file dữ liệu.", removed);
        return removed;
    }

    private static int TryDelete(string file, ILogger logger)
    {
        // Instance cũ vừa thoát: hệ thống có thể chưa kịp nhả handle file trong vài chục mili-giây.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                File.Delete(file);
                return 1;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(100);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogError(ex, "Không xoá được {File} khi khôi phục cài đặt gốc.", file);
                return 0;
            }
        }

        return 0;
    }
}

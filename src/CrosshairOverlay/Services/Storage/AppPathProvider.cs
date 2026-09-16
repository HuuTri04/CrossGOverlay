using System.IO;
using CrosshairOverlay.Core.Abstractions;

namespace CrosshairOverlay.Services.Storage;

/// <inheritdoc cref="IAppPathProvider"/>
public sealed class AppPathProvider : IAppPathProvider
{
    private const string FolderName = "CrosshairOverlay";

    /// <summary>
    /// Thư mục dữ liệu theo lựa chọn của người dùng (xem <see cref="StorageLocation"/>): thư mục tuỳ chỉnh
    /// nếu có và dùng được, không thì %APPDATA%\CrosshairOverlay. Log luôn ở LocalAppData.
    /// </summary>
    public static AppPathProvider FromStorageLocation()
    {
        var location = StorageLocation.Resolve();
        return new AppPathProvider(location.RootDirectory, DefaultLogsDirectory)
        {
            IsCustomLocation = location.IsCustom,
            StorageFallbackFrom = location.FallbackFrom,
        };
    }

    private static string DefaultLogsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderName, "logs");

    /// <param name="rootOverride">Thư mục dữ liệu; null là thư mục mặc định. Test dùng để trỏ sang thư mục tạm.</param>
    /// <param name="logsDirectory">Thư mục log; null thì theo <paramref name="rootOverride"/> (test) hoặc LocalAppData.</param>
    public AppPathProvider(string? rootOverride = null, string? logsDirectory = null)
    {
        RootDirectory = rootOverride ?? StorageLocation.DefaultRoot;

        PresetsDirectory = Path.Combine(RootDirectory, "presets");
        SettingsFilePath = Path.Combine(RootDirectory, "settings.json");

        // Log nằm ở LocalAppData: dữ liệu máy-cụ-thể, không nên theo roaming profile đi khắp nơi.
        LogsDirectory = logsDirectory
            ?? (rootOverride is not null ? Path.Combine(rootOverride, "logs") : DefaultLogsDirectory);
    }

    /// <summary>Đang dùng thư mục người dùng tự chọn.</summary>
    public bool IsCustomLocation { get; private init; }

    /// <summary>Thư mục tuỳ chỉnh không truy cập được lúc khởi động, đang tạm dùng mặc định. Null nếu bình thường.</summary>
    public string? StorageFallbackFrom { get; private init; }

    public string RootDirectory { get; }
    public string PresetsDirectory { get; }
    public string SettingsFilePath { get; }
    public string LogsDirectory { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(PresetsDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}

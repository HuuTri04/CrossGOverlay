using System.IO;
using CrosshairOverlay.Core.Abstractions;

namespace CrosshairOverlay.Services.Storage;

/// <inheritdoc cref="IAppPathProvider"/>
public sealed class AppPathProvider : IAppPathProvider
{
    private const string FolderName = "CrosshairOverlay";

    /// <param name="rootOverride">Chỉ dùng trong test để trỏ sang thư mục tạm.</param>
    public AppPathProvider(string? rootOverride = null)
    {
        RootDirectory = rootOverride
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), FolderName);

        PresetsDirectory = Path.Combine(RootDirectory, "presets");
        SettingsFilePath = Path.Combine(RootDirectory, "settings.json");

        // Log nằm ở LocalAppData: dữ liệu máy-cụ-thể, không nên theo roaming profile đi khắp nơi.
        LogsDirectory = rootOverride is not null
            ? Path.Combine(rootOverride, "logs")
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                FolderName,
                "logs");
    }

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

using System.IO;
using System.IO.Compression;
using System.Text.Json;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;

namespace CrosshairOverlay.Services.Storage;

/// <summary>On-demand ZIP backup. Restore runs before services load in the replacement process.</summary>
internal static class DataBackupArchive
{
    private const int MaxEntries = 2048;
    private const long MaxUncompressedBytes = 256L * 1024 * 1024;

    public static void Create(IAppPathProvider paths, string destination)
    {
        var temp = destination + ".tmp";
        try
        {
            using (var archive = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                if (!File.Exists(paths.SettingsFilePath))
                    throw new InvalidDataException("Không tìm thấy settings.json.");
                archive.CreateEntryFromFile(paths.SettingsFilePath, "settings.json", CompressionLevel.Optimal);

                if (Directory.Exists(paths.PresetsDirectory))
                    foreach (var file in Directory.EnumerateFiles(paths.PresetsDirectory, "*.json"))
                        archive.CreateEntryFromFile(file, "presets/" + Path.GetFileName(file), CompressionLevel.Optimal);

                var images = Path.Combine(paths.RootDirectory, CustomImageStore.FolderName);
                if (Directory.Exists(images))
                    foreach (var file in Directory.EnumerateFiles(images))
                        archive.CreateEntryFromFile(file, "CustomImages/" + Path.GetFileName(file), CompressionLevel.Optimal);
            }

            Validate(temp);
            File.Move(temp, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public static void Validate(string source)
    {
        using var archive = ZipFile.OpenRead(source);
        if (archive.Entries.Count is 0 or > MaxEntries)
            throw new InvalidDataException("Số mục trong bản sao lưu không hợp lệ.");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        var settingsFound = false;
        var presetsFound = false;
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name != "settings.json"
                && !(name.StartsWith("presets/", StringComparison.Ordinal) && IsSafeLeaf(name[8..], ".json"))
                && !(name.StartsWith("CustomImages/", StringComparison.Ordinal) && IsSafeImage(name[13..])))
                throw new InvalidDataException($"Mục không hợp lệ trong ZIP: {name}");
            if (!names.Add(name)) throw new InvalidDataException("Bản sao lưu có mục trùng tên.");
            total = checked(total + entry.Length);
            if (total > MaxUncompressedBytes) throw new InvalidDataException("Bản sao lưu quá lớn.");

            if (name == "settings.json")
            {
                using var stream = entry.Open();
                if (JsonSerializer.Deserialize<AppSettings>(stream, AppJson.Options) is null)
                    throw new InvalidDataException("settings.json rỗng.");
                settingsFound = true;
            }
            else if (name.StartsWith("presets/", StringComparison.Ordinal))
            {
                using var stream = entry.Open();
                if (JsonSerializer.Deserialize<CrosshairProfile>(stream, AppJson.Options) is not { } preset
                    || !string.Equals(Path.GetFileNameWithoutExtension(name), preset.Id.ToString(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Preset không hợp lệ: {name}");
                presetsFound = true;
            }
        }

        if (!settingsFound || !presetsFound)
            throw new InvalidDataException("Bản sao lưu thiếu settings.json hoặc preset.");
    }

    private static bool IsSafeLeaf(string name, string extension) =>
        name.Length > extension.Length && name == Path.GetFileName(name)
        && !name.Contains(':') && !name.Contains('\\') && !name.Contains('/')
        && name.EndsWith(extension, StringComparison.OrdinalIgnoreCase);

    private static bool IsSafeImage(string name) =>
        IsSafeLeaf(name, ".png") || IsSafeLeaf(name, ".jpg")
        || IsSafeLeaf(name, ".jpeg") || IsSafeLeaf(name, ".gif");

    public static void Restore(IAppPathProvider paths, string source)
    {
        Validate(source);
        var staging = Path.Combine(paths.RootDirectory, ".restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            using (var archive = ZipFile.OpenRead(source))
            {
                foreach (var entry in archive.Entries)
                {
                    var target = Path.Combine(staging, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target);
                }
            }

            // All data was validated and fully extracted before touching the live files.
            var stagedPresets = Path.Combine(staging, "presets");
            var oldPresets = paths.PresetsDirectory + ".restore-old-" + Guid.NewGuid().ToString("N");
            var images = Path.Combine(paths.RootDirectory, CustomImageStore.FolderName);
            var oldImages = images + ".restore-old-" + Guid.NewGuid().ToString("N");
            var stagedImages = Path.Combine(staging, CustomImageStore.FolderName);
            if (!Directory.Exists(stagedImages)) Directory.CreateDirectory(stagedImages);
            var hadPresets = Directory.Exists(paths.PresetsDirectory);
            var hadImages = Directory.Exists(images);
            var presetsInstalled = false;
            var imagesInstalled = false;
            try
            {
                if (hadPresets) Directory.Move(paths.PresetsDirectory, oldPresets);
                Directory.Move(stagedPresets, paths.PresetsDirectory);
                presetsInstalled = true;
                if (hadImages) Directory.Move(images, oldImages);
                Directory.Move(stagedImages, images);
                imagesInstalled = true;

                // This is the commit point: the settings file is atomically replaced last.
                var newSettings = Path.Combine(staging, "settings.json");
                if (File.Exists(paths.SettingsFilePath))
                    File.Replace(newSettings, paths.SettingsFilePath, paths.SettingsFilePath + ".bak");
                else
                    File.Move(newSettings, paths.SettingsFilePath);
            }
            catch
            {
                if (imagesInstalled) Directory.Delete(images, true);
                if (hadImages && Directory.Exists(oldImages)) Directory.Move(oldImages, images);
                if (presetsInstalled) Directory.Delete(paths.PresetsDirectory, true);
                if (hadPresets && Directory.Exists(oldPresets)) Directory.Move(oldPresets, paths.PresetsDirectory);
                throw;
            }

            // Cleanup failure cannot undo a successfully committed restore.
            try
            {
                if (Directory.Exists(oldPresets)) Directory.Delete(oldPresets, true);
                if (Directory.Exists(oldImages)) Directory.Delete(oldImages, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }
}

using System.IO;
using System.Text.Json;

namespace CrosshairOverlay.Services.Storage;

/// <summary>Kết quả xác định thư mục dữ liệu lúc khởi động.</summary>
/// <param name="RootDirectory">Thư mục dữ liệu sẽ dùng.</param>
/// <param name="IsCustom">Người dùng đã chọn thư mục riêng (và nó dùng được).</param>
/// <param name="FallbackFrom">
/// Thư mục tuỳ chỉnh đã chọn nhưng KHÔNG truy cập được lần này (vd ổ USB đã rút) — đang tạm dùng thư
/// mục mặc định. Null nếu không có gì bất thường.
/// </param>
public sealed record StorageLocationResult(string RootDirectory, bool IsCustom, string? FallbackFrom);

/// <summary>
/// Thư mục gốc chứa dữ liệu người dùng (settings.json, presets, CustomImages) và file "con trỏ" cho
/// biết thư mục đó nằm ở đâu.
/// </summary>
/// <remarks>
/// <para>
/// Đường dẫn tuỳ chỉnh KHÔNG thể nằm trong settings.json — chính file đó sẽ bị dời đi. Nó được ghi vào
/// một file nhỏ ở <c>%LOCALAPPDATA%\CrosshairOverlay\storage-location.json</c>, nơi không bao giờ di
/// chuyển, luôn ghi được và không mất khi cập nhật ứng dụng. Hai chỗ khác đã cân nhắc và không dùng:
/// </para>
/// <list type="bullet">
/// <item><c>Properties.Settings.Default</c>: .NET 8 không có sẵn (cần gói ConfigurationManager) và lưu
/// vào thư mục gắn với SỐ PHIÊN BẢN — mỗi lần cập nhật ứng dụng là mất đường dẫn.</item>
/// <item><c>bootstrap.ini</c> cạnh file .exe: cài vào Program Files thì không ghi được nếu không có
/// quyền admin, và bộ cập nhật có thể ghi đè thư mục đó.</item>
/// </list>
/// </remarks>
public static class StorageLocation
{
    public const string AppFolderName = "CrosshairOverlay";

    /// <summary>Tên thư mục con được tạo khi người dùng chọn một thư mục đã có sẵn thứ khác.</summary>
    public const string DataSubfolderName = "CrossGOverlay";

    public static string DefaultRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName);

    public static string DefaultPointerFile { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName,
            "storage-location.json");

    private sealed record Pointer(string DataPath);

    // ------------------------------------------------------------------ đọc / ghi con trỏ

    public static StorageLocationResult Resolve(string? pointerFile = null, string? defaultRoot = null)
    {
        pointerFile ??= DefaultPointerFile;
        defaultRoot ??= DefaultRoot;

        var custom = ReadPointer(pointerFile);
        if (custom is null || SamePath(custom, defaultRoot)) return new StorageLocationResult(defaultRoot, false, null);

        // Thư mục tuỳ chỉnh phải thật sự dùng được. Không được âm thầm tạo một bộ dữ liệu trống ở đó
        // (ổ đĩa đã rút rồi cắm lại sẽ thấy dữ liệu "biến mất"), cũng không được làm sập app.
        return IsUsable(custom)
            ? new StorageLocationResult(custom, true, null)
            : new StorageLocationResult(defaultRoot, false, custom);
    }

    /// <summary>Ghi con trỏ. Trỏ về thư mục mặc định thì xoá con trỏ đi.</summary>
    public static void SavePointer(string dataPath, string? pointerFile = null, string? defaultRoot = null)
    {
        pointerFile ??= DefaultPointerFile;
        defaultRoot ??= DefaultRoot;

        if (SamePath(dataPath, defaultRoot))
        {
            if (File.Exists(pointerFile)) File.Delete(pointerFile);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(pointerFile)!);
        var temp = pointerFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new Pointer(Path.GetFullPath(dataPath))));
        File.Move(temp, pointerFile, overwrite: true);
    }

    private static string? ReadPointer(string pointerFile)
    {
        try
        {
            if (!File.Exists(pointerFile)) return null;

            var pointer = JsonSerializer.Deserialize<Pointer>(File.ReadAllText(pointerFile));
            var path = pointer?.DataPath;
            return string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ? null : Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                       or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ chọn & kiểm tra thư mục mới

    /// <summary>
    /// Thư mục dữ liệu thật sẽ dùng khi người dùng chọn <paramref name="pickedFolder"/>.
    /// </summary>
    /// <remarks>
    /// Thư mục trống, hoặc đã là thư mục dữ liệu của ứng dụng (có settings.json / presets): dùng luôn.
    /// Thư mục đang chứa thứ khác (chọn nhầm Desktop, Documents…): tạo thư mục con
    /// <see cref="DataSubfolderName"/> — không rải settings.json lẫn vào file của người dùng.
    /// </remarks>
    public static string ChooseTarget(string pickedFolder)
    {
        var folder = Path.GetFullPath(pickedFolder);
        if (!Directory.Exists(folder) || LooksLikeDataFolder(folder) || !Directory.EnumerateFileSystemEntries(folder).Any())
            return folder;

        return Path.Combine(folder, DataSubfolderName);
    }

    private static bool LooksLikeDataFolder(string folder) =>
        File.Exists(Path.Combine(folder, "settings.json")) || Directory.Exists(Path.Combine(folder, "presets"));

    public enum Problem
    {
        None,
        SameAsCurrent,

        /// <summary>Thư mục mới nằm BÊN TRONG thư mục hiện tại: sao chép sẽ tự chép vào chính nó.</summary>
        InsideCurrent,

        NotWritable,
    }

    public static Problem Validate(string currentRoot, string target)
    {
        if (SamePath(currentRoot, target)) return Problem.SameAsCurrent;
        if (IsInside(target, currentRoot)) return Problem.InsideCurrent;
        return IsUsable(target) ? Problem.None : Problem.NotWritable;
    }

    /// <summary>Tạo được thư mục và ghi/xoá được một file thử trong đó.</summary>
    public static bool IsUsable(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException)
        {
            return false;
        }
    }

    // ------------------------------------------------------------------ sao chép

    /// <summary>
    /// Sao chép TOÀN BỘ nội dung thư mục dữ liệu cũ (kể cả thư mục con) sang thư mục mới, ghi đè file
    /// trùng tên. Bỏ file tạm của lần ghi dở. Chạy trên thread pool.
    /// </summary>
    /// <returns>Số file đã chép.</returns>
    public static Task<int> CopyAsync(string source, string destination, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            if (!Directory.Exists(source)) return 0;

            var copied = 0;
            Directory.CreateDirectory(destination);

            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;

                var target = Path.Combine(destination, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
                copied++;
            }

            // Thư mục con rỗng (vd CustomImages chưa có ảnh) cũng giữ lại cho đủ cấu trúc.
            foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));

            return copied;
        }, cancellationToken);

    // ------------------------------------------------------------------ tiện ích

    public static bool SamePath(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    /// <summary><paramref name="child"/> nằm hẳn bên trong <paramref name="parent"/>.</summary>
    public static bool IsInside(string child, string parent)
    {
        var p = Normalize(parent) + Path.DirectorySeparatorChar;
        return Normalize(child).StartsWith(p, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}

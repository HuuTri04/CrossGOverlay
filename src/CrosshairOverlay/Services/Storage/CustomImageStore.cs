using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Localization;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay.Services.Storage;

/// <inheritdoc cref="ICustomImageStore"/>
public sealed partial class CustomImageStore : ICustomImageStore
{
    [global::System.Text.RegularExpressions.GeneratedRegex("_[0-9a-f]{12}$")]
    private static partial global::System.Text.RegularExpressions.Regex TrailingHash();

    /// <summary>Tên thư mục con, đồng thời là tiền tố của mọi đường dẫn tương đối lưu trong preset.</summary>
    public const string FolderName = "CustomImages";

    /// <summary>File lớn hơn thế này gần như chắc chắn không phải ảnh tâm ngắm.</summary>
    public const long MaxFileBytes = 20L * 1024 * 1024;

    /// <summary>
    /// Cạnh lớn nhất cho phép, pixel. Ảnh được giải mã toàn bộ vào RAM (4 byte mỗi pixel) và
    /// giữ trong cache suốt phiên — 4096×4096 đã là 64 MB.
    /// </summary>
    public const int MaxPixelSide = 4096;

    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif" };

    private readonly ILogger<CustomImageStore> _logger;
    private readonly string _root;

    public CustomImageStore(IAppPathProvider paths, ILogger<CustomImageStore> logger)
    {
        _logger = logger;
        _root = Path.GetFullPath(paths.RootDirectory);
        Directory = Path.Combine(_root, FolderName);
    }

    public string Directory { get; }

    public string Import(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var source = Path.GetFullPath(sourcePath);
        CheckExtension(source);

        var info = new FileInfo(source);
        if (!info.Exists) throw new FileNotFoundException(Tr.Get("Image_ErrUnreadable"), source);
        if (info.Length > MaxFileBytes) throw TooLarge();

        // Đọc hết vào bộ nhớ MỘT lần: dùng chung cho kiểm tra, băm và ghi. Đọc file gốc nhiều lần
        // thì người dùng có thể sửa nó giữa chừng, và thứ được kiểm tra khác thứ được lưu.
        return ImportBytes(Path.GetFileName(source), File.ReadAllBytes(source));
    }

    public string ImportBytes(string originalFileName, byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalFileName);
        ArgumentNullException.ThrowIfNull(bytes);

        // Tên đến từ file preset của người khác: chỉ lấy phần tên, bỏ mọi thư mục đi kèm.
        var name = Path.GetFileName(originalFileName);
        var extension = CheckExtension(name);

        if (bytes.LongLength > MaxFileBytes) throw TooLarge();
        ValidateImage(bytes);

        var fileName = BuildFileName(name, extension, bytes);
        var target = Path.Combine(Directory, fileName);

        global::System.IO.Directory.CreateDirectory(Directory);

        if (File.Exists(target))
        {
            // Tên chứa mã băm nội dung, nên file đã có chắc chắn là cùng một ảnh — không ghi lại.
            // Chỉ làm mới thời điểm để lượt dọn kho coi nó là "vừa được dùng".
            File.SetLastWriteTimeUtc(target, DateTime.UtcNow);
        }
        else
        {
            var temp = target + ".tmp";
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, target, overwrite: true);
            _logger.LogInformation("Đã chép ảnh tâm ngắm vào kho: {File}", fileName);
        }

        return FolderName + "/" + fileName;
    }

    public int CleanupUnused(IEnumerable<string?> referencedStoredPaths, TimeSpan minimumAge)
    {
        ArgumentNullException.ThrowIfNull(referencedStoredPaths);
        if (!global::System.IO.Directory.Exists(Directory)) return 0;

        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var stored in referencedStoredPaths)
        {
            if (Resolve(stored) is { } full) keep.Add(full);
        }

        var now = DateTime.UtcNow;
        var removed = 0;

        // Chỉ duyệt đúng thư mục kho, không đệ quy: file ở chỗ khác không bao giờ là việc của kho.
        foreach (var file in global::System.IO.Directory.EnumerateFiles(Directory))
        {
            try
            {
                var age = now - File.GetLastWriteTimeUtc(file);

                // File tạm còn sót lại từ lần chép bị ngắt giữa chừng.
                if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                {
                    if (age > TimeSpan.FromHours(1)) { File.Delete(file); removed++; }
                    continue;
                }

                if (keep.Contains(Path.GetFullPath(file)) || age < minimumAge) continue;

                File.Delete(file);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Đang bị giữ (vd renderer vừa mở) — để lần sau.
                _logger.LogDebug(ex, "Chưa xoá được ảnh không dùng: {File}", file);
            }
        }

        if (removed > 0) _logger.LogInformation("Đã dọn {Count} ảnh không còn preset nào dùng.", removed);
        return removed;
    }

    private static string CheckExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (!AllowedExtensions.Contains(extension))
            throw new InvalidDataException(Tr.Get("Image_ErrUnsupported"));

        return extension;
    }

    private static InvalidDataException TooLarge() =>
        new(Tr.Format("Image_ErrTooLarge", MaxFileBytes / (1024 * 1024), MaxPixelSide));

    public string? Resolve(string? storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath)) return null;

        try
        {
            // Preset tạo trước khi có kho ảnh lưu đường dẫn tuyệt đối — vẫn dùng được.
            if (Path.IsPathRooted(storedPath)) return Path.GetFullPath(storedPath);

            var full = Path.GetFullPath(Path.Combine(_root, storedPath));

            // Đường dẫn tương đối đến từ file preset, mà preset thì người dùng nhận từ người khác.
            // "..\..\Windows\..." phải bị chặn, không được phép trỏ ra ngoài thư mục ứng dụng.
            var rootWithSeparator = _root.EndsWith(Path.DirectorySeparatorChar)
                ? _root
                : _root + Path.DirectorySeparatorChar;

            return full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// Giải mã thử để chắc chắn đây là ảnh thật, và không quá lớn.
    /// </summary>
    /// <remarks>
    /// Kiểm tra bằng nội dung chứ không tin đuôi file: một file .txt đổi tên thành .png vẫn qua
    /// được bước kiểm tra đuôi, và nếu lọt vào kho thì crosshair sẽ trống trơn mà không rõ lý do.
    /// </remarks>
    private static void ValidateImage(byte[] bytes)
    {
        BitmapFrame frame;
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            frame = decoder.Frames[0];
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException
                                       or ArgumentException or InvalidOperationException
                                       or IOException or OverflowException)
        {
            throw new InvalidDataException(Tr.Get("Image_ErrUnreadable"), ex);
        }

        if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0)
            throw new InvalidDataException(Tr.Get("Image_ErrUnreadable"));

        if (frame.PixelWidth > MaxPixelSide || frame.PixelHeight > MaxPixelSide)
            throw TooLarge();
    }

    /// <summary>
    /// Tên gốc đã làm sạch + 12 ký tự đầu của SHA-256 nội dung.
    /// </summary>
    /// <remarks>
    /// Giữ tên gốc để người dùng nhìn giao diện vẫn nhận ra ảnh của mình; mã băm để hai ảnh
    /// khác nhau cùng tên "crosshair.png" không đè lên nhau.
    /// </remarks>
    private static string BuildFileName(string source, string extension, byte[] bytes)
    {
        var hash = Convert.ToHexString(SHA256.HashData(bytes))[..12].ToLowerInvariant();

        // Bỏ mã băm cũ nếu có: ảnh xuất ra rồi nhập lại không được thành "ten_abc_abc.png".
        var original = TrailingHash().Replace(Path.GetFileNameWithoutExtension(source), string.Empty);
        var clean = new StringBuilder(original.Length);
        foreach (var ch in original)
            clean.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_');

        var stem = clean.Length == 0 ? "image" : clean.ToString();
        if (stem.Length > 40) stem = stem[..40];

        return $"{stem}_{hash}{extension.ToLowerInvariant()}";
    }
}

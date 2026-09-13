using System.IO;
using System.Text.Json;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay.Services.Storage;

/// <inheritdoc cref="IPresetRepository"/>
public sealed class PresetRepository : IPresetRepository
{
    /// <summary>Base64 dài hơn thế này thì giải mã ra chắc chắn vượt giới hạn kích thước ảnh.</summary>
    private static readonly long MaxEmbeddedChars = (CustomImageStore.MaxFileBytes * 4 / 3) + 4;

    private readonly IAppPathProvider _paths;
    private readonly ICustomImageStore _images;
    private readonly ILogger<PresetRepository> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public PresetRepository(IAppPathProvider paths, ICustomImageStore images, ILogger<PresetRepository> logger)
    {
        _paths = paths;
        _images = images;
        _logger = logger;
    }

    public event EventHandler? PresetsChanged;

    public bool LastLoadSkippedFiles { get; private set; }

    public async Task<IReadOnlyList<CrosshairProfile>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_paths.PresetsDirectory);
        AtomicFile.CleanupTemporaries(_paths.PresetsDirectory);

        var results = new List<CrosshairProfile>();
        var skipped = false;

        foreach (var file in Directory.EnumerateFiles(_paths.PresetsDirectory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var preset = await TryReadAsync(file, cancellationToken).ConfigureAwait(false);
            if (preset is not null) results.Add(preset);
            else skipped = true;
        }

        LastLoadSkippedFiles = skipped;

        if (results.Count == 0)
        {
            // Chạy lần đầu, hoặc người dùng đã xoá sạch thư mục. Không để app rơi vào trạng
            // thái không có preset nào.
            _logger.LogInformation("Chưa có preset nào, ghi thư viện mặc định ra đĩa.");

            foreach (var preset in DefaultPresets.CreateLibrary())
            {
                await SaveAsync(preset, cancellationToken).ConfigureAwait(false);
                results.Add(preset);
            }
        }

        results.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return results;
    }

    public async Task<CrosshairProfile?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var path = PathFor(id);
        return File.Exists(path)
            ? await TryReadAsync(path, cancellationToken).ConfigureAwait(false)
            : null;
    }

    public async Task SaveAsync(CrosshairProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.Id == Guid.Empty) profile.Id = Guid.NewGuid();
        profile.ModifiedUtc = DateTimeOffset.UtcNow;

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await AtomicFile.WriteJsonAsync(PathFor(profile.Id), profile, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }

        _logger.LogDebug("Đã lưu preset '{Name}' ({Id}).", profile.Name, profile.Id);
        PresetsChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var path = PathFor(id);

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                _logger.LogDebug("Đã xoá preset {Id}.", id);
                PresetsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Không xoá được preset {Id}.", id);
            throw;
        }

        return Task.CompletedTask;
    }

    public async Task<CrosshairProfile> ImportAsync(
        string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var imported = await AtomicFile.ReadJsonAsync<CrosshairProfile>(filePath, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new InvalidDataException($"'{filePath}' không phải file preset hợp lệ.");

        // Cấp Id mới: file import về có thể trùng Id với một preset đang có, ghi đè lên nó
        // là hành vi người dùng không hề mong đợi.
        imported.Id = Guid.NewGuid();
        imported.CreatedUtc = DateTimeOffset.UtcNow;
        MarkMigrated(imported);
        MaterializeEmbeddedImage(imported);

        if (string.IsNullOrWhiteSpace(imported.Name))
            imported.Name = Path.GetFileNameWithoutExtension(filePath);

        await SaveAsync(imported, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Đã import preset '{Name}' từ {Path}.", imported.Name, filePath);
        return imported;
    }

    public async Task ExportAsync(
        CrosshairProfile profile, string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        // Ghi một BẢN SAO có nhúng ảnh, không đụng vào preset đang dùng: chuỗi base64 vài MB không
        // được phép bám vào preset trong thư viện và bị ghi lại mỗi lần lưu.
        var export = profile.Clone();
        export.EmbeddedImage = await TryEmbedImageAsync(profile, cancellationToken).ConfigureAwait(false);

        await AtomicFile.WriteJsonAsync(filePath, export, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Đã export preset '{Name}' ra {Path} (kèm ảnh: {HasImage}).",
            profile.Name, filePath, export.EmbeddedImage is not null);
    }

    /// <summary>
    /// Ghi nhận preset đã ở định dạng hiện tại.
    /// </summary>
    /// <remarks>
    /// Việc chuyển đổi thật (khối <c>Lines</c> cũ thành nhánh trong, <c>Gap</c> thành
    /// <c>Offset</c>) đã xảy ra ngay lúc đọc JSON. Số phiên bản phải được nâng theo, nếu không lần
    /// lưu kế tiếp sẽ ghi ra một file mang cấu trúc mới nhưng vẫn tự nhận là phiên bản cũ.
    /// </remarks>
    private static void MarkMigrated(CrosshairProfile preset)
    {
        if (preset.SchemaVersion < CrosshairProfile.CurrentSchemaVersion)
            preset.SchemaVersion = CrosshairProfile.CurrentSchemaVersion;
    }

    /// <summary>Đọc ảnh của preset để nhúng vào file export; không có ảnh hoặc ảnh đã mất thì trả null.</summary>
    private async Task<EmbeddedImageData?> TryEmbedImageAsync(CrosshairProfile profile, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profile.Image.FilePath)) return null;

        var resolved = _images.Resolve(profile.Image.FilePath);
        if (resolved is null || !File.Exists(resolved))
        {
            _logger.LogWarning("Preset '{Name}' trỏ tới ảnh không còn tồn tại — export không kèm ảnh.", profile.Name);
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(resolved, cancellationToken).ConfigureAwait(false);
        return new EmbeddedImageData
        {
            FileName = Path.GetFileName(resolved),
            Data = Convert.ToBase64String(bytes),
        };
    }

    /// <summary>
    /// Đưa ảnh nhúng vào kho rồi trỏ preset tới bản trong kho.
    /// </summary>
    /// <remarks>
    /// Ảnh nhúng hỏng hay không hợp lệ KHÔNG làm hỏng việc nhập preset: phần còn lại vẫn dùng
    /// được. Đường dẫn ảnh được giữ nguyên, nên giao diện hiện rõ "không tìm thấy ảnh" thay vì
    /// âm thầm mất ảnh.
    /// </remarks>
    private void MaterializeEmbeddedImage(CrosshairProfile preset)
    {
        if (preset.EmbeddedImage is not { } embedded) return;
        preset.EmbeddedImage = null;

        try
        {
            if (embedded.Data.Length > MaxEmbeddedChars) throw new InvalidDataException("Ảnh nhúng quá lớn.");

            var bytes = Convert.FromBase64String(embedded.Data);
            preset.Image.FilePath = _images.ImportBytes(embedded.FileName, bytes);
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or IOException
                                       or UnauthorizedAccessException or ArgumentException)
        {
            _logger.LogWarning(ex, "Bỏ qua ảnh nhúng không hợp lệ trong preset '{Name}'.", preset.Name);
        }
    }

    private string PathFor(Guid id) => Path.Combine(_paths.PresetsDirectory, $"{id:D}.json");

    private async Task<CrosshairProfile?> TryReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var preset = await AtomicFile.ReadJsonAsync<CrosshairProfile>(path, cancellationToken)
                .ConfigureAwait(false);

            if (preset is null)
            {
                _logger.LogWarning("File preset rỗng, bỏ qua: {Path}", path);
                return null;
            }

            if (preset.Id == Guid.Empty) preset.Id = Guid.NewGuid();
            MarkMigrated(preset);

            // File export bị thả thẳng vào thư mục preset: vẫn đưa ảnh vào kho như lúc import.
            MaterializeEmbeddedImage(preset);
            return preset;
        }
        catch (JsonException ex)
        {
            // Một file hỏng không được phép làm hỏng cả thư viện preset.
            _logger.LogWarning(ex, "Bỏ qua file preset không đọc được: {Path}", path);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Không mở được file preset: {Path}", path);
            return null;
        }
    }
}

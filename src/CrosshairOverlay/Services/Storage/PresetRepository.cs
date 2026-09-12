using System.IO;
using System.Text.Json;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay.Services.Storage;

/// <inheritdoc cref="IPresetRepository"/>
public sealed class PresetRepository : IPresetRepository
{
    private readonly IAppPathProvider _paths;
    private readonly ILogger<PresetRepository> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public PresetRepository(IAppPathProvider paths, ILogger<PresetRepository> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public event EventHandler? PresetsChanged;

    public async Task<IReadOnlyList<CrosshairProfile>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_paths.PresetsDirectory);
        AtomicFile.CleanupTemporaries(_paths.PresetsDirectory);

        var results = new List<CrosshairProfile>();

        foreach (var file in Directory.EnumerateFiles(_paths.PresetsDirectory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var preset = await TryReadAsync(file, cancellationToken).ConfigureAwait(false);
            if (preset is not null) results.Add(preset);
        }

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

        await AtomicFile.WriteJsonAsync(filePath, profile, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Đã export preset '{Name}' ra {Path}.", profile.Name, filePath);
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

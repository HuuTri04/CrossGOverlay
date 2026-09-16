using System.IO;
using CrosshairOverlay.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay.Services.Storage;

/// <inheritdoc cref="IStorageLocationService"/>
public sealed class StorageLocationService : IStorageLocationService
{
    private readonly IAppPathProvider _paths;
    private readonly IAppSettingsService _settings;
    private readonly IPresetLibrary _library;
    private readonly ILogger<StorageLocationService> _logger;
    private readonly string _pointerFile;
    private readonly string _defaultRoot;

    public StorageLocationService(
        IAppPathProvider paths, IAppSettingsService settings, IPresetLibrary library,
        ILogger<StorageLocationService> logger)
        : this(paths, settings, library, logger, StorageLocation.DefaultPointerFile, StorageLocation.DefaultRoot)
    {
    }

    /// <param name="pointerFile">File con trỏ; test trỏ sang thư mục tạm.</param>
    internal StorageLocationService(
        IAppPathProvider paths, IAppSettingsService settings, IPresetLibrary library,
        ILogger<StorageLocationService> logger, string pointerFile, string defaultRoot)
    {
        _paths = paths;
        _settings = settings;
        _library = library;
        _logger = logger;
        _pointerFile = pointerFile;
        _defaultRoot = defaultRoot;
    }

    public string CurrentRoot => _paths.RootDirectory;

    public string ResolveTarget(string pickedFolder) => StorageLocation.ChooseTarget(pickedFolder);

    public StorageLocation.Problem Validate(string target) => StorageLocation.Validate(CurrentRoot, target);

    public async Task<StorageChangeResult> ChangeAsync(string target, bool copyExistingData)
    {
        var problem = Validate(target);
        if (problem != StorageLocation.Problem.None) return StorageChangeResult.Failed(problem.ToString());

        try
        {
            var copied = 0;
            if (copyExistingData)
            {
                // Ghi nốt mọi thay đổi còn chờ debounce, để bản sao là trạng thái MỚI NHẤT.
                await _library.FlushAsync().ConfigureAwait(true);
                await _settings.FlushAsync().ConfigureAwait(true);
                await _settings.SaveAsync().ConfigureAwait(true);

                copied = await StorageLocation.CopyAsync(CurrentRoot, target).ConfigureAwait(true);
            }

            // Con trỏ ghi SAU CÙNG: chép hỏng giữa chừng thì lần khởi động tới vẫn dùng thư mục cũ nguyên vẹn.
            StorageLocation.SavePointer(target, _pointerFile, _defaultRoot);

            _logger.LogWarning(
                "Đổi thư mục dữ liệu: {Old} → {New} (sao chép: {Copy}, {Count} file). Có hiệu lực sau khi khởi động lại.",
                CurrentRoot, target, copyExistingData, copied);

            return StorageChangeResult.Succeeded(copied);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException or OperationCanceledException)
        {
            _logger.LogError(ex, "Không đổi được thư mục dữ liệu sang {Target}.", target);
            return StorageChangeResult.Failed(ex.Message);
        }
    }
}

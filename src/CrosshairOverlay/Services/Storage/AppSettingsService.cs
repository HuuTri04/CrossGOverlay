using System.IO;
using System.Text.Json;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay.Services.Storage;

/// <inheritdoc cref="IAppSettingsService"/>
public sealed class AppSettingsService : IAppSettingsService
{
    /// <summary>
    /// Độ trễ gom các thay đổi liên tiếp. Người dùng kéo một slider có thể bắn hàng trăm
    /// thay đổi mỗi giây — ghi đĩa từng lần là vô nghĩa và bào SSD.
    /// </summary>
    private static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(750);

    private readonly IAppPathProvider _paths;
    private readonly ILogger<AppSettingsService> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Timer _debounceTimer;

    private int _savePending;
    private bool _disposed;

    public AppSettingsService(IAppPathProvider paths, ILogger<AppSettingsService> logger)
    {
        _paths = paths;
        _logger = logger;
        Current = AppSettings.CreateDefault();

        _debounceTimer = new Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);
    }

    public AppSettings Current { get; private set; }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var loaded = await AtomicFile.ReadJsonAsync<AppSettings>(_paths.SettingsFilePath, cancellationToken)
                .ConfigureAwait(false);

            if (loaded is null)
            {
                _logger.LogInformation("Chưa có settings.json, dùng cấu hình mặc định.");
                Current = AppSettings.CreateDefault();
                return;
            }

            Current = Normalize(loaded);
            _logger.LogInformation(
                "Đã nạp cấu hình: {Hotkeys} hotkey, {Profiles} game profile.",
                Current.Hotkeys.Count, Current.GameProfiles.Count);
        }
        catch (JsonException ex)
        {
            // File hỏng: giữ lại làm bằng chứng để debug rồi chạy tiếp với mặc định, thay vì
            // chặn người dùng ở màn hình lỗi.
            _logger.LogError(ex, "settings.json hỏng, đổi tên thành .corrupt và dùng mặc định.");
            TryQuarantine();
            Current = AppSettings.CreateDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Không đọc được settings.json, dùng cấu hình mặc định.");
            Current = AppSettings.CreateDefault();
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        Interlocked.Exchange(ref _savePending, 0);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await AtomicFile.WriteJsonAsync(_paths.SettingsFilePath, Current, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogDebug("Đã lưu settings.json.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Không ném lên: mất một lần ghi cấu hình không đáng để làm sập app.
            _logger.LogError(ex, "Không ghi được settings.json.");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void RequestSave()
    {
        if (_disposed) return;

        Interlocked.Exchange(ref _savePending, 1);
        _debounceTimer.Change(SaveDebounce, Timeout.InfiniteTimeSpan);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);

        if (Interlocked.Exchange(ref _savePending, 0) == 1)
            await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    private void OnDebounceElapsed(object? state)
    {
        // Chạy trên thread pool. SaveAsync tự nuốt lỗi I/O, nhưng vẫn bọc thêm một lớp vì
        // exception thoát ra từ callback của Timer sẽ giết cả tiến trình.
        _ = Task.Run(async () =>
        {
            try
            {
                await SaveAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lưu cấu hình nền thất bại.");
            }
        });
    }

    /// <summary>
    /// Vá những chỗ thiếu sau khi đọc file: người dùng sửa tay hoặc nâng cấp từ bản cũ đều
    /// có thể để lại cấu hình khuyết.
    /// </summary>
    private AppSettings Normalize(AppSettings settings)
    {
        // "PresetOrder": null trong file sửa tay.
        settings.PresetOrder ??= [];

        if (settings.Hotkeys.Count == 0)
        {
            foreach (var hotkey in HotkeyBinding.CreateDefaults())
                settings.Hotkeys.Add(hotkey);
        }
        else
        {
            // Phiên bản mới thêm HotkeyAction mới — bổ sung binding trống cho chúng.
            foreach (var action in Enum.GetValues<HotkeyAction>())
            {
                if (settings.Hotkeys.Any(h => h.Action == action)) continue;

                settings.Hotkeys.Add(new HotkeyBinding
                {
                    Action = action,
                    Enabled = false,
                });
            }
        }

        if (settings.SchemaVersion > AppSettings.CurrentSchemaVersion)
        {
            _logger.LogWarning(
                "settings.json thuộc schema v{Found}, bản app này hiểu tới v{Known}. "
                    + "Những mục không nhận ra sẽ bị bỏ qua.",
                settings.SchemaVersion, AppSettings.CurrentSchemaVersion);
        }

        settings.SchemaVersion = AppSettings.CurrentSchemaVersion;

        // An toàn trước anti-cheat: chỉ Normal hoặc High. JSON chấp nhận cả số, nên người dùng sửa tay
        // "ProcessPriority": 24 (mã Realtime của Windows) vẫn đọc được thành một giá trị enum không tồn
        // tại. Nơi áp dụng đã chỉ chọn High/Normal, nhưng chặn luôn ở đây để giá trị lạ không nằm lại
        // trong cài đặt và bị ghi ngược ra file.
        if (!Enum.IsDefined(settings.ProcessPriority))
        {
            _logger.LogWarning(
                "Mức ưu tiên tiến trình không hợp lệ ({Value}) trong settings.json — dùng Normal.",
                (int)settings.ProcessPriority);
            settings.ProcessPriority = ProcessPriorityMode.Normal;
        }

        return settings;
    }

    private void TryQuarantine()
    {
        try
        {
            var target = _paths.SettingsFilePath + ".corrupt";
            File.Move(_paths.SettingsFilePath, target, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Không đổi tên được settings.json hỏng.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _debounceTimer.Dispose();
        _writeLock.Dispose();
    }
}

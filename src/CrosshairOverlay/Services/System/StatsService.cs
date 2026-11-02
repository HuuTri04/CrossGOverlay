using System.IO;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Services.Storage;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay.Services.System;

/// <summary>
/// Event-driven overlay usage history. The only periodic operation is one atomic save every
/// five minutes while there is data to persist; no sampling loop runs when the overlay is hidden.
/// </summary>
public sealed class StatsService : IDisposable
{
    private static readonly TimeSpan SaveInterval = TimeSpan.FromMinutes(5);
    private readonly IOverlayController _overlay;
    private readonly IPresetLibrary _library;
    private readonly ILogger<StatsService> _logger;
    private readonly string _filePath;
    private readonly string _legacyPath;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private StatsHistoryData _data = new();
    private Timer? _saveTimer;
    private DateTimeOffset _segmentStart;
    private Guid _activeId;
    private bool _visible;
    private bool _saveArmed;
    private bool _dirty;
    private bool _started;

    public StatsService(IOverlayController overlay, IPresetLibrary library, IAppPathProvider paths,
        ILogger<StatsService> logger)
    {
        _overlay = overlay;
        _library = library;
        _logger = logger;
        _filePath = Path.Combine(paths.RootDirectory, "stats_history.json");
        _legacyPath = Path.Combine(paths.RootDirectory, "usage-stats.json");
    }

    public async Task StartAsync()
    {
        if (_started) return;
        var loaded = await AtomicFile.LoadJsonAsync<StatsHistoryData>(_filePath);
        var migrated = false;
        if (loaded is null)
        {
            var old = await AtomicFile.LoadJsonAsync<LegacyUsageStatsData>(_legacyPath);
            loaded = new StatsHistoryData();
            if (old is not null)
            {
                loaded.Days.Add(new StatsDayData
                {
                    Day = old.Day,
                    SwitchCount = Math.Max(0, old.SwitchCount),
                    SecondsByPreset = old.SecondsByPreset ?? [],
                });
                migrated = true;
            }
        }

        lock (_gate)
        {
            _data = loaded;
            _data.Days ??= [];
            NormalizeDays();
            PruneOldDays(DateOnly.FromDateTime(DateTime.Now));
            _activeId = _library.Active?.Id ?? Guid.Empty;
            _visible = _overlay.IsVisible;
            _segmentStart = DateTimeOffset.Now;
            _saveTimer = new Timer(OnSaveTimer, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _dirty |= migrated;
            _library.ActiveChanged += OnActiveChanged;
            _overlay.VisibilityChanged += OnVisibilityChanged;
            _started = true;
            if (_visible || _dirty) ArmSave();
        }
    }

    public bool AutoCleanupOldData
    {
        get { lock (_gate) return _data.AutoCleanupOldData; }
        set
        {
            lock (_gate)
            {
                if (_data.AutoCleanupOldData == value) return;
                _data.AutoCleanupOldData = value;
                PruneOldDays(DateOnly.FromDateTime(DateTime.Now));
                _dirty = true;
                ArmSave();
            }
        }
    }

    private void OnActiveChanged(object? sender, EventArgs e)
    {
#if BENCHMARK
        if (Core.Diagnostics.BenchmarkPresetProbe.SkipStats) return;
        using var benchmarkScope = Core.Diagnostics.BenchmarkPresetProbe.Subscriber("T4c.StatsService");
#endif
        lock (_gate)
        {
            Accumulate(DateTimeOffset.Now);
            var next = _library.Active?.Id ?? Guid.Empty;
            if (next != _activeId)
            {
                GetDay(DateOnly.FromDateTime(DateTime.Now)).SwitchCount++;
                _dirty = true;
                ArmSave();
            }
            _activeId = next;
        }
    }

    private void OnVisibilityChanged(object? sender, OverlayVisibilityChangedEventArgs e)
    {
        lock (_gate)
        {
            Accumulate(DateTimeOffset.Now);
            _visible = e.IsVisible;
            if (_visible || _dirty) ArmSave();
            else DisarmSaveIfIdle();
        }
    }

    private void Accumulate(DateTimeOffset now)
    {
        var start = _segmentStart;
        if (start == default) { _segmentStart = now; return; }
        if (_visible && _activeId != Guid.Empty && now > start)
        {
            while (DateOnly.FromDateTime(start.LocalDateTime) < DateOnly.FromDateTime(now.LocalDateTime))
            {
                var nextDate = start.LocalDateTime.Date.AddDays(1);
                var midnight = new DateTimeOffset(nextDate, TimeZoneInfo.Local.GetUtcOffset(nextDate));
                AddSeconds(DateOnly.FromDateTime(start.LocalDateTime), _activeId,
                    Math.Max(0, (midnight - start).TotalSeconds));
                start = midnight;
            }
            AddSeconds(DateOnly.FromDateTime(now.LocalDateTime), _activeId,
                Math.Max(0, (now - start).TotalSeconds));
        }
        _segmentStart = now;
        PruneOldDays(DateOnly.FromDateTime(now.LocalDateTime));
    }

    private void AddSeconds(DateOnly day, Guid presetId, double seconds)
    {
        if (seconds <= 0) return;
        var entry = GetDay(day);
        entry.SecondsByPreset[presetId] = entry.SecondsByPreset.GetValueOrDefault(presetId) + seconds;
        _dirty = true;
    }

    private StatsDayData GetDay(DateOnly day)
    {
        var entry = _data.Days.FirstOrDefault(d => d.Day == day);
        if (entry is not null) return entry;
        entry = new StatsDayData { Day = day };
        _data.Days.Add(entry);
        return entry;
    }

    private void NormalizeDays()
    {
        _data.Days = _data.Days.Where(d => d is not null)
            .GroupBy(d => d.Day).Select(group =>
            {
                var merged = new StatsDayData { Day = group.Key };
                foreach (var day in group)
                {
                    merged.SwitchCount += Math.Max(0, day.SwitchCount);
                    foreach (var (id, seconds) in day.SecondsByPreset ?? [])
                        if (id != Guid.Empty && double.IsFinite(seconds) && seconds > 0)
                            merged.SecondsByPreset[id] = merged.SecondsByPreset.GetValueOrDefault(id) + seconds;
                }
                return merged;
            }).OrderBy(d => d.Day).ToList();
    }

    private void PruneOldDays(DateOnly today)
    {
        if (!_data.AutoCleanupOldData) return;
        var removed = _data.Days.RemoveAll(d => d.Day < today.AddDays(-29) || d.Day > today);
        if (removed > 0) _dirty = true;
    }

    public StatsHistorySnapshot Snapshot()
    {
        lock (_gate)
        {
            if (_started) Accumulate(DateTimeOffset.Now);
            var names = _library.Presets.ToDictionary(p => p.Id, p => p.Name);
            var days = _data.Days.Select(day => new StatsDayUsage(day.Day, day.SwitchCount,
                day.SecondsByPreset.Select(p => new PresetUsage(p.Key,
                    names.GetValueOrDefault(p.Key, "—"), p.Value)).ToArray())).ToArray();
            long size = 0;
            try { if (File.Exists(_filePath)) size = new FileInfo(_filePath).Length; }
            catch (IOException) { }
            return new StatsHistorySnapshot(_library.Active?.Name ?? "—", _data.AutoCleanupOldData, size, days);
        }
    }

    private void ArmSave()
    {
        if (!_started || _saveArmed || _saveTimer is null) return;
        _saveArmed = true;
        _saveTimer.Change(SaveInterval, Timeout.InfiniteTimeSpan);
    }

    private void DisarmSaveIfIdle()
    {
        if (_visible || _dirty || !_saveArmed || _saveTimer is null) return;
        _saveTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _saveArmed = false;
    }

    private void OnSaveTimer(object? state)
    {
        lock (_gate) _saveArmed = false;
        _ = SaveOnTimerAsync();
    }

    private async Task SaveOnTimerAsync()
    {
        try { await SaveNowAsync().ConfigureAwait(false); }
        catch (Exception ex)
        { _logger.LogWarning(ex, "Could not save usage statistics."); }
        finally { lock (_gate) if (_started && (_visible || _dirty)) ArmSave(); }
    }

    public async Task SaveNowAsync(CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StatsHistoryData copy;
            lock (_gate)
            {
                if (!_started) return;
                Accumulate(DateTimeOffset.Now);
                if (!_dirty) return;
                copy = CloneData();
                _dirty = false;
            }
            try
            {
                await Task.Run(() => AtomicFile.WriteJsonAtomicAsync(_filePath, copy, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                lock (_gate) DisarmSaveIfIdle();
            }
            catch
            {
                lock (_gate) _dirty = true;
                throw;
            }
        }
        finally { _writeLock.Release(); }
    }

    public async Task ClearAllStatsAsync(CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StatsHistoryData empty;
            lock (_gate)
            {
                empty = new StatsHistoryData { AutoCleanupOldData = _data.AutoCleanupOldData };
                _data = empty;
                _segmentStart = DateTimeOffset.Now;
                // Later usage events may mark the new history dirty during the async write.
                _dirty = false;
            }
            try
            {
                await Task.Run(() => AtomicFile.WriteJsonAtomicAsync(_filePath, empty, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                lock (_gate) DisarmSaveIfIdle();
            }
            catch
            {
                lock (_gate) _dirty = true;
                throw;
            }
        }
        finally { _writeLock.Release(); }
    }

    private StatsHistoryData CloneData() => new()
    {
        AutoCleanupOldData = _data.AutoCleanupOldData,
        Days = _data.Days.Select(day => new StatsDayData
        {
            Day = day.Day,
            SwitchCount = day.SwitchCount,
            SecondsByPreset = new Dictionary<Guid, double>(day.SecondsByPreset),
        }).ToList(),
    };

    public void Dispose()
    {
        Timer? timer;
        lock (_gate)
        {
            if (!_started) return;
            _started = false;
            _saveArmed = false;
            timer = _saveTimer;
            _saveTimer = null;
        }
        _library.ActiveChanged -= OnActiveChanged;
        _overlay.VisibilityChanged -= OnVisibilityChanged;
        timer?.Dispose();
    }
}

public sealed class StatsHistoryData
{
    public bool AutoCleanupOldData { get; set; } = true;
    public List<StatsDayData> Days { get; set; } = [];
}

public sealed class StatsDayData
{
    public DateOnly Day { get; set; }
    public int SwitchCount { get; set; }
    public Dictionary<Guid, double> SecondsByPreset { get; set; } = [];
}

public sealed class LegacyUsageStatsData
{
    public DateOnly Day { get; set; }
    public int SwitchCount { get; set; }
    public Dictionary<Guid, double>? SecondsByPreset { get; set; }
}

public sealed record PresetUsage(Guid PresetId, string Name, double Seconds);
public sealed record StatsDayUsage(DateOnly Day, int SwitchCount, IReadOnlyList<PresetUsage> Presets)
{
    public double TotalSeconds => Presets.Sum(p => p.Seconds);
}
public sealed record StatsHistorySnapshot(string ActiveName, bool AutoCleanupOldData,
    long FileSizeBytes, IReadOnlyList<StatsDayUsage> Days);

using System.Collections.ObjectModel;
using System.ComponentModel;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay.Services.Presets;

/// <inheritdoc cref="IPresetLibrary"/>
public sealed class PresetLibrary : IPresetLibrary
{
    /// <summary>
    /// Kéo một slider bắn hàng trăm <c>PropertyChanged</c> mỗi giây. Gom lại rồi mới ghi đĩa.
    /// </summary>
    private static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(600);

    private readonly IPresetRepository _repository;
    private readonly ILogger<PresetLibrary> _logger;
    private readonly ObservableCollection<CrosshairProfile> _presets = [];
    private readonly HashSet<Guid> _dirty = [];
    private readonly object _dirtyGate = new();
    private readonly Timer _saveTimer;

    private CrosshairProfile? _active;
    private bool _disposed;

    public PresetLibrary(IPresetRepository repository, ILogger<PresetLibrary> logger)
    {
        _repository = repository;
        _logger = logger;
        Presets = new ReadOnlyObservableCollection<CrosshairProfile>(_presets);
        _saveTimer = new Timer(OnSaveTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
    }

    public ReadOnlyObservableCollection<CrosshairProfile> Presets { get; }

    public CrosshairProfile? Active => _active;

    public event EventHandler? ActiveChanged;

    public event EventHandler? OrderChanged;

    public async Task InitializeAsync(
        Guid preferredActiveId, IReadOnlyList<Guid>? order = null, CancellationToken cancellationToken = default)
    {
        var loaded = await _repository.GetAllAsync(cancellationToken).ConfigureAwait(true);

        foreach (var preset in ApplyOrder(loaded, order))
        {
            Track(preset);
            _presets.Add(preset);
        }

        var active = _presets.FirstOrDefault(p => p.Id == preferredActiveId) ?? _presets.FirstOrDefault();
        if (active is not null) SetActive(active);

        _logger.LogInformation("Thư viện preset đã nạp {Count} mục.", _presets.Count);
    }

    public void SetActive(CrosshairProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (ReferenceEquals(_active, profile)) return;

        _active = profile;
        ActiveChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Xếp preset theo thứ tự đã lưu; preset không có trong đó giữ thứ tự sẵn có (theo tên, từ repository)
    /// và đứng sau. Id lạ trong thứ tự (preset đã bị xoá tay) bị bỏ qua.
    /// </summary>
    internal static IReadOnlyList<CrosshairProfile> ApplyOrder(
        IReadOnlyList<CrosshairProfile> presets, IReadOnlyList<Guid>? order)
    {
        if (order is null || order.Count == 0) return presets;

        var rank = new Dictionary<Guid, int>(order.Count);
        for (var i = 0; i < order.Count; i++) rank.TryAdd(order[i], i);

        var ordered = new List<CrosshairProfile>(presets.Count);
        ordered.AddRange(presets.Where(p => rank.ContainsKey(p.Id)).OrderBy(p => rank[p.Id]));
        ordered.AddRange(presets.Where(p => !rank.ContainsKey(p.Id)));   // OrderBy/Where giữ thứ tự gốc
        return ordered;
    }

    public bool Move(CrosshairProfile profile, int newIndex)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var oldIndex = _presets.IndexOf(profile);
        if (oldIndex < 0 || newIndex < 0 || newIndex >= _presets.Count || oldIndex == newIndex) return false;

        // ObservableCollection.Move phát đúng một thông báo Move: ListBox giữ nguyên mục đang chọn, không
        // xoá-rồi-thêm làm SelectedItem nhảy về null.
        _presets.Move(oldIndex, newIndex);
        _logger.LogInformation("Đổi thứ tự preset '{Name}': {Old} → {New}.", profile.Name, oldIndex, newIndex);

        OrderChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void StepActive(int direction)
    {
        if (_presets.Count == 0 || _active is null) return;

        var index = _presets.IndexOf(_active);
        if (index < 0) index = 0;

        // Phép mod của C# giữ dấu âm, nên cộng thêm Count trước khi mod để -1 quay về cuối.
        var next = ((index + direction) % _presets.Count + _presets.Count) % _presets.Count;
        SetActive(_presets[next]);
    }

    public async Task<CrosshairProfile> CreateAsync(CancellationToken cancellationToken = default)
    {
        var preset = CrosshairProfile.CreateDefault();
        preset.Name = MakeUniqueName(Localization.Tr.Get("Preset_DefaultName"));

        await _repository.SaveAsync(preset, cancellationToken).ConfigureAwait(true);

        Track(preset);
        _presets.Add(preset);
        OrderChanged?.Invoke(this, EventArgs.Empty);
        SetActive(preset);

        return preset;
    }

    public async Task<CrosshairProfile> AddAsync(
        CrosshairProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        profile.Name = MakeUniqueName(profile.Name);
        await _repository.SaveAsync(profile, cancellationToken).ConfigureAwait(true);

        Track(profile);
        _presets.Add(profile);
        OrderChanged?.Invoke(this, EventArgs.Empty);
        SetActive(profile);

        return profile;
    }

    public async Task<CrosshairProfile> DuplicateAsync(
        CrosshairProfile source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var copy = source.Clone(newIdentity: true);
        copy.Name = MakeUniqueName(copy.Name);

        await _repository.SaveAsync(copy, cancellationToken).ConfigureAwait(true);

        Track(copy);
        _presets.Insert(Math.Min(_presets.IndexOf(source) + 1, _presets.Count), copy);
        OrderChanged?.Invoke(this, EventArgs.Empty);
        SetActive(copy);

        return copy;
    }

    public async Task DeleteAsync(CrosshairProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!_presets.Contains(profile)) return;

        var index = _presets.IndexOf(profile);

        Untrack(profile);
        _presets.Remove(profile);
        await _repository.DeleteAsync(profile.Id, cancellationToken).ConfigureAwait(true);

        lock (_dirtyGate) _dirty.Remove(profile.Id);

        // Thư viện rỗng là trạng thái app không xử lý được — overlay sẽ không có gì để vẽ.
        if (_presets.Count == 0)
        {
            var fallback = CrosshairProfile.CreateDefault();
            await _repository.SaveAsync(fallback, cancellationToken).ConfigureAwait(true);
            Track(fallback);
            _presets.Add(fallback);
        }

        OrderChanged?.Invoke(this, EventArgs.Empty);

        if (ReferenceEquals(_active, profile))
        {
            _active = null;
            SetActive(_presets[Math.Clamp(index, 0, _presets.Count - 1)]);
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        _saveTimer.Change(Timeout.Infinite, Timeout.Infinite);
        await SaveDirtyAsync(cancellationToken).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ nội bộ

    private void Track(CrosshairProfile preset) =>
        ProfileNotifications.Hook(preset, OnPresetChanged, subscribe: true);

    private void Untrack(CrosshairProfile preset) =>
        ProfileNotifications.Hook(preset, OnPresetChanged, subscribe: false);

    private void OnPresetChanged(object? sender, PropertyChangedEventArgs e)
    {
        // sender có thể là chính preset hoặc một khối con của nó, nên tìm ngược lên chủ sở hữu.
        var owner = sender as CrosshairProfile ?? FindOwner(sender);
        if (owner is null) return;

        lock (_dirtyGate) _dirty.Add(owner.Id);
        _saveTimer.Change(SaveDebounce, Timeout.InfiniteTimeSpan);
    }

    private CrosshairProfile? FindOwner(object? part)
    {
        if (part is null) return null;

        foreach (var preset in _presets)
        {
            if (ReferenceEquals(preset.InnerLines, part)
                || ReferenceEquals(preset.OuterLines, part)
                || ReferenceEquals(preset.CenterDot, part)
                || ReferenceEquals(preset.Outline, part)
                || ReferenceEquals(preset.Ring, part)
                || ReferenceEquals(preset.Image, part))
            {
                return preset;
            }
        }

        return null;
    }

    private void OnSaveTimerElapsed(object? state) => _ = SaveDirtySafeAsync();

    private async Task SaveDirtySafeAsync()
    {
        try
        {
            await SaveDirtyAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Exception thoát khỏi callback của Timer sẽ giết cả tiến trình.
            _logger.LogError(ex, "Tự động lưu preset thất bại.");
        }
    }

    private async Task SaveDirtyAsync(CancellationToken cancellationToken)
    {
        Guid[] pending;
        lock (_dirtyGate)
        {
            if (_dirty.Count == 0) return;
            pending = [.. _dirty];
            _dirty.Clear();
        }

        foreach (var id in pending)
        {
            var preset = _presets.FirstOrDefault(p => p.Id == id);
            if (preset is null) continue;

            try
            {
                await _repository.SaveAsync(preset, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Không lưu được preset '{Name}'.", preset.Name);
            }
        }
    }

    private string MakeUniqueName(string desired)
    {
        if (string.IsNullOrWhiteSpace(desired)) desired = "Crosshair";
        if (!_presets.Any(p => NameMatches(p, desired))) return desired;

        for (var suffix = 2; suffix < 1000; suffix++)
        {
            var candidate = $"{desired} {suffix}";
            if (!_presets.Any(p => NameMatches(p, candidate))) return candidate;
        }

        return $"{desired} {Guid.NewGuid():N}"[..40];
    }

    private static bool NameMatches(CrosshairProfile preset, string name) =>
        string.Equals(preset.Name, name, StringComparison.CurrentCultureIgnoreCase);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _saveTimer.Dispose();

        foreach (var preset in _presets) Untrack(preset);
    }
}

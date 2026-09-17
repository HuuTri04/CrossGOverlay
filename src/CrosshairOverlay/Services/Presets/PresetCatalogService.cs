using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Services.Storage;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay.Services.Presets;

/// <inheritdoc cref="IPresetCatalogService"/>
public sealed class PresetCatalogService : IPresetCatalogService
{
    /// <summary>Đường dẫn tương đối tới thư mục chạy, khớp mục Content trong csproj.</summary>
    public const string RelativePath = "Data/builtin_presets.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly HashSet<string> KnownShapes =
        new(["ClassicCross", "TShape", "XShape", "Dot", "Circle", "Square"], StringComparer.Ordinal);

    private readonly string _filePath;
    private readonly ILogger<PresetCatalogService> _logger;
    private readonly object _gate = new();

    private Task<IReadOnlyList<CatalogPreset>>? _loading;

    /// <summary>Mẫu kèm chuỗi tìm kiếm đã chuẩn hoá (thường, bỏ dấu) — tính một lần lúc đọc.</summary>
    private Indexed[] _index = [];
    private Dictionary<string, int> _counts = new(StringComparer.Ordinal);

    public PresetCatalogService(ILogger<PresetCatalogService> logger)
        : this(Path.Combine(AppContext.BaseDirectory, RelativePath), logger)
    {
    }

    internal PresetCatalogService(string filePath, ILogger<PresetCatalogService> logger)
    {
        _filePath = filePath;
        _logger = logger;
    }

    public bool IsLoaded { get; private set; }

    public Task<IReadOnlyList<CatalogPreset>> LoadAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            // Đọc lỗi bất ngờ (task faulted) thì lần mở sau được thử lại; đọc xong thì dùng mãi bản cache.
            if (_loading is null || _loading.IsFaulted || _loading.IsCanceled)
                _loading = Task.Run(() => ReadAsync(cancellationToken), cancellationToken);

            return _loading;
        }
    }

    public IReadOnlyList<string> GetCategories() => CatalogCategories.Ordered;

    public int CountIn(string category) =>
        _counts.TryGetValue(category, out var count) ? count : 0;

    public IReadOnlyList<CatalogPreset> FilterPresets(string? searchText, string? category)
    {
        var index = _index;   // đọc một lần: tham chiếu mảng chỉ được gán nguyên khối
        if (index.Length == 0) return [];

        var anyCategory = string.IsNullOrEmpty(category) || category == CatalogCategories.All;
        var tokens = Normalize(searchText).Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var result = new List<CatalogPreset>(anyCategory && tokens.Length == 0 ? index.Length : 64);
        foreach (var item in index)
        {
            if (!anyCategory && !string.Equals(item.Preset.Category, category, StringComparison.Ordinal)) continue;
            if (!MatchesAll(item.SearchText, tokens)) continue;

            result.Add(item.Preset);
        }

        return result;
    }

    // ------------------------------------------------------------------ nội bộ

    private async Task<IReadOnlyList<CatalogPreset>> ReadAsync(CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        List<CatalogPreset>? raw = null;

        try
        {
            await using var stream = new FileStream(
                _filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 64 * 1024, useAsync: true);
            raw = await JsonSerializer.DeserializeAsync<List<CatalogPreset>>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            _logger.LogWarning("Không có file thư viện mẫu: {Path}.", _filePath);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Không đọc được file thư viện mẫu {Path}.", _filePath);
        }

        var valid = new List<CatalogPreset>(raw?.Count ?? 0);
        var seenIds = new HashSet<Guid>();
        var skipped = 0;

        foreach (var preset in raw ?? [])
        {
            if (preset is null || !IsValid(preset) || !seenIds.Add(preset.Id))
            {
                skipped++;
                continue;
            }

            valid.Add(preset);
        }

        _index = [.. valid.Select(p => new Indexed(p, BuildSearchText(p)))];
        _counts = valid.GroupBy(p => p.Category, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        _counts[CatalogCategories.All] = valid.Count;
        IsLoaded = true;

        _logger.LogInformation(
            "Thư viện mẫu: {Count} mẫu ({Skipped} mục hỏng bị bỏ) đọc trong {Elapsed:0} ms.",
            valid.Count, skipped, watch.Elapsed.TotalMilliseconds);

        return valid;
    }

    /// <summary>Mục hỏng (sửa tay, thiếu trường, số NaN) bị bỏ riêng lẻ — không làm hỏng cả thư viện.</summary>
    internal static bool IsValid(CatalogPreset preset) =>
        preset.Id != Guid.Empty
        && !string.IsNullOrWhiteSpace(preset.Name)
        && KnownShapes.Contains(preset.ShapeType)
        && CatalogCategories.Ordered.Contains(preset.Category)
        && preset.Category != CatalogCategories.All
        && JsonColorConverter.Parse(preset.Color) is not null
        && double.IsFinite(preset.Thickness) && double.IsFinite(preset.Size) && double.IsFinite(preset.Gap)
        && double.IsFinite(preset.DotSize) && double.IsFinite(preset.OutlineThickness)
        && double.IsFinite(preset.OutlineOpacity);

    /// <summary>
    /// Tên mẫu cộng từ khoá của danh mục và hình dạng bằng CẢ tiếng Việt lẫn tiếng Anh: tên mẫu là tiếng Anh
    /// ("Cross L6 T2"), nhưng người dùng gõ "chữ thập" vẫn phải ra.
    /// </summary>
    private static string BuildSearchText(CatalogPreset preset)
    {
        var category = preset.Category switch
        {
            CatalogCategories.ProPlayers => "pro player tuyển thủ",
            CatalogCategories.Dots => "dot chấm nhỏ",
            CatalogCategories.Crosses => "cross chữ thập",
            CatalogCategories.Circles => "circle vòng tròn",
            _ => "tactical khác chiến thuật",
        };

        var shape = preset.ShapeType switch
        {
            "TShape" => "t-shape chữ t",
            "XShape" => "x-shape chữ x",
            "Square" => "box square khung vuông",
            "Circle" => "circle vòng",
            "Dot" => "dot chấm",
            _ => "cross chữ thập",
        };

        return Normalize($"{preset.Name} {category} {shape} {(preset.HasDot ? "dot chấm giữa" : string.Empty)}");
    }

    private static bool MatchesAll(string text, string[] tokens)
    {
        foreach (var token in tokens)
        {
            if (!text.Contains(token, StringComparison.Ordinal)) return false;
        }

        return true;
    }

    /// <summary>Chữ thường, bỏ dấu tiếng Việt (kể cả đ → d), gộp khoảng trắng.</summary>
    internal static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSpace = false;

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;

            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace) builder.Append(' ');
                lastWasSpace = true;
                continue;
            }

            builder.Append(ch == 'đ' ? 'd' : ch);
            lastWasSpace = false;
        }

        return builder.ToString();
    }

    private readonly record struct Indexed(CatalogPreset Preset, string SearchText);
}
